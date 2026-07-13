using System.Collections.Generic;
using System.Linq;
using TakhtyaTaboot.Util;
using Xunit;
using Kind = TakhtyaTaboot.Util.WarProgressMath.Kind;
using Contribution = TakhtyaTaboot.Util.WarProgressMath.Contribution;
using Snapshot = TakhtyaTaboot.Util.WarProgressMath.Snapshot;

namespace TakhtyaTaboot.Tests
{
    // Pins the war-progress model (wiki ch.30 §3a): the bar means a different thing per aim, a defender
    // wins by DENYING, subjugation shows the better of its two roads, and — the load-bearing rule —
    // every point on the bar can be named in the breakdown. If the tooltip cannot explain a number,
    // the number is a bug.
    public class WarProgressMathTests
    {
        private static List<Contribution> Ledger(params (Kind k, float d)[] items)
            => items.Select((x, i) => new Contribution(100 + i, x.k, x.d, null)).ToList();

        // ── The score is the ledger, and only the ledger ─────────────────────────────
        [Fact]
        public void The_score_is_the_sum_of_named_contributions()
        {
            var ledger = Ledger((Kind.BattleWon, 10f), (Kind.BattleWon, 10f), (Kind.FiefLost, -20f));
            Assert.Equal(0f, WarProgressMath.Score(ledger), 3);
        }

        [Fact]
        public void An_empty_war_has_scored_nothing()
        {
            Assert.Equal(0f, WarProgressMath.Score(null), 3);
            Assert.Equal(0f, WarProgressMath.Score(new List<Contribution>()), 3);
        }

        [Fact]
        public void Securing_a_declared_war_aim_is_worth_more_than_taking_a_random_fief()
            => Assert.True(WarProgressMath.DefaultDelta(Kind.TargetTaken)
                         > WarProgressMath.DefaultDelta(Kind.FiefTaken));

        [Fact]
        public void A_war_that_merely_drags_favours_nobody()
            => Assert.True(WarProgressMath.DefaultDelta(Kind.TimeGrind) < 0f);

        // ── The rollup IS the tooltip ────────────────────────────────────────────────
        [Fact]
        public void The_breakdown_rolls_up_by_kind_and_leads_with_what_decided_the_war()
        {
            var ledger = Ledger((Kind.BattleWon, 10f), (Kind.BattleWon, 10f), (Kind.BattleWon, 10f),
                                (Kind.VillageRaided, 3f), (Kind.KingCaptured, 25f));
            var rolled = WarProgressMath.Rollup(ledger);

            Assert.Equal(3, rolled.Count);                       // three kinds, not five lines
            Assert.Equal(Kind.BattleWon, rolled[0].kind);        // +30 leads
            Assert.Equal(3, rolled[0].count);
            Assert.Equal(30f, rolled[0].total, 3);
            Assert.Equal(Kind.KingCaptured, rolled[1].kind);     // +25 next
            Assert.Equal(Kind.VillageRaided, rolled[2].kind);    // +3 last
        }

        [Fact]
        public void A_heavy_loss_leads_the_breakdown_just_as_a_heavy_win_does()
        {
            // Ordered by ABSOLUTE weight: the player must see what is hurting him first.
            var ledger = Ledger((Kind.BattleWon, 10f), (Kind.FiefLost, -20f));
            Assert.Equal(Kind.FiefLost, WarProgressMath.Rollup(ledger)[0].kind);
        }

        // ── The ledger stays bounded ─────────────────────────────────────────────────
        [Fact]
        public void A_decade_long_war_folds_its_old_rows_but_keeps_the_totals_exact()
        {
            var ledger = new List<Contribution>();
            for (int i = 0; i < 500; i++) ledger.Add(new Contribution(i, Kind.BattleWon, 1f, null));
            float before = WarProgressMath.Score(ledger);

            var compact = WarProgressMath.Compact(ledger);

            Assert.True(compact.Count <= WarProgressMath.MaxLedgerRows + 1);   // + the one folded row
            Assert.True(compact.Count < ledger.Count);
            Assert.Equal(before, WarProgressMath.Score(compact), 3);           // not a point is lost
        }

        [Fact]
        public void A_short_war_is_never_compacted()
        {
            var ledger = Ledger((Kind.BattleWon, 10f), (Kind.FiefTaken, 20f));
            Assert.Equal(2, WarProgressMath.Compact(ledger).Count);
        }

        // ── Conquest: the bar is the targets ─────────────────────────────────────────
        [Fact]
        public void A_war_of_conquest_is_measured_in_the_fiefs_it_was_declared_for()
        {
            var s = new Snapshot { Aim = WarAim.ProvincialConquest, TargetsTotal = 4, TargetsHeld = 1 };
            Assert.Equal(25f, WarProgressMath.Percent(s), 3);
            Assert.False(WarProgressMath.Complete(s));

            s.TargetsHeld = 4;
            Assert.Equal(100f, WarProgressMath.Percent(s), 3);
            Assert.True(WarProgressMath.Complete(s));
        }

