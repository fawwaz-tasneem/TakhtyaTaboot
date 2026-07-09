using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace TakhtyaTaboot
{
    // The monsoon (wiki Ch.05/17 §1): the rains mire armies on the march (x0.70), the
    // bright weeks after them are the marching season (x1.10). A Harmony postfix on the
    // vanilla speed model — deliberately NOT the mod's first GameModel override, matching
    // how AuthorityEffectPatches adjusts sibling models. Season-only in v1 (no terrain).
    // Multipliers in Util.SeasonMath (unit-tested). Gated by the MCM "Monsoon" toggle.
    [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), "CalculateFinalSpeed")]
    public static class MonsoonSpeedPatch
    {
        static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
        {
            try
            {
                if (!Config.Tune.MonsoonEnabled || Campaign.Current == null) return;
                int season = (int)CampaignTime.Now.GetSeasonOfYear;
                float mult = Util.SeasonMath.MoveSpeedMultiplier(season);
                if (Math.Abs(mult - 1f) < 0.001f) return;
                __result.AddFactor(mult - 1f, new TextObject("{=!}" + Util.SeasonMath.SpeedExplanation(season)));
            }
            catch (Exception e) { Util.TYTLog.Error("MonsoonSpeedPatch failed", e); }
        }
    }
}
