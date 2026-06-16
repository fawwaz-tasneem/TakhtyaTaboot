using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace TakhtyaTaboot
{
    // Tax effect: when imperial authority is low, governors withhold revenue, so a
    // clan's income falls. Factor applied to the vanilla income calculation.
    [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanIncome")]
    public static class AuthorityTaxPatch
    {
        static void Postfix(Clan clan, ref ExplainedNumber __result)
        {
            var kingdom = clan?.Kingdom;
            if (kingdom == null) return;
            float rate = ImperialAuthorityBehavior.Instance?.GetTaxCollectionRate(kingdom) ?? 1f;
            if (rate < 0.999f)
                __result.AddFactor(rate - 1f, new TextObject("{=!}Imperial authority weakened"));
        }
    }

    // Call-to-arms effect: an army serving a ruler with weak authority/legitimacy
    // loses cohesion faster — lords drift home rather than answer a fading writ.
    [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), "CalculateDailyCohesionChange")]
    public static class AuthorityCohesionPatch
    {
        static void Postfix(Army army, ref ExplainedNumber __result)
        {
            var kingdom = army?.Kingdom;
            if (kingdom == null) return;

            float compliance = ImperialAuthorityBehavior.Instance?.GetCallToArmsCompliance(kingdom) ?? 0.9f;
            float legitMod = LegitimacyBehavior.Instance?.GetCallToArmsModifier(kingdom.Leader) ?? 1f;
            // Effective answer-rate from both meters (0..~1).
            float answer = compliance * legitMod;
            if (answer < 0.9f)
                __result.Add(-(0.9f - answer) * 10f, new TextObject("{=!}Weak imperial authority"));
        }
    }
}
