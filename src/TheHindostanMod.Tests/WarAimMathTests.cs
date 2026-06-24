using System.Linq;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the casus-belli / war-aim rules to the user's spec: total subjugation needs a collapsed
    // enemy throne; revenge wars end in reparation or judgement; imprisonment runs 2-5 years.
    public class WarAimMathTests
    {
        // ── Total subjugation gate (all three conditions required) ────────────────────
        [Fact]
        public void Subjugation_requires_collapsed_throne_low_legitimacy_and_a_loyal_bloc()
        {
            Assert.True(WarAimMath.SubjugationAllowed(losingKingCapturedOrKilled: true, losingKingLegitimacy: 59f, loyalLordFraction: 0.30f));
            Assert.False(WarAimMath.SubjugationAllowed(false, 59f, 0.30f)); // king neither captured nor killed
            Assert.False(WarAimMath.SubjugationAllowed(true, 60f, 0.30f));  // legitimacy not below 60
            Assert.False(WarAimMath.SubjugationAllowed(true, 59f, 0.29f));  // loyal bloc under 30%
        }

        [Theory]
        [InlineData(new[] { 20, 15, 11, 10, -5 }, 0.8f)]  // 4 of 5 at >= 10
        [InlineData(new[] { 9, 8, 7 }, 0f)]               // none qualify
        [InlineData(new int[0], 0f)]                      // empty -> 0
        public void Fraction_with_relation_at_least_counts_qualifying_lords(int[] rels, float expected)
            => Assert.Equal(expected, WarAimMath.FractionWithRelationAtLeast(rels, 10), 3);

        [Fact]
        public void Loyal_bloc_needs_at_least_thirty_percent_at_plus_ten()
        {
            Assert.True(WarAimMath.HasLoyalBloc(new[] { 10, 10, 10, -100, -100, -100, -100, -100, -100, -100 })); // 3/10 = 30%
            Assert.False(WarAimMath.HasLoyalBloc(new[] { 10, 10, -100, -100, -100, -100, -100, -100, -100, -100 })); // 2/10 = 20%
        }

        // ── Judgement of a surrendered culprit ────────────────────────────────────────
        [Theory]
        [InlineData(1, 2)]   // below floor -> 2
        [InlineData(2, 2)]
        [InlineData(4, 4)]
        [InlineData(5, 5)]
        [InlineData(9, 5)]   // above ceiling -> 5
        public void Imprisonment_is_clamped_to_two_through_five_years(int requested, int expected)
            => Assert.Equal(expected, WarAimMath.ClampImprisonYears(requested));

        [Fact]
        public void An_imprisoned_lords_clan_wanes_with_the_years_held()
        {
            Assert.Equal(1.0f, WarAimMath.ImprisonedClanStrengthFactor(0), 3);   // free / released
            Assert.Equal(0.5f, WarAimMath.ImprisonedClanStrengthFactor(5), 3);   // full term
            Assert.True(WarAimMath.ImprisonedClanStrengthFactor(2) > WarAimMath.ImprisonedClanStrengthFactor(4));
        }

        // ── War-aim gates which terms a victor may impose ─────────────────────────────
        [Fact]
        public void A_war_for_a_province_takes_only_that_province_not_the_realm()
        {
            Assert.True(WarAimMath.AllowsAnnexProvince(WarAim.ProvincialConquest));
            Assert.False(WarAimMath.AllowsAnnexAll(WarAim.ProvincialConquest));
            Assert.False(WarAimMath.AllowsTribute(WarAim.ProvincialConquest));
            Assert.False(WarAimMath.AllowsJudgement(WarAim.ProvincialConquest));
        }

        [Fact]
        public void Total_subjugation_may_annex_everything()
        {
            Assert.True(WarAimMath.AllowsAnnexAll(WarAim.TotalSubjugation));
            Assert.True(WarAimMath.AllowsAnnexProvince(WarAim.TotalSubjugation));
        }

        [Fact]
        public void Only_a_revenge_war_offers_a_judgement_of_the_culprit()
        {
            Assert.True(WarAimMath.AllowsJudgement(WarAim.Revenge));
            Assert.True(WarAimMath.AllowsTribute(WarAim.Revenge));
            var verdicts = WarAimMath.AvailableVerdicts(WarAim.Revenge).ToList();
            Assert.Contains(WarVerdict.Pardon, verdicts);
            Assert.Contains(WarVerdict.Fine, verdicts);
            Assert.Contains(WarVerdict.Imprison, verdicts);
            Assert.Contains(WarVerdict.Execute, verdicts);
            Assert.Empty(WarAimMath.AvailableVerdicts(WarAim.Tribute));
            Assert.Empty(WarAimMath.AvailableVerdicts(WarAim.ProvincialConquest));
        }

        [Fact]
        public void A_tribute_war_extracts_payment_not_land_or_blood()
        {
            Assert.True(WarAimMath.AllowsTribute(WarAim.Tribute));
            Assert.False(WarAimMath.AllowsAnnexProvince(WarAim.Tribute));
            Assert.False(WarAimMath.AllowsJudgement(WarAim.Tribute));
        }
    }
}
