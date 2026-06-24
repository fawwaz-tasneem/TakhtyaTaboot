using System.Linq;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the user's spec exactly: Aurangzeb dies at 2 months, then one emperor per month, ending
    // with Muhammad Shah at 7 months. If anyone retimes the succession these tests state the intent.
    public class ImperialSuccessionPlanTests
    {
        [Fact]
        public void Seven_reigns_open_with_Aurangzeb_and_end_with_Muhammad_Shah()
        {
            Assert.Equal(7, ImperialSuccessionPlan.Reigns.Length);
            Assert.Equal("Aurangzeb Alamgir", ImperialSuccessionPlan.Reigns[ImperialSuccessionPlan.FirstEmperorIndex].Name);
            Assert.Equal("Muhammad Shah", ImperialSuccessionPlan.Reigns[ImperialSuccessionPlan.FinalEmperorIndex].Name);
            Assert.Equal("lord_1_1", ImperialSuccessionPlan.Reigns[ImperialSuccessionPlan.FinalEmperorIndex].HeroId);
        }

        [Theory]
        [InlineData(0, "Aurangzeb Alamgir")]    // campaign open
        [InlineData(59, "Aurangzeb Alamgir")]   // day before the 2-month mark
        [InlineData(60, "Bahadur Shah I")]      // 2 months
        [InlineData(95, "Jahandar Shah")]       // 3 months passed
        [InlineData(125, "Farrukhsiyar")]
        [InlineData(155, "Rafi ud-Darajat")]
        [InlineData(185, "Shah Jahan II")]
        [InlineData(210, "Muhammad Shah")]      // 7 months
        [InlineData(5000, "Muhammad Shah")]     // reigns on indefinitely
        public void ReigningIndexAt_tracks_the_throne(double days, string expected)
            => Assert.Equal(expected, ImperialSuccessionPlan.Reigns[ImperialSuccessionPlan.ReigningIndexAt(days)].Name);

        [Fact]
        public void Aurangzeb_dies_at_exactly_two_months()
        {
            var due = ImperialSuccessionPlan.AccessionsDue(59, 60);
            Assert.Single(due);
            Assert.Equal(1, due[0]);  // Bahadur Shah I crowned -> Aurangzeb (index 0) dies
        }

        [Fact]
        public void Each_intermediate_emperor_lasts_one_month()
        {
            for (int i = 2; i <= ImperialSuccessionPlan.FinalEmperorIndex; i++)
                Assert.Equal(ImperialSuccessionPlan.AccessionDay(i - 1) + ImperialSuccessionPlan.DaysPerMonth,
                             ImperialSuccessionPlan.AccessionDay(i));
        }

        [Fact]
        public void A_long_pause_processes_every_missed_accession_in_order()
        {
            var due = ImperialSuccessionPlan.AccessionsDue(0, 10000);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, due.ToArray());
        }

        [Fact]
        public void Window_is_half_open_so_no_accession_fires_twice()
        {
            Assert.Empty(ImperialSuccessionPlan.AccessionsDue(60, 60));      // empty window
            Assert.Single(ImperialSuccessionPlan.AccessionsDue(59.9, 60.1)); // fires once when crossed
            Assert.Empty(ImperialSuccessionPlan.AccessionsDue(60.1, 89));    // already past, not again
        }
    }
}
