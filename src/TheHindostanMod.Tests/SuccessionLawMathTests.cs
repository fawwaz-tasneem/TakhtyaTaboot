using System.Collections.Generic;
using System.Linq;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the succession-law rules: each law selects a different candidate pool and resolution path,
    // a named/lawful heir leads, the magnates' vote settles only on a decisive margin, a law change
    // wounds a particular estate, and a formal law softly (never fully) suppresses the war-of-princes.
    public class SuccessionLawMathTests
    {
        private static SuccessionLawMath.LawCandidate Dyn(string id, int cat, int sonRank = -1, float power = 100f, bool wali = false, bool naib = false)
            => new SuccessionLawMath.LawCandidate { Id = id, Category = cat, IsDynasty = true, SonRank = sonRank, Power = power, IsWali = wali, IsNaib = naib };

        private static SuccessionLawMath.LawCandidate Magnate(string id, float power)
            => new SuccessionLawMath.LawCandidate { Id = id, Category = 5, IsDynasty = false, SonRank = -1, Power = power };

        // A typical universe: two sons, a brother, then two outside magnates by power.
        private static List<SuccessionLawMath.LawCandidate> Universe() => new List<SuccessionLawMath.LawCandidate>
        {
            Dyn("son1", 1, sonRank: 0),
            Dyn("son2", 1, sonRank: 1),
            Dyn("bro",  2),
            Magnate("mag1", 900f),
            Magnate("mag2", 500f),
        };

        // ── Candidate pool per law ──────────────────────────────────────────────────────
        [Fact]
        public void Undeclared_keeps_the_engines_first_three()
        {
            var pool = SuccessionLawMath.OrderedPool(SuccessionLaw.Undeclared, Universe(), 3);
            Assert.Equal(new[] { "son1", "son2", "bro" }, pool);
        }

        [Fact]
        public void Primogeniture_narrows_to_a_near_uncontested_line()
        {
            var pool = SuccessionLawMath.OrderedPool(SuccessionLaw.MalePrimogeniture, Universe(), 3);
            Assert.Equal(new[] { "son1", "son2" }, pool);   // at most two dynasty princes, no magnates
        }

        [Fact]
        public void Primogeniture_admits_one_rival_only_when_the_dynasty_is_thin()
        {
            var thin = new List<SuccessionLawMath.LawCandidate> { Dyn("son1", 1, 0), Magnate("mag1", 900f) };
            var pool = SuccessionLawMath.OrderedPool(SuccessionLaw.MalePrimogeniture, thin, 3);
            Assert.Equal(new[] { "son1", "mag1" }, pool);
        }

        [Fact]
        public void Magnate_election_always_seats_the_dynasty_heir_plus_top_houses()
        {
            var pool = SuccessionLawMath.OrderedPool(SuccessionLaw.MagnateElection, Universe(), 3);
            Assert.Contains("son1", pool);                  // dynasty heir always stands (favoured)
            Assert.Contains("mag1", pool);                  // strongest house
            Assert.Equal(3, pool.Count);
        }

        [Fact]
        public void Appointed_heir_leads_the_pool_then_the_naib()
        {
            var u = new List<SuccessionLawMath.LawCandidate>
            {
                Dyn("son1", 1, 0),
                Dyn("son2", 1, 1, wali: true),
                Dyn("bro",  2, naib: true),
            };
            var pool = SuccessionLawMath.OrderedPool(SuccessionLaw.AppointedHeir, u, 3);
            Assert.Equal("son2", pool[0]);                  // the Wali Ahd first
            Assert.Equal("bro",  pool[1]);                  // the Naib next
        }

        // ── Starting support (law layer) ────────────────────────────────────────────────
        [Fact]
        public void A_named_wali_and_a_primogeniture_eldest_son_get_the_heir_boost()
        {
            var wali = Dyn("h", 1, 0, wali: true);
            Assert.Equal(25f, SuccessionLawMath.LawSupportBonus(SuccessionLaw.AppointedHeir, wali, 25f, 20f));
            var eldest = Dyn("h", 1, sonRank: 0);
            Assert.Equal(25f, SuccessionLawMath.LawSupportBonus(SuccessionLaw.MalePrimogeniture, eldest, 25f, 20f));
            var younger = Dyn("h", 1, sonRank: 1);
            Assert.Equal(0f, SuccessionLawMath.LawSupportBonus(SuccessionLaw.MalePrimogeniture, younger, 25f, 20f));
        }

        [Fact]
        public void Magnate_election_tilts_toward_the_dynasty()
        {
            var dyn = Dyn("h", 5); // dynasty flag set by helper
            Assert.Equal(20f, SuccessionLawMath.LawSupportBonus(SuccessionLaw.MagnateElection, dyn, 25f, 20f));
            var outsider = Magnate("m", 100f);
            Assert.Equal(0f, SuccessionLawMath.LawSupportBonus(SuccessionLaw.MagnateElection, outsider, 25f, 20f));
        }

        // ── The vote ────────────────────────────────────────────────────────────────────
        [Fact]
        public void Tally_sums_weighted_ballots_and_finds_the_winner()
        {
            var r = SuccessionLawMath.Tally(new[] { ("a", 3f), ("b", 2f), ("a", 1f) });
            Assert.Equal("a", r.WinnerId);
            Assert.Equal(4f, r.WinnerVotes);
            Assert.Equal(2f, r.RunnerUpVotes);
            Assert.Equal(6f, r.TotalVotes);
        }

        [Fact]
        public void A_vote_settles_the_throne_only_on_a_decisive_margin()
        {
            var clear = SuccessionLawMath.Tally(new[] { ("a", 10f), ("b", 2f) });
            Assert.True(SuccessionLawMath.IsDecisive(clear, 1.25f));

            var close = SuccessionLawMath.Tally(new[] { ("a", 10f), ("b", 9f) });
            Assert.False(SuccessionLawMath.IsDecisive(close, 1.25f));   // near-tie -> civil war

            var lone = SuccessionLawMath.Tally(new[] { ("a", 4f) });
            Assert.True(SuccessionLawMath.IsDecisive(lone, 1.25f));     // unopposed is decisive
        }

        // ── Law-change edict ────────────────────────────────────────────────────────────
        [Fact]
        public void Legitimacy_gates_and_discounts_the_law_change()
        {
            Assert.True(SuccessionLawMath.MeetsLegitimacyFloor(50f, 50f));
            Assert.False(SuccessionLawMath.MeetsLegitimacyFloor(49f, 50f));
            // A more legitimate throne pays less to overturn the same number of houses.
            int cheap = SuccessionLawMath.LawChangeInfluenceCost(150f, 5, 100f);
            int dear  = SuccessionLawMath.LawChangeInfluenceCost(150f, 5, 0f);
            Assert.True(cheap < dear);
        }

        [Theory]
        [InlineData(SuccessionLaw.MalePrimogeniture, SuccessionLaw.MagnateElection, AngeredEstate.Princes)]
        [InlineData(SuccessionLaw.MalePrimogeniture, SuccessionLaw.PrincelyElection, AngeredEstate.Princes)]
        [InlineData(SuccessionLaw.MagnateElection, SuccessionLaw.AppointedHeir, AngeredEstate.Magnates)]
        [InlineData(SuccessionLaw.PrincelyElection, SuccessionLaw.MalePrimogeniture, AngeredEstate.Magnates)]
        [InlineData(SuccessionLaw.MalePrimogeniture, SuccessionLaw.MalePrimogeniture, AngeredEstate.None)]
        public void A_law_change_wounds_the_right_estate(SuccessionLaw from, SuccessionLaw to, AngeredEstate expected)
            => Assert.Equal(expected, SuccessionLawMath.WhoIsAngered(from, to));

        // ── Soft suppression of the war-of-princes ──────────────────────────────────────
        [Fact]
        public void Undeclared_and_a_collapsed_throne_are_always_contested()
        {
            Assert.True(SuccessionLawMath.ShouldContest(SuccessionLaw.Undeclared, true, 90f, 90f, 0.99f));
            Assert.True(SuccessionLawMath.ShouldContest(SuccessionLaw.MalePrimogeniture, true, 30f, 10f, 0.99f)); // collapse
        }

        [Fact]
        public void No_valid_heir_is_always_contested()
            => Assert.True(SuccessionLawMath.ShouldContest(SuccessionLaw.AppointedHeir, false, 80f, 80f, 0.99f));

        [Fact]
        public void A_secure_law_with_a_valid_heir_accedes_cleanly()
        {
            // Legitimacy at/above 60 drives the contest chance to zero -> clean accession on any roll.
            Assert.False(SuccessionLawMath.ShouldContest(SuccessionLaw.MalePrimogeniture, true, 70f, 70f, 0.0f));
            Assert.False(SuccessionLawMath.ShouldContest(SuccessionLaw.AppointedHeir, true, 60f, 60f, 0.0f));
        }

        [Fact]
        public void The_contest_floor_keeps_a_chance_of_a_crisis_even_under_a_secure_law()
        {
            // legit 80 -> baseChance 0 -> normally a clean accession; a 0.25 floor restores a real chance.
            Assert.True(SuccessionLawMath.ShouldContest(SuccessionLaw.AppointedHeir, true, 80f, 80f, 0.10f, 0.25f));
            Assert.False(SuccessionLawMath.ShouldContest(SuccessionLaw.AppointedHeir, true, 80f, 80f, 0.30f, 0.25f));
            // Floor of 0 keeps fully clean accessions available.
            Assert.False(SuccessionLawMath.ShouldContest(SuccessionLaw.AppointedHeir, true, 80f, 80f, 0.10f, 0f));
        }

        [Fact]
        public void A_shaky_throne_can_still_be_contested_even_with_a_valid_heir()
        {
            // legit 30 -> baseChance 0.5, primogeniture suppression 0.35 -> ~0.175 contest window.
            Assert.True(SuccessionLawMath.ShouldContest(SuccessionLaw.MalePrimogeniture, true, 30f, 60f, 0.10f));
            Assert.False(SuccessionLawMath.ShouldContest(SuccessionLaw.MalePrimogeniture, true, 30f, 60f, 0.50f));
        }

        // ── AI law adoption by dynasty size ─────────────────────────────────────────────
        [Fact]
        public void AI_picks_a_law_by_dynasty_size()
        {
            Assert.Equal(SuccessionLaw.MagnateElection, SuccessionLawMath.ChooseLawForAi(0, 80f, 80f, false)); // no son
            Assert.Equal(SuccessionLaw.AppointedHeir,   SuccessionLawMath.ChooseLawForAi(1, 80f, 80f, false)); // thin line
            Assert.Equal(SuccessionLaw.MalePrimogeniture, SuccessionLawMath.ChooseLawForAi(4, 80f, 80f, false)); // broad & steady
            Assert.Equal(SuccessionLaw.PrincelyElection, SuccessionLawMath.ChooseLawForAi(4, 30f, 30f, true));  // broad but fractured
        }

        // ── Buying off a rival claimant ─────────────────────────────────────────────────
        [Fact]
        public void Offer_value_sums_each_resource_in_gold_equivalents()
        {
            // 100k gold + 50 influence (×2000) + 10 men (×2000) + a 200k fief = 100k + 100k + 20k + 200k.
            float v = SuccessionLawMath.OfferValue(100000, 50, 10, 200000f);
            Assert.Equal(420000f, v, 3);
            // Negatives are clamped to zero, never a credit.
            Assert.Equal(0f, SuccessionLawMath.OfferValue(-5, -5, -5, -5f), 3);
        }

        [Fact]
        public void A_stronger_claimant_holds_out_for_a_higher_price()
        {
            float weak   = SuccessionLawMath.RivalPrice(100000f, 0.0f); // also-ran
            float strong = SuccessionLawMath.RivalPrice(100000f, 1.0f); // front-runner
            Assert.Equal(50000f, weak, 3);    // baseGold × 0.5
            Assert.Equal(150000f, strong, 3); // baseGold × 1.5
            Assert.True(strong > weak);
        }

        [Fact]
        public void Acceptance_rises_with_a_richer_offer_and_is_always_bounded()
        {
            float price = SuccessionLawMath.RivalPrice(100000f, 0.5f); // 100k
            float low  = SuccessionLawMath.PersuasionAcceptChance(50000f, price, 0);   // ratio .5 -> .25
            float par  = SuccessionLawMath.PersuasionAcceptChance(100000f, price, 0);  // ratio 1  -> .50
            float rich = SuccessionLawMath.PersuasionAcceptChance(300000f, price, 0);  // ratio 3  -> capped .95
            Assert.True(rich > par && par > low);
            Assert.Equal(0.25f, low, 2);
            Assert.Equal(0.50f, par, 2);
            Assert.InRange(rich, 0.05f, 0.95f);
            Assert.Equal(0.95f, rich, 2);
            // A friendly rival is easier; a hostile one harder.
            Assert.True(SuccessionLawMath.PersuasionAcceptChance(100000f, price, 60)
                      > SuccessionLawMath.PersuasionAcceptChance(100000f, price, -60));
        }
    }
}
