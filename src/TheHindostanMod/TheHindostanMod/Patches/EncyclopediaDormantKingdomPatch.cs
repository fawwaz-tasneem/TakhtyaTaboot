using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encyclopedia.Pages;

namespace TakhtyaTaboot
{
    // While the empire stands whole (UnifiedEmpireBehavior), Bengal and Hyderabad exist only as
    // dormant kingdom shells — alive so the breakaway can repopulate them, but holding no clans
    // and no land. The vanilla Kingdoms encyclopedia lists every non-bandit faction, so the
    // shells showed up as "kingdoms" on day one and made the unified start look broken. Hide
    // any dormant realm from the encyclopedia list; it reappears the moment it holds clans again.
    [HarmonyPatch(typeof(DefaultEncyclopediaFactionPage), nameof(DefaultEncyclopediaFactionPage.IsValidEncyclopediaItem))]
    internal static class EncyclopediaDormantKingdomPatch
    {
        private static void Postfix(object o, ref bool __result)
        {
            try
            {
                if (__result && o is Kingdom k && UnifiedEmpireBehavior.IsDormant(k))
                    __result = false;
            }
            catch (Exception e) { Util.TYTLog.Error("EncyclopediaDormantKingdomPatch failed", e); }
        }
    }
}
