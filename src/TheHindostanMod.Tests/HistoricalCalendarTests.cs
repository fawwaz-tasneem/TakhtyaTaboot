using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Proves the test harness AND pins the AD<->game-year mapping the whole scripted-events
    // timeline depends on. If someone moves the campaign's start year, these tests state the
    // intended anchors so the breakage is loud and obvious.
    public class HistoricalCalendarTests
    {
        [Fact]
        public void Base_game_year_is_1707()
            => Assert.Equal(1707, HistoricalCalendar.ToADYear(HistoricalCalendar.BaseGameYear));

        [Theory]
        [InlineData(1084, 1707)]  // Aurangzeb's death — campaign opens
        [InlineData(1096, 1719)]  // Muhammad Shah's accession
        [InlineData(1116, 1739)]  // Nadir Shah's invasion
        [InlineData(1138, 1761)]  // Haider Ali / Third Panipat era
        public void GameYear_maps_to_AD(int gameYear, int ad)
            => Assert.Equal(ad, HistoricalCalendar.ToADYear(gameYear));

        [Theory]
        [InlineData(1707, 1084)]
        [InlineData(1719, 1096)]
        [InlineData(1739, 1116)]
        public void AD_maps_to_GameYear(int ad, int gameYear)
            => Assert.Equal(gameYear, HistoricalCalendar.ToGameYear(ad));

        [Fact]
        public void Mapping_round_trips_across_the_century()
        {
            for (int ad = 1700; ad <= 1800; ad++)
                Assert.Equal(ad, HistoricalCalendar.ToADYear(HistoricalCalendar.ToGameYear(ad)));
        }

        [Fact]
        public void HasReached_is_inclusive_on_the_target_year()
        {
            Assert.True(HistoricalCalendar.HasReached(1096, 1719));   // exactly 1719
            Assert.True(HistoricalCalendar.HasReached(1097, 1719));   // past it
            Assert.False(HistoricalCalendar.HasReached(1095, 1719));  // year before
        }

        [Fact]
        public void YearsElapsed_never_negative_and_counts_from_open()
        {
            Assert.Equal(0, HistoricalCalendar.YearsElapsed(1084));
            Assert.Equal(0, HistoricalCalendar.YearsElapsed(1080));  // before start clamps to 0
            Assert.Equal(12, HistoricalCalendar.YearsElapsed(1096));
        }
    }
}
