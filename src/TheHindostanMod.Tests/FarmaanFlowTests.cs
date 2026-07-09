using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    public class FarmaanFlowTests
    {
        [Fact]
        public void Ceremonial_IsNeverSuppressed()
        {
            // Even queued twice, mid-cooldown, with no actions: a coronation always shows.
            Assert.Equal(FarmaanDecision.Show,
                FarmaanFlow.Decide(true, 100, 101, 30, FarmaanPriority.Ceremonial, false));
        }

        [Fact]
        public void SameKeyAlreadyQueued_Drops()
        {
            Assert.Equal(FarmaanDecision.Drop,
                FarmaanFlow.Decide(true, -1, 10, 0, FarmaanPriority.Urgent, true));
            Assert.Equal(FarmaanDecision.Drop,
                FarmaanFlow.Decide(true, -1, 10, 30, FarmaanPriority.Routine, false));
        }

        [Fact]
        public void RoutineNoActionsNoCooldown_AlwaysDowngrades()
        {
            // The stipend receipt: permanently a log line + digest item, never a popup.
            Assert.Equal(FarmaanDecision.Downgrade,
                FarmaanFlow.Decide(false, -1, 10, 0, FarmaanPriority.Routine, false));
            Assert.Equal(FarmaanDecision.Downgrade,
                FarmaanFlow.Decide(false, 5, 500, 0, FarmaanPriority.Routine, false));
        }

        [Fact]
        public void FirstShowing_ShowsForUrgentAndCooldownedRoutine()
        {
            Assert.Equal(FarmaanDecision.Show,
                FarmaanFlow.Decide(false, -1, 10, 30, FarmaanPriority.Urgent, true));
            Assert.Equal(FarmaanDecision.Show,
                FarmaanFlow.Decide(false, -1, 10, 30, FarmaanPriority.Routine, false));
        }

        [Fact]
        public void WithinCooldown_RoutineDowngrades_UrgentDrops()
        {
            Assert.Equal(FarmaanDecision.Downgrade,
                FarmaanFlow.Decide(false, 100, 110, 30, FarmaanPriority.Routine, false));
            Assert.Equal(FarmaanDecision.Drop,
                FarmaanFlow.Decide(false, 100, 110, 30, FarmaanPriority.Urgent, false));
        }

        [Fact]
        public void CooldownExpiry_BoundaryIsExact()
        {
            // Shown day 100 with a 30-day cooldown: day 129 still suppressed, day 130 shows.
            Assert.Equal(FarmaanDecision.Drop,
                FarmaanFlow.Decide(false, 100, 129, 30, FarmaanPriority.Urgent, true));
            Assert.Equal(FarmaanDecision.Show,
                FarmaanFlow.Decide(false, 100, 130, 30, FarmaanPriority.Urgent, true));
        }

        [Fact]
        public void ChoicesAreNeverDowngraded()
        {
            // A Routine farmaan WITH actions inside its cooldown must drop, not downgrade —
            // downgrading would silently discard the player's choices.
            Assert.Equal(FarmaanDecision.Drop,
                FarmaanFlow.Decide(false, 100, 110, 30, FarmaanPriority.Routine, true));
            // And with no cooldown at all it shows rather than downgrades.
            Assert.Equal(FarmaanDecision.Show,
                FarmaanFlow.Decide(false, -1, 10, 0, FarmaanPriority.Routine, true));
        }

        [Fact]
        public void NeverShownBefore_CooldownIrrelevant()
        {
            Assert.Equal(FarmaanDecision.Show,
                FarmaanFlow.Decide(false, -1, 1000, 365, FarmaanPriority.Urgent, false));
        }
    }
}
