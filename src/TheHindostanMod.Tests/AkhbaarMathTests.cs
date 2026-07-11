using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the harkara's arithmetic: famous lords are cheaper to trace than obscure ones,
    // foreign realms cost half again, the road takes two to twelve days, and the report
    // speaks in hearsay — rounded counts and worded composition, never exact rosters.
    public class AkhbaarMathTests
    {
        [Fact]
        public void An_obscure_lord_costs_more_to_trace_than_a_famous_one()
        {
            int obscure = AkhbaarMath.DispatchCost(1, sameRealm: true);
            int famous = AkhbaarMath.DispatchCost(6, sameRealm: true);
            Assert.True(obscure > famous);
            Assert.Equal(AkhbaarMath.BaseCost, famous); // a tier-6 house is court knowledge
        }

        [Fact]
        public void A_foreign_lord_costs_half_again()
        {
            int home = AkhbaarMath.DispatchCost(4, sameRealm: true);
            int abroad = AkhbaarMath.DispatchCost(4, sameRealm: false);
            Assert.Equal((int)(home * AkhbaarMath.ForeignRealmFactor), abroad);
        }

        [Fact]
        public void Cost_tolerates_degenerate_tiers()
        {
            Assert.Equal(AkhbaarMath.DispatchCost(0, true), AkhbaarMath.DispatchCost(-3, true));
            Assert.Equal(AkhbaarMath.DispatchCost(6, true), AkhbaarMath.DispatchCost(99, true));
        }

        [Fact]
        public void The_road_takes_two_days_even_to_the_next_camp()
            => Assert.Equal(AkhbaarMath.MinDays, AkhbaarMath.DaysToLocate(0f));

        [Fact]
        public void The_road_is_capped_at_twelve_days_even_across_all_Hindostan()
            => Assert.Equal(AkhbaarMath.MaxDays, AkhbaarMath.DaysToLocate(99999f));

        [Fact]
        public void A_farther_lord_takes_longer_to_find()
            => Assert.True(AkhbaarMath.DaysToLocate(600f) > AkhbaarMath.DaysToLocate(150f));

        [Fact]
        public void Negative_distance_does_not_undercut_the_minimum()
            => Assert.Equal(AkhbaarMath.MinDays, AkhbaarMath.DaysToLocate(-50f));

        [Theory]
        [InlineData(0, 0)]     // nobody to count
        [InlineData(7, 7)]     // a handful is counted exactly
        [InlineData(43, 40)]   // a war band to the nearest ten
        [InlineData(112, 100)] // a force to the nearest twenty-five
        [InlineData(340, 350)] // a host only to the nearest fifty
        public void Counts_are_rounded_to_hearsay_steps(int actual, int rough)
            => Assert.Equal(rough, AkhbaarMath.RoughCount(actual));

        [Theory]
        [InlineData(0, "no men under arms")]
        [InlineData(10, "a meagre escort")]
        [InlineData(50, "a modest war band")]
        [InlineData(150, "a strong force")]
        [InlineData(300, "a formidable host")]
        [InlineData(800, "a great host")]
        public void Strength_words_scale_with_the_muster(int men, string word)
            => Assert.Equal(word, AkhbaarMath.StrengthWord(men));

        [Fact]
        public void An_empty_camp_reads_as_no_fighting_men()
            => Assert.Equal("no fighting men to speak of", AkhbaarMath.CompositionLine(0, 0, 0));

        [Fact]
        public void A_dominant_arm_reads_as_almost_wholly()
            => Assert.Equal("almost wholly horse, with foot",
                AkhbaarMath.CompositionLine(30, 0, 70));

        [Fact]
        public void A_leading_arm_reads_as_chiefly()
            => Assert.Equal("chiefly foot, with horse and bows",
                AkhbaarMath.CompositionLine(50, 20, 30));

        [Fact]
        public void An_even_spread_reads_as_a_mixed_force()
            => Assert.Equal("a mixed force, foot foremost, with bows and horse",
                AkhbaarMath.CompositionLine(35, 33, 32));

        [Fact]
        public void Arms_beneath_a_tenth_are_beneath_the_scouts_notice()
            => Assert.Equal("almost wholly foot", AkhbaarMath.CompositionLine(95, 5, 0));
    }
}
