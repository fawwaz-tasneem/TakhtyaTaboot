using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    public class ToleranceMathTests
    {
        [Fact]
        public void LoyaltyDrift_SameFaithIsUntouched()
        {
            foreach (ToleranceMath.Stance s in new[] { ToleranceMath.Stance.Strict, ToleranceMath.Stance.Moderate, ToleranceMath.Stance.Tolerant })
                Assert.Equal(0f, ToleranceMath.LoyaltyDriftPerDay(s, true), 3);
        }

        [Theory]
        [InlineData(ToleranceMath.Stance.Strict, -2f)]
        [InlineData(ToleranceMath.Stance.Moderate, -0.5f)]
        [InlineData(ToleranceMath.Stance.Tolerant, 1f)]
        public void LoyaltyDrift_OtherFaithByStance(ToleranceMath.Stance stance, float expected)
            => Assert.Equal(expected, ToleranceMath.LoyaltyDriftPerDay(stance, false), 3);

        [Fact]
        public void IncomeFactor_StrictSqueezesOtherFaithOnly()
        {
            Assert.Equal(1f, ToleranceMath.IncomeFactor(ToleranceMath.Stance.Strict, true, false), 3);
            Assert.Equal(0.9f, ToleranceMath.IncomeFactor(ToleranceMath.Stance.Strict, false, false), 3);
        }

        [Fact]
        public void IncomeFactor_ToleranceCostsALittleForEveryone()
        {
            Assert.Equal(0.97f, ToleranceMath.IncomeFactor(ToleranceMath.Stance.Tolerant, true, false), 3);
            Assert.Equal(0.97f, ToleranceMath.IncomeFactor(ToleranceMath.Stance.Tolerant, false, false), 3);
        }

        [Fact]
        public void IncomeFactor_NeverBelowHalf()
            => Assert.True(ToleranceMath.IncomeFactor(ToleranceMath.Stance.Strict, false, true) >= 0.5f);

        [Theory]
        [InlineData(ToleranceMath.Stance.Strict, ToleranceMath.Stance.Tolerant, 20)]   // great softening
        [InlineData(ToleranceMath.Stance.Tolerant, ToleranceMath.Stance.Strict, -20)]  // great hardening
        [InlineData(ToleranceMath.Stance.Moderate, ToleranceMath.Stance.Moderate, 0)]  // no change
        [InlineData(ToleranceMath.Stance.Moderate, ToleranceMath.Stance.Strict, -10)]
        public void StanceChange_RelationShiftIsSymmetric(ToleranceMath.Stance from, ToleranceMath.Stance to, int expected)
            => Assert.Equal(expected, ToleranceMath.StanceChangeRelationShift(from, to));

        [Fact]
        public void StanceNames_AreDistinct()
        {
            Assert.NotEqual(ToleranceMath.StanceName(ToleranceMath.Stance.Strict), ToleranceMath.StanceName(ToleranceMath.Stance.Tolerant));
            Assert.NotEqual(ToleranceMath.StanceName(ToleranceMath.Stance.Moderate), ToleranceMath.StanceName(ToleranceMath.Stance.Tolerant));
        }
    }
}