        [Fact]
        public void A_conquest_war_is_not_won_by_score_no_matter_how_crushing()
        {
            // The old system's exact defect: a big score used to unlock "demand a province". It must not.
            var s = new Snapshot { Aim = WarAim.ProvincialConquest, TargetsTotal = 2, TargetsHeld = 0, Score = 500f };
            Assert.False(WarProgressMath.Complete(s));
            Assert.Equal(0f, WarProgressMath.Percent(s), 3);
        }

        [Fact]
        public void A_conquest_war_can_be_un_won_when_a_target_is_retaken()
        {
            var s = new Snapshot { Aim = WarAim.ProvincialConquest, TargetsTotal = 2, TargetsHeld = 2 };
            Assert.True(WarProgressMath.Complete(s));
            s.TargetsHeld = 1;                                  // they took it back
            Assert.False(WarProgressMath.Complete(s));
            Assert.Equal(50f, WarProgressMath.Percent(s), 3);
        }

        // ── Tribute / chastisement: the bar is the score ─────────────────────────────
        [Fact]
        public void A_war_for_tribute_is_won_by_beating_them_decisively()
        {
            var s = new Snapshot { Aim = WarAim.Tribute, Score = WarProgressMath.DecisiveScore / 2f };
            Assert.Equal(50f, WarProgressMath.Percent(s), 3);
            Assert.False(WarProgressMath.Complete(s));

            s.Score = WarProgressMath.DecisiveScore;
            Assert.True(WarProgressMath.Complete(s));
        }

        [Fact]
        public void A_losing_war_shows_no_progress_rather_than_a_negative_bar()
        {
            var s = new Snapshot { Aim = WarAim.Tribute, Score = -60f };
            Assert.Equal(0f, WarProgressMath.Percent(s), 3);
        }

        // ── Subjugation: the better of two roads ─────────────────────────────────────
        [Fact]
        public void Subjugation_shows_partial_credit_toward_the_collapse_gate()
        {
            var s = new Snapshot
            {
                Aim = WarAim.TotalSubjugation,
                EnemyKingFallen = true,              // 1 of 3
                EnemyKingLegitimacy = 90f,           // fails
                LoyalLordFraction = 0f,              // fails
                EnemyFiefsTotal = 10, EnemyFiefsTaken = 0
            };
            Assert.Equal(100f / 3f, WarProgressMath.CollapseReadiness(s), 1);
            Assert.False(WarProgressMath.Complete(s));
        }

        [Fact]
        public void Subjugation_completes_the_moment_their_throne_collapses()
        {
            var s = new Snapshot
            {
                Aim = WarAim.TotalSubjugation,
                EnemyKingFallen = true,
                EnemyKingLegitimacy = 40f,           // below the ceiling
                LoyalLordFraction = 0.5f,            // a loyal bloc
                EnemyFiefsTotal = 10, EnemyFiefsTaken = 1
            };
            Assert.True(WarProgressMath.Complete(s));
            Assert.Equal(100f, WarProgressMath.Percent(s), 3);   // the collapse road is complete
        }

        [Fact]
        public void If_their_throne_will_not_collapse_every_last_fief_must_fall()
        {
            var s = new Snapshot
            {
                Aim = WarAim.TotalSubjugation,
                EnemyKingFallen = false,             // the gate is shut, and stays shut
                EnemyKingLegitimacy = 100f,
                LoyalLordFraction = 0f,
                EnemyFiefsTotal = 10, EnemyFiefsTaken = 9
            };
            Assert.False(WarProgressMath.Complete(s));
            Assert.Equal(90f, WarProgressMath.Percent(s), 3);    // the fief road is what shows

            s.EnemyFiefsTaken = 10;
            Assert.True(WarProgressMath.Complete(s));            // the last stone falls
        }

        [Fact]
        public void The_subjugation_bar_shows_whichever_road_is_nearer()
        {
            var s = new Snapshot
            {
                Aim = WarAim.TotalSubjugation,
                EnemyKingFallen = true, EnemyKingLegitimacy = 40f, LoyalLordFraction = 0f,  // 2 of 3 = 67%
                EnemyFiefsTotal = 10, EnemyFiefsTaken = 2                                    // 20%
            };
            Assert.Equal(100f * 2f / 3f, WarProgressMath.Percent(s), 1);   // the collapse road leads
        }

        // ── Defence: the bar fills as you deny ───────────────────────────────────────
        [Fact]
        public void A_defender_wins_by_denying_the_aggressors_aim()
        {
            var s = new Snapshot
            {
                Aim = WarAim.ProvincialConquest, IsDefender = true,
                TargetsTotal = 4, TargetsHeld = 1        // he holds one of the four he came for
            };
            Assert.Equal(75f, WarProgressMath.Percent(s), 3);   // we have denied three-quarters of it

            s.TargetsHeld = 4;                                   // he has taken them all
            Assert.Equal(0f, WarProgressMath.Percent(s), 3);     // we have denied nothing
        }

