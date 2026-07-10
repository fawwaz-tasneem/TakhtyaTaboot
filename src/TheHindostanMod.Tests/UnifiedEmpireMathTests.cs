using System.Collections.Generic;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the unified-empire premise (roadmap A.1): a FRESH campaign folds Bengal/Hyderabad into
    // the empire exactly once; an old save is never retro-unified; at the breakaway only clans
    // still serving the empire go home, and the recorded ruling house keeps its throne if it can.
    public class UnifiedEmpireMathTests
    {
        // ── ShouldUnify: the phase machine's single entry ────────────────────────────
        [Theory]
        [InlineData((int)UnifiedEmpireMath.Phase.NotArmed, 0.0, true)]    // brand-new campaign
        [InlineData((int)UnifiedEmpireMath.Phase.NotArmed, 0.5, true)]    // still day one
        [InlineData((int)UnifiedEmpireMath.Phase.NotArmed, 1.0, false)]   // window closed
        [InlineData((int)UnifiedEmpireMath.Phase.NotArmed, 400.0, false)] // old save under new mod version
        [InlineData((int)UnifiedEmpireMath.Phase.Unified, 0.0, false)]    // already unified — never twice
        [InlineData((int)UnifiedEmpireMath.Phase.Sundered, 0.0, false)]   // terminal — never re-armed
        public void ShouldUnify_only_arms_a_fresh_campaign_once(int phase, double ageDays, bool expected)
            => Assert.Equal(expected, UnifiedEmpireMath.ShouldUnify(phase, ageDays));

        [Fact]
        public void ShouldUnify_rejects_a_negative_clock()
            => Assert.False(UnifiedEmpireMath.ShouldUnify((int)UnifiedEmpireMath.Phase.NotArmed, -1.0));

        // ── SelectReturning: who goes home at the breakaway ──────────────────────────
        [Fact]
        public void Only_clans_still_serving_the_empire_return()
        {
            var recorded = new[] { "clan_a", "clan_b", "clan_c" };
            var alive = new HashSet<string> { "clan_a", "clan_c" }; // clan_b died or defected
            Assert.Equal(new List<string> { "clan_a", "clan_c" },
                UnifiedEmpireMath.SelectReturning(recorded, alive));
        }

        [Fact]
        public void Returning_list_drops_blanks_and_duplicates_and_tolerates_nulls()
        {
            var recorded = new[] { "clan_a", "", "clan_a", null };
            var alive = new HashSet<string> { "clan_a" };
            Assert.Equal(new List<string> { "clan_a" }, UnifiedEmpireMath.SelectReturning(recorded, alive));
            Assert.Empty(UnifiedEmpireMath.SelectReturning(null, alive));
            Assert.Empty(UnifiedEmpireMath.SelectReturning(recorded, null));
        }

        // ── ChooseRuler: the throne of the revived realm ─────────────────────────────
        [Fact]
        public void Recorded_ruling_house_keeps_its_throne_when_it_returns()
            => Assert.Equal("nawab",
                UnifiedEmpireMath.ChooseRuler("nawab", new List<string> { "stronger", "nawab" }));

        [Fact]
        public void Fallen_ruling_house_yields_to_the_first_candidate()
            => Assert.Equal("stronger",
                UnifiedEmpireMath.ChooseRuler("nawab_gone", new List<string> { "stronger", "weaker" }));

        [Fact]
        public void No_returning_clans_means_no_ruler()
        {
            Assert.Null(UnifiedEmpireMath.ChooseRuler("nawab", new List<string>()));
            Assert.Null(UnifiedEmpireMath.ChooseRuler("nawab", null));
        }

        // ── Pack/Unpack: the SyncData round-trip ─────────────────────────────────────
        [Fact]
        public void Pack_unpack_round_trips()
        {
            var ids = new List<string> { "clan_a", "clan_b", "clan_c" };
            Assert.Equal(ids, UnifiedEmpireMath.Unpack(UnifiedEmpireMath.Pack(ids)));
        }

        [Fact]
        public void Unpack_of_empty_or_null_is_an_empty_list()
        {
            Assert.Empty(UnifiedEmpireMath.Unpack(""));
            Assert.Empty(UnifiedEmpireMath.Unpack(null));
            Assert.Empty(UnifiedEmpireMath.Unpack(UnifiedEmpireMath.Pack(null)));
        }
    }
}
