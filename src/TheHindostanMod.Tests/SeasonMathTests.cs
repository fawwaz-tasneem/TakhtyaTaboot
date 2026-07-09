using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    public class SeasonMathTests
    {
        [Theory]
        [InlineData(SeasonMath.HotSeason, 1.0f)]
        [InlineData(SeasonMath.Monsoon, 0.70f)]
        [InlineData(SeasonMath.PostMonsoon, 1.10f)]
        [InlineData(SeasonMath.CoolSeason, 1.0f)]
        [InlineData(99, 1.0f)] // unknown season is neutral, never a crash
        public void MoveSpeedMultiplier_BySeason(int season, float expected)
            => Assert.Equal(expected, SeasonMath.MoveSpeedMultiplier(season), 3);

        [Fact]
        public void SpeedExplanation_OnlyWhereSpeedChanges()
        {
            Assert.NotNull(SeasonMath.SpeedExplanation(SeasonMath.Monsoon));
            Assert.NotNull(SeasonMath.SpeedExplanation(SeasonMath.PostMonsoon));
            Assert.Null(SeasonMath.SpeedExplanation(SeasonMath.HotSeason));
            Assert.Null(SeasonMath.SpeedExplanation(SeasonMath.CoolSeason));
        }

        [Fact]
        public void SeasonNames_AreDistinct()
        {
            var names = new[] { SeasonMath.SeasonName(0), SeasonMath.SeasonName(1), SeasonMath.SeasonName(2), SeasonMath.SeasonName(3) };
            Assert.Equal(4, System.Linq.Enumerable.Count(System.Linq.Enumerable.Distinct(names)));
        }
    }
}
