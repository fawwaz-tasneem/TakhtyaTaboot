using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    public class RusukhMathTests
    {
        [Fact]
        public void Growth_accrues_and_is_clamped_to_max()
        {
            Assert.True(RusukhMath.Growth(0f, 0.3f, 50, 0.5f) > 0f);
            Assert.Equal(RusukhMath.Max, RusukhMath.Growth(100f, 5f, 300, 1f)); // never exceeds 100
        }

        [Fact]
        public void Growth_is_faster_with_better_steward_and_relations()
        {
            float weak  = RusukhMath.Growth(0f, 0.3f, 0, 0f);
            float strong = RusukhMath.Growth(0f, 0.3f, 200, 1f);
            Assert.True(strong > weak);
        }

        [Fact]
        public void Decay_reduces_and_floors_at_zero()
        {
            Assert.Equal(40f, RusukhMath.Decay(50f, 10f));
            Assert.Equal(0f, RusukhMath.Decay(3f, 10f)); // never negative
        }

        [Fact]
        public void Decay_can_outpace_growth_so_influence_fades_when_removed()
        {
            // With the intended rates (grow 0.3/day, decay 0.9/day) a season off-fief loses more
            // than a season on-fief gained.
            float gained = RusukhMath.Growth(50f, 0.3f, 100, 0.5f) - 50f;
            float lost   = 50f - RusukhMath.Decay(50f, 0.9f);
            Assert.True(lost > gained);
        }

        [Theory]
        [InlineData(0f, 100f, 100f, 0f)]    // no roots vs a strong crown -> cannot defy
        [InlineData(100f, 0f, 0f, 1f)]      // deep roots vs a collapsed crown -> certain to defy
        public void Defiance_pits_roots_against_crown_grip(float rusukh, float auth, float legit, float expected)
            => Assert.Equal(expected, RusukhMath.DefianceChance(rusukh, auth, legit), 3);

        [Fact]
        public void Defiance_needs_roots_above_the_crowns_weighted_grip()
        {
            // Against a 50/50 crown the weighted grip is 0.4, so rusukh 40 sits exactly at the
            // threshold -> no defiance; anything below also cannot defy.
            Assert.Equal(0f, RusukhMath.DefianceChance(40f, 50f, 50f), 3);
            Assert.Equal(0f, RusukhMath.DefianceChance(20f, 50f, 50f), 3);
            // Deep roots against the same crown -> a real, rising chance.
            Assert.True(RusukhMath.DefianceChance(95f, 50f, 50f) > 0f);
            Assert.True(RusukhMath.DefianceChance(95f, 50f, 50f) > RusukhMath.DefianceChance(60f, 50f, 50f));
        }

        [Fact]
        public void Benefits_are_gated_behind_a_minimum_footing()
        {
            Assert.Equal(0, RusukhMath.InfluenceBonus(24f));
            Assert.Equal(0, RusukhMath.GoldBacking(24f));
            Assert.True(RusukhMath.InfluenceBonus(80f) > 0);
            Assert.True(RusukhMath.GoldBacking(80f) >= 750);
        }

        [Fact]
        public void Levy_multiplier_runs_one_to_one_and_a_half()
        {
            Assert.Equal(1.0f, RusukhMath.LevyMultiplier(0f), 3);
            Assert.Equal(1.5f, RusukhMath.LevyMultiplier(100f), 3);
        }

        [Theory]
        [InlineData(10f, "Newcomer")]
        [InlineData(30f, "Rooted")]
        [InlineData(60f, "Established")]
        [InlineData(90f, "Entrenched")]
        public void Tier_labels_match_thresholds(float r, string label)
            => Assert.Equal(label, RusukhMath.Tier(r));
    }
}
