using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // The monsoon beyond speed (roadmap C.4, wiki Ch.17 §4). The rains that MonsoonSpeedPatch
    // already mires armies with now also drive the harvest: once a year, when the monsoon
    // breaks, the subcontinent draws a good/bad rains roll. A bountiful year swells the
    // post-monsoon village tax; a failed one thins it — and can tip a hard-pressed village
    // into FAMINE, a plea in the same shape as the bandit-relief pleas: open the granaries at
    // a price, or let the district starve (hearth and loyalty bleed, threat climbs).
    // All the arithmetic (harvest multiplier, famine odds, verdict) is pure in Util.SeasonMath.
    // VillageDevelopmentBehavior reads HarvestMultiplier() into its tax accrual.
    public class MonsoonBehavior : CampaignBehaviorBase
    {
        public static MonsoonBehavior Instance { get; private set; }

        private float _quality = 0.5f;      // this year's monsoon, 0 (failed) .. 1 (bountiful)
        private int _lastRolledYear = -1;   // the year the current _quality was rolled for
        private Dictionary<string, int> _lastFamineYear = new Dictionary<string, int>(); // villageId -> year

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Monsoon.DailyTick", OnDailyTick));
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
        }

        // ── The harvest read by the village tax pipeline ─────────────────────────────
        public float MonsoonQuality => _quality;

        public float HarvestMultiplier()
        {
            if (!Config.Tune.MonsoonHarvestEnabled) return 1f;
            return SeasonMath.HarvestTaxMultiplier((int)CampaignTime.Now.GetSeasonOfYear, _quality);
        }

        // ── The yearly rains roll ────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (!Config.Tune.MonsoonHarvestEnabled) return;
            int season = (int)CampaignTime.Now.GetSeasonOfYear;
            int year = CampaignTime.Now.GetYear;
            if (season == SeasonMath.Monsoon && year != _lastRolledYear)
            {
                _lastRolledYear = year;
                _quality = MBRandom.RandomFloat; // uniform 0..1: some years feast, some famine
                AnnounceMonsoon();
            }
        }

        private void AnnounceMonsoon()
        {
            string verdict = SeasonMath.MonsoonVerdict(_quality);
            // Only the years that matter interrupt the player; a middling year is a log line.
            if (SeasonMath.IsBountifulMonsoon(_quality) || SeasonMath.IsFailedMonsoon(_quality))
            {
                RoyalFarmaan.Issue(
                    SeasonMath.IsFailedMonsoon(_quality) ? "The Rains Have Failed" : "A Bountiful Monsoon",
                    "From the revenue office (diwani)",
                    $"{verdict}\n \nThe post-monsoon harvest will run "
                    + (SeasonMath.IsFailedMonsoon(_quality)
                        ? "thin; the village coffers will yield less at collection, and the hardest-pressed districts may hunger."
                        : "fat; the village coffers will swell when the harvest is gathered in."),
                    seal: "Entered in the revenue registers, " + RoyalFarmaan.CurrentDate(),
                    primary: "Noted", dedupeKey: "monsoon_" + _lastRolledYear, cooldownDays: 30);
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The monsoon of the year: " + verdict, Color.FromUint(0xFF9A8866)));
            }
            TYTLog.Info($"Monsoon: year {_lastRolledYear} quality {_quality:0.00} ({SeasonMath.MonsoonVerdict(_quality)}).");
        }

        // ── Famine, on a failed-rains harvest ────────────────────────────────────────
        private void OnDailyTickSettlement(Settlement s)
        {
            if (!Config.Tune.MonsoonHarvestEnabled || s == null || !s.IsVillage || s.Village == null) return;
            if (!SeasonMath.IsFailedMonsoon(_quality)) return;
            int season = (int)CampaignTime.Now.GetSeasonOfYear;
            if (season != SeasonMath.PostMonsoon && season != SeasonMath.CoolSeason) return; // hunger comes at harvest
            if (!IsPlayerVillage(s)) return; // AI holdings weather it off-screen

            int year = CampaignTime.Now.GetYear;
            if (_lastFamineYear.TryGetValue(s.StringId, out int fy) && fy == year) return; // one famine per village per year

            float threat = VillageDevelopmentBehavior.Instance?.GetThreat(s) ?? 0f;
            float chance = SeasonMath.FamineDailyChance(_quality, s.Village.Hearth, threat);
            if (chance <= 0f || MBRandom.RandomFloat >= chance) return;

            _lastFamineYear[s.StringId] = year;
            UI.RoyalFarmaan.SuppressImmediate = true; // inside settlement iteration; enqueue only
            try { TYTLog.Guard("Monsoon.Famine:" + s.Name, () => TriggerFamine(s)); }
            finally { UI.RoyalFarmaan.SuppressImmediate = false; }
        }

        private void TriggerFamine(Settlement s)
        {
            int cost = FamineReliefCost(s);
            RoyalFarmaan.Issue("Famine in the District", $"{s.Name} hungers",
                $"The failed rains have emptied the granaries of {s.Name}. The headman reports the people eating seed-grain and " +
                $"the weak already dying. You may open the granaries and buy in grain to see them through — some {cost} rupees — " +
                "or let the district fend for itself and bear what follows.",
                seal: "Sent in the failing light of the year",
                primary: $"Open the granaries (pay {cost} rupees)",
                onPrimary: () => TYTLog.Guard("Monsoon.Relieve", () => RelieveFamine(s, cost)),
                secondary: "Let them fend for themselves",
                onSecondary: () => TYTLog.Guard("Monsoon.Starve", () => LetStarve(s)),
                dedupeKey: "famine:" + s.StringId, priority: FarmaanPriority.Urgent);
        }

        private static int FamineReliefCost(Settlement s)
            => (int)MathF.Max(500f, MathF.Min(8000f, (s.Village?.Hearth ?? 0f) * 2f));

        private void RelieveFamine(Settlement s, int cost)
        {
            if (Hero.MainHero.Gold < cost)
            { Notify($"You cannot raise the {cost} rupees to relieve {s.Name}. The district must fend for itself.", true); LetStarve(s); return; }
            Hero.MainHero.ChangeHeroGold(-cost);
            // Grain steadies the district: the worst hunger passes, order returns, the gentry remember it.
            if (s.Village != null) s.Village.Hearth = MathF.Max(1f, s.Village.Hearth - 5f); // some loss even relieved
            VillageDevelopmentBehavior.Instance?.AddThreat(s, -10f);
            foreach (Hero n in s.Notables?.Where(h => h != null && h.IsAlive) ?? Enumerable.Empty<Hero>())
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, +2);
            Notify($"You open the granaries of {s.Name}. The people are fed, and they will remember whose grain filled their bowls.", false);
            TYTLog.Info($"Monsoon: famine relieved at {s.StringId} for {cost}.");
        }

        private void LetStarve(Settlement s)
        {
            if (s.Village != null) s.Village.Hearth = MathF.Max(1f, s.Village.Hearth - 30f); // the village empties
            VillageDevelopmentBehavior.Instance?.AddThreat(s, +12f);
            foreach (Hero n in s.Notables?.Where(h => h != null && h.IsAlive) ?? Enumerable.Empty<Hero>())
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, -4);
            Notify($"Famine runs its course in {s.Name}. The hearth withers, and the survivors will not soon forget you did nothing.", true);
            TYTLog.Info($"Monsoon: famine unrelieved at {s.StringId}.");
        }

        private static bool IsPlayerVillage(Settlement s)
            => s != null && s.IsVillage
               && (s.OwnerClan == Clan.PlayerClan
                   || FeudalTitlesBehavior.Instance?.GetVillageLord(s) == Hero.MainHero);

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_monsoon_quality", ref _quality);
            dataStore.SyncData("hind_monsoon_year", ref _lastRolledYear);
            var ids = _lastFamineYear.Keys.ToList();
            var years = _lastFamineYear.Values.ToList();
            dataStore.SyncData("hind_monsoon_famIds", ref ids);
            dataStore.SyncData("hind_monsoon_famYears", ref years);
            if (!dataStore.IsSaving)
            {
                _lastFamineYear = new Dictionary<string, int>();
                for (int i = 0; i < ids.Count && i < years.Count; i++) _lastFamineYear[ids[i]] = years[i];
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("monsoon_status", "hindostan")]
        public static string MonsoonStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            int season = (int)CampaignTime.Now.GetSeasonOfYear;
            return $"Monsoon quality this year: {Instance._quality:0.00} ({SeasonMath.MonsoonVerdict(Instance._quality)})\n" +
                   $"Season: {SeasonMath.SeasonName(season)}  Harvest tax x{Instance.HarvestMultiplier():0.00}";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set_monsoon", "hindostan")]
        public static string SetMonsoon(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            float q = 0.1f;
            if (args != null && args.Count > 0) float.TryParse(args[0], out q);
            Instance._quality = MathF.Max(0f, MathF.Min(1f, q));
            Instance._lastRolledYear = CampaignTime.Now.GetYear; // don't let the auto-roll overwrite it this year
            return $"Monsoon quality set to {Instance._quality:0.00} ({SeasonMath.MonsoonVerdict(Instance._quality)}). " +
                   "Enter a village you hold in autumn/winter to court famine.";
        }
    }
}
