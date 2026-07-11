using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core.ViewModelCollection.Generic;

namespace TakhtyaTaboot
{
    // Adds the lord's imperial standing to the encyclopedia hero page's Info section (the
    // "Stats" list, alongside Culture / Occupation / Age) instead of dumping it into the
    // description. Fiefs and the liege are rendered as clickable encyclopedia links.
    //
    // Click handling on these value widgets is not enabled by the vanilla prefab; the
    // UI.EncyclopediaLinkEnabler UIExtenderEx patch turns it on.
    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), "RefreshValues")]
    internal static class EncyclopediaHeroStatsPatch
    {
        private static void Postfix(EncyclopediaHeroPageVM __instance)
        {
            try
            {
                if (!Util.SaveGuardBehavior.CampaignReady) return;
                if (!(__instance.Obj is Hero hero) || __instance.Stats == null) return;

                bool isLord = hero.IsLord && hero.Clan != null && !hero.Clan.IsBanditFaction;
                bool isZamindar = FeudalTitlesBehavior.Instance?.IsVillageZamindar(hero) ?? false;
                if (!isLord && !isZamindar) return;

                if (isLord) AddLordRows(__instance, hero);
                else AddZamindarRows(__instance, hero);

                AddRoyalStyleRow(__instance, hero);
                AddDispositionRow(__instance, hero);
            }
            catch { /* never break the encyclopedia */ }
        }

        private static void Row(EncyclopediaHeroPageVM vm, string definition, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            vm.Stats.Add(new StringPairItemVM(definition, value, null));
        }

        private static string Link(Hero h) => h?.EncyclopediaLinkWithName?.ToString() ?? h?.Name?.ToString();
        private static string Link(Settlement s) => s?.EncyclopediaLinkWithName?.ToString() ?? s?.Name?.ToString();

        private static void AddLordRows(EncyclopediaHeroPageVM vm, Hero hero)
        {
            string tier = FeudalTitlesBehavior.Instance?.GetTier(hero);
            Row(vm, "Feudal standing", tier);

            // A sovereign holds no mansab (he grants them); showing "Unranked (zat 0)" for the
            // emperor is wrong, so skip the row for the realm's ruler.
            bool isSovereign = hero.Clan?.Kingdom != null && hero.Clan.Kingdom.Leader == hero;
            if (!isSovereign && MansabdariBehavior.Instance != null)
            {
                string title = MansabdariBehavior.Instance.GetTitle(hero.Clan);
                int zat = MansabdariBehavior.Instance.GetZat(hero.Clan);
                int sawar = MansabdariBehavior.Instance.GetSawar(hero.Clan);
                if (!string.IsNullOrEmpty(title)) Row(vm, "Mansab", $"{title} — {Util.MansabRankMath.DualRankLabel(zat, sawar)}");
            }

            Row(vm, "Council office", CouncilBehavior.Instance?.GetPostOf(hero));

            Hero liege = FeudalTitlesBehavior.Instance?.GetFeudalLiege(hero);
            if (liege != null && liege != hero) Row(vm, "Liege", Link(liege));

            var seats = hero.Clan.Settlements?.Where(s => s.IsTown || s.IsCastle).ToList();
            if (seats != null && seats.Count > 0)
                Row(vm, seats.Count > 1 ? "Towns & castles" : "Seat", string.Join(", ", seats.Select(Link)));

            // The player's own progression toward the next mansab — shown only on their page,
            // since valour is a player mechanic the AI does not use.
            if (hero.Clan == Clan.PlayerClan && MansabdariBehavior.Instance != null)
                Row(vm, "Valour earned", $"{MansabdariBehavior.Instance.GetValour(hero.Clan):0}");
        }

        // The dynasty layer: a prince or princess of a reigning (or fallen) line carries
        // their culture's royal style — Shahzada, Yuvraj, Kanwar...
        private static void AddRoyalStyleRow(EncyclopediaHeroPageVM vm, Hero hero)
        {
            string style = DynastyBehavior.Instance?.RoyalStyle(hero);
            if (!string.IsNullOrEmpty(style)) Row(vm, "Royal style", style);
        }

        // What this hero PERSONALLY thinks of the player — vanilla relation plus the
        // opinion ledger's live records, with the strongest reasons named.
        private static void AddDispositionRow(EncyclopediaHeroPageVM vm, Hero hero)
        {
            var op = OpinionBehavior.Instance;
            if (op == null || hero == Hero.MainHero) return;
            float effective = op.EffectiveOpinion(hero, Hero.MainHero);
            var top = op.TopModifiers(hero, Hero.MainHero, 2);
            string reasons = top.Count == 0 ? ""
                : " (" + string.Join(", ", top.Select(t => Util.OpinionMath.Describe(t.type))) + ")";
            if (top.Count > 0) // only show the row when something personal stands between you
                Row(vm, "Disposition toward you", $"{effective:0}{reasons}");
        }

        private static void AddZamindarRows(EncyclopediaHeroPageVM vm, Hero hero)
        {
            var ft = FeudalTitlesBehavior.Instance;
            if (ft == null) return;
            List<Settlement> villages = ft.GetVillagesLordedBy(hero);
            if (villages == null || villages.Count == 0) return;

            Row(vm, "Feudal standing", "Village Zamindar");
            Row(vm, villages.Count > 1 ? "Villages held" : "Village held", string.Join(", ", villages.Select(Link)));
            Row(vm, "Levy", $"about {villages.Sum(v => ft.GetLevySize(v))} men");

            Hero liege = ft.GetFeudalLiege(hero);
            if (liege != null && liege != hero) Row(vm, "Liege", Link(liege));
        }
    }
}
