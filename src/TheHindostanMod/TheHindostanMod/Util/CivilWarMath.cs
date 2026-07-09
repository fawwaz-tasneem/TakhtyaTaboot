using System;

namespace TakhtyaTaboot.Util
{
    // Pure math for AI leadership challenges (wiki Ch.16): when a great amir turns on
    // his ruler, how likely the bid is, and what it costs him in deserting troops.
    // NO TaleWorlds types — linked into TheHindostanMod.Tests (CivilWarMathTests).
    public static class CivilWarMath
    {
        public const int RelationTrigger = -30;     // hatred alone can drive a bid
        public const float LegitimacyTrigger = 30f; // a shaky throne invites one
        public const float StrengthRatioFloor = 0.8f;
        public const float MonthlyBidChance = 0.25f;
        public const int WarDeadlineDays = 45;

        // Preconditions: a challenger must either hate the ruler outright, or see a
        // weak throne he is strong enough to take (strengthRatio = his clan / ruling clan).
        public static bool Qualifies(int relationWithRuler, float rulerLegitimacy, float strengthRatio)
            => relationWithRuler < RelationTrigger
               || (rulerLegitimacy < LegitimacyTrigger && strengthRatio > StrengthRatioFloor);

        // Even a qualified amir does not rise every month; roll in [0,1).
        public static bool BidFires(int relationWithRuler, float rulerLegitimacy, float strengthRatio, double roll)
            => Qualifies(relationWithRuler, rulerLegitimacy, strengthRatio) && roll < MonthlyBidChance;

        // Challenging UP the ladder bleeds men: soldiers desert a lord who turns on a
        // superior. Fraction of troops lost at the challenge's outset, capped at 40%.
        public static float DesertionRate(int challengerRank, int targetRank)
        {
            int steps = Math.Max(0, targetRank - challengerRank);
            return Math.Min(0.4f, 0.10f + 0.05f * steps);
        }

        // A season of war resolved by weight of arms, each side's fortune rolling ±20%.
        public static bool RebelWins(float rebelStrength, float loyalStrength, double rebelRoll, double loyalRoll)
            => rebelStrength * (0.8f + 0.4f * (float)rebelRoll)
               >= loyalStrength * (0.8f + 0.4f * (float)loyalRoll);
    }
}
