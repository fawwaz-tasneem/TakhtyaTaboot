using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the coronation darbar: attendance rises with a house head's regard for the new
    // sovereign, a late oath is harder to win than mere attendance, and the hall's verdict
    // reads sensibly from the count who bent the knee.
    public class CoronationMathTests
    {
        [Fact]
        public void Warmer_regard_fills_the_hall()
            => Assert.True(CoronationMath.AttendanceChance(60f) > CoronationMath.AttendanceChance(-60f));

        [Fact]
        public void A_neutral_lord_usually_but_not_always_attends()
        {
            float c = CoronationMath.AttendanceChance(0f);
            Assert.InRange(c, 0.5f, 0.8f);
        }

        [Fact]
        public void Attendance_is_bounded_even_at_extremes()
        {
            Assert.InRange(CoronationMath.AttendanceChance(9999f), 0f, 0.99f);
            Assert.InRange(CoronationMath.AttendanceChance(-9999f), 0.03f, 1f);
        }

        [Fact]
        public void The_roll_decides_attendance()
        {
            // At neutral (~0.65), a low roll attends and a high roll stays away.
            Assert.True(CoronationMath.Attends(0f, 0.1f));
            Assert.False(CoronationMath.Attends(0f, 0.9f));
        }

        [Fact]
        public void A_late_oath_is_harder_to_win_than_attendance()
            => Assert.True(CoronationMath.LateOathChance(0f) < CoronationMath.AttendanceChance(0f));

        [Fact]
        public void A_bitter_lord_refuses_the_late_oath()
            => Assert.False(CoronationMath.AcceptsLateOath(-80f, 0.5f));

        [Fact]
        public void A_friendly_lord_accepts_the_late_oath()
            => Assert.True(CoronationMath.AcceptsLateOath(80f, 0.1f));

        [Theory]
        [InlineData(0, 0, "hold court alone")]
        [InlineData(5, 5, "unquestioned")]
        [InlineData(8, 10, "firm ground")]
        [InlineData(5, 10, "working majority")]
        [InlineData(3, 10, "grip on the realm is thin")]
        [InlineData(1, 10, "near empty")]
        public void The_hall_reads_its_own_verdict(int attended, int summoned, string fragment)
            => Assert.Contains(fragment, CoronationMath.LoyaltyVerdict(attended, summoned));
    }
}
