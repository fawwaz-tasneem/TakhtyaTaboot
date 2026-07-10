using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TakhtyaTaboot
{
    // A war for the throne is won or lost — never settled by treaty. Every path to white peace (the AI's
    // MakePeaceKingdomDecision, the player's truce barter, a diplomacy mod, the engine) funnels through
    // MakePeaceAction.Apply, so blocking it here forbids peace from ALL of them at once, rather than
    // reversing a treaty after the fact (which left the option visible and leaked through odd paths).
    //
    // Only succession-rebellion kingdoms (StringId "hind_rebel_*") are affected, and only when the mod's
    // own resolution code is NOT the caller (Util.ThroneWar.AllowInternalPeace) — so folding a beaten
    // rebel realm back into the throne still settles cleanly. The breakaway therefore has exactly two
    // exits: take the crown, or be destroyed.
    //
    // v1.3.11 removed the 4-arg Apply overload; every public entry (Apply, ApplyByKingdomDecision)
    // now funnels through the private ApplyInternal, so that is the single choke point to block.
    [HarmonyPatch(typeof(MakePeaceAction), "ApplyInternal",
        new[] { typeof(IFaction), typeof(IFaction), typeof(int), typeof(int), typeof(MakePeaceAction.MakePeaceDetail) })]
    internal static class NoThroneWarPeacePatch
    {
        private static bool Prefix(IFaction faction1, IFaction faction2)
        {
            try
            {
                if (Util.ThroneWar.AllowInternalPeace) return true;
                if (!Util.ThroneWar.IsRebelKingdom(faction1) && !Util.ThroneWar.IsRebelKingdom(faction2))
                    return true;

                // The player is owed an explanation when their own truce is refused by the nature of the war.
                IFaction pf = Hero.MainHero?.MapFaction;
                if (pf != null && (pf == faction1 || pf == faction2))
                    InformationManager.DisplayMessage(new InformationMessage(
                        "A war for the throne cannot be ended by treaty — only by victory in the field.",
                        Color.FromUint(0xFFCC4400)));

                return false; // skip MakePeaceAction.Apply: the war goes on
            }
            catch (Exception e) { Util.TYTLog.Error("NoThroneWarPeacePatch failed", e); return true; }
        }
    }
}
