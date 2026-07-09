using System;

namespace TakhtyaTaboot.Util
{
    // Pure math for the nazrana gift cycle (wiki Ch.17 §2): the ceremonial gift a
    // vassal owes his sovereign, distinct from war indemnities and tributary payments.
    // NO TaleWorlds types — linked into TheHindostanMod.Tests (NazranaMathTests).
    public static class NazranaMath
    {
        public enum Tier { Minimal, Expected, Lavish }

        // Expected gift by mansab rank index (0 = unranked owes nothing), before scale.
        private static readonly int[] BaseAmounts = { 0, 200, 600, 1400, 3000, 6000, 12000 };

        public const int DeadlineDays = 14;
        public const int MissesBeforeDemotion = 3;

        public static int BaseAmount(int rankIndex, float scale = 1f)
        {
            if (rankIndex < 0) rankIndex = 0;
            if (rankIndex >= BaseAmounts.Length) rankIndex = BaseAmounts.Length - 1;
            return (int)Math.Round(BaseAmounts[rankIndex] * Math.Max(0f, scale));
        }

        public static int TierAmount(int rankIndex, Tier tier, float scale = 1f)
        {
            int b = BaseAmount(rankIndex, scale);
            return tier == Tier.Minimal ? (int)Math.Round(b * 0.2f)
                 : tier == Tier.Lavish ? b * 2
                 : b;
        }

        // (relation with the sovereign, clan influence) for presenting a gift of a tier.
        public static (int relation, int influence) TierEffects(Tier tier)
            => tier == Tier.Minimal ? (-2, 0)
             : tier == Tier.Lavish ? (6, 15)
             : (2, 5);

        // Penalty for letting the deadline lapse.
        public static (int relation, int influence) MissedEffects() => (-5, -5);

        // What an AI clan pays its ruler weekly (a trickle of the ceremonial cycle).
        // Lords who despise their ruler withhold entirely.
        public static int WeeklyAiPayment(int rankIndex, int relationWithRuler, float scale = 1f)
            => relationWithRuler < -20 ? 0 : (int)Math.Round(BaseAmount(rankIndex, scale) * 0.1f);

        public static bool AiWithholds(int relationWithRuler) => relationWithRuler < -20;
    }
}
