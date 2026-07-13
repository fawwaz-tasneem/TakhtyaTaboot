using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;

namespace TakhtyaTaboot
{
    // THE ONE GATE ON WAR (wiki ch.30 §6). Every public DeclareWarAction.ApplyBy* funnels through the
    // private ApplyInternal, so this single prefix forbids a war from ALL of them at once — the AI's
    // kingdom decisions, barters, the engine, the player.
    //
    // It replaces NoMughalCivilWarPatch, which prefixed the SAME method. Two prefixes on one method work,
    // but their ordering is incidental rather than declared, and a reader has to find both to know what
    // can actually start a war. One gate, two rules, in a stated order:
    //
    //   1. THE TRUCE. A dictated peace is a real bar, not a suggestion. While it stands, neither realm
    //      may declare on the other — which is what makes "conclude the war on our terms" mean anything.
    //      Throne wars (hind_rebel_*) are never bound: a claim war is fought to its end.
    //   2. MUGHAL KINSHIP. The three Mughal-successor realms (the Empire, Bengal, Hyderabad) are kin and
    //      never make war on one another. Only the three ORIGINAL realms; a breakaway is a separate
    //      hind_rebel_* kingdom, so an accession war against the Empire still plays out normally.
    [HarmonyPatch(typeof(DeclareWarAction), "ApplyInternal",
        new[] { typeof(IFaction), typeof(IFaction), typeof(DeclareWarAction.DeclareWarDetail) })]
    internal static class WarDeclarationGate
    {
        private static bool Prefix(IFaction faction1, IFaction faction2)
        {
            try
            {
                if (faction1 == null || faction2 == null || faction1 == faction2) return true;

                // ── Rule 1: a standing truce bars the declaration ────────────────────
                // A war for the throne is never bound by a treaty of any kind, truce included.
                bool throneWar = Util.ThroneWar.IsRebelKingdom(faction1) || Util.ThroneWar.IsRebelKingdom(faction2);
                if (!throneWar && (WarfareBehavior.Instance?.IsTruced(faction1, faction2) ?? false))
                {
                    Explain(faction1, faction2,
                        $"A truce stands between {faction1.Name} and {faction2.Name}. It cannot be broken while it holds.");
                    return false;
                }

                // ── Rule 2: the Mughal kin do not war upon one another ────────────────
                if (FactionRelationsBehavior.IsMughalKingdom(faction1)
                    && FactionRelationsBehavior.IsMughalKingdom(faction2))
                {
                    Explain(faction1, faction2,
                        "The Mughal successor realms are kin — they do not make war upon one another.");
                    return false;
                }

                return true;
            }
            catch (Exception e) { Util.TYTLog.Error("WarDeclarationGate failed", e); return true; }
        }

        // The player is owed a reason when a war he expected simply does not happen.
        private static void Explain(IFaction a, IFaction b, string why)
        {
            IFaction pf = Hero.MainHero?.MapFaction;
            if (pf == null || (pf != a && pf != b)) return;
            InformationManager.DisplayMessage(new InformationMessage(why, Color.FromUint(0xFFCC4400)));
        }
    }
}
