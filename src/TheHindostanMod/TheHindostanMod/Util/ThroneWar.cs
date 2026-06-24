using TaleWorlds.CampaignSystem;

namespace TakhtyaTaboot.Util
{
    // A war for the throne is binary: the pretender wins the crown or is destroyed — there is no white
    // peace, no quiet partition, no separate kingdom that simply walks away. The mod's succession
    // breakaways are real Kingdoms whose StringId is stamped "hind_rebel_" at creation
    // (RevoltCascadeBehavior.CreateRebelKingdom), so that prefix is the persistent marker we key off.
    //
    // The earlier approach reversed any treaty AFTER the engine concluded it; that was reactive and leaky
    // (the option still showed, AI re-proposed, some paths slipped through). Instead NoThroneWarPeacePatch
    // blocks peace at the single chokepoint (MakePeaceAction) so the option simply never takes effect.
    // The mod's OWN resolution code (folding a defeated rebel realm back into the throne) still needs to
    // settle that war legitimately, so it brackets its peace calls with AllowInternalPeace.
    public static class ThroneWar
    {
        // Set true (briefly, on the calling thread) by the mod's resolution code when it legitimately needs
        // to conclude peace with a rebel realm it is dissolving. The patch honours this and lets it through.
        [System.ThreadStatic] public static bool AllowInternalPeace;

        public static bool IsRebelKingdom(IFaction f)
            => f is Kingdom k && k.StringId != null && k.StringId.StartsWith("hind_rebel_");

        // Run an action with internal peace permitted, then restore the flag.
        public static void WithInternalPeace(System.Action act)
        {
            bool prev = AllowInternalPeace;
            AllowInternalPeace = true;
            try { act(); }
            finally { AllowInternalPeace = prev; }
        }
    }
}
