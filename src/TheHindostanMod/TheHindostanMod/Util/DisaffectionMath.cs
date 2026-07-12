using System;

namespace TakhtyaTaboot.Util
{
    // Disaffection conspiracies, PURE and unit-tested — the Diplomacy mod's secession and
    // abdication factions rebuilt natively on this mod's opinion ledger. Lords whose personal
    // regard for the sovereign has curdled band together; when the conspiracy is strong enough
    // and has simmered long enough, it serves an ULTIMATUM: abdicate in favour of the lawful
    // heir (when the quarrel is with the man) or let the malcontents go their own way (when
    // the quarrel is with the realm). Refusal means civil war through the mod's existing
    // claim-kingdom machinery. DisaffectionBehavior owns the engine side.
    public static class DisaffectionMath
    {
        // A lord is DISAFFECTED when his effective opinion of the sovereign falls this low.
        public const float DisaffectionThreshold = -15f;

        public static bool IsDisaffected(float effectiveOpinion) => effectiveOpinion <= DisaffectionThreshold;

        // A conspiracy FORMS when enough houses have curdled: at least MinClans of them, whose
        // combined strength is at least FormStrengthRatio of the loyalists'.
        public const int MinClans = 2;
        public const float FormStrengthRatio = 0.25f;

        public static bool ConspiracyForms(int disaffectedClans, float conspiratorStrength, float loyalistStrength)
            => disaffectedClans >= MinClans
               && conspiratorStrength >= Math.Max(1f, loyalistStrength) * FormStrengthRatio;

        // It serves its ultimatum when it has simmered SimmerDays and grown to UltimatumStrengthRatio.
        public const int SimmerDays = 21;
        public const float UltimatumStrengthRatio = 0.4f;

        public static bool UltimatumReady(int daysSimmered, float conspiratorStrength, float loyalistStrength)
            => daysSimmered >= SimmerDays
               && conspiratorStrength >= Math.Max(1f, loyalistStrength) * UltimatumStrengthRatio;

        // The demand: when the realm has a lawful heir to put forward and the ruler's own
        // legitimacy is low, the conspiracy demands ABDICATION (the quarrel is with the man);
        // otherwise it demands SECESSION (the quarrel is with the realm itself).
        public const float AbdicationLegitimacyCeiling = 55f;

        public static bool DemandsAbdication(bool lawfulHeirExists, float rulerLegitimacy)
            => lawfulHeirExists && rulerLegitimacy < AbdicationLegitimacyCeiling;

        // Whether an AI ruler YIELDS to the ultimatum rather than fight: he yields when the
        // conspiracy plainly outmatches his loyalists and his legitimacy gives him nothing to
        // stand on. A strong or legitimate ruler calls the bluff.
        public static bool AiRulerYields(float conspiratorStrength, float loyalistStrength, float legitimacy, float rng01)
        {
            float ratio = conspiratorStrength / Math.Max(1f, loyalistStrength);
            float yieldChance = Clamp01(0.5f * (ratio - 0.6f) + (50f - legitimacy) / 120f);
            return rng01 < yieldChance;
        }

        // A warned sovereign (the player betrays the plot) scatters the conspiracy: the
        // ringleader takes the fall, the rest slink back. Grudge magnitudes for the fallout.
        public const float ExposedGrudge = -15f;   // conspirators' grudge toward the informer
        public const float WarnedFavor = +10f;     // the sovereign's regard for the informer

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
