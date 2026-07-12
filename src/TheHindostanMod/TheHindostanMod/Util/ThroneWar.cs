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

        // GRADUATED claim kingdoms: a secession that WON its independence keeps its hind_rebel_*
        // StringId (ids are immutable) but is a claim kingdom no longer — it makes peace and war
        // like any realm, the safety net stops vetoing its lifecycle, and it may hold a
        // coronation. DisaffectionBehavior persists this set across saves and reloads it here.
        private static readonly System.Collections.Generic.HashSet<string> _graduated =
            new System.Collections.Generic.HashSet<string>();

        public static bool IsRebelKingdom(IFaction f)
            => f is Kingdom k && k.StringId != null && k.StringId.StartsWith("hind_rebel_")
               && !_graduated.Contains(k.StringId);

        public static void Graduate(string kingdomId)
        { if (!string.IsNullOrEmpty(kingdomId)) _graduated.Add(kingdomId); }

        public static System.Collections.Generic.List<string> GraduatedIds
            => new System.Collections.Generic.List<string>(_graduated);

        public static void LoadGraduated(System.Collections.Generic.IEnumerable<string> ids)
        {
            _graduated.Clear();
            if (ids == null) return;
            foreach (string id in ids) if (!string.IsNullOrEmpty(id)) _graduated.Add(id);
        }

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
