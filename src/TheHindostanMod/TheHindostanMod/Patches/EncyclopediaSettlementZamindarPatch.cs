using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;

namespace TakhtyaTaboot
{
    // The engine's settlement page names only the ENGINE owner (for most villages, the
    // town lord's clan leader — "Asaf Jah I"), which hides the whole zamindari layer.
    // Append the actual holder to the village's information paragraph so the man who
    // governs, taxes and answers for the village is named on his village's page.
    [HarmonyPatch(typeof(EncyclopediaSettlementPageVM), "RefreshValues")]
    public static class EncyclopediaSettlementZamindarPatch
    {
        static void Postfix(EncyclopediaSettlementPageVM __instance)
        {
            try
            {
                var s = AccessTools.Field(typeof(EncyclopediaSettlementPageVM), "_settlement")
                            ?.GetValue(__instance) as Settlement;
                if (s == null || !s.IsVillage) return;

                Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
                if (z == null) return;

                string line = z == s.OwnerClan?.Leader
                    ? $"{z.Name} holds the village in direct zamindari."
                    : $"The village is held in zamindari by {z.Name}, who governs and taxes it under {s.OwnerClan?.Leader?.Name.ToString() ?? "the crown"}.";

                string info = __instance.InformationText ?? "";
                if (!info.Contains("zamindari")) // idempotent across refreshes
                    __instance.InformationText = string.IsNullOrEmpty(info) ? line : info + " " + line;
            }
            catch (Exception e) { Util.TYTLog.Error("EncyclopediaSettlementZamindarPatch failed", e); }
        }
    }
}
