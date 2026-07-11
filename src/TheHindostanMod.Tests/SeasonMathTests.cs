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

        // ── Harvest & famine (monsoon beyond speed) ──────────────────────────────────
        [Fact]
        public void Harvest_only_moves_after_the_rains()
        {
            Assert.Equal(1.0f, SeasonMath.HarvestTaxMultiplier(SeasonMath.HotSeason, 0.1f), 3);
            Assert.Equal(1.0f, SeasonMath.HarvestTaxMultiplier(SeasonMath.Monsoon, 0.9f), 3);
        }

        [Fact]
        public void A_bountiful_year_fattens_the_autumn_collection()
        {
            float bountiful = SeasonMath.HarvestTaxMultiplier(SeasonMath.PostMonsoon, 1.0f);
            float failed = SeasonMath.HarvestTaxMultiplier(SeasonMath.PostMonsoon, 0.0f);
            Assert.True(bountiful > 1f);
            Assert.True(failed < 1f);
            Assert.True(bountiful > failed);
        }

        [Fact]
        public void The_cool_season_carries_a_milder_echo_of_the_harvest()
        {
            float autumnSpread = SeasonMath.HarvestTaxMultiplier(SeasonMath.PostMonsoon, 1f)
                               - SeasonMath.HarvestTaxMultiplier(SeasonMath.PostMonsoon, 0f);
            float coolSpread = SeasonMath.HarvestTaxMultiplier(SeasonMath.CoolSeason, 1f)
                             - SeasonMath.HarvestTaxMultiplier(SeasonMath.CoolSeason, 0f);
            Assert.True(coolSpread < autumnSpread);
        }

        [Fact]
        public void The_harvest_multiplier_tolerates_out_of_range_quality()
        {
            Assert.Equal(SeasonMath.HarvestTaxMultiplier(SeasonMath.PostMonsoon, 0f),
                         SeasonMath.HarvestTaxMultiplier(SeasonMath.PostMonsoon, -5f), 3);
            Assert.Equal(SeasonMath.HarvestTaxMultiplier(SeasonMath.PostMonsoon, 1f),
                         SeasonMath.HarvestTaxMultiplier(SeasonMath.PostMonsoon, 9f), 3);
        }

        [Theory]
        [InlineData(0.1f, "failed")]
        [InlineData(0.5f, "middling")]
        [InlineData(0.9f, "bountiful")]
        public void The_monsoon_reads_its_own_verdict(float quality, string fragment)
            => Assert.Contains(fragment, SeasonMath.MonsoonVerdict(quality));

        [Fact]
        public void Famine_never_strikes_in_a_fair_or_good_year()
        {
            Assert.Equal(0f, SeasonMath.FamineDailyChance(0.5f, 100f, 90f), 5);
            Assert.Equal(0f, SeasonMath.FamineDailyChance(0.9f, 50f, 100f), 5);
        }

        [Fact]
        public void Failed_rains_with_a_thin_hungry_disordered_village_risks_famine()
        {
            float risk = SeasonMath.FamineDailyChance(0.05f, 50f, 90f);
            Assert.True(risk > 0f);
            Assert.InRange(risk, 0f, 0.05f);
        }

        [Fact]
        public void Famine_bites_harder_as_the_hearth_thins_and_disorder_grows()
        {
            float fat = SeasonMath.FamineDailyChance(0.1f, 800f, 10f);
            float thin = SeasonMath.FamineDailyChance(0.1f, 50f, 90f);
            Assert.True(thin > fat);
        }
    }
}
