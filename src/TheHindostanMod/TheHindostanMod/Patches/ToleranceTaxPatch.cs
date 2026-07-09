using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace TakhtyaTaboot
{
    // Income effect of the realm's religious policy (same shape as AuthorityTaxPatch):
    // strict orthodoxy squeezes other-faith lords, tolerance tithes everyone lightly,
    // and an enacted jizya swells the RULING house's registers.
    // Numbers in Util.ToleranceMath (unit-tested).
    [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanIncome")]
    public static class ToleranceTaxPatch
    {
        static void Postfix(Clan clan, ref ExplainedNumber __result)
        {
            var kingdom = clan?.Kingdom;
            var tolerance = ReligiousToleranceBehavior.Instance;
            if (kingdom == null || tolerance == null) return;

            var stance = tolerance.GetStance(kingdom);
            bool matches = tolerance.ClanFaithMatchesRuler(clan);
            bool jizya = tolerance.JizyaEnacted(kingdom);

            float factor = Util.ToleranceMath.IncomeFactor(stance, matches, jizya);
            if (factor < 0.999f)
                __result.AddFactor(factor - 1f, new TextObject("{=!}The realm's faith policy"));

            if (jizya && clan == kingdom.RulingClan)
                __result.AddFactor(Util.ToleranceMath.JizyaIncomeFactor, new TextObject("{=!}The jizya"));
        }
    }
}
