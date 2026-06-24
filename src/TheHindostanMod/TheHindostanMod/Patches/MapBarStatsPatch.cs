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
    // The mod's empire stats (Authority, Legitimacy, Mansab/Zat, Valour, Unrest) on the campaign
    // info bar. They go in the bar's SECONDARY (lower) row — NOT the primary row — so the vanilla
    // top row keeps its width, and the mod stats stack below it. The vanilla "extend" arrow on the
    // info bar collapses/expands that lower row, which is exactly the toggle we want (collapsed =
    // vanilla view only; expanded = full view with our stats). Each stat carries a distinct icon.
    //
    // (The icons are drawn from the vanilla MapBar.Right.Icons brush, which only has gameplay-stat
    // glyphs — bespoke art, e.g. a crown for legitimacy, would need custom sprite assets and is a
    // separate step; these are the closest-fitting distinct vanilla icons.)
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
                    if (!__instance.SecondaryInfoItems.Contains(it))
                        __instance.SecondaryInfoItems.Add(it);
                foreach (MapInfoItemVM it in mine)
                    UpdateItem(it);
            }
            catch { /* never break the map bar */ }
        }

        private static MapInfoItemVM[] Create()
        {
            return new[]
            {
                Make("hind_authority",  "influence"),       // imperial writ / power
                Make("hind_legitimacy", "morale"),          // acceptance of the throne
                Make("hind_mansab",     "troops"),          // mansab is a troop-rank
                Make("hind_valour",     "hit_points"),      // battlefield prowess
                Make("hind_unrest",     "hit_points_sick"), // the realm's sickness
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
                case "hit_points": return "hind_valour";
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
                case "hind_valour":
                    if (MansabdariBehavior.Instance == null || Clan.PlayerClan == null) return ("—", false);
                    return ($"{MansabdariBehavior.Instance.GetValour(Clan.PlayerClan):0}", false);
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
                case "hind_valour":     title = "Valour"; desc = "Battlefield merit earned in war and by your own kills. Spent to rise in mansab."; break;
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
