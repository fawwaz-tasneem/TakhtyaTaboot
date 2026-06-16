using System.Linq;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace TakhtyaTaboot
{
    // Adds a "At the Imperial Court" section to a lord's encyclopedia entry, showing
    // their mansab rank and title, any council office they hold, and the fiefs they
    // command — so the player can read a noble's standing straight from the page.
    [HarmonyPatch(typeof(Hero), "EncyclopediaText", MethodType.Getter)]
    internal static class EncyclopediaInfoPatch
    {
        private static void Postfix(Hero __instance, ref TextObject __result)
        {
            try
            {
                if (__instance == null) return;
                bool isLord = __instance.IsLord && __instance.Clan != null && !__instance.Clan.IsBanditFaction;
                bool isZamindar = FeudalTitlesBehavior.Instance?.IsVillageZamindar(__instance) ?? false;
                if (!isLord && !isZamindar) return;

                string extra = isLord ? BuildLordSection(__instance) : BuildZamindarSection(__instance);
                if (string.IsNullOrEmpty(extra)) return;

                string baseText = __result != null ? __result.ToString() : "";
                __result = new TextObject(baseText + extra);
            }
            catch { /* never break the encyclopedia */ }
        }

        private static string BuildLordSection(Hero hero)
        {
            var sb = new StringBuilder();
            sb.Append("\n \nAt the Imperial Court:\n");

            string tier = FeudalTitlesBehavior.Instance?.GetTier(hero);
            if (!string.IsNullOrEmpty(tier)) sb.Append($"  Feudal standing: {tier}.\n");

            if (MansabdariBehavior.Instance != null)
            {
                string title = MansabdariBehavior.Instance.GetTitle(hero.Clan);
                int zat = MansabdariBehavior.Instance.GetMansab(hero.Clan);
                sb.Append($"  Mansab: {title} (zat {zat}).\n");
            }

            string post = CouncilBehavior.Instance?.GetPostOf(hero);
            if (!string.IsNullOrEmpty(post)) sb.Append($"  Council office: {post}.\n");

            Hero liege = FeudalTitlesBehavior.Instance?.GetFeudalLiege(hero);
            if (liege != null) sb.Append($"  Liege: {liege.Name}.\n");

            var seats = hero.Clan.Settlements?.Where(s => s.IsTown || s.IsCastle).ToList();
            if (seats != null && seats.Count > 0)
                sb.Append($"  Towns & castles held: {string.Join(", ", seats.Select(s => s.Name))}.\n");
            else
                sb.Append("  Holds no town or castle.\n");

            // Villages are held beneath this lord by their zamindars — name them, so the
            // lord is never confused with the village lord.
            var villages = hero.Clan.Settlements?.Where(s => s.IsVillage).ToList();
            if (villages != null && villages.Count > 0)
            {
                var ft = FeudalTitlesBehavior.Instance;
                var parts = villages.Select(v =>
                {
                    Hero z = ft?.GetVillageLord(v);
                    return z != null ? $"{v.Name} (zamindar {z.Name})" : $"{v.Name} (zamindar vacant)";
                });
                sb.Append($"  Villages under his zamindars: {string.Join(", ", parts)}.\n");
            }

            return sb.ToString();
        }

        private static string BuildZamindarSection(Hero hero)
        {
            var ft = FeudalTitlesBehavior.Instance;
            if (ft == null) return "";
            var villages = ft.GetVillagesLordedBy(hero);
            if (villages.Count == 0) return "";

            var sb = new StringBuilder();
            sb.Append("\n \nIn the Feudal Order:\n");
            sb.Append("  Feudal standing: Village Zamindar.\n");
            sb.Append($"  Holds the village{(villages.Count > 1 ? "s" : "")} of {string.Join(", ", villages.Select(v => v.Name))} in zamindari.\n");
            int levy = villages.Sum(v => ft.GetLevySize(v));
            sb.Append($"  Commands a levy of about {levy} men.\n");
            Hero liege = ft.GetFeudalLiege(hero);
            if (liege != null) sb.Append($"  Liege: {liege.Name}.\n");
            return sb.ToString();
        }
    }
}
