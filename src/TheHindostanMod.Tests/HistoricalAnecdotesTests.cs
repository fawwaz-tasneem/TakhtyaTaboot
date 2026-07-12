using System.Linq;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the road-news pool: a healthy spread of tales, all distinct and non-empty, the
    // same teller keeps his tale within a week, and the wheel turns safely for any seed.
    public class HistoricalAnecdotesTests
    {
        [Fact]
        public void The_pool_is_deep_and_distinct()
        {
            Assert.True(HistoricalAnecdotes.Pool.Length >= 12);
            Assert.Equal(HistoricalAnecdotes.Pool.Length, HistoricalAnecdotes.Pool.Distinct().Count());
            Assert.All(HistoricalAnecdotes.Pool, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        }

        [Fact]
        public void The_same_teller_keeps_his_tale_for_the_week()
            => Assert.Equal(HistoricalAnecdotes.Tale(42, 7), HistoricalAnecdotes.Tale(42, 7));

        [Fact]
        public void The_wheel_turns_with_the_weeks()
            => Assert.NotEqual(HistoricalAnecdotes.Tale(42, 7), HistoricalAnecdotes.Tale(42, 8));

        [Fact]
        public void Negative_seeds_are_safe()
            => Assert.False(string.IsNullOrWhiteSpace(HistoricalAnecdotes.Tale(-13, 0)));
    }
}
