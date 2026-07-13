using System.Collections.Generic;
using System.Linq;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the claim ledger's rules (wiki ch.30 §1-2): governing deepens a claim, dispossession does
    // NOT erase it but fades it at half the rate (the grudge outlives the holding), seeds fall in a
    // 0-10 year band, an entrenched house is dear to rotate and past the purse impossible, and the
    // wakil's manufactured claim is shallow and perishable.
    public class ClaimMathTests
    {
        // ── Accrual & decay ──────────────────────────────────────────────────────────
        [Fact]
        public void Governing_a_fief_deepens_the_claim_by_a_year_a_year()
            => Assert.Equal(1f, ClaimMath.Accrue(0f, ClaimMath.DaysPerYear), 3);

        [Fact]
        public void A_claim_never_grows_past_the_cap()
            => Assert.Equal(ClaimMath.MaxClaimYears, ClaimMath.Accrue(19f, 100 * ClaimMath.DaysPerYear), 3);

        [Fact]
        public void Losing_the_fief_does_not_erase_the_claim_it_only_fades_it()
        {
            float held = ClaimMath.Accrue(0f, 10 * ClaimMath.DaysPerYear);
            Assert.Equal(10f, held, 3);

            float afterAYearLost = ClaimMath.Decay(held, ClaimMath.DaysPerYear);
            Assert.Equal(9.5f, afterAYearLost, 3);   // half-rate: the grudge is stubborn
            Assert.False(ClaimMath.IsForgotten(afterAYearLost));
        }

        [Fact]
        public void The_grudge_outlives_the_holding_that_made_it()
        {
            // Ten years of governance buys twenty years of grievance.
            int forget = ClaimMath.DaysToForget(10f);
            Assert.Equal(20 * ClaimMath.DaysPerYear, forget);
            Assert.True(forget > 10 * ClaimMath.DaysPerYear);
        }

        [Fact]
        public void A_claim_decays_to_nothing_and_no_further()
        {
            float v = ClaimMath.Decay(1f, 100 * ClaimMath.DaysPerYear);
            Assert.Equal(0f, v, 3);
            Assert.True(ClaimMath.IsForgotten(v));
        }

        [Fact]
        public void Zero_or_negative_elapsed_days_change_nothing()
        {
            Assert.Equal(4f, ClaimMath.Accrue(4f, 0), 3);
            Assert.Equal(4f, ClaimMath.Decay(4f, -5), 3);
        }

        // ── Seeding ──────────────────────────────────────────────────────────────────
        [Fact]
        public void Seeded_claims_land_in_the_zero_to_ten_year_band()
        {
            // Sweep the whole plausible normal range, including the far tails.
            for (float z = -6f; z <= 6f; z += 0.1f)
                Assert.InRange(ClaimMath.SeedYears(z), ClaimMath.SeedMinYears, ClaimMath.SeedMaxYears);
        }

        [Fact]
        public void The_seed_centres_on_the_mean_and_spreads_by_the_deviation()
        {
            Assert.Equal(ClaimMath.SeedMeanYears, ClaimMath.SeedYears(0f), 3);
            Assert.Equal(ClaimMath.SeedMeanYears + ClaimMath.SeedStdDevYears, ClaimMath.SeedYears(1f), 3);
            Assert.True(ClaimMath.SeedYears(-1f) < ClaimMath.SeedYears(1f));
        }

        [Fact]
        public void The_seed_draw_is_actually_normal_not_uniform()
        {
            // Box-Muller over a deterministic lattice: the mass must clump around the mean, which a
            // uniform 0-10 draw would not. ~68% inside one sigma is the shape we are claiming.
            var samples = new List<float>();
            for (int i = 1; i <= 100; i++)
                for (int j = 1; j <= 100; j++)
                    samples.Add(ClaimMath.SeedYears(ClaimMath.StandardNormal(i / 101f, j / 101f)));

            double mean = samples.Average();
            Assert.InRange(mean, 4.5, 5.5);

            double withinOneSigma = samples.Count(s =>
                s > ClaimMath.SeedMeanYears - ClaimMath.SeedStdDevYears &&
                s < ClaimMath.SeedMeanYears + ClaimMath.SeedStdDevYears) / (double)samples.Count;
            Assert.InRange(withinOneSigma, 0.60, 0.75);   // uniform would give ~0.50
        }

        [Fact]
        public void The_normal_draw_survives_a_zero_roll()
        {
            float z = ClaimMath.StandardNormal(0f, 0.5f);   // log(0) would be -inf
            Assert.False(float.IsNaN(z));
            Assert.False(float.IsInfinity(z));
            Assert.InRange(ClaimMath.SeedYears(z), ClaimMath.SeedMinYears, ClaimMath.SeedMaxYears);
        }

        // ── Mansabdari rotation friction ─────────────────────────────────────────────
        [Fact]
        public void A_house_with_no_claim_rotates_at_the_base_price()
        {
            Assert.Equal(1f, ClaimMath.RotationInfluenceMultiplier(0f), 3);
            Assert.Equal(100, ClaimMath.RotationInfluenceCost(100, 0f));
        }

        [Fact]
        public void An_entrenched_house_is_dear_to_shift()
        {
            Assert.Equal(ClaimMath.MaxRotationSurcharge,
                         ClaimMath.RotationInfluenceMultiplier(ClaimMath.MaxClaimYears), 3);
            Assert.Equal(300, ClaimMath.RotationInfluenceCost(100, ClaimMath.MaxClaimYears));
        }

        [Fact]
        public void The_price_of_rotation_rises_with_the_claim()
        {
            int cheap = ClaimMath.RotationInfluenceCost(100, 2f);
            int dear  = ClaimMath.RotationInfluenceCost(100, 15f);
            Assert.True(dear > cheap);
            Assert.True(cheap >= 100);
        }

        [Fact]
        public void Past_the_crowns_purse_the_writ_stops_at_his_gate()
        {
            int cost = ClaimMath.RotationInfluenceCost(200, ClaimMath.MaxClaimYears);   // 600
            Assert.False(ClaimMath.CanAffordRotation(599f, cost));
            Assert.True(ClaimMath.CanAffordRotation(600f, cost));
        }

        // ── The wakil ────────────────────────────────────────────────────────────────
        [Fact]
        public void A_dull_agent_still_makes_slow_progress()
            => Assert.Equal(ClaimMath.AgentBaseWeeklyGain, ClaimMath.AgentWeeklyRelationGain(0, 0), 3);

        [Fact]
        public void A_brilliant_courtier_turns_a_town_far_faster_than_a_dull_one()
        {
            float dull     = ClaimMath.AgentWeeklyRelationGain(10, 2);
            float brilliant = ClaimMath.AgentWeeklyRelationGain(280, 9);
            Assert.True(brilliant > dull * 2f);
            Assert.InRange(brilliant, 0f, ClaimMath.AgentMaxWeeklyGain);
        }

        [Fact]
        public void The_agents_pace_is_capped_so_no_courtier_turns_a_town_in_a_week()
            => Assert.Equal(ClaimMath.AgentMaxWeeklyGain, ClaimMath.AgentWeeklyRelationGain(9999, 99), 3);

        [Fact]
        public void The_town_is_turned_when_two_thirds_of_its_merchants_are_won()
        {
            Assert.False(ClaimMath.ClaimEarned(1, 3));
            Assert.True(ClaimMath.ClaimEarned(2, 3));
            Assert.True(ClaimMath.ClaimEarned(4, 6));
            Assert.False(ClaimMath.ClaimEarned(3, 6));
        }

        [Fact]
        public void An_empty_town_can_never_be_turned()
            => Assert.False(ClaimMath.ClaimEarned(0, 0));

        [Fact]
        public void The_merchants_needed_rounds_up_never_down()
        {
            Assert.Equal(2, ClaimMath.MerchantsNeeded(3));   // 2/3 of 3 = 2
            Assert.Equal(3, ClaimMath.MerchantsNeeded(4));   // 2.67 -> 3
            Assert.Equal(4, ClaimMath.MerchantsNeeded(5));   // 3.33 -> 4
        }

        [Fact]
        public void A_manufactured_claim_is_shallower_than_a_lifetime_of_governance()
            => Assert.True(ClaimMath.ExternalClaimYears < ClaimMath.MaxClaimYears / 2f);

        [Fact]
        public void The_external_claim_is_perishable_act_within_two_years_or_lose_it()
        {
            Assert.True(ClaimMath.ExternalClaimLive(1000, 1000));
            Assert.True(ClaimMath.ExternalClaimLive(1000, 1000 + ClaimMath.ExternalClaimWindowDays - 1));
            Assert.False(ClaimMath.ExternalClaimLive(1000, 1000 + ClaimMath.ExternalClaimWindowDays));
        }

        [Fact]
        public void The_window_counts_down_and_stops_at_nothing()
        {
            Assert.Equal(ClaimMath.ExternalClaimWindowDays, ClaimMath.ExternalClaimDaysLeft(500, 500));
            Assert.Equal(30, ClaimMath.ExternalClaimDaysLeft(500, 500 + ClaimMath.ExternalClaimWindowDays - 30));
            Assert.Equal(0, ClaimMath.ExternalClaimDaysLeft(500, 500 + ClaimMath.ExternalClaimWindowDays + 999));
        }

        // ── Comparing claims ─────────────────────────────────────────────────────────
        [Fact]
        public void The_deeper_pretension_outranks_the_shallower()
        {
            Assert.True(ClaimMath.Outranks(9f, 4f));
            Assert.False(ClaimMath.Outranks(4f, 9f));
            Assert.False(ClaimMath.Outranks(4f, 4f));   // a tie is not an outranking
        }

        [Fact]
        public void The_strongest_claim_is_picked_as_the_casus_belli()
        {
            Assert.Equal(9f, ClaimMath.Strongest(new[] { 2f, 9f, 4f }), 3);
            Assert.Equal(0f, ClaimMath.Strongest(new float[0]), 3);
            Assert.Equal(0f, ClaimMath.Strongest(null), 3);
        }

        [Fact]
        public void A_whisper_of_a_claim_is_not_worth_a_war()
        {
            Assert.False(ClaimMath.WorthAWar(1f));
            Assert.True(ClaimMath.WorthAWar(ClaimMath.ActionableClaim));
            Assert.True(ClaimMath.WorthAWar(12f));
        }

        // ── Voice ────────────────────────────────────────────────────────────────────
        [Fact]
        public void Every_claim_can_name_itself_for_the_farmaan()
        {
            Assert.Equal("forgotten", ClaimMath.Describe(0f));
            Assert.Equal("an ancient claim", ClaimMath.Describe(ClaimMath.MaxClaimYears));
            foreach (float v in new[] { 0f, 1f, 3f, 7f, 12f, 18f, 20f })
                Assert.False(string.IsNullOrWhiteSpace(ClaimMath.Describe(v)));
        }
    }
}
