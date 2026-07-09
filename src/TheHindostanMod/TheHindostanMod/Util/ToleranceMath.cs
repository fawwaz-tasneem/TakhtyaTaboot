using System;

namespace TakhtyaTaboot.Util
{
    // Pure math for the realm's religious policy (wiki Ch.17 §6). Stances follow the
    // arc of Mughal history: Sulh-e-Kul tolerance, the middle road, and strict
    // orthodoxy with the jizya. NO TaleWorlds types — linked into TheHindostanMod.Tests.
    public static class ToleranceMath
    {
        // Serialized as ints in SyncData: order must never change.
        public enum Stance { Strict = 0, Moderate = 1, Tolerant = 2 }

        // Daily loyalty drift for a town whose populace follows a DIFFERENT faith than
        // its realm's ruler. Same-faith towns are untouched (0).
        public static float LoyaltyDriftPerDay(Stance stance, bool faithMatchesRuler)
        {
            if (faithMatchesRuler) return 0f;
            switch (stance)
            {
                case Stance.Strict: return -2f;
                case Stance.Tolerant: return +1f;
                default: return -0.5f;
            }
        }

        // Clan income factor by the realm's stance and whether the clan's own faith
        // matches the ruler's: orthodoxy squeezes the other faith's estates, tolerance
        // trades a small tithe for peace.
        public static float IncomeFactor(Stance stance, bool clanFaithMatchesRuler, bool jizyaEnacted)
        {
            // (jizyaEnacted deliberately unused here: the jizya pays the RULING clan a bonus
            // in ToleranceTaxPatch rather than squeezing other clans' estates twice.)
            float f = 1f;
            if (stance == Stance.Strict && !clanFaithMatchesRuler) f -= 0.10f;
            if (stance == Stance.Tolerant) f -= 0.03f; // the cost of even-handed courts
            return Math.Max(0.5f, f);
        }

        // The jizya: extra revenue for the crown, at a steady political price.
        public const float JizyaIncomeFactor = 0.15f;      // +15% ruler clan income
        public const float JizyaWeeklyAuthorityCost = 0.5f;
        public const float JizyaEnactLegitimacyCost = 2f;
        public const int JizyaEnactRelationHit = -15;      // with each other-faith clan leader

        // One-time relation shift with each clan leader of the OTHER faith when the
        // stance changes (softening earns goodwill, hardening earns enmity).
        public static int StanceChangeRelationShift(Stance from, Stance to)
        {
            int softness(Stance s) => s == Stance.Strict ? 0 : s == Stance.Moderate ? 1 : 2;
            int delta = softness(to) - softness(from);
            return delta * 10;
        }

        public static string StanceName(Stance s)
            => s == Stance.Strict ? "Mulk-e-Sharia (strict orthodoxy)"
             : s == Stance.Tolerant ? "Sulh-e-Kul (universal peace)"
             : "The Middle Road";
    }
}
