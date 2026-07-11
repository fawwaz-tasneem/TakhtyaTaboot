using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the bonded-labour trade-off: capacity scales with hearth (bounded), yields rise
    // linearly with the gang, unrest bites harder the fuller the village is of it, and the
    // gang thins faster where the district is lawless — with a deterministic fractional roll.
    public class SlaveLabourMathTests
    {
        [Fact]
        public void Capacity_scales_with_hearth_within_bounds()
        {
            Assert.Equal(SlaveLabourMath.MinCap, SlaveLabourMath.LabourCap(0f));
            Assert.Equal(SlaveLabourMath.MaxCap, SlaveLabourMath.LabourCap(999999f));
            int small = SlaveLabourMath.LabourCap(400f);
            int large = SlaveLabourMath.LabourCap(1600f);
            Assert.True(large > small);
            Assert.InRange(small, SlaveLabourMath.MinCap, SlaveLabourMath.MaxCap);
        }

        [Fact]
        public void Negative_hearth_still_yields_the_floor()
            => Assert.Equal(SlaveLabourMath.MinCap, SlaveLabourMath.LabourCap(-100f));

        [Fact]
        public void Yields_rise_linearly_with_the_gang()
        {
            Assert.Equal(0f, SlaveLabourMath.TaxBonusPct(0));
            Assert.Equal(10 * SlaveLabourMath.TaxPctPerLabourer, SlaveLabourMath.TaxBonusPct(10), 4);
            Assert.Equal(20 * SlaveLabourMath.ProsperityPerLabourer, SlaveLabourMath.BoundProsperityPerDay(20), 4);
        }

        [Fact]
        public void No_labourers_means_no_unrest_and_no_loss()
        {
            Assert.Equal(0f, SlaveLabourMath.DailyUnrest(0, 800f));
            Assert.Equal(0, SlaveLabourMath.DailyLoss(0, 100f, 0f));
            Assert.Equal(0, SlaveLabourMath.Fugitives(0));
        }

        [Fact]
        public void A_full_village_is_more_dangerous_per_head_than_a_sparse_one()
        {
            // Same gang size, but in a small village it is near capacity; in a big one, sparse.
            float packed = SlaveLabourMath.DailyUnrest(20, 400f);   // near/over a small cap
            float roomy = SlaveLabourMath.DailyUnrest(20, 2000f);   // well under a big cap
            Assert.True(packed > roomy);
        }

        [Fact]
        public void Unrest_grows_with_the_gang()
            => Assert.True(SlaveLabourMath.DailyUnrest(30, 800f) > SlaveLabourMath.DailyUnrest(10, 800f));

        [Fact]
        public void A_lawless_district_loses_more_labourers()
        {
            int calm = SlaveLabourMath.DailyLoss(100, 0f, 0f);    // 1% -> 1
            int lawless = SlaveLabourMath.DailyLoss(100, 100f, 0f); // 5% -> 5
            Assert.True(lawless > calm);
        }

        [Fact]
        public void The_fractional_roll_decides_the_last_man()
        {
            // 10 men at ~1% expects 0.1 lost: a low roll rounds up to 1, a high roll to 0.
            Assert.Equal(1, SlaveLabourMath.DailyLoss(10, 0f, 0.05f));
            Assert.Equal(0, SlaveLabourMath.DailyLoss(10, 0f, 0.5f));
        }

        [Fact]
        public void Losses_never_exceed_the_gang()
            => Assert.True(SlaveLabourMath.DailyLoss(3, 100f, 0f) <= 3);

        [Fact]
        public void About_half_of_the_lost_flee_to_banditry()
        {
            Assert.Equal(3, SlaveLabourMath.Fugitives(5)); // rounded up
            Assert.Equal(1, SlaveLabourMath.Fugitives(1)); // a lone runaway still flees
        }

        // ── The ThreatStep unrest extension (labour resists policing) ─────────────────
        [Fact]
        public void Unrest_is_added_after_the_watch_multiplier()
        {
            // With a strong watch multiplier, bandit threat is scaled down — but unrest is
            // added on top of the scaled value, so it is NOT suppressed by the watchtower.
            float watchedNoUnrest = VillageFiefMath.ThreatStep(50f, false, false, 0f, 0.6f, 0f, false, 0f);
            float watchedUnrest = VillageFiefMath.ThreatStep(50f, false, false, 0f, 0.6f, 0f, false, 5f);
            Assert.Equal(watchedNoUnrest + 5f, watchedUnrest, 3);
        }

        [Fact]
        public void Unrest_slows_even_active_relief()
        {
            float calmRelief = VillageFiefMath.ThreatStep(50f, true, false, 0f, 1f, 0f, false, 0f);
            float restiveRelief = VillageFiefMath.ThreatStep(50f, true, false, 0f, 1f, 0f, false, 4f);
            Assert.True(restiveRelief > calmRelief);
        }
    }
}
