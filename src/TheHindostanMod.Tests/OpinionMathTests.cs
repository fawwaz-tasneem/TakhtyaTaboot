using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    public class OpinionMathTests
    {
        [Fact]
        public void CurrentValue_FreshRecordIsFullStrength()
            => Assert.Equal(-10f, OpinionMath.CurrentValue(-10f, 0, 360), 3);

        [Fact]
        public void CurrentValue_HalvesAtExactlyOneHalfLife()
        {
            Assert.Equal(5f, OpinionMath.CurrentValue(10f, 180, 180), 2);
            Assert.Equal(-6f, OpinionMath.CurrentValue(-12f, 540, 540), 2);
        }

        [Fact]
        public void CurrentValue_QuartersAtTwoHalfLives()
            => Assert.Equal(2.5f, OpinionMath.CurrentValue(10f, 360, 180), 2);

        [Fact]
        public void CurrentValue_ZeroHalfLifeNeverDecays()
            => Assert.Equal(7f, OpinionMath.CurrentValue(7f, 10000, 0), 3);

        [Fact]
        public void IsDead_ThresholdBehaviour()
        {
            Assert.True(OpinionMath.IsDead(0.49f));
            Assert.True(OpinionMath.IsDead(-0.49f));
            Assert.False(OpinionMath.IsDead(0.5f));
            Assert.False(OpinionMath.IsDead(-3f));
        }

        [Fact]
        public void OldGrudge_EventuallyDies()
        {
            // A -12 grudge with a 540-day half-life is forgotten within ~7 half-lives.
            float after = OpinionMath.CurrentValue(OpinionMath.OpinionType.Grudge, -12f, 540 * 7);
            Assert.True(OpinionMath.IsDead(after));
        }

        [Fact]
        public void Effective_SumsAndClamps()
        {
            Assert.Equal(25f, OpinionMath.Effective(20, 5f), 3);
            Assert.Equal(100f, OpinionMath.Effective(95, 50f), 3);
            Assert.Equal(-100f, OpinionMath.Effective(-90, -40f), 3);
            Assert.Equal(-3f, OpinionMath.Effective(5, -8f), 3);
        }

        [Fact]
        public void Table_SignsMatchIntent()
        {
            Assert.True(OpinionMath.DefaultMagnitude(OpinionMath.OpinionType.SworeFealty) > 0);
            Assert.True(OpinionMath.DefaultMagnitude(OpinionMath.OpinionType.MissedCeremony) < 0);
            Assert.True(OpinionMath.DefaultMagnitude(OpinionMath.OpinionType.Grudge) < 0);
            Assert.True(OpinionMath.DefaultMagnitude(OpinionMath.OpinionType.Insult) < 0);
            Assert.True(OpinionMath.DefaultMagnitude(OpinionMath.OpinionType.Favor) > 0);
            Assert.True(OpinionMath.DefaultMagnitude(OpinionMath.OpinionType.KinBond) > 0);
        }

        [Fact]
        public void Table_KinBondOutlastsInsult()
            => Assert.True(OpinionMath.HalfLifeDays(OpinionMath.OpinionType.KinBond)
                           > OpinionMath.HalfLifeDays(OpinionMath.OpinionType.Insult));

        [Fact]
        public void Describe_EveryTypeHasWords()
        {
            foreach (OpinionMath.OpinionType t in System.Enum.GetValues(typeof(OpinionMath.OpinionType)))
                Assert.False(string.IsNullOrEmpty(OpinionMath.Describe(t)));
        }
    }
}
