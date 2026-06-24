using System.Collections.Generic;

namespace TakhtyaTaboot.Util
{
    // Why a war is fought (its casus belli) and what the victor may demand of the vanquished. A war's
    // AIM is fixed when it is declared and constrains how it can be resolved: a war for a province can
    // only take that province; a war of revenge ends in reparation or the surrender of the culprit; the
    // wholesale annexation of a realm is permissible only when the enemy throne has utterly collapsed.
    //
    // This is the PURE, engine-free core (unit-tested in TheHindostanMod.Tests). The behavior layer
    // raises trait-driven affronts, declares the war with an aim, and applies the terms proven here.
    public enum WarAim
    {
        ProvincialConquest, // annex the specific contested fief(s)
        Tribute,            // a one-off reparation and/or seasonal tribute
        Revenge,            // redress an affront: reparation OR surrender the culprit for judgement
        TotalSubjugation,   // absorb the ENTIRE realm — only when the enemy throne has collapsed
        Succession          // a war of princes; terms handled by the succession system
    }

    // The sentence a victor passes on a surrendered culprit (a war of revenge).
    public enum WarVerdict { Pardon, Fine, Imprison, Execute }

    public static class WarAimMath
    {
        public const int   MinImprisonYears            = 2;
        public const int   MaxImprisonYears            = 5;
        public const float SubjugationLegitimacyCeiling = 60f;
        public const float SubjugationLoyalLordFraction = 0.30f;
        public const int   SubjugationLoyalRelation     = 10;

        // Total subjugation (annexing ALL the loser's land) is allowed only when the enemy throne has
        // truly collapsed: the losing king is captured or dead, his legitimacy is below the ceiling, and
        // the victor commands the goodwill of a real bloc of the realm's lords.
        public static bool SubjugationAllowed(bool losingKingCapturedOrKilled, float losingKingLegitimacy,
                                              float loyalLordFraction)
            => losingKingCapturedOrKilled
               && losingKingLegitimacy < SubjugationLegitimacyCeiling
               && loyalLordFraction >= SubjugationLoyalLordFraction;

        // Fraction (0..1) of the given relations that meet or exceed a threshold.
        public static float FractionWithRelationAtLeast(IEnumerable<int> relations, int threshold)
        {
            if (relations == null) return 0f;
            int n = 0, hit = 0;
            foreach (int r in relations) { n++; if (r >= threshold) hit++; }
            return n == 0 ? 0f : (float)hit / n;
        }

        // The design's exact loyal-bloc test: >= 30% of the realm's lords at >= +10 relation with the victor.
        public static bool HasLoyalBloc(IEnumerable<int> lordRelationsToWinner)
            => FractionWithRelationAtLeast(lordRelationsToWinner, SubjugationLoyalRelation) >= SubjugationLoyalLordFraction;

        // A surrendered culprit may be imprisoned for 2..5 years.
        public static int ClampImprisonYears(int requested)
            => requested < MinImprisonYears ? MinImprisonYears
             : requested > MaxImprisonYears ? MaxImprisonYears : requested;

        // While its leader rots in prison a clan wanes: an effective-strength multiplier (1.0 free ..
        // 0.5 at the full five years) that recovers as the term elapses. The behavior applies this as
        // influence/recruitment drag rather than mutating raw party strength.
        public static float ImprisonedClanStrengthFactor(int yearsRemaining)
        {
            int y = yearsRemaining < 0 ? 0 : (yearsRemaining > MaxImprisonYears ? MaxImprisonYears : yearsRemaining);
            return 1f - 0.10f * y;
        }

        // What the victor may demand, gated by the war's aim.
        public static bool AllowsAnnexProvince(WarAim aim) => aim == WarAim.ProvincialConquest || aim == WarAim.TotalSubjugation;
        public static bool AllowsAnnexAll(WarAim aim)      => aim == WarAim.TotalSubjugation;
        public static bool AllowsTribute(WarAim aim)       => aim == WarAim.Tribute || aim == WarAim.Revenge;
        public static bool AllowsJudgement(WarAim aim)     => aim == WarAim.Revenge;

        public static IEnumerable<WarVerdict> AvailableVerdicts(WarAim aim)
            => aim == WarAim.Revenge
                ? new[] { WarVerdict.Pardon, WarVerdict.Fine, WarVerdict.Imprison, WarVerdict.Execute }
                : new WarVerdict[0];
    }
}
