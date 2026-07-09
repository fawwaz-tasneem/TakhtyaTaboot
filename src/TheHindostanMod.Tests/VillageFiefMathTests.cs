using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    public class VillageFiefMathTests
    {
        // ── Governor modifiers ──────────────────────────────────────────────────────
        [Theory]
        [InlineData(0, 1f)]
        [InlineData(150, 1.5f)]     // cap
        [InlineData(300, 1.5f)]     // over cap clamps
        [InlineData(-10, 1f)]       // negative skill never helps or hurts below base
        public void BuildSpeedFactor_ClampsToBand(int engineering, float expected)
            => Assert.Equal(expected, VillageFiefMath.BuildSpeedFactor(engineering), 3);

        [Theory]
        [InlineData(0, 1f)]
        [InlineData(160, 1.4f)]
        [InlineData(400, 1.4f)]
        public void TaxYieldFactor_ClampsToBand(int steward, float expected)
            => Assert.Equal(expected, VillageFiefMath.TaxYieldFactor(steward), 3);

        [Fact]
        public void ThreatDecayBonus_CapsAtTwo()
        {
            Assert.Equal(0f, VillageFiefMath.ThreatDecayBonus(0, 0), 3);
            Assert.Equal(2f, VillageFiefMath.ThreatDecayBonus(300, 300), 3);
            Assert.True(VillageFiefMath.ThreatDecayBonus(75, 75) > 0.9f);
        }

        // ── Taxes ───────────────────────────────────────────────────────────────────
        [Fact]
        public void DailyTax_ZeroHearthYieldsNothing()
            => Assert.Equal(0f, VillageFiefMath.DailyTax(0f, 0f, 0f, 1f, 0, 0.004f), 5);

        [Fact]
        public void DailyTax_ZeroRateYieldsNothing()
            => Assert.Equal(0f, VillageFiefMath.DailyTax(800f, 0f, 0f, 1f, 0, 0f), 5);

        [Fact]
        public void DailyTax_HealthyVillageIsSmallButPositive()
        {
            float tax = VillageFiefMath.DailyTax(600f, 0f, 20f, 1f, 60, 0.004f);
            Assert.InRange(tax, 1f, 4f); // supplements, not dwarfs, the economy
        }

        [Fact]
        public void DailyTax_MaxThreatHalvesCollection()
        {
            float calm = VillageFiefMath.DailyTax(600f, 0f, 0f, 1f, 0, 0.004f);
            float overrun = VillageFiefMath.DailyTax(600f, 0f, 100f, 1f, 0, 0.004f);
            Assert.Equal(calm * 0.5f, overrun, 3);
        }

        [Fact]
        public void DailyTax_BonusAndStewardRaiseYield()
        {
            float basic = VillageFiefMath.DailyTax(600f, 0f, 0f, 1f, 0, 0.004f);
            float improved = VillageFiefMath.DailyTax(600f, 20f, 0f, 1f, 200, 0.004f);
            Assert.True(improved > basic * 1.2f);
        }

        [Theory]
        [InlineData(0f, 40)]        // floor
        [InlineData(1000f, 120)]    // matches the old flat 120 at hearth 1000
        [InlineData(5000f, 200)]    // ceiling
        public void SeasonalTribute_ScalesWithHearthWithinBand(float hearth, int expected)
            => Assert.Equal(expected, VillageFiefMath.SeasonalTributeForVillage(hearth));

        // ── Threat ──────────────────────────────────────────────────────────────────
        [Fact]
        public void ThreatStep_ReliefAlwaysRecedes()
            => Assert.Equal(42f, VillageFiefMath.ThreatStep(50f, true, true, 0f, 1f, 0f, false), 3);

        [Fact]
        public void ThreatStep_ClampsToZeroAndHundred()
        {
            Assert.Equal(0f, VillageFiefMath.ThreatStep(0f, false, false, 50f, 1f, 10f, true), 3);
            Assert.Equal(100f, VillageFiefMath.ThreatStep(100f, false, true, 0f, 1f, 0f, false), 3);
        }

        [Fact]
        public void ThreatStep_WarFeedsIt_DefenceSuppressesIt()
        {
            float peace = VillageFiefMath.ThreatStep(40f, false, false, 0f, 1f, 0f, false);
            float war = VillageFiefMath.ThreatStep(40f, false, true, 0f, 1f, 0f, false);
            float defended = VillageFiefMath.ThreatStep(40f, false, true, 0f, 1f, 3f, false);
            Assert.True(war > peace);
            Assert.True(defended < war);
        }

        [Fact]
        public void ThreatStep_WatchMultiplierScalesResult()
        {
            float open = VillageFiefMath.ThreatStep(50f, false, false, 0f, 1f, 0f, false);
            float watched = VillageFiefMath.ThreatStep(50f, false, false, 0f, 0.6f, 0f, false);
            Assert.Equal(open * 0.6f, watched, 3);
        }

        [Theory]
        [InlineData(95f, -1f)]
        [InlineData(85f, 0f)]
        [InlineData(70f, 0.5f)]
        [InlineData(30f, 1f)]
        public void HearthGrowthFactor_Bands(float threat, float expected)
            => Assert.Equal(expected, VillageFiefMath.HearthGrowthFactor(threat), 3);

        // ── AI priorities ───────────────────────────────────────────────────────────
        [Theory]
        [InlineData(60f, 900f, VillageFiefMath.PriorityDefence)] // danger first, always
        [InlineData(50f, 100f, VillageFiefMath.PriorityDefence)] // boundary: 50 is dangerous
        [InlineData(10f, 200f, VillageFiefMath.PriorityFood)]
        [InlineData(10f, 300f, VillageFiefMath.PriorityEconomy)] // boundary: 300 is fed
        [InlineData(0f, 1200f, VillageFiefMath.PriorityEconomy)]
        public void AiPriorityCategory_PicksByNeed(float threat, float hearth, int expected)
            => Assert.Equal(expected, VillageFiefMath.AiPriorityCategory(threat, hearth));

        [Fact]
        public void AiGoldFloor_LordsKeepMoreBack()
            => Assert.True(VillageFiefMath.AiGoldFloor(true) > VillageFiefMath.AiGoldFloor(false));
    }
}
