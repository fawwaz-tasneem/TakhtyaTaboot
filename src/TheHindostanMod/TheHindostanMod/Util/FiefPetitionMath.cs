using System;

namespace TakhtyaTaboot.Util
{
    // The arithmetic of petitioning the court for a fief, PURE and unit-tested. "Claim your
    // due" is no longer an instant grant for a flat influence fee: the player files a petition
    // with a gold gift (nazrana) and an influence stake, and the court weighs it week by week.
    // A lavish, well-backed petition from a favoured servant is granted quickly; a meagre one
    // waits; and below a floor of the sovereign's regard the court refuses outright.
    // FiefPetitionBehavior owns the engine side; CareerProgressionBehavior does the granting.
    public static class FiefPetitionMath
    {
        // Below this effective regard from the sovereign, the court will not hear the petition
        // at all — no gift buys a fief from a lord who despises you.
        public const float RelationFloor = -10f;

        public static bool BelowRelationFloor(float sovereignRegard) => sovereignRegard < RelationFloor;

        // Weekly chance the court grants the petition WHEN a qualifying fief is available. Rises
        // with the gift, the influence staked, and the sovereign's regard — bounded so even a
        // lavish suit takes a little time and a thin one keeps a slim hope.
        public static float ApprovalChancePerWeek(int goldGift, int influenceStake, float sovereignRegard)
        {
            float giftTerm = Clamp(goldGift / 50000f, 0f, 0.40f);            // up to +0.40 at 50k
            float infTerm = Clamp(influenceStake / 200f, 0f, 0.30f);         // up to +0.30 at 200
            float regardTerm = Clamp(sovereignRegard / 200f, -0.20f, 0.30f); // regard tips it either way
            return Clamp(0.10f + giftTerm + infTerm + regardTerm, 0.03f, 0.95f);
        }

        public static bool CourtGrants(int goldGift, int influenceStake, float sovereignRegard, float rng01)
            => rng01 < ApprovalChancePerWeek(goldGift, influenceStake, sovereignRegard);

        // What the court expects for each fief tier (0 village, 1 castle, 2 town) — a greater
        // honour asks a greater gift and a heavier stake. These are the "handsome" baseline; the
        // behaviour offers a modest (cheaper, slower) and a lavish (dearer, faster) tier around it.
        public static int TierGiftBase(int tier) => tier <= 0 ? 2000 : tier == 1 ? 8000 : 20000;
        public static int TierInfluenceBase(int tier) => tier <= 0 ? 25 : tier == 1 ? 60 : 120;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