        [Fact]
        public void A_defender_completes_by_clawing_everything_back_AND_breaking_them()
        {
            var s = new Snapshot
            {
                Aim = WarAim.ProvincialConquest, IsDefender = true,
                TargetsTotal = 2, TargetsHeld = 0,               // he holds none of his aims
                Score = WarProgressMath.DecisiveScore            // ...and we have broken him
            };
            Assert.True(WarProgressMath.Complete(s));

            s.Score = 5f;                                        // merely holding is not yet victory
            Assert.False(WarProgressMath.Complete(s));

            s.Score = WarProgressMath.DecisiveScore;
            s.TargetsHeld = 1;                                   // he still sits on one of our towns
            Assert.False(WarProgressMath.Complete(s));
        }

        // ── Throne wars are not this system's business ───────────────────────────────
        [Fact]
        public void A_war_for_the_throne_has_no_progress_bar_it_is_won_or_lost()
        {
            var s = new Snapshot { Aim = WarAim.Succession, Score = 999f };
            Assert.Equal(0f, WarProgressMath.Percent(s), 3);
            Assert.False(WarProgressMath.Complete(s));
        }

        // ── THE LOAD-BEARING RULE: the bar explains itself ───────────────────────────
        [Fact]
        public void Every_contribution_that_moved_the_bar_is_named_in_the_breakdown()
        {
            var ledger = Ledger((Kind.BattleWon, 10f), (Kind.KingCaptured, 25f), (Kind.FiefLost, -20f));
            var s = new Snapshot { Aim = WarAim.Tribute, Score = WarProgressMath.Score(ledger) };

            var lines = WarProgressMath.Breakdown(s, ledger);
            string all = string.Join("\n", lines.Select(l => l.Label + " " + l.Value));

            Assert.Contains(WarProgressMath.Describe(Kind.BattleWon), all);
            Assert.Contains(WarProgressMath.Describe(Kind.KingCaptured), all);
            Assert.Contains(WarProgressMath.Describe(Kind.FiefLost), all);
        }

        [Fact]
        public void The_conquest_breakdown_names_every_target_and_whether_it_is_taken()
        {
            var s = new Snapshot { Aim = WarAim.ProvincialConquest, TargetsTotal = 2, TargetsHeld = 1 };
            var targets = new[] { ("Bijapur", true), ("Golconda", false) };

            var lines = WarProgressMath.Breakdown(s, new List<Contribution>(), targets);
            string all = string.Join("\n", lines.Select(l => l.Label + " " + l.Value));

            Assert.Contains("Bijapur", all);
            Assert.Contains("TAKEN", all);
            Assert.Contains("Golconda", all);
            Assert.Contains("still theirs", all);
        }

        [Fact]
        public void The_subjugation_breakdown_shows_all_three_gate_conditions_with_live_values()
        {
            // The player must never have to guess why "demand submission" is greyed out.
            var s = new Snapshot
            {
                Aim = WarAim.TotalSubjugation,
                EnemyKingFallen = false, EnemyKingLegitimacy = 73f, LoyalLordFraction = 0.22f,
                EnemyFiefsTotal = 12, EnemyFiefsTaken = 3
            };
            var lines = WarProgressMath.Breakdown(s, new List<Contribution>());
            string all = string.Join("\n", lines.Select(l => l.Label + " " + l.Value + " " + l.Detail));

            Assert.Contains("still at liberty", all);   // their king
            Assert.Contains("73", all);                 // their legitimacy, to the point
            Assert.Contains("22%", all);                // their lords with us
            Assert.Contains("3 of 12", all);            // ...and the other road
        }

        [Fact]
        public void A_defenders_breakdown_names_the_aggressors_aim_openly()
        {
            var s = new Snapshot
            {
                Aim = WarAim.TotalSubjugation, IsDefender = true,
                EnemyFiefsTotal = 8, EnemyFiefsTaken = 2
            };
            var lines = WarProgressMath.Breakdown(s, new List<Contribution>());
            string all = string.Join("\n", lines.Select(l => l.Label + " " + l.Value + " " + l.Detail));

            Assert.Contains("defending", all.ToLowerInvariant());
            Assert.Contains("Total subjugation", all);   // we are told exactly what they want
        }

        [Fact]
        public void Every_aim_and_every_kind_can_name_itself()
        {
            foreach (WarAim aim in new[] { WarAim.ProvincialConquest, WarAim.Tribute, WarAim.Revenge,
                                           WarAim.TotalSubjugation, WarAim.Succession })
                Assert.False(string.IsNullOrWhiteSpace(WarProgressMath.AimName(aim)));

            foreach (Kind k in System.Enum.GetValues(typeof(Kind)).Cast<Kind>())
            {
                Assert.False(string.IsNullOrWhiteSpace(WarProgressMath.Describe(k)));
                Assert.NotEqual(0f, WarProgressMath.DefaultDelta(k));   // no kind is worth nothing
            }
        }

        [Fact]
        public void Every_state_of_a_war_has_a_headline()
        {
            foreach (float score in new[] { -50f, 0f, 10f, 20f, 29f, 30f, 100f })
                Assert.False(string.IsNullOrWhiteSpace(
                    WarProgressMath.Headline(new Snapshot { Aim = WarAim.Tribute, Score = score })));
        }
    }
}
