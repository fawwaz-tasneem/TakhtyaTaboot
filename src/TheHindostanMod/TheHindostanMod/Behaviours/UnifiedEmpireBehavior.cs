using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Roadmap A.1 — the empire stands WHOLE until Aurangzeb dies. On a fresh campaign the two
    // vassal subahs, Bengal (empire_w) and Hyderabad (empire_s), fold into the Mughal Empire:
    // their clans take service directly under the Peacock Throne (fiefs travel with the clans,
    // so the map shows one empire), their quarrels pass to the imperial throne, and the emptied
    // kingdom objects sleep as DORMANT SHELLS — alive, at peace, holding their identity (ids,
    // banners, names) for the breakaway. When the scripted cascade kills Aurangzeb
    // (ImperialSuccessionEventBehavior, month 2), the fold reverses: the recorded clans return
    // home, the Nawab and the Nizam are re-seated, the recorded wars resume, and
    // FactionRelationsBehavior re-asserts the familiar three-realm stance.
    //
    // Everything is engine-side here; the phase machine and selection rules are pure and
    // tested (Util/UnifiedEmpireMath). Old saves are safe by construction: unification only
    // arms inside the campaign's first day, and the breakaway only fires from the Unified phase.
    public class UnifiedEmpireBehavior : CampaignBehaviorBase
    {
        private const string EmpireId = "empire";

        // The two realms that live inside the empire until Aurangzeb dies. Index-aligned with
        // the per-realm record fields below (0 = Bengal, 1 = Hyderabad).
        private static readonly string[] VassalRealmIds = { "empire_w", "empire_s" };

        public static UnifiedEmpireBehavior Instance { get; private set; }

        private int _phase;                          // UnifiedEmpireMath.Phase
        private bool _proclaim;                      // opening farmaan owed on the next daily tick
        private string _clansW = "", _clansS = "";   // clans folded in, per realm (CSV of ids)
        private string _rulerW = "", _rulerS = "";   // ruling clan at the fold, per realm
        private string _warsW = "", _warsS = "";     // kingdoms each realm was at war with (CSV)
        private string _coloursW = "", _coloursS = ""; // each clan's own colours (CSV of id:c1:c2)

        public override void RegisterEvents()
        {
            Instance = this;
            // Session launch is on the live map (WorldGenGuardBehavior registers first and has
            // already published WorldGen.Ready), so moving clans between kingdoms is safe here —
            // and doing it before the player's first look keeps the day-one map a single colour.
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this,
                _ => TYTLog.Guard("UnifiedEmpire.SessionLaunched", OnSessionLaunched));
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
                () => TYTLog.Guard("UnifiedEmpire.DailyTick", OnDailyTick));
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this,
                () => TYTLog.Guard("UnifiedEmpire.WeeklyTick", OnWeeklyTick));
            // The engine culls a kingdom with no settlements (FactionDiscontinuationCampaignBehavior);
            // a dormant shell must survive until the breakaway, so veto it while unified.
            CampaignEvents.CanKingdomBeDiscontinuedEvent.AddNonSerializedListener(this, OnCanKingdomBeDiscontinued);
        }

        public override void SyncData(IDataStore ds)
        {
            ds.SyncData("tyt_unified_phase", ref _phase);
            ds.SyncData("tyt_unified_proclaim", ref _proclaim);
            ds.SyncData("tyt_unified_clans_w", ref _clansW);
            ds.SyncData("tyt_unified_clans_s", ref _clansS);
            ds.SyncData("tyt_unified_ruler_w", ref _rulerW);
            ds.SyncData("tyt_unified_ruler_s", ref _rulerS);
            ds.SyncData("tyt_unified_wars_w", ref _warsW);
            ds.SyncData("tyt_unified_wars_s", ref _warsS);
            ds.SyncData("tyt_unified_colours_w", ref _coloursW);
            ds.SyncData("tyt_unified_colours_s", ref _coloursS);
        }

        // A live kingdom with no living clans — a realm of nobody. True for the shells during
        // the unified window; used by relations/UI passes to leave dormant realms alone.
        public static bool IsDormant(Kingdom k)
            => k != null && !k.IsEliminated && !k.Clans.Any(c => c != null && !c.IsEliminated);

        private static Kingdom Find(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);

        // ── The fold (campaign open) ─────────────────────────────────────────────────
        private void OnSessionLaunched()
        {
            double age = CampaignTime.Now.ToDays - Campaign.Current.Models.CampaignTimeModel.CampaignStartTime.ToDays;
            if (!UnifiedEmpireMath.ShouldUnify(_phase, age)) return;
            Unify();
        }

        private void Unify()
        {
            Kingdom empire = Find(EmpireId);
            if (empire == null || empire.IsEliminated)
            { TYTLog.Warn("UnifiedEmpire: no empire kingdom; premise idle."); return; }

            var folded = new List<string>();
            for (int i = 0; i < VassalRealmIds.Length; i++)
            {
                Kingdom realm = Find(VassalRealmIds[i]);
                if (realm == null || realm.IsEliminated) continue;

                // Snapshot before the fold: the clans, the throne, and the realm's quarrels —
                // the breakaway replays this record.
                var clans = realm.Clans
                    .Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService && c != Clan.PlayerClan)
                    .ToList();
                string rulerId = realm.RulingClan?.StringId ?? "";
                var warIds = Kingdom.All
                    .Where(k => k != realm && !k.IsEliminated && realm.IsAtWarWith(k))
                    .Select(k => k.StringId).ToList();
                SetRealmRecord(i, UnifiedEmpireMath.Pack(clans.Select(c => c.StringId)), rulerId,
                    UnifiedEmpireMath.Pack(warIds));

                // The subah's quarrels pass to the imperial throne (Mysore's Deccan war reaches
                // the emperor once Hyderabad is his own province)...
                foreach (string wid in warIds)
                {
                    Kingdom enemy = Find(wid);
                    if (enemy != null && !enemy.IsEliminated && !FactionRelationsBehavior.IsMughalKingdom(enemy)
                        && !empire.IsAtWarWith(enemy))
                        DeclareWarAction.ApplyByDefault(enemy, empire);
                }

                // ...and its lords take service directly under the Peacock Throne. Fiefs travel
                // with the clans, so the map shows one empire from the first frame.
                var colourRecords = new List<string>();
                foreach (Clan c in clans)
                {
                    ChangeKingdomAction.ApplyByJoinToKingdom(c, empire, showNotification: false);
                    // Leaving a kingdom zeroes influence; restore the weight of their tier so the
                    // Nawab does not arrive at the darbar politically naked.
                    ChangeClanInfluenceAction.Apply(c,
                        Campaign.Current.Models.ClanTierModel.CalculateInitialInfluence(c) - c.Influence);
                    // Dress the house in imperial colours for the duration (banners recolour by
                    // themselves on joining; Color/Color2 — shields, trims, nameplates — do not,
                    // and left alone they made the unified map read as three realms). The
                    // ancestral colours are recorded and restored at the breakaway.
                    colourRecords.Add(UnifiedEmpireMath.PackColour(c.StringId, c.Color, c.Color2));
                    c.Color = empire.Color;
                    c.Color2 = empire.Color2;
                    RedrawClan(c);
                }
                SetColourRecord(i, UnifiedEmpireMath.Pack(colourRecords));

                // The emptied shell sleeps at peace — no war may bind a realm of nobody.
                QuietShell(realm);
                folded.Add(realm.Name.ToString());
            }

            if (folded.Count == 0)
            { TYTLog.Warn("UnifiedEmpire: no vassal realms found to fold; premise idle."); return; }

            _phase = (int)UnifiedEmpireMath.Phase.Unified;
            _proclaim = true; // farmaan on the next daily tick, once the map UI is fully up
            // The farmaan waits for the first tick, but the player deserves an immediate sign
            // that the world was reshaped (a message is safe at any point of session launch).
            TaleWorlds.Library.InformationManager.DisplayMessage(new TaleWorlds.Library.InformationMessage(
                $"The empire stands whole: {string.Join(" and ", folded)} fold under the Peacock Throne.",
                TaleWorlds.Library.Color.FromUint(0xFFD4AF37)));
            TYTLog.Info($"UnifiedEmpire: folded {string.Join(", ", folded)} into the empire; shells dormant until Aurangzeb dies.");
        }

        // ── The dormant window ───────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (!_proclaim || _phase != (int)UnifiedEmpireMath.Phase.Unified) return;
            _proclaim = false;
            UI.RoyalFarmaan.Issue("One Throne, One Hindostan",
                "From the imperial camp in the Deccan",
                "By the grace of God the empire of Hindostan stands whole. The subah of Bengal and the " +
                "subah of the Deccan answer to the Peacock Throne alone; their nawabs and nizams sit in " +
                "the imperial darbar as servants of Aurangzeb Alamgir, Emperor of Hindostan. So it has " +
                "been, and so — while the old lion lives — it shall remain.",
                seal: "Sealed by the Grand Vizier");
        }

        private void OnWeeklyTick()
        {
            // Belt-and-braces: should any AI decision drag a dormant shell into a war, settle it.
            if (_phase != (int)UnifiedEmpireMath.Phase.Unified) return;
            foreach (string id in VassalRealmIds)
            {
                Kingdom shell = Find(id);
                if (shell != null && !shell.IsEliminated && IsDormant(shell)) QuietShell(shell);
            }
        }

        private void OnCanKingdomBeDiscontinued(Kingdom k, ref bool result)
        {
            if (_phase == (int)UnifiedEmpireMath.Phase.Unified && k?.StringId != null
                && System.Array.IndexOf(VassalRealmIds, k.StringId) >= 0)
                result = false;
        }

        private static void QuietShell(Kingdom shell)
        {
            foreach (Kingdom other in Kingdom.All
                         .Where(k => k != shell && !k.IsEliminated && shell.IsAtWarWith(k)).ToList())
                MakePeaceAction.Apply(shell, other);
        }

        // ── The breakaway (Aurangzeb's death) ────────────────────────────────────────
        // Called by ImperialSuccessionEventBehavior the moment the cascade processes Aurangzeb's
        // passing, whether or not the crowning itself succeeded — his death, not the accession,
        // is what lets the subahs slip the leash.
        public void OnAurangzebPassing()
        {
            if (_phase != (int)UnifiedEmpireMath.Phase.Unified) return;
            _phase = (int)UnifiedEmpireMath.Phase.Sundered; // set first: never re-entered
            Kingdom empire = Find(EmpireId);

            for (int i = 0; i < VassalRealmIds.Length; i++)
            {
                Kingdom realm = Find(VassalRealmIds[i]);
                if (realm == null || realm.IsEliminated) continue;
                GetRealmRecord(i, out string clansCsv, out string rulerId, out string warsCsv);

                // Only clans still alive and still serving the empire go home; strongest first
                // so a fallen ruling house yields the throne to the mightiest survivor.
                var aliveInEmpire = new HashSet<string>(
                    empire?.Clans.Where(c => c != null && !c.IsEliminated && c != Clan.PlayerClan)
                        .Select(c => c.StringId) ?? Enumerable.Empty<string>());
                List<string> returningIds = UnifiedEmpireMath.SelectReturning(
                    UnifiedEmpireMath.Unpack(clansCsv), aliveInEmpire);
                List<Clan> returning = returningIds
                    .Select(id => Clan.All.FirstOrDefault(c => c.StringId == id))
                    .Where(c => c != null)
                    .OrderByDescending(c => c.CurrentTotalStrength)
                    .ToList();

                if (returning.Count == 0)
                {
                    // History bends: nobody is left to raise the banner — the subah is absorbed
                    // for good and the shell is retired (mirrors the engine's own discontinuation).
                    TYTLog.Warn($"UnifiedEmpire: no clans left to revive {realm.StringId}; the subah stays imperial.");
                    realm.RulingClan = null;
                    DestroyKingdomAction.Apply(realm);
                    continue;
                }

                string chosenRulerId = UnifiedEmpireMath.ChooseRuler(rulerId,
                    returning.Select(c => c.StringId).ToList());
                Clan ruler = returning.First(c => c.StringId == chosenRulerId);

                // Seat the ruler first (CreateKingdom detail re-anchors RulingClan), then the rest.
                ChangeKingdomAction.ApplyByCreateKingdom(ruler, realm, showNotification: false);
                foreach (Clan c in returning.Where(c => c != ruler))
                    ChangeKingdomAction.ApplyByJoinToKingdom(c, realm, showNotification: false);

                // The houses shed their imperial dress and ride under their own colours again
                // (restored for every recorded clan still alive, returning or not — a house that
                // defected mid-window still owns its ancestral colours).
                GetColourRecord(i, out string coloursCsv);
                foreach (string entry in UnifiedEmpireMath.Unpack(coloursCsv))
                    if (UnifiedEmpireMath.TryUnpackColour(entry, out string cid, out uint c1, out uint c2))
                    {
                        Clan c = Clan.All.FirstOrDefault(x => x.StringId == cid);
                        if (c == null || c.IsEliminated) continue;
                        c.Color = c1;
                        c.Color2 = c2;
                        RedrawClan(c);
                    }

                // The realm resumes the quarrels it carried into the fold (e.g. Mysore vs Hyderabad).
                foreach (string wid in UnifiedEmpireMath.Unpack(warsCsv))
                {
                    Kingdom enemy = Find(wid);
                    if (enemy != null && !enemy.IsEliminated && !FactionRelationsBehavior.IsMughalKingdom(enemy)
                        && !realm.IsAtWarWith(enemy))
                        DeclareWarAction.ApplyByDefault(enemy, realm);
                }

                AnnounceBreakaway(i, realm, ruler);
                TYTLog.Info($"UnifiedEmpire: {realm.StringId} stands apart under {ruler.Name} ({returning.Count} clan(s) returned).");
            }

            // The familiar three-realm stance: intra-Mughal peace, kinship floors, the Maratha war.
            FactionRelationsBehavior.Instance?.ReassertStance();
        }

        private static void AnnounceBreakaway(int realmIndex, Kingdom realm, Clan ruler)
        {
            string rulerName = ruler.Leader?.Name?.ToString() ?? ruler.Name.ToString();
            if (realmIndex == 0)
                UI.RoyalFarmaan.Issue("Bengal Stands Apart", "From Murshidabad",
                    $"Alamgir is dead, and with him the fear that bound the east. {rulerName} keeps the revenue " +
                    $"of Bengal for Bengal and rules as Nawab in his own right. The subah answers to " +
                    "Shahjahanabad no longer — though the khutba is still read in the emperor's name.",
                    seal: "Sealed at the Nawab's court");
            else
                UI.RoyalFarmaan.Issue("The Deccan Raises Its Own Banner", "From the Deccan",
                    $"With the old emperor gone, {rulerName} gathers the southern subahs to himself and rules " +
                    $"as Nizam in his own right. The Deccan sends no more treasure north — though the emperor's " +
                    "name still graces the coin.",
                    seal: "Sealed at the Nizam's court");
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("unified_status", "hindostan")]
        public static string Status(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Phase: {(UnifiedEmpireMath.Phase)Instance._phase}");
            Kingdom empire = Find(EmpireId);
            sb.AppendLine($"Empire clans: {empire?.Clans.Count(c => c != null && !c.IsEliminated) ?? 0}");
            for (int i = 0; i < VassalRealmIds.Length; i++)
            {
                Kingdom shell = Find(VassalRealmIds[i]);
                Instance.GetRealmRecord(i, out string clansCsv, out string rulerId, out _);
                var recorded = UnifiedEmpireMath.Unpack(clansCsv);
                int home = recorded.Count(id => Clan.All.FirstOrDefault(c => c.StringId == id)?.Kingdom?.StringId == EmpireId);
                sb.AppendLine($"{VassalRealmIds[i]}: " +
                    (shell == null ? "MISSING" : shell.IsEliminated ? "eliminated"
                     : $"{(IsDormant(shell) ? "dormant shell" : "LIVE")}, {shell.Clans.Count} clan(s)") +
                    $" | recorded {recorded.Count} clan(s), {home} now in empire | ruler record: {rulerId}");
            }
            return sb.ToString();
        }

        // Repaint everything that carries the clan's colours (parties and held settlements).
        private static void RedrawClan(Clan c)
        {
            foreach (var wpc in c.WarPartyComponents)
                wpc.MobileParty?.Party?.SetVisualAsDirty();
            foreach (var s in c.Settlements)
                s.Party?.SetVisualAsDirty();
        }

        // ── Per-realm record plumbing (index-aligned with VassalRealmIds) ───────────
        private void SetRealmRecord(int i, string clansCsv, string rulerId, string warsCsv)
        {
            if (i == 0) { _clansW = clansCsv; _rulerW = rulerId; _warsW = warsCsv; }
            else { _clansS = clansCsv; _rulerS = rulerId; _warsS = warsCsv; }
        }

        private void GetRealmRecord(int i, out string clansCsv, out string rulerId, out string warsCsv)
        {
            if (i == 0) { clansCsv = _clansW; rulerId = _rulerW; warsCsv = _warsW; }
            else { clansCsv = _clansS; rulerId = _rulerS; warsCsv = _warsS; }
        }

        private void SetColourRecord(int i, string coloursCsv)
        {
            if (i == 0) _coloursW = coloursCsv; else _coloursS = coloursCsv;
        }

        private void GetColourRecord(int i, out string coloursCsv)
            => coloursCsv = i == 0 ? _coloursW : _coloursS;
    }
}
