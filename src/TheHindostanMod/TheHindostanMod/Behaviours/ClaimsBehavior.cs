using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // THE CLAIM LEDGER (wiki ch.30 §1) — the spine of the diplomacy layer.
    //
    // A claim is a HOUSE's standing pretension to a place, remembered across generations: it belongs to
    // the CLAN, so a successor inherits what his father built, and it is denominated in YEARS OF STANDING.
    // Governing deepens it (1/yr, capped at 20). Losing the fief does NOT erase it — it freezes and fades
    // at half the rate, so a grudge outlives the holding that made it, and a house dispossessed of Bijapur
    // carries the grievance for decades. Any king it serves may take that claim up as his casus belli.
    //
    // Claims come two ways:
    //   • GOVERNANCE — hold the fief and the claim accrues.
    //   • EXTERNAL (the wakil) — leave a companion in a town to cultivate its merchant houses until
    //     two-thirds of them stand at +40. A shallower claim (3 yrs), and PERISHABLE: act within two
    //     years or it lapses. (The agent itself is step 8; this ledger already stores and expires them.)
    //
    // The ledger runs under BOTH tenure laws. The law changes what a claim DOES, not whether it accrues:
    // under Feudal it awards the conquest to the claimant house; under Mansabdari it makes the sitting
    // holder expensive to rotate — and past the crown's purse, immovable.
    //
    // The pure rules are the tested ClaimMath; this is the thin, guarded campaign layer.
    public class ClaimsBehavior : CampaignBehaviorBase
    {
        public static ClaimsBehavior Instance { get; private set; }

        public enum ClaimKind { Governance = 0, External = 1 }

        // Parallel lists (the engine cannot serialize dictionaries — SyncData convention throughout).
        private List<string> _clanId     = new List<string>();
        private List<string> _settleId   = new List<string>();
        private List<float>  _strength   = new List<float>();   // years of standing
        private List<int>    _updatedDay = new List<int>();     // day the strength was last settled
        private List<int>    _kind       = new List<int>();     // ClaimKind
        private List<int>    _grantedDay = new List<int>();     // External only: when the window opened

        private bool _seeded;
        private bool _ready;

        // Fast lookup, rebuilt from the lists on load. "clanId|settlementId" -> index.
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => TYTLog.Guard("Claims.Launch", OnSessionLaunched));
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => { if (_ready) TYTLog.Guard("Claims.Weekly", OnWeeklyTick); });
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
        }

        private static int Today => (int)CampaignTime.Now.ToDays;

        // Fiefs worth a war: towns and castles. Villages ride with their bound town (the zamindari
        // layer is a separate ledger and is never fought over as such).
        private static bool Claimable(Settlement s) => s != null && (s.IsTown || s.IsCastle);

        // ── Session ──────────────────────────────────────────────────────────────────
        private void OnSessionLaunched()
        {
            _ready = true;
            Reindex();
            if (!_seeded) SeedTheLedger();
            EnsureHolderClaims();
        }

        // Seed at 1707: no clan has tenure history at world-gen, so every sitting holder is given a claim
        // on what it holds, drawn from a NORMAL distribution over 0-10 years. Historical holders therefore
        // begin with real, varied pretensions — the map has grudges to act on from the first campaign year
        // rather than a flat zero for a decade. (Also runs once on an existing save, which is what we want:
        // it back-fills the ledger for campaigns that predate this system.)
        private void SeedTheLedger()
        {
            _seeded = true;
            int seeded = 0;
            foreach (Settlement s in Settlement.All.Where(Claimable))
            {
                Clan owner = s.OwnerClan;
                if (owner == null || owner.IsEliminated) continue;
                float years = ClaimMath.SeedYears(ClaimMath.StandardNormal(MBRandom.RandomFloat, MBRandom.RandomFloat));
                Set(owner, s, years, ClaimKind.Governance, Today);
                seeded++;
            }
            TYTLog.Info($"Claims: seeded {seeded} founding claims (normal, 0-{ClaimMath.SeedMaxYears:0} yrs).");
        }

        // Any house holding a fief the ledger has never seen (a conquest during world-gen, a clan created
        // after the seed) starts accruing from zero rather than silently holding nothing.
        private void EnsureHolderClaims()
        {
            foreach (Settlement s in Settlement.All.Where(Claimable))
            {
                Clan owner = s.OwnerClan;
                if (owner == null || owner.IsEliminated) continue;
                if (Find(owner, s) < 0) Set(owner, s, 0f, ClaimKind.Governance, Today);
            }
        }

        // ── The weekly settling of the ledger ────────────────────────────────────────
        // One pass: a house that governs deepens its claim, a house dispossessed watches it fade, and
        // claims faded to nothing (or external claims past their window) are struck from the roll.
        private void OnWeeklyTick()
        {
            int today = Today;
            EnsureHolderClaims();

            for (int i = _clanId.Count - 1; i >= 0; i--)
            {
                Clan clan = FindClan(_clanId[i]);
                Settlement s = FindSettlement(_settleId[i]);

                if (clan == null || clan.IsEliminated || s == null) { RemoveAt(i); continue; }

                int days = today - _updatedDay[i];
                if (days <= 0) continue;
                _updatedDay[i] = today;

                // An external claim does not accrue or decay — it simply runs out of time.
                if ((ClaimKind)_kind[i] == ClaimKind.External)
                {
                    if (!ClaimMath.ExternalClaimLive(_grantedDay[i], today))
                    {
                        NotifyIfPlayers(clan, $"The claim your wakil built in {s.Name} has lapsed — it was never acted upon.");
                        RemoveAt(i);
                    }
                    continue;
                }

                bool governs = s.OwnerClan == clan;
                _strength[i] = governs
                    ? ClaimMath.Accrue(_strength[i], days)
                    : ClaimMath.Decay(_strength[i], days);

                // A grudge finally forgotten: the conquest is legitimate at last.
                if (!governs && ClaimMath.IsForgotten(_strength[i])) RemoveAt(i);
            }
        }

        // ── Settlements changing hands ───────────────────────────────────────────────
        // The taker begins to accrue at once; the dispossessed KEEPS his claim, which from this day
        // begins its long fade. That is the whole grudge engine — nothing is deleted here.
        private void OnOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturer,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!WorldGen.Ready || !_ready) return;   // never touch the parallel world-gen distribution
            if (!Claimable(s)) return;

            TYTLog.GuardQuiet("Claims.OwnerChanged", () =>
            {
                Clan taker = newOwner?.Clan;
                if (taker == null || taker.IsEliminated) return;

                // The taker's claim: an existing (decaying) one is resumed where it stands — a house
                // retaking its ancestral seat does not start from nothing — otherwise it begins at zero.
                int i = Find(taker, s);
                if (i < 0) Set(taker, s, 0f, ClaimKind.Governance, Today);
                else { _updatedDay[i] = Today; _kind[i] = (int)ClaimKind.Governance; }

                // A conquest CONSUMES the external claim it was made on: the wakil's work is spent.
                int ext = FindOfKind(taker, s, ClaimKind.External);
                if (ext >= 0) RemoveAt(ext);
            });
        }

        private void OnClanDestroyed(Clan clan)
        {
            if (clan == null) return;
            TYTLog.GuardQuiet("Claims.ClanDestroyed", () =>
            {
                for (int i = _clanId.Count - 1; i >= 0; i--)
                    if (_clanId[i] == clan.StringId) RemoveAt(i);
            });
        }

        // ── Reads (the API the war layer, the tenure law and the UI all go through) ──

        // What this house's pretension to this place is worth, in years of standing.
        public float GetClaim(Clan clan, Settlement s)
        {
            int i = Find(clan, s);
            return i < 0 ? 0f : _strength[i];
        }

        // The claim of the house that currently SITS in the fief — what the crown must pay to move it on.
        public float HolderClaim(Settlement s)
            => s?.OwnerClan == null ? 0f : GetClaim(s.OwnerClan, s);

        // Every house with a pretension to this place, deepest first. The current holder is included —
        // the caller decides whether a sitting house counts as a claimant.
        public List<(Clan clan, float strength, ClaimKind kind)> ClaimantsOf(Settlement s)
        {
            var result = new List<(Clan, float, ClaimKind)>();
            if (s == null) return result;
            for (int i = 0; i < _settleId.Count; i++)
            {
                if (_settleId[i] != s.StringId) continue;
                Clan c = FindClan(_clanId[i]);
                if (c == null || c.IsEliminated) continue;
                result.Add((c, _strength[i], (ClaimKind)_kind[i]));
            }
            return result.OrderByDescending(r => r.Item2).ToList();
        }

        // Every place this house has a pretension to, deepest first.
        public List<(Settlement settlement, float strength, ClaimKind kind)> ClaimsOf(Clan clan)
        {
            var result = new List<(Settlement, float, ClaimKind)>();
            if (clan == null) return result;
            for (int i = 0; i < _clanId.Count; i++)
            {
                if (_clanId[i] != clan.StringId) continue;
                Settlement s = FindSettlement(_settleId[i]);
                if (s == null) continue;
                result.Add((s, _strength[i], (ClaimKind)_kind[i]));
            }
            return result.OrderByDescending(r => r.Item2).ToList();
        }

        // THE CASUS BELLI QUERY. Every fief the defender holds that a house of the aggressor's realm has
        // an actionable pretension to — deepest claim first. This is what a war of conquest is declared
        // FOR: its targets, and (on victory) the house each target is owed to.
        //
        // Throne wars are excluded on both sides: a hind_rebel_* war is binary and settles by its own
        // deadline, and its claim is the crown itself, not a province.
        public List<(Settlement settlement, Clan claimant, float strength)> ClaimTargets(Kingdom aggressor, Kingdom defender)
        {
            var result = new List<(Settlement, Clan, float)>();
            if (aggressor == null || defender == null || aggressor == defender) return result;
            if (ThroneWar.IsRebelKingdom(aggressor) || ThroneWar.IsRebelKingdom(defender)) return result;

            foreach (Settlement s in defender.Settlements.Where(Claimable))
            {
                Clan best = null;
                float bestStrength = 0f;
                foreach (var (clan, strength, _) in ClaimantsOf(s))
                {
                    if (clan.Kingdom != aggressor) continue;
                    if (!ClaimMath.WorthAWar(strength)) continue;
                    if (strength > bestStrength) { best = clan; bestStrength = strength; }
                }
                if (best != null) result.Add((s, best, bestStrength));
            }
            return result.OrderByDescending(r => r.Item3).ToList();
        }

        // Does this realm hold ANY cause worth marching on against that one?
        public bool HasCasusBelli(Kingdom aggressor, Kingdom defender)
            => ClaimTargets(aggressor, defender).Count > 0;

        // ── Writes ───────────────────────────────────────────────────────────────────

        // The wakil has turned a town: the house gains a shallow, perishable claim on it.
        public void GrantExternalClaim(Clan clan, Settlement s)
        {
            if (clan == null || clan.IsEliminated || !Claimable(s)) return;
            if (s.OwnerClan == clan) return;   // you cannot manufacture a claim on your own seat

            // Never let the wakil's shallow claim overwrite a deeper ancestral one.
            int i = Find(clan, s);
            if (i >= 0 && _strength[i] >= ClaimMath.ExternalClaimYears)
            {
                NotifyIfPlayers(clan, $"Your wakil's work in {s.Name} adds nothing — your house already holds a deeper claim there.");
                return;
            }

            Set(clan, s, ClaimMath.ExternalClaimYears, ClaimKind.External, Today, grantedDay: Today);
            NotifyIfPlayers(clan, $"The merchant houses of {s.Name} are yours. Your house now holds a claim there — " +
                                  $"act on it within two years, or it lapses.");
            TYTLog.Info($"Claims: {clan.Name} gained an EXTERNAL claim on {s.Name}.");
        }

        // A live external claim and the days left in its window.
        public bool HasLiveExternalClaim(Clan clan, Settlement s, out int daysLeft)
        {
            daysLeft = 0;
            int i = FindOfKind(clan, s, ClaimKind.External);
            if (i < 0) return false;
            if (!ClaimMath.ExternalClaimLive(_grantedDay[i], Today)) return false;
            daysLeft = ClaimMath.ExternalClaimDaysLeft(_grantedDay[i], Today);
            return true;
        }

        // Direct write — used by the seed, the console, and (later) by the scripted-history layer.
        public void SetClaim(Clan clan, Settlement s, float years, ClaimKind kind = ClaimKind.Governance)
        {
            if (clan == null || !Claimable(s)) return;
            Set(clan, s, years, kind, Today, kind == ClaimKind.External ? Today : 0);
        }

        // ── Ledger internals ─────────────────────────────────────────────────────────
        private static string Key(string clanId, string settleId) => clanId + "|" + settleId;

        private int Find(Clan clan, Settlement s)
        {
            if (clan?.StringId == null || s?.StringId == null) return -1;
            return _index.TryGetValue(Key(clan.StringId, s.StringId), out int i) && i < _clanId.Count ? i : -1;
        }

        private int FindOfKind(Clan clan, Settlement s, ClaimKind kind)
        {
            int i = Find(clan, s);
            return i >= 0 && (ClaimKind)_kind[i] == kind ? i : -1;
        }

        private void Set(Clan clan, Settlement s, float years, ClaimKind kind, int updatedDay, int grantedDay = 0)
        {
            int i = Find(clan, s);
            if (i >= 0)
            {
                _strength[i] = years; _kind[i] = (int)kind; _updatedDay[i] = updatedDay;
                if (kind == ClaimKind.External) _grantedDay[i] = grantedDay;
                return;
            }
            _clanId.Add(clan.StringId); _settleId.Add(s.StringId);
            _strength.Add(years); _updatedDay.Add(updatedDay);
            _kind.Add((int)kind); _grantedDay.Add(grantedDay);
            _index[Key(clan.StringId, s.StringId)] = _clanId.Count - 1;
        }

        // Removal swaps the tail in (O(1)) — so the index must be repaired for the moved row, not just
        // the removed one. Getting this wrong silently corrupts every lookup after the first deletion.
        private void RemoveAt(int i)
        {
            if (i < 0 || i >= _clanId.Count) return;
            _index.Remove(Key(_clanId[i], _settleId[i]));
            int last = _clanId.Count - 1;
            if (i != last)
            {
                _clanId[i] = _clanId[last]; _settleId[i] = _settleId[last];
                _strength[i] = _strength[last]; _updatedDay[i] = _updatedDay[last];
                _kind[i] = _kind[last]; _grantedDay[i] = _grantedDay[last];
                _index[Key(_clanId[i], _settleId[i])] = i;
            }
            _clanId.RemoveAt(last); _settleId.RemoveAt(last); _strength.RemoveAt(last);
            _updatedDay.RemoveAt(last); _kind.RemoveAt(last); _grantedDay.RemoveAt(last);
        }

        private void Reindex()
        {
            _index.Clear();
            // Old saves predate _grantedDay/_kind; pad rather than crash on a ragged load.
            while (_grantedDay.Count < _clanId.Count) _grantedDay.Add(0);
            while (_kind.Count < _clanId.Count) _kind.Add((int)ClaimKind.Governance);
            while (_updatedDay.Count < _clanId.Count) _updatedDay.Add(Today);
            for (int i = 0; i < _clanId.Count; i++) _index[Key(_clanId[i], _settleId[i])] = i;
        }

        private static Clan FindClan(string id)
            => string.IsNullOrEmpty(id) ? null : Clan.All.FirstOrDefault(c => c.StringId == id);

        private static Settlement FindSettlement(string id)
            => string.IsNullOrEmpty(id) ? null : Settlement.All.FirstOrDefault(s => s.StringId == id);

        // Tell the player when it is HIS house, or a house of his realm, that gained or lost something.
        private static void NotifyIfPlayers(Clan clan, string text)
        {
            if (clan != Clan.PlayerClan) return;
            InformationManager.DisplayMessage(new InformationMessage(text, Color.FromUint(0xFFD4AF37)));
        }

        // ── Save / load ──────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_claim_clan",    ref _clanId);
            dataStore.SyncData("hind_claim_settle",  ref _settleId);
            dataStore.SyncData("hind_claim_str",     ref _strength);
            dataStore.SyncData("hind_claim_day",     ref _updatedDay);
            dataStore.SyncData("hind_claim_kind",    ref _kind);
            dataStore.SyncData("hind_claim_granted", ref _grantedDay);
            dataStore.SyncData("hind_claim_seeded",  ref _seeded);

            if (!dataStore.IsSaving)
            {
                _clanId ??= new List<string>();
                _settleId ??= new List<string>();
                _strength ??= new List<float>();
                _updatedDay ??= new List<int>();
                _kind ??= new List<int>();
                _grantedDay ??= new List<int>();
                // Reindex runs at session launch (Settlement.All is not safe to touch here).
            }
        }

        // ── Console ──────────────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("claims", "hindostan")]
        public static string Claims(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Clan clan = Clan.PlayerClan;
            if (args != null && args.Count > 0)
            {
                string filter = string.Join(" ", args).ToLowerInvariant();
                clan = Clan.All.FirstOrDefault(c => !c.IsEliminated
                    && c.Name.ToString().ToLowerInvariant().Contains(filter));
                if (clan == null) return $"No clan matching '{filter}'.";
            }
            var claims = Instance.ClaimsOf(clan);
            if (claims.Count == 0) return $"{clan.Name} holds no claims.";

            var sb = new StringBuilder($"Claims of {clan.Name}:\n");
            foreach (var (s, strength, kind) in claims)
            {
                bool holds = s.OwnerClan == clan;
                string state = kind == ClaimKind.External
                    ? (Instance.HasLiveExternalClaim(clan, s, out int left) ? $"wakil's claim, {left}d left" : "wakil's claim, LAPSED")
                    : holds ? "governs — deepening"
                    : $"dispossessed — fading, forgotten in {ClaimMath.DaysToForget(strength)}d";
                sb.AppendLine($"  {s.Name,-22} {strength,5:0.0} yrs  ({ClaimMath.Describe(strength)}; {state})");
            }
            return sb.ToString();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("claims_on", "hindostan")]
        public static string ClaimsOn(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (args == null || args.Count == 0) return "Usage: hindostan.claims_on <settlement name part>";
            string filter = string.Join(" ", args).ToLowerInvariant();
            Settlement s = Settlement.All.FirstOrDefault(x => Claimable(x)
                && x.Name.ToString().ToLowerInvariant().Contains(filter));
            if (s == null) return $"No town or castle matching '{filter}'.";

            var claimants = Instance.ClaimantsOf(s);
            if (claimants.Count == 0) return $"No house claims {s.Name}.";

            var sb = new StringBuilder($"Claims upon {s.Name} (held by {s.OwnerClan?.Name.ToString() ?? "nobody"}):\n");
            foreach (var (clan, strength, kind) in claimants)
                sb.AppendLine($"  {clan.Name,-22} {strength,5:0.0} yrs  ({ClaimMath.Describe(strength)}" +
                              $"{(kind == ClaimKind.External ? ", wakil's claim" : "")}" +
                              $"{(s.OwnerClan == clan ? ", sits here" : "")})");
            return sb.ToString();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("grant_claim", "hindostan")]
        public static string GrantClaim(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (args == null || args.Count < 2)
                return "Usage: hindostan.grant_claim <years> <settlement name part> — grants YOUR clan a claim.";
            if (!float.TryParse(args[0], out float years)) return "First argument must be a number of years.";
            string filter = string.Join(" ", args.Skip(1)).ToLowerInvariant();
            Settlement s = Settlement.All.FirstOrDefault(x => Claimable(x)
                && x.Name.ToString().ToLowerInvariant().Contains(filter));
            if (s == null) return $"No town or castle matching '{filter}'.";

            Instance.SetClaim(Clan.PlayerClan, s, years);
            return $"{Clan.PlayerClan.Name} now holds {years:0.0} years of claim upon {s.Name} ({ClaimMath.Describe(years)}).";
        }

        // What the crown would pay to move the sitting house on (the Mansabdari friction, step 3).
        [CommandLineFunctionality.CommandLineArgumentFunction("rotation_price", "hindostan")]
        public static string RotationPrice(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (args == null || args.Count == 0) return "Usage: hindostan.rotation_price <settlement name part>";
            string filter = string.Join(" ", args).ToLowerInvariant();
            Settlement s = Settlement.All.FirstOrDefault(x => Claimable(x)
                && x.Name.ToString().ToLowerInvariant().Contains(filter));
            if (s == null) return $"No town or castle matching '{filter}'.";

            float claim = Instance.HolderClaim(s);
            return $"{s.OwnerClan?.Name} holds {s.Name} with {claim:0.0} yrs of claim ({ClaimMath.Describe(claim)}). " +
                   $"Rotating them costs x{ClaimMath.RotationInfluenceMultiplier(claim):0.00} the base influence.";
        }
    }
}
