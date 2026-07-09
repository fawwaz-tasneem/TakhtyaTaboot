using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    public class NazranaMathTests
    {
        [Theory]
        [InlineData(0, 0)]      // unranked owes nothing
        [InlineData(1, 200)]
        [InlineData(6, 12000)]
        [InlineData(99, 12000)] // over-index clamps to the top rank
        [InlineData(-1, 0)]     // under-index clamps to unranked
        public void BaseAmount_ByRankWithClamping(int rank, int expected)
            => Assert.Equal(expected, NazranaMath.BaseAmount(rank));

        [Fact]
        public void BaseAmount_ScalesLinearly()
            => Assert.Equal(400, NazranaMath.BaseAmount(1, 2f));

        [Theory]
        [InlineData(NazranaMath.Tier.Minimal, 120)]   // 20% of 600
        [InlineData(NazranaMath.Tier.Expected, 600)]
        [InlineData(NazranaMath.Tier.Lavish, 1200)]   // 200%
        public void TierAmount_Rank2(NazranaMath.Tier tier, int expected)
            => Assert.Equal(expected, NazranaMath.TierAmount(2, tier));

        [Fact]
        public void TierEffects_LavishBeatsExpectedBeatsMinimal()
        {
            var min = NazranaMath.TierEffects(NazranaMath.Tier.Minimal);
            var exp = NazranaMath.TierEffects(NazranaMath.Tier.Expected);
            var lav = NazranaMath.TierEffects(NazranaMath.Tier.Lavish);
            Assert.True(min.relation < exp.relation && exp.relation < lav.relation);
            Assert.True(min.influence <= exp.influence && exp.influence < lav.influence);
        }

        [Fact]
        public void MissedEffects_AreNegative()
        {
            var (rel, infl) = NazranaMath.MissedEffects();
            Assert.True(rel < 0 && infl < 0);
        }

        [Theory]
        [InlineData(3, 0, 140)]     // 10% of 1400
        [InlineData(3, -20, 140)]   // -20 still pays (boundary)
        [InlineData(3, -21, 0)]     // beyond it, withheld
        [InlineData(0, 50, 0)]      // unranked pays nothing
        public void WeeklyAiPayment_RespectsRelationGate(int rank, int relation, int expected)
            => Assert.Equal(expected, NazranaMath.WeeklyAiPayment(rank, relation));

        [Fact]
        public void AiWithholds_MatchesPaymentGate()
        {
            Assert.False(NazranaMath.AiWithholds(-20));
            Assert.True(NazranaMath.AiWithholds(-21));
        }
    }
}
