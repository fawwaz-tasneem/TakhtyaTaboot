using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins war exhaustion: it accrues from blood and loss (harder on small realms), caps at
    // 100, decays in peace, and its thresholds order sensibly (urge before sue).
    public class WarExhaustionMathTests
    {
        [Fact]
        public void Exhaustion_accrues_and_caps()
        {
            float e = WarExhaustionMath.Accrue(0f, 50f, 1f);
            Assert.Equal(50f, e, 3);
            Assert.Equal(WarExhaustionMath.Cap, WarExhaustionMath.Accrue(90f, 999f, 1f), 3);
        }

        [Fact]
        public void Negative_points_never_reduce_exhaustion()
            => Assert.Equal(40f, WarExhaustionMath.Accrue(40f, -10f, 1f), 3);

        [Fact]
        public void A_small_realm_feels_the_same_losses_harder()
        {
            float small = WarExhaustionMath.StrengthScale(800f);
            float large = WarExhaustionMath.StrengthScale(6000f);
            Assert.True(small > large);
            Assert.InRange(small, 0.5f, 2f);
            Assert.InRange(large, 0.5f, 2f);
        }

        [Fact]
        public void The_scale_is_clamped_so_no_realm_is_immune_or_glass()
        {
            Assert.Equal(2f, WarExhaustionMath.StrengthScale(1f), 3);
            Assert.Equal(0.5f, WarExhaustionMath.StrengthScale(999999f), 3);
        }

        [Fact]
        public void Peace_decays_weariness_to_zero_and_no_further()
        {
            float e = WarExhaustionMath.DecayInPeace(1f);
            Assert.Equal(0f, e, 3);
            Assert.Equal(0f, WarExhaustionMath.DecayInPeace(0f), 3);
            Assert.True(WarExhaustionMath.DecayInPeace(50f) < 50f);
        }

        [Fact]
        public void The_council_urges_before_the_realm_sues()
        {
            Assert.True(WarExhaustionMath.AdvisoryThreshold < WarExhaustionMath.CriticalThreshold);
            Assert.True(WarExhaustionMath.CouncilUrgesPeace(WarExhaustionMath.AdvisoryThreshold));
            Assert.False(WarExhaustionMath.SuesForPeace(WarExhaustionMath.AdvisoryThreshold));
            Assert.True(WarExhaustionMath.SuesForPeace(WarExhaustionMath.CriticalThreshold));
        }

        [Theory]
        [InlineData(0f, "fresh")]
        [InlineData(45f, "strained")]
        [InlineData(70f, "weary")]
        [InlineData(90f, "reeling")]
        [InlineData(100f, "spent")]
        public void Tiers_read_correctly(float e, string tier)
            => Assert.Equal(tier, WarExhaustionMath.Tier(e));
    }
}
