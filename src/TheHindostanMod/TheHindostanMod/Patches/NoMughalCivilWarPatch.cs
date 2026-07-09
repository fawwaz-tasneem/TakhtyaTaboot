using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;

namespace TakhtyaTaboot
{
    // The three Mughal-successor realms (the Empire, Bengal, Hyderabad) are kin and must never
    // go to war with one another. Every public DeclareWarAction.ApplyBy* entry point funnels
    // through the private ApplyInternal, so blocking it here forbids a Mughal-vs-Mughal war from
    // ALL of them at once — AI kingdom decisions, barters, the engine, even the player — rather
    // than undoing a war after the fact (which let it flicker for up to a week before the weekly
    // FactionRelationsBehavior sweep caught it).
    //
    // Only the three ORIGINAL realms are protected (FactionRelationsBehavior.IsMughalKingdom keys
    // off their StringIds). A succession breakaway is a separate "hind_rebel_*" kingdom, so an
    // accession war against the Empire is NOT both-Mughal and still plays out normally.
    [HarmonyPatch(typeof(DeclareWarAction), "ApplyInternal",
        new[] { typeof(IFaction), typeof(IFaction), typeof(DeclareWarAction.DeclareWarDetail) })]
    internal static class NoMughalCivilWarPatch
    {
        private static bool Prefix(IFaction faction1, IFaction faction2)
        {
            try
            {
                if (faction1 == faction2) return true;
                if (!FactionRelationsBehavior.IsMughalKingdom(faction1)
                    || !FactionRelationsBehavior.IsMughalKingdom(faction2))
                    return true; // not a war between two Mughal realms — let it proceed

                // If the player's own realm is a party, explain why the war won't take.
                IFaction pf = Hero.MainHero?.MapFaction;
                if (pf != null && (pf == faction1 || pf == faction2))
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The Mughal successor realms are kin — they do not make war upon one another.",
                        Color.FromUint(0xFFCC4400)));

                return false; // skip ApplyInternal: no war is declared
            }
            catch (Exception e) { Util.TYTLog.Error("NoMughalCivilWarPatch failed", e); return true; }
        }
    }
}
