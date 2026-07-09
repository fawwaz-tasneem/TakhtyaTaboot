using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    public class CourtFactionMathTests
    {
        [Theory]
        [InlineData(2, 0, 0, 0, CourtFactionMath.CourtFaction.War)]
        [InlineData(0, 1, 0, 0, CourtFactionMath.CourtFaction.Peace)]
        [InlineData(0, 0, 2, 0, CourtFactionMath.CourtFaction.Reform)]
        [InlineData(0, 0, 0, 1, CourtFactionMath.CourtFaction.Orthodox)]
        public void Affinity_FollowsStrongestTrait(int valor, int calc, int gen, int honor, CourtFactionMath.CourtFaction expected)
            => Assert.Equal(expected, CourtFactionMath.Affinity(valor, calc, gen, honor, 0));

        [Fact]
        public void Affinity_TieGoesInDeclaredOrder()
            // valor and honor tied: the war party claims him first
            => Assert.Equal(CourtFactionMath.CourtFaction.War, CourtFactionMath.Affinity(1, 0, 0, 1, 0));

        [Theory]
        [InlineData(0, CourtFactionMath.CourtFaction.War)]
        [InlineData(1, CourtFactionMath.CourtFaction.Peace)]
        [InlineData(-1, CourtFactionMath.CourtFaction.Orthodox)] // negative hash still maps into range
        public void Affinity_TraitlessLordFallsToStableHash(int hash, CourtFactionMath.CourtFaction expected)
            => Assert.Equal(expected, CourtFactionMath.Affinity(0, 0, 0, 0, hash));

        [Fact]
        public void MemberWeight_CouncilSeatDoubles()
        {
            Assert.Equal(100f, CourtFactionMath.MemberWeight(100f, false), 3);
            Assert.Equal(200f, CourtFactionMath.MemberWeight(100f, true), 3);
            Assert.Equal(0f, CourtFactionMath.MemberWeight(-5f, true), 3);
        }

        [Fact]
        public void Dominant_PicksMax_TieGoesToFirst()
        {
            Assert.Equal(2, CourtFactionMath.Dominant(new[] { 1f, 2f, 5f, 0f }));
            Assert.Equal(0, CourtFactionMath.Dominant(new[] { 3f, 3f, 3f, 3f }));
            Assert.Equal(0, CourtFactionMath.Dominant(new float[0]));
        }
    }
}
