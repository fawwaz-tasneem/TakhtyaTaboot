using TakhtyaTaboot.Util;
using Xunit;
using static TakhtyaTaboot.Util.DarbarCourtMath;

namespace TakhtyaTaboot.Tests
{
    // Pins judgment at the darbar: a decisive ruling pleases one party and angers the other while
    // asserting authority, a compromise pleases both but spends influence, dismissal costs
    // legitimacy, and a granted plea wins the deepest gratitude.
    public class DarbarCourtMathTests
    {
        [Fact]
        public void A_ruling_for_the_plaintiff_pleases_him_and_angers_the_defendant()
        {
            var o = Judge(CourtStance.ForPlaintiff);
            Assert.True(o.PlaintiffOpinion > 0f);
            Assert.True(o.DefendantOpinion < 0f);
            Assert.True(o.Influence > 0);
        }

        [Fact]
        public void A_ruling_for_the_defendant_is_the_mirror()
        {
            var o = Judge(CourtStance.ForDefendant);
            Assert.True(o.DefendantOpinion > 0f);
            Assert.True(o.PlaintiffOpinion < 0f);
        }

        [Fact]
        public void A_compromise_pleases_both_but_spends_influence()
        {
            var o = Judge(CourtStance.Compromise);
            Assert.True(o.PlaintiffOpinion > 0f && o.DefendantOpinion > 0f);
            Assert.True(o.Influence < 0);
            Assert.True(o.Legitimacy > 0f);
        }

        [Fact]
        public void Dismissing_a_dispute_angers_both_and_costs_legitimacy()
        {
            var o = Judge(CourtStance.Dismiss);
            Assert.True(o.PlaintiffOpinion < 0f && o.DefendantOpinion < 0f);
            Assert.True(o.Legitimacy < 0f);
        }

        [Fact]
        public void Granting_a_plea_wins_the_deepest_gratitude()
        {
            Assert.True(JudgePlea(CourtStance.ForPlaintiff).PlaintiffOpinion
                        > JudgePlea(CourtStance.Compromise).PlaintiffOpinion);
            Assert.True(JudgePlea(CourtStance.ForPlaintiff).Legitimacy > 0f);
        }

        [Fact]
        public void Turning_a_plea_away_stings_the_petitioner_and_the_crown()
        {
            var o = JudgePlea(CourtStance.Dismiss);
            Assert.True(o.PlaintiffOpinion < 0f);
            Assert.True(o.Legitimacy < 0f);
        }
    }
}
