using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the qasid: a messenger is cheaper and faster than a scout, foreign courts cost
    // half again, and the road time is bounded at both ends.
    public class MessengerMathTests
    {
        [Fact]
        public void A_foreign_court_costs_half_again()
            => Assert.Equal((int)(MessengerMath.BaseCost * 1.5f), MessengerMath.DispatchCost(true));

        [Fact]
        public void Own_realm_pays_the_base_fee()
            => Assert.Equal(MessengerMath.BaseCost, MessengerMath.DispatchCost(false));

        [Fact]
        public void The_qasid_outpaces_the_scout_on_the_same_road()
            => Assert.True(MessengerMath.DaysToReach(600f) < AkhbaarMath.DaysToLocate(600f));

        [Fact]
        public void The_road_time_is_bounded()
        {
            Assert.Equal(0.5f, MessengerMath.DaysToReach(0f));
            Assert.Equal(4f, MessengerMath.DaysToReach(99999f));
        }

        [Fact]
        public void A_farther_lord_takes_longer()
            => Assert.True(MessengerMath.DaysToReach(400f) > MessengerMath.DaysToReach(100f));
    }
}
