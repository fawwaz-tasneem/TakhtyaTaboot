using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the conspiracy arithmetic: disaffection is personal opinion, a conspiracy needs
    // both heads and strength, the ultimatum needs time AND muscle, the demand follows the
    // heir-and-legitimacy rule, and a weak illegitimate AI ruler yields where a strong one fights.
    public class DisaffectionMathTests
    {
        [Fact]
        public void Disaffection_is_a_low_opinion()
        {
            Assert.True(DisaffectionMath.IsDisaffected(-30f));
            Assert.True(DisaffectionMath.IsDisaffected(DisaffectionMath.DisaffectionThreshold));
            Assert.False(DisaffectionMath.IsDisaffected(0f));
        }

        [Fact]
        public void A_conspiracy_needs_both_heads_and_strength()
        {
            Assert.False(DisaffectionMath.ConspiracyForms(1, 9999f, 1000f)); // one malcontent is a grudge, not a plot
            Assert.False(DisaffectionMath.ConspiracyForms(3, 100f, 1000f));  // three weaklings are gossip
            Assert.True(DisaffectionMath.ConspiracyForms(2, 300f, 1000f));   // two houses at 30% is a plot
        }

        [Fact]
        public void The_ultimatum_needs_time_and_muscle()
        {
            Assert.False(DisaffectionMath.UltimatumReady(5, 500f, 1000f));   // strong but hasty
            Assert.False(DisaffectionMath.UltimatumReady(30, 300f, 1000f));  // patient but weak
            Assert.True(DisaffectionMath.UltimatumReady(21, 400f, 1000f));   // simmered and strong
        }

        [Fact]
        public void The_demand_follows_the_heir_and_the_legitimacy()
        {
            Assert.True(DisaffectionMath.DemandsAbdication(true, 40f));   // an heir to raise, a weak king to fell
            Assert.False(DisaffectionMath.DemandsAbdication(false, 40f)); // no heir -> they leave instead
            Assert.False(DisaffectionMath.DemandsAbdication(true, 80f));  // a legitimate king is not asked to go
        }

        [Fact]
        public void A_weak_illegitimate_ruler_yields_where_a_strong_one_fights()
        {
            // Overwhelming conspiracy + rock-bottom legitimacy: yields even on a middling roll.
            Assert.True(DisaffectionMath.AiRulerYields(2000f, 1000f, 20f, 0.4f));
            // Legitimate ruler with the stronger host: never yields.
            Assert.False(DisaffectionMath.AiRulerYields(400f, 1000f, 80f, 0.1f));
        }

        [Fact]
        public void The_yield_roll_is_bounded()
        {
            // Even at extremes the chance stays a probability: rolls at the edges behave.
            Assert.True(DisaffectionMath.AiRulerYields(99999f, 1f, 0f, 0.99f));
            Assert.False(DisaffectionMath.AiRulerYields(1f, 99999f, 100f, 0.01f));
        }
    }
}
