using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the tenure-edict rules: a mansab is a rotational office with a fixed term, dislodging
    // an entrenched holder costs more, and a holder may defy the crown per his Rusukh. If anyone
    // retunes the rotation these tests state the intent.
    public class MansabTenureMathTests
    {
        // ── Term of office ───────────────────────────────────────────────────────────
        [Theory]
        [InlineData(0, 1080, false)]
        [InlineData(1079, 1080, false)]
        [InlineData(1080, 1080, true)]   // exactly at term -> due for rotation
        [InlineData(2000, 1080, true)]
        public void TermExpired_at_or_past_the_term(int held, int term, bool expired)
            => Assert.Equal(expired, MansabTenureMath.TermExpired(held, term));

        [Fact]
        public void A_zero_or_negative_term_never_expires()
        {
            Assert.False(MansabTenureMath.TermExpired(5000, 0));
            Assert.False(MansabTenureMath.TermExpired(5000, -10));
        }

        [Theory]
        [InlineData(0, 1080, 1080)]
        [InlineData(1000, 1080, 80)]
        [InlineData(1080, 1080, 0)]
        [InlineData(2000, 1080, 0)]      // never goes negative
        public void DaysUntilRotation_counts_down_and_floors_at_zero(int held, int term, int expected)
            => Assert.Equal(expected, MansabTenureMath.DaysUntilRotation(held, term));

        [Fact]
        public void OverdueFraction_is_zero_in_term_and_grows_past_it()
        {
            Assert.Equal(0f, MansabTenureMath.OverdueFraction(500, 1080), 3);
            Assert.Equal(0f, MansabTenureMath.OverdueFraction(1080, 1080), 3);
            Assert.Equal(1f, MansabTenureMath.OverdueFraction(2160, 1080), 3); // one full term overdue
        }

        // ── Edict cost engine (Feudal -> Mansabdari) ─────────────────────────────────
        [Fact]
        public void Legitimacy_gates_the_edict()
        {
            Assert.True(MansabTenureMath.MeetsLegitimacyFloor(60f, 50f));
            Assert.True(MansabTenureMath.MeetsLegitimacyFloor(50f, 50f));
            Assert.False(MansabTenureMath.MeetsLegitimacyFloor(49f, 50f));
        }

        [Fact]
        public void A_loyal_or_rootless_noble_costs_nothing_to_convert()
        {
            // relationFactor 1 = fully loyal -> no opposition, whatever his power/roots.
            Assert.Equal(0f, MansabTenureMath.OppositionWeight(100f, 5, 1f, 100f), 3);
            // rusukh 0 = rootless client -> no opposition, however resentful.
            Assert.Equal(0f, MansabTenureMath.OppositionWeight(100f, 5, 0f, 0f), 3);
        }

        [Fact]
        public void Opposition_rises_with_power_stake_resentment_and_roots()
        {
            float weak  = MansabTenureMath.OppositionWeight(10f, 1, 0.5f, 25f);
            float mighty = MansabTenureMath.OppositionWeight(100f, 5, 0f, 100f);
            Assert.True(mighty > weak);
            Assert.True(weak > 0f);
        }

        [Fact]
        public void Edict_gold_is_base_plus_opposition_discounted_by_legitimacy()
        {
            // No legitimacy -> no discount: pay base + opposition in full.
            Assert.Equal(3000, MansabTenureMath.EdictGoldCost(1000f, 2000f, 0f));
            // Full legitimacy -> half off.
            Assert.Equal(1500, MansabTenureMath.EdictGoldCost(1000f, 2000f, 100f));
        }

        [Fact]
        public void Edict_influence_is_a_flat_base_plus_per_noble()
        {
            Assert.Equal(50, MansabTenureMath.EdictInfluenceCost(40f, 10));
        }

        [Fact]
        public void A_magnate_whose_roots_outreach_the_crown_resists_the_reform()
        {
            // Deep roots vs a collapsed crown -> certain defiance -> resists any threshold below 1.
            Assert.True(MansabTenureMath.WillResistEdict(100f, 0f, 0f, 0.5f));
            // A rootless client vs a firm crown -> no defiance -> never resists.
            Assert.False(MansabTenureMath.WillResistEdict(0f, 100f, 100f, 0.5f));
        }

        // ── Cost of enforcing a rotation ─────────────────────────────────────────────
        [Fact]
        public void Cost_scales_with_rank_and_entrenchment()
        {
            // A newcomer (rusukh 0) at rank 1 pays the base; a deeply rooted (rusukh 100) holder
            // at the same rank costs twice as much to dislodge.
            Assert.Equal(100, MansabTenureMath.EnforcementInfluenceCost(1, 0f, 100f));
            Assert.Equal(200, MansabTenureMath.EnforcementInfluenceCost(1, 100f, 100f));
            // A greater office costs proportionally more.
            Assert.Equal(600, MansabTenureMath.EnforcementInfluenceCost(3, 100f, 100f));
        }

        [Fact]
        public void Gold_cost_uses_the_same_curve_as_influence()
        {
            Assert.Equal(MansabTenureMath.EnforcementInfluenceCost(2, 50f, 500f),
                         MansabTenureMath.EnforcementGoldCost(2, 50f, 500f));
        }

        [Fact]
        public void Unranked_holders_are_costed_as_at_least_rank_one()
        {
            Assert.Equal(MansabTenureMath.EnforcementInfluenceCost(1, 0f, 100f),
                         MansabTenureMath.EnforcementInfluenceCost(0, 0f, 100f));
        }

        // ── Eligibility guards ───────────────────────────────────────────────────────
        [Fact]
        public void Rotation_needs_an_expired_term_a_vassal_and_a_held_fief()
        {
            Assert.True(MansabTenureMath.CanEnactRotation(termExpired: true, holderIsSovereign: false, holderHoldsFief: true));
            Assert.False(MansabTenureMath.CanEnactRotation(false, false, true));  // still in term
            Assert.False(MansabTenureMath.CanEnactRotation(true, true, true));    // can't rotate the sovereign
            Assert.False(MansabTenureMath.CanEnactRotation(true, false, false));  // holds no post
        }

        [Fact]
        public void A_successor_must_serve_the_realm_be_alive_qualified_and_not_the_outgoing_holder()
        {
            Assert.True(MansabTenureMath.IsEligibleSuccessor(3, 3, candidateInRealm: true, candidateAlive: true, isOutgoingHolder: false));
            Assert.False(MansabTenureMath.IsEligibleSuccessor(2, 3, true, true, false));   // under-ranked
            Assert.False(MansabTenureMath.IsEligibleSuccessor(5, 3, false, true, false));  // not in the realm
            Assert.False(MansabTenureMath.IsEligibleSuccessor(5, 3, true, false, false));  // dead
            Assert.False(MansabTenureMath.IsEligibleSuccessor(5, 3, true, true, true));    // the outgoing holder
        }

        // ── Resolving against defiance ───────────────────────────────────────────────
        [Theory]
        [InlineData(0.5f, 0.4f, MansabTenureMath.RotationOutcome.Defied)]   // roll under chance -> defies
        [InlineData(0.5f, 0.6f, MansabTenureMath.RotationOutcome.Complied)] // roll over chance -> complies
        [InlineData(0f, 0f, MansabTenureMath.RotationOutcome.Complied)]     // no chance -> never defies
        [InlineData(1f, 0.99f, MansabTenureMath.RotationOutcome.Defied)]    // certain -> always defies
        public void Resolve_compares_roll_against_defiance_chance(float chance, float roll, MansabTenureMath.RotationOutcome expected)
            => Assert.Equal(expected, MansabTenureMath.Resolve(chance, roll));

        [Fact]
        public void Defiance_chance_feeds_resolution_from_rusukh_math()
        {
            // A deeply rooted magnate against a collapsed crown is certain to defy.
            float chance = RusukhMath.DefianceChance(100f, 0f, 0f);
            Assert.Equal(MansabTenureMath.RotationOutcome.Defied, MansabTenureMath.Resolve(chance, 0.99f));
            // A newcomer against a strong crown cannot.
            float none = RusukhMath.DefianceChance(0f, 100f, 100f);
            Assert.Equal(MansabTenureMath.RotationOutcome.Complied, MansabTenureMath.Resolve(none, 0f));
        }

        [Theory]
        // Whether he resists is roll-vs-chance; how far it escalates is the chance magnitude.
        [InlineData(0f, 0f, MansabTenureMath.RotationResult.Complied)]      // no chance -> always obeys
        [InlineData(0f, 0.99f, MansabTenureMath.RotationResult.Complied)]
        [InlineData(0.2f, 0.5f, MansabTenureMath.RotationResult.Complied)]  // roll over chance -> obeys
        [InlineData(0.2f, 0.1f, MansabTenureMath.RotationResult.Reprimand)] // weak resistance
        [InlineData(0.5f, 0.1f, MansabTenureMath.RotationResult.Dismissal)] // moderate
        [InlineData(0.9f, 0.1f, MansabTenureMath.RotationResult.Traitor)]   // deep roots -> rebellion
        [InlineData(0.9f, 0.95f, MansabTenureMath.RotationResult.Complied)] // even a magnate may comply on the roll
        public void Rotation_ladder_escalates_with_the_strength_of_defiance(
            float chance, float roll, MansabTenureMath.RotationResult expected)
            => Assert.Equal(expected, MansabTenureMath.ResolveRotationOrder(chance, roll));

        [Fact]
        public void Defiance_humiliates_the_crown_in_proportion_to_the_office()
        {
            Assert.Equal(5f, MansabTenureMath.AuthorityPenaltyForDefiance(1, 5f), 3);
            Assert.Equal(25f, MansabTenureMath.AuthorityPenaltyForDefiance(5, 5f), 3);
            Assert.Equal(5f, MansabTenureMath.AuthorityPenaltyForDefiance(0, 5f), 3); // unranked treated as rank 1
        }
    }
}
