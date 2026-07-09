using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    public class CivilWarMathTests
    {
        [Theory]
        [InlineData(-31, 80f, 0.1f, true)]   // hatred alone suffices
        [InlineData(-30, 80f, 2f, false)]    // boundary: -30 is not yet hatred
        [InlineData(0, 29f, 0.9f, true)]     // weak throne + strong clan
        [InlineData(0, 29f, 0.8f, false)]    // boundary: ratio must EXCEED 0.8
        [InlineData(0, 30f, 2f, false)]      // boundary: legitimacy 30 holds
        public void Qualifies_Thresholds(int relation, float legitimacy, float ratio, bool expected)
            => Assert.Equal(expected, CivilWarMath.Qualifies(relation, legitimacy, ratio));

        [Fact]
        public void BidFires_NeedsQualificationAndLuck()
        {
            Assert.True(CivilWarMath.BidFires(-40, 80f, 1f, 0.0));
            Assert.False(CivilWarMath.BidFires(-40, 80f, 1f, 0.25)); // roll must be under the chance
            Assert.False(CivilWarMath.BidFires(0, 80f, 1f, 0.0));    // unqualified never fires
        }

        [Theory]
        [InlineData(6, 6, 0.10f)]   // equal rank: base desertion only
        [InlineData(5, 6, 0.15f)]
        [InlineData(1, 6, 0.35f)]
        [InlineData(0, 7, 0.40f)]   // cap
        [InlineData(6, 1, 0.10f)]   // never negative for challenging downward
        public void DesertionRate_ScalesWithRankGap(int challenger, int target, float expected)
            => Assert.Equal(expected, CivilWarMath.DesertionRate(challenger, target), 3);

        [Fact]
        public void RebelWins_StrengthAndFortuneDecide()
        {
            Assert.True(CivilWarMath.RebelWins(1000f, 1000f, 0.5, 0.5));   // tie goes to the bold
            Assert.False(CivilWarMath.RebelWins(500f, 1000f, 0.5, 0.5));
            Assert.True(CivilWarMath.RebelWins(700f, 1000f, 1.0, 0.0));    // fortune can upset the odds
        }
    }
}
