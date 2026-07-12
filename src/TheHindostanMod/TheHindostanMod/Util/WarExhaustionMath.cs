using System;

namespace TakhtyaTaboot.Util
{
    // War exhaustion, PURE and unit-tested — the Diplomacy-mod mechanic rebuilt natively so it
    // respects this mod's own war rules. Each side of each war carries an exhaustion score
    // (0..100) fed by blood and loss: casualties, fiefs lost, villages raided, and the slow
    // grind of time. An exhausted AI realm sues for peace; an exhausted PLAYER-ruled realm is
    // never forced (the sovereign dictates his own peace through the terms menu) but bleeds
    // authority while he ignores it. Exhaustion decays slowly in peacetime, so back-to-back
    // wars begin already weary.
    //
    // INTEGRATION RULE (the reason this is native): wars involving the mod's claim kingdoms
    // (hind_rebel_*: accession wars, AI civil wars, secession wars) are NEVER tracked and
    // never peace out by exhaustion — those are binary, settled by their own deadlines
    // (ThroneWar/NoThroneWarPeacePatch). WarExhaustionBehavior owns the engine side.
    public static class WarExhaustionMath
    {
        public const float Cap = 100f;
        public const float AdvisoryThreshold = 60f;   // the council urges peace
        public const float CriticalThreshold = 100f;  // an AI realm sues for peace

        // ── Accrual ──────────────────────────────────────────────────────────────────
        public const float DailyCreep = 0.3f;          // the grind of a war simply running
        public const float PerCasualty = 0.03f;        // ~33 dead = 1 point
        public const float PerFiefLost = 8f;           // a town or castle falls
        public const float PerVillageRaided = 3f;
        public const float PeaceDecayPerDay = 1.5f;    // weariness fades once the war ends

        // Smaller realms feel each loss more: the same 300 dead exhaust a 1000-strength realm
        // about twice as hard as a 3000-strength one. Scale is clamped so no realm is immune.
        public static float StrengthScale(float totalStrength)
        {
            float s = 2000f / Math.Max(500f, totalStrength);
            return s < 0.5f ? 0.5f : s > 2f ? 2f : s;
        }

        public static float Accrue(float current, float points, float strengthScale)
            => Clamp(current + Math.Max(0f, points) * Math.Max(0f, strengthScale));

        public static float DecayInPeace(float current)
            => Clamp(current - PeaceDecayPerDay);

        // ── Reads ────────────────────────────────────────────────────────────────────
        public static bool CouncilUrgesPeace(float exhaustion) => exhaustion >= AdvisoryThreshold;
        public static bool SuesForPeace(float exhaustion) => exhaustion >= CriticalThreshold;

        public static string Tier(float e)
            => e >= CriticalThreshold ? "spent"
             : e >= 80f ? "reeling"
             : e >= AdvisoryThreshold ? "weary"
             : e >= 30f ? "strained"
             : "fresh";

        private static float Clamp(float v) => v < 0f ? 0f : v > Cap ? Cap : v;
    }
}
