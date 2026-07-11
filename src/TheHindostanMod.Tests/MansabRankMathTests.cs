using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the dual-rank stipend: pay follows ZAT (status), scales with it, and never goes negative.
    public class MansabRankMathTests
    {
        [Fact]
        public void Stipend_scales_with_zat()
            => Assert.True(MansabRankMath.StipendForZat(5000, 0.4f) > MansabRankMath.StipendForZat(100, 0.4f));

        [Fact]
        public void Stipend_is_zat_times_rate()
            => Assert.Equal(400, MansabRankMath.StipendForZat(1000, 0.4f));

        [Fact]
        public void Stipend_never_goes_negative()
        {
            Assert.Equal(0, MansabRankMath.StipendForZat(-500, 0.4f));
            Assert.Equal(0, MansabRankMath.StipendForZat(1000, -1f));
        }

        [Fact]
        public void The_dual_rank_label_shows_both_numbers()
        {
            string s = MansabRankMath.DualRankLabel(3000, 500);
            Assert.Contains("zat 3000", s);
            Assert.Contains("sawar 500", s);
        }
    }
}
