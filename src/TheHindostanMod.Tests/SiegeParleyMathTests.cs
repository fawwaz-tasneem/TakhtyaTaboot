using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the qiladar's arithmetic: an even garrison never treats, coin needs a clear
    // advantage, free terms need real desperation, and the numbers are deterministic so the
    // player can reason about them instead of gambling.
    public class SiegeParleyMathTests
    {
        [Fact]
        public void An_evenly_matched_garrison_never_treats()
        {
            float resolve = SiegeParleyMath.Resolve(1000f, 1000f, 0f); // ratio 1, no bread at all
            Assert.False(SiegeParleyMath.AcceptsBribe(resolve));
            Assert.False(SiegeParleyMath.AcceptsTerms(resolve));
        }

        [Fact]
        public void Two_to_one_odds_open_the_door_to_coin_but_not_to_terms()
        {
            float resolve = SiegeParleyMath.Resolve(500f, 1000f, 10f); // 35 + 12 = 47
            Assert.True(SiegeParleyMath.AcceptsBribe(resolve));
            Assert.False(SiegeParleyMath.AcceptsTerms(resolve));
        }

        [Fact]
        public void Overwhelming_odds_and_thin_granaries_break_the_commander()
        {
            float resolve = SiegeParleyMath.Resolve(300f, 1000f, 5f); // 21 + 6 = 27
            Assert.True(SiegeParleyMath.AcceptsTerms(resolve));
            Assert.True(SiegeParleyMath.AcceptsBribe(resolve));
        }

        [Fact]
        public void Full_granaries_stiffen_a_wavering_garrison()
        {
            float starving = SiegeParleyMath.Resolve(500f, 1000f, 0f);
            float provisioned = SiegeParleyMath.Resolve(500f, 1000f, 40f);
            Assert.True(provisioned > starving);
            Assert.False(SiegeParleyMath.AcceptsBribe(provisioned)); // 35 + 48 = 83
        }

        [Fact]
        public void Resolve_is_clamped_and_tolerates_degenerate_inputs()
        {
            Assert.InRange(SiegeParleyMath.Resolve(99999f, 1f, 99999f), 0f, 100f);
            Assert.InRange(SiegeParleyMath.Resolve(0f, 0f, 0f), 0f, 100f);
            Assert.InRange(SiegeParleyMath.Resolve(-5f, -5f, -5f), 0f, 100f);
        }

        [Fact]
        public void A_firmer_commander_demands_more_gold()
        {
            int weak = SiegeParleyMath.BribeCost(200, 3000f, 20f);
            int firm = SiegeParleyMath.BribeCost(200, 3000f, 50f);
            Assert.True(firm > weak);
        }

        [Fact]
        public void A_bribe_is_never_trivial()
            => Assert.True(SiegeParleyMath.BribeCost(0, 0f, 0f) >= 2000);

        [Theory]
        [InlineData(80f, "unshakeable")]
        [InlineData(60f, "firm")]
        [InlineData(40f, "wavering")]
        [InlineData(20f, "broken")]
        public void Resolve_tiers_read_correctly(float resolve, string tier)
            => Assert.Equal(tier, SiegeParleyMath.ResolveTier(resolve));
    }
}
