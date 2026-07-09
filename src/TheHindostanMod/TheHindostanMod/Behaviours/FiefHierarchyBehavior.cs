using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;

namespace TakhtyaTaboot
{
    // The feudal backbone. Resolves the liege/vassal chain from fief ownership
    // (NOT settlement.Governor — see wiki Ch.15), and drives the two obligations a
    // vassal owes upward: tribute each season and service when the realm goes to war.
    // Every message from a liege or the sovereign is delivered as a Royal Farmaan.
    //
    // Interactions:
    //   ImperialAuthorityBehavior — tax rate + call-to-arms compliance scale with it
    //   LegitimacyBehavior        — a weak ruler's summons carries less weight
    //   MansabdariBehavior        — a fief above your mansab rank is flagged
    public class FiefHierarchyBehavior : CampaignBehaviorBase
    {
        public static FiefHierarchyBehavior Instance { get; private set; }

        private const int SeasonDays = 21;

        // War tracking for call-to-arms detection.
        private List<string> _activeWars = new List<string>();
        // Active call to arms (player).
        private int _callDeadlineDay = -1;
        private string _callLiegeId = "";
        private bool _callHeeded;
        // Tax / standing.
        private int _lastTaxDay = -1;
        private int _daysInPoorStanding;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("FiefHierarchy.WeeklyTick", OnWeeklyTick));
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("FiefHierarchy.DailyTick", OnDailyTick));
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_fh_wars", ref _activeWars);
            dataStore.SyncData("hind_fh_callDeadline", ref _callDeadlineDay);
            dataStore.SyncData("hind_fh_callLiege", ref _callLiegeId);
            dataStore.SyncData("hind_fh_callHeeded", ref _callHeeded);
            dataStore.SyncData("hind_fh_lastTax", ref _lastTaxDay);
            dataStore.SyncData("hind_fh_poorStanding", ref _daysInPoorStanding);
        }

        // ── Liege / vassal / fief resolution ───────────────────────────────────────

        public Hero GetLiege(Hero lord)
        {
            if (lord == null) return null;

            // ONE canonical liege chain: FeudalTitlesBehavior.GetFeudalLiege honours the
            // stored zamindar layer and explicit liege bonds. Before this delegation, tribute
            // and the call-to-arms were computed from engine ownership alone, so the player
            // was shown one liege on the hierarchy screen while paying and serving another.
            Hero canonical = FeudalTitlesBehavior.Instance?.GetFeudalLiege(lord);
            if (canonical != null) return canonical;

            Kingdom kingdom = lord.Clan?.Kingdom;
            if (kingdom == null) return null;
            if (lord == kingdom.Leader) return null; // the sovereign answers to none

            // Fallback (feudal layer not available): engine-derived resolution as before.
            var fiefs = GetFiefs(lord);
            if (fiefs.Count > 0 && fiefs.All(s => s.IsVillage))
            {
                Settlement bound = fiefs[0].Village?.Bound;
                Hero boundHolder = bound?.Owner;
                if (boundHolder != null && boundHolder != lord) return boundHolder;
            }
            // Town/castle holders (and landless vassals) answer directly to the sovereign.
            return kingdom.Leader;
        }

        public List<Settlement> GetFiefs(Hero lord)
        {
            if (lord?.Clan == null) return new List<Settlement>();
            // Ownership, not governorship: settlements the clan holds, attributed to the
            // clan leader as the political holder.
            return lord.Clan.Settlements
                .Where(s => s.IsTown || s.IsCastle || s.IsVillage)
                .ToList();
        }

        public List<Hero> GetVassals(Hero liege)
        {
            if (liege == null) return new List<Hero>();
            Kingdom k = liege.Clan?.Kingdom;
            if (k == null) return new List<Hero>();
            return k.Clans
                .Where(c => !c.IsEliminated && c.Leader != null && c.Leader != liege)
                .Select(c => c.Leader)
                .Where(h => GetLiege(h) == liege)
                .ToList();
        }

        public bool HoldsAnyFief(Hero lord) => GetFiefs(lord).Count > 0;

        // ── Fief grant (emperor's gift / cheat) ────────────────────────────────────

        public void GrantFief(Settlement settlement, Hero newHolder, bool asImperialDecree)
        {
            if (settlement == null || newHolder == null) return;
            ChangeOwnerOfSettlementAction.ApplyByGift(settlement, newHolder);

            // A village grant must also register in the feudal layer, or GetTier/hierarchy
            // would show the holder as an unlanded noble while the tax code charged for it.
            if (settlement.IsVillage)
                FeudalTitlesBehavior.Instance?.AssignZamindar(settlement, newHolder);

            if (newHolder == Hero.MainHero)
            {
                Kingdom k = newHolder.Clan?.Kingdom;
                string rankWarn = "";
                if (MansabdariBehavior.Instance != null && !MansabdariBehavior.Instance.CanHold(Clan.PlayerClan, settlement))
                    rankWarn = $"\n\nYour present mansab does not formally entitle you to hold a {SettlementKind(settlement)}. " +
                               "Rise in rank, or the court may question the grant.";

                if (asImperialDecree && k != null)
                    RoyalFarmaan.FromRuler(k, "Grant of a Jagir",
                        $"Know all who read this: the settlement of {settlement.Name} and its revenues are " +
                        $"conferred upon {newHolder.Name}, to hold and to govern in the name of the Empire." + rankWarn);
                else
                    RoyalFarmaan.Issue("Grant of a Jagir", "By the Imperial Court",
                        $"{settlement.Name} passes into your hands." + rankWarn, "Sealed by decree");
            }
        }

        // (Village sub-infeudation now lives in FeudalTitlesBehavior — every village has
        // a zamindar in our feudal layer, assigned without disturbing engine ownership.)

        // ── War service: the call to arms ──────────────────────────────────────────

        private void OnWeeklyTick()
        {
            DetectNewWars();
            TrySeasonalTax();
        }

        private void DetectNewWars()
        {
            Kingdom pk = Hero.MainHero?.Clan?.Kingdom;
            if (pk == null) return;

            // The sovereign issues summons; they do not receive them.
            bool playerOwesService = HoldsAnyFief(Hero.MainHero) && Hero.MainHero != pk.Leader;

            foreach (Kingdom enemy in Kingdom.All.Where(k => k != pk && !k.IsEliminated))
            {
                string key = WarKey(pk, enemy);
                bool atWar = pk.IsAtWarWith(enemy);
                if (atWar && !_activeWars.Contains(key))
                {
                    _activeWars.Add(key);
                    if (playerOwesService) IssueCallToArms(pk, enemy);
                }
                else if (!atWar && _activeWars.Contains(key))
                {
                    _activeWars.Remove(key);
                }
            }
        }

        public void IssueCallToArms(Kingdom kingdom, Kingdom enemy)
        {
            Hero liege = GetLiege(Hero.MainHero) ?? kingdom.Leader;
            if (liege == null) return;
            _callLiegeId = liege.StringId;
            _callDeadlineDay = (int)CampaignTime.Now.ToDays + 14;
            _callHeeded = false;

            float legitMod = LegitimacyBehavior.Instance?.GetCallToArmsModifier(kingdom.Leader) ?? 1f;
            string weak = legitMod < 0.75f
                ? " (Yet the court is divided, and many lords drag their feet.)" : "";

            RoyalFarmaan.FromLiege(liege, "Summons to War",
                $"War is declared upon {enemy.Name}. By the bond of your jagir you are summoned to bring your " +
                $"retinue to the imperial host within fourteen days. Fail, and your standing at court will suffer.{weak}",
                "I shall march (heed the summons)", () => HeedCall(),
                "I will not come (defy the summons)", () => DefyCall());
        }

        private void HeedCall()
        {
            _callHeeded = true;
            Notify("You have pledged to answer the summons. Join your liege's army within the fortnight.", false);
        }

        private void DefyCall()
        {
            Hero liege = HeroById(_callLiegeId);
            ApplyDefiancePenalty(liege, "refused the call to arms");
            ClearCall();
        }

        private void OnDailyTick()
        {
            // Resolve an active, heeded call to arms.
            if (_callDeadlineDay < 0) return;
            int today = (int)CampaignTime.Now.ToDays;
            Hero liege = HeroById(_callLiegeId);

            if (_callHeeded && IsServingWith(liege))
            {
                if (liege != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, liege, 5);
                ChangeClanInfluenceAction.Apply(Clan.PlayerClan, 10f);
                RoyalFarmaan.FromLiege(liege, "Service Acknowledged",
                    "Your banners stand with mine in the field. The court notes your loyalty with favour.",
                    "It is my duty");
                ClearCall();
                return;
            }

            if (today >= _callDeadlineDay)
            {
                ApplyDefiancePenalty(liege, "failed to answer the call to arms in time");
                ClearCall();
            }
        }

        private bool IsServingWith(Hero liege)
        {
            if (liege == null || MobileParty.MainParty?.Army == null) return false;
            Army army = MobileParty.MainParty.Army;
            if (army.LeaderParty?.LeaderHero == liege) return true;
            return army.Parties.Any(p => p.LeaderHero == liege)
                || army.LeaderParty?.LeaderHero == liege.Clan?.Kingdom?.Leader;
        }

        private void ApplyDefiancePenalty(Hero liege, string why)
        {
            if (liege != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, liege, -10);
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k != null) ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -3f, "a vassal shirked the summons");
            _daysInPoorStanding += 14;
            Notify($"You {why}. Your liege is displeased, and your standing at court falls.", true);
        }

        private void ClearCall()
        {
            _callDeadlineDay = -1;
            _callLiegeId = "";
            _callHeeded = false;
        }

        // ── Tribute: the seasonal tax ──────────────────────────────────────────────

        private void TrySeasonalTax()
        {
            if (!HoldsAnyFief(Hero.MainHero)) return;
            Hero liege = GetLiege(Hero.MainHero);
            if (liege == null) return; // the sovereign pays tribute to no one

            int today = (int)CampaignTime.Now.ToDays;
            if (_lastTaxDay < 0) { _lastTaxDay = today; return; }
            if (today - _lastTaxDay < SeasonDays) return;
            _lastTaxDay = today;

            int owed = ComputeSeasonalTax(Hero.MainHero);
            if (owed <= 0) return;

            RoyalFarmaan.FromLiege(liege, "Demand for Tribute",
                $"The season turns, and your jagir owes its tribute. {owed} dinars are due to your liege's treasury. " +
                "Pay, and keep your honour at court; withhold, and answer for it.",
                $"Pay {owed} dinars", () => PayTax(liege, owed),
                "Withhold the tribute", () => WithholdTax(liege, owed));
        }

        private int ComputeSeasonalTax(Hero lord)
        {
            // Villages: tribute owed UP scales with hearth (see VillageFiefMath) — the
            // village COFFER (VillageDevelopmentBehavior) is what the fief yields DOWN,
            // so a maintained village nets its zamindar a positive income.
            float baseSum = 0f;
            foreach (Settlement s in GetFiefs(lord))
                baseSum += s.IsTown ? 600f : s.IsCastle ? 300f
                    : Util.VillageFiefMath.SeasonalTributeForVillage(s.Village?.Hearth ?? 0f);

            // A firm emperor collects in full; a weakened one cannot reach into the provinces.
            float authorityRate = 1f;
            Kingdom k = lord.Clan?.Kingdom;
            if (k != null && ImperialAuthorityBehavior.Instance != null)
                authorityRate = ImperialAuthorityBehavior.Instance.GetTaxCollectionRate(k);

            return (int)(baseSum * authorityRate);
        }

        private void PayTax(Hero liege, int owed)
        {
            if (Hero.MainHero.Gold >= owed)
            {
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, liege, owed, true);
                if (liege != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, liege, 2);
                if (_daysInPoorStanding > 0) _daysInPoorStanding = Math.Max(0, _daysInPoorStanding - 7);
                Notify($"You pay {owed} dinars in tribute. Your standing holds.", false);
            }
            else
            {
                int partial = Hero.MainHero.Gold;
                if (partial > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, liege, partial, true);
                _daysInPoorStanding += 14;
                if (liege != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, liege, -5);
                Notify($"You could pay only {partial} of {owed} dinars. You fall into tax default.", true);
            }
        }

        private void WithholdTax(Hero liege, int owed)
        {
            _daysInPoorStanding += 21;
            if (liege != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, liege, -8);
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k != null) ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -2f, "a vassal withheld tribute");
            Notify("You withhold the tribute. Your liege will remember this.", true);
        }

        // ── Fief change reactions ──────────────────────────────────────────────────

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!Util.WorldGen.Ready) return; // skip the parallel world-gen distribution (see Util/WorldGen.cs)
            if (newOwner == Hero.MainHero && MansabdariBehavior.Instance != null
                && !MansabdariBehavior.Instance.CanHold(Clan.PlayerClan, settlement))
            {
                Notify($"You now hold {settlement.Name}, but your mansab rank is below what custom expects for a {SettlementKind(settlement)}.", true);
            }
        }

        // ── Helpers / public API for cheats ────────────────────────────────────────

        public int GetDaysInPoorStanding() => _daysInPoorStanding;

        public string DescribePlayerStanding()
        {
            Hero p = Hero.MainHero;
            Hero liege = GetLiege(p);
            var fiefs = GetFiefs(p);
            var vassals = GetVassals(p);
            string rank = MansabdariBehavior.Instance?.GetTitle(Clan.PlayerClan) ?? "Unranked";
            return
                $"Liege: {(liege?.Name?.ToString() ?? "none (you answer to no one)")}\n" +
                $"Mansab: {rank}\n" +
                $"Fiefs held: {(fiefs.Count == 0 ? "none" : string.Join(", ", fiefs.Select(f => f.Name)))}\n" +
                $"Vassals: {(vassals.Count == 0 ? "none" : string.Join(", ", vassals.Select(v => v.Name)))}\n" +
                $"Days in poor standing: {_daysInPoorStanding}\n" +
                $"Next tribute due in: {(HoldsAnyFief(p) && _lastTaxDay >= 0 ? Math.Max(0, SeasonDays - ((int)CampaignTime.Now.ToDays - _lastTaxDay)) + " days" : "n/a")}";
        }

        // Force a call to arms for testing.
        public string ForceCallToArms()
        {
            Kingdom pk = Hero.MainHero?.Clan?.Kingdom;
            if (pk == null) return "You serve no kingdom.";
            if (!HoldsAnyFief(Hero.MainHero)) return "You hold no fief, so you owe no service.";
            Kingdom enemy = Kingdom.All.FirstOrDefault(k => k != pk && !k.IsEliminated);
            IssueCallToArms(pk, enemy ?? pk);
            return "A summons to war has been issued.";
        }

        // Force the tribute demand for testing.
        public string ForceTax()
        {
            if (!HoldsAnyFief(Hero.MainHero)) return "You hold no fief, so you owe no tribute.";
            Hero liege = GetLiege(Hero.MainHero);
            if (liege == null) return "You are sovereign; you owe tribute to no one.";
            int owed = ComputeSeasonalTax(Hero.MainHero);
            RoyalFarmaan.FromLiege(liege, "Demand for Tribute",
                $"{owed} dinars are due to your liege's treasury this season.",
                $"Pay {owed} dinars", () => PayTax(liege, owed),
                "Withhold the tribute", () => WithholdTax(liege, owed));
            return "A tribute demand has been issued.";
        }

        private static string SettlementKind(Settlement s)
            => s.IsTown ? "town" : s.IsCastle ? "castle" : "village";

        private static string WarKey(Kingdom a, Kingdom b)
            => string.Compare(a.StringId, b.StringId, StringComparison.Ordinal) < 0
                ? a.StringId + "|" + b.StringId : b.StringId + "|" + a.StringId;

        private static Hero HeroById(string id)
            => string.IsNullOrEmpty(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));
    }
}
