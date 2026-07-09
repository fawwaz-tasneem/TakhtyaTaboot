using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;

namespace TakhtyaTaboot
{
    // The career ladder: a noble must EARN the rank before he holds the fief. A lord
    // climbs village (zamindar) -> castle (Qiledar) -> town (Subahdar), and may never
    // keep a fief above his mansab. If the player comes to hold a town or castle his
    // rank does not justify, the court gives him a season to rise — or it reclaims the
    // fief and bestows it on a worthier noble. A guided "claim your due" lets the player
    // take the next fief his rank entitles him to.
    public class CareerProgressionBehavior : CampaignBehaviorBase
    {
        public static CareerProgressionBehavior Instance { get; private set; }

        private const int GraceDays = 30;
        private const int ClaimInfluenceCost = 30;

        // settlementId -> day the court will reclaim it if the player has not risen.
        private Dictionary<string, int> _flagged = new Dictionary<string, int>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("CareerProgression.DailyTick", OnDailyTick));
        }

        private void OnSessionLaunched(CampaignGameStarter starter) => ScanPlayerFiefs(announce: false);

        private static bool PlayerIsSovereign()
            => Hero.MainHero?.Clan?.Kingdom != null && Hero.MainHero.Clan.Kingdom.Leader == Hero.MainHero;

        private static MansabdariBehavior M => MansabdariBehavior.Instance;

        // ── Enforcement ──────────────────────────────────────────────────────────────
        private void OnOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!Util.WorldGen.Ready) return; // skip the parallel world-gen distribution (see Util/WorldGen.cs)
            if (newOwner != Hero.MainHero || s == null || !(s.IsTown || s.IsCastle)) return;
            if (PlayerIsSovereign() || M == null) return;
            if (M.CanHold(Clan.PlayerClan, s)) return;
            FlagFief(s, announce: true);
        }

        private void ScanPlayerFiefs(bool announce)
        {
            if (M == null) return;
            foreach (Settlement s in Clan.PlayerClan.Settlements.Where(x => x.IsTown || x.IsCastle).ToList())
            {
                if (PlayerIsSovereign() || M.CanHold(Clan.PlayerClan, s)) { _flagged.Remove(s.StringId); continue; }
                if (!_flagged.ContainsKey(s.StringId)) FlagFief(s, announce);
            }
            // Drop stale flags.
            foreach (string id in _flagged.Keys.ToList())
            {
                Settlement s = Settlement.All.FirstOrDefault(x => x.StringId == id);
                if (s == null || s.OwnerClan != Clan.PlayerClan) _flagged.Remove(id);
            }
        }

        private void FlagFief(Settlement s, bool announce)
        {
            _flagged[s.StringId] = (int)CampaignTime.Now.ToDays + GraceDays;
            if (!announce) return;
            Kingdom k = Clan.PlayerClan?.Kingdom;
            string req = M.RequiredTitle(s);
            string body = $"You have come to hold {s.Name}, a {Kind(s)} — but your mansab ({M.GetTitle(Clan.PlayerClan)}) " +
                          $"does not entitle you to it. Rise to the rank of {req} within {GraceDays} days, or the court will " +
                          $"reclaim {s.Name} and bestow it upon a worthier noble.";
            if (k != null) RoyalFarmaan.FromRuler(k, "A Fief Beyond Your Station", body, "I shall earn it");
            else Notify(body, true);
        }

        private void OnDailyTick()
        {
            if (M == null || _flagged.Count == 0) return;
            int today = (int)CampaignTime.Now.ToDays;

            foreach (string id in _flagged.Keys.ToList())
            {
                Settlement s = Settlement.All.FirstOrDefault(x => x.StringId == id);
                if (s == null || s.OwnerClan != Clan.PlayerClan) { _flagged.Remove(id); continue; }

                if (PlayerIsSovereign() || M.CanHold(Clan.PlayerClan, s))
                {
                    _flagged.Remove(id);
                    Kingdom k = Clan.PlayerClan?.Kingdom;
                    if (k != null)
                        RoyalFarmaan.FromRuler(k, "Confirmed in Your Fief",
                            $"Your rank now justifies your holding of {s.Name}. The court confirms you in it, and the matter is closed.",
                            "As is my right");
                    continue;
                }

                if (today >= _flagged[id]) ReclaimFief(s);
            }
        }

        private void ReclaimFief(Settlement s)
        {
            _flagged.Remove(s.StringId);
            Kingdom k = Clan.PlayerClan?.Kingdom;

            Hero recipient = null;
            if (k != null)
                recipient = k.Clans
                    .Where(c => !c.IsEliminated && c.Leader != null && c.Leader != Hero.MainHero && M.CanHold(c, s))
                    .OrderByDescending(c => M.GetRankIndex(c))
                    .ThenBy(c => c.Settlements.Count)
                    .Select(c => c.Leader)
                    .FirstOrDefault();
            recipient = recipient ?? k?.Leader;
            if (recipient == null || recipient == Hero.MainHero) return; // nowhere to send it; leave be

            ChangeOwnerOfSettlementAction.ApplyByGift(s, recipient);
            if (k != null)
                RoyalFarmaan.FromRuler(k, "A Fief Reclaimed",
                    $"You failed to rise to the rank that {s.Name} demands. By order of the court it passes to {recipient.Name}, " +
                    "who holds the mansab to govern it. Earn your rank, and such honours may yet be yours.",
                    "The court's will be done");
            else Notify($"{s.Name} has been reclaimed by the court and given to {recipient.Name}.", true);
        }

        // ── Guided claim: take the next fief your rank entitles you to ───────────────
        public bool CanClaim(out string reason) => TryFindClaim(out _, out _, out reason);

        public void ClaimFief()
        {
            if (!TryFindClaim(out Settlement target, out bool villageZamindari, out string reason))
            { Notify(reason, true); return; }
            if (Clan.PlayerClan.Influence < ClaimInfluenceCost)
            { Notify($"The court expects {ClaimInfluenceCost} influence to process the grant.", true); return; }

            Kingdom k = Clan.PlayerClan.Kingdom;

            if (villageZamindari)
            {
                // Seat FIRST; charge and proclaim only if the seat took. AssignZamindar can
                // legitimately refuse (mercenary service, rank gate) — the old order spent the
                // influence and announced a grant that never happened.
                if (FeudalTitlesBehavior.Instance?.AssignZamindar(target, Hero.MainHero) != true)
                { Notify("The grant could not be sealed — the court finds you ineligible to hold land.", true); return; }
                ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -ClaimInfluenceCost);
                RoyalFarmaan.FromRuler(k, "Your First Fief",
                    $"As a {M.GetTitle(Clan.PlayerClan)}, you are granted the zamindari of {target.Name}. Hold it well — " +
                    "rise in mansab, and greater fiefs will follow.", "I am honoured");
            }
            else
            {
                ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -ClaimInfluenceCost);
                Hero sovereign = k?.Leader;
                ChangeOwnerOfSettlementAction.ApplyByGift(target, Hero.MainHero);
                if (sovereign != null && sovereign != Hero.MainHero)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, sovereign, -3);
                RoyalFarmaan.FromRuler(k, "A Fief Befitting Your Rank",
                    $"Your mansab as {M.GetTitle(Clan.PlayerClan)} entitles you to govern {target.Name}. The court bestows it " +
                    "upon you this day. Serve faithfully.", "I accept the charge");
            }
        }

        // Find the lowest fief tier the player qualifies for but does not yet hold, and a
        // settlement to grant from.
        private bool TryFindClaim(out Settlement target, out bool villageZamindari, out string reason)
        {
            target = null; villageZamindari = false; reason = "";
            if (M == null) { reason = "The court is not in session."; return false; }
            Kingdom k = Clan.PlayerClan?.Kingdom;
            if (k == null) { reason = "You must serve an empire to be granted a fief."; return false; }
            if (Clan.PlayerClan.IsUnderMercenaryService) { reason = "As a mercenary you hold no fiefs. Swear as a full vassal first."; return false; }
            if (PlayerIsSovereign()) { reason = "As sovereign, you bestow fiefs — you do not petition for them."; return false; }

            int rank = M.GetRankIndex(Clan.PlayerClan);
            bool holdsTown = Clan.PlayerClan.Settlements.Any(s => s.IsTown);
            bool holdsCastle = Clan.PlayerClan.Settlements.Any(s => s.IsCastle);
            var ft = FeudalTitlesBehavior.Instance;
            bool holdsVillage = (ft?.GetVillagesLordedBy(Hero.MainHero).Count ?? 0) > 0
                                || Clan.PlayerClan.Settlements.Any(s => s.IsVillage);

            // Village rung (Zamindar, rank >= 1).
            if (rank >= 1 && !holdsVillage && !holdsCastle && !holdsTown)
            {
                Settlement v = FindGrantableVillage(k);
                if (v == null) { reason = "No village can be found to grant you just now."; return false; }
                target = v; villageZamindari = true; return true;
            }

            // Castle rung (Qiledar, rank >= 3).
            if (rank >= 3 && !holdsCastle && !holdsTown)
            {
                Settlement c = FindSovereignDemesne(k, isTown: false);
                if (c == null) { reason = "The court has no castle to bestow upon you at present."; return false; }
                target = c; return true;
            }

            // Town rung (Subahdar, rank >= 5).
            if (rank >= 5 && !holdsTown)
            {
                Settlement t = FindSovereignDemesne(k, isTown: true);
                if (t == null) { reason = "The court has no town to bestow upon you at present."; return false; }
                target = t; return true;
            }

            reason = holdsTown || holdsCastle || holdsVillage
                ? "You already hold a fief at the height of your present rank. Rise in mansab to claim more."
                : "Your mansab does not yet entitle you to a fief. Petition for promotion first.";
            return false;
        }

        // A village to make the player zamindar of — preferring one bound to the sovereign's towns.
        private Settlement FindGrantableVillage(Kingdom k)
        {
            var ft = FeudalTitlesBehavior.Instance;
            var villages = Settlement.All.Where(s => s.IsVillage && s.MapFaction == k).ToList();
            if (villages.Count == 0) return null;
            Settlement preferred = villages.FirstOrDefault(v => v.Village?.Bound?.OwnerClan == k.Leader?.Clan);
            return preferred ?? villages.FirstOrDefault();
        }

        // A castle or town the crown can bestow on a deserving vassal. Prefers a vacant fief, then the
        // sovereign's surplus; failing both, the crown will part with even its last of the kind for a
        // worthy claimant (previously this only ever offered the crown's SURPLUS, so the option almost
        // never appeared — an AI emperor rarely holds two towns or two castles himself).
        private Settlement FindSovereignDemesne(Kingdom k, bool isTown)
        {
            if (k == null) return null;
            bool Match(Settlement s) => isTown ? s.IsTown : s.IsCastle;

            // 1) A fief the realm holds that sits in no clan's hands.
            Settlement vacant = k.Settlements.FirstOrDefault(s => Match(s) && s.OwnerClan == null);
            if (vacant != null) return vacant;

            Clan ruling = k.Leader?.Clan;
            if (ruling == null) return null;
            var of = ruling.Settlements.Where(Match)
                .OrderBy(s => isTown ? (s.Town?.Prosperity ?? 0f) : 0f).ToList();
            // 2) Surplus first (keep one), 3) else spare even the last for a worthy vassal.
            return of.Count > 1 ? of.First() : of.FirstOrDefault();
        }

        private static string Kind(Settlement s) => s.IsTown ? "town" : s.IsCastle ? "castle" : "village";

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        public override void SyncData(IDataStore dataStore)
        {
            var ids = _flagged.Keys.ToList();
            var days = _flagged.Values.ToList();
            dataStore.SyncData("hind_career_flagIds", ref ids);
            dataStore.SyncData("hind_career_flagDays", ref days);
            if (!dataStore.IsSaving)
            {
                _flagged = new Dictionary<string, int>();
                for (int i = 0; i < ids.Count && i < days.Count; i++) _flagged[ids[i]] = days[i];
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("career_status", "hindostan")]
        public static string Status(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            string flags = Instance._flagged.Count == 0 ? "none"
                : string.Join(", ", Instance._flagged.Select(kv =>
                    (Settlement.All.FirstOrDefault(s => s.StringId == kv.Key)?.Name?.ToString() ?? kv.Key)
                    + " (reclaimed day " + kv.Value + ")"));
            bool can = Instance.CanClaim(out string reason);
            return $"Mansab: {MansabdariBehavior.Instance?.GetTitle(Clan.PlayerClan)}\n" +
                   $"Fiefs held beyond your rank: {flags}\n" +
                   $"Claim available: {(can ? "yes" : "no — " + reason)}";
        }
    }
}
