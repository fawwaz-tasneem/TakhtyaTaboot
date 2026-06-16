using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core.ViewModelCollection.Information;

namespace TakhtyaTaboot
{
    // Injects the mod's empire stats (Authority, Legitimacy, Mansab, Unrest) into the
    // vanilla bottom info bar, rendered exactly like gold/influence — same icons,
    // warning colours and hover tooltips. Done by appending MapInfoItemVM entries to
    // PrimaryInfoItems after the game rebuilds them each refresh.
    [HarmonyPatch(typeof(MapInfoVM), "RefreshValues")]
    internal static class MapBarStatsPatch
    {
        private static readonly ConditionalWeakTable<MapInfoVM, MapInfoItemVM[]> _items =
            new ConditionalWeakTable<MapInfoVM, MapInfoItemVM[]>();

        private static void Postfix(MapInfoVM __instance)
        {
            try
            {
                if (Campaign.Current == null || Hero.MainHero == null) return;
                MapInfoItemVM[] mine = _items.GetValue(__instance, _ => Create());
                foreach (MapInfoItemVM it in mine)
                    if (!__instance.PrimaryInfoItems.Contains(it))
                        __instance.PrimaryInfoItems.Add(it);
                foreach (MapInfoItemVM it in mine)
                    UpdateItem(it);
            }
            catch { /* never break the map bar */ }
        }

        private static MapInfoItemVM[] Create()
        {
            return new[]
            {
                Make("hind_authority",  "influence"),
                Make("hind_legitimacy", "morale"),
                Make("hind_mansab",     "troops"),
                Make("hind_unrest",     "hit_points_sick"),
            };
        }

        private static MapInfoItemVM Make(string id, string icon)
        {
            var item = new MapInfoItemVM(id, () => Tooltip(id));
            item.VisualId = icon;
            return item;
        }

        private static void UpdateItem(MapInfoItemVM item)
        {
            var (value, warn) = Compute(GetId(item));
            item.Value = value;
            item.HasWarning = warn;
        }

        // MapInfoItemVM has no public id getter, so map by reference identity is awkward;
        // instead recompute from the icon we assigned (unique per stat).
        private static string GetId(MapInfoItemVM item)
        {
            switch (item.VisualId)
            {
                case "influence": return "hind_authority";
                case "morale": return "hind_legitimacy";
                case "troops": return "hind_mansab";
                default: return "hind_unrest";
            }
        }

        private static (string value, bool warn) Compute(string id)
        {
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            switch (id)
            {
                case "hind_authority":
                    if (k == null || ImperialAuthorityBehavior.Instance == null) return ("—", false);
                    float a = ImperialAuthorityBehavior.Instance.GetAuthority(k);
                    return ($"{a:0}", a < 40f);
                case "hind_legitimacy":
                    if (k?.Leader == null || LegitimacyBehavior.Instance == null) return ("—", false);
                    float l = LegitimacyBehavior.Instance.GetLegitimacy(k.Leader);
                    return ($"{l:0}", l < 40f);
                case "hind_mansab":
                    if (MansabdariBehavior.Instance == null || Clan.PlayerClan == null) return ("—", false);
                    return ($"{MansabdariBehavior.Instance.GetMansab(Clan.PlayerClan)}", false);
                default: // unrest
                    float peak = 0f;
                    if (k != null && RevoltCascadeBehavior.Instance != null)
                        foreach (Settlement s in k.Settlements)
                        {
                            float p = RevoltCascadeBehavior.Instance.GetPressure(s);
                            if (p > peak) peak = p;
                        }
                    return ($"{peak:0}", peak >= 50f);
            }
        }

        private static List<TooltipProperty> Tooltip(string id)
        {
            var (value, _) = Compute(id);
            string title, desc;
            switch (id)
            {
                case "hind_authority":  title = "Imperial Authority"; desc = "How far the emperor's writ runs (0-100). Drives tax and obedience."; break;
                case "hind_legitimacy": title = "Legitimacy"; desc = "How widely the ruler's right to the throne is accepted (0-100)."; break;
                case "hind_mansab":     title = "Mansab (Zat)"; desc = "Your rank at court. Higher zat unlocks greater fiefs and command."; break;
                default:                title = "Realm Unrest"; desc = "The peak revolt pressure in your realm. Above 80 a province may rise."; break;
            }
            return new List<TooltipProperty>
            {
                new TooltipProperty(title, value, 0, false, TooltipProperty.TooltipPropertyFlags.Title),
                new TooltipProperty("", desc, 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine),
                new TooltipProperty("", "See Encyclopedia > Concepts > The Empire.", 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine),
            };
        }
    }
}
