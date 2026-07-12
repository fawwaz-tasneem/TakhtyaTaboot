using System;
using System.Linq;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the historical cast table: every trait name is one of the five the engine knows,
    // levels and relations stay in the engine's ranges, nobody relates to himself, and no
    // pair or hero+trait is defined twice (a duplicate would silently overwrite the first).
    public class HistoricalCastTests
    {
        [Fact]
        public void Every_trait_name_is_one_the_engine_knows()
            => Assert.All(HistoricalCast.Traits, t => Assert.Contains(t.Trait, HistoricalCast.ValidTraits));

        [Fact]
        public void Trait_levels_stay_within_the_engine_range()
            => Assert.All(HistoricalCast.Traits, t => Assert.InRange(t.Level, -2, 2));

        [Fact]
        public void Relations_stay_within_the_engine_range()
            => Assert.All(HistoricalCast.Relations, r => Assert.InRange(r.Relation, -100, 100));

        [Fact]
        public void No_lord_relates_to_himself()
            => Assert.All(HistoricalCast.Relations, r => Assert.NotEqual(r.A, r.B));

        [Fact]
        public void No_pair_is_defined_twice()
        {
            var keys = HistoricalCast.Relations
                .Select(r => string.CompareOrdinal(r.A, r.B) < 0 ? r.A + "|" + r.B : r.B + "|" + r.A)
                .ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        [Fact]
        public void No_hero_trait_is_defined_twice()
        {
            var keys = HistoricalCast.Traits.Select(t => t.Hero + "|" + t.Trait).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        [Fact]
        public void Every_id_is_nonempty()
        {
            Assert.All(HistoricalCast.Relations, r =>
            { Assert.False(string.IsNullOrWhiteSpace(r.A)); Assert.False(string.IsNullOrWhiteSpace(r.B)); });
            Assert.All(HistoricalCast.Traits, t => Assert.False(string.IsNullOrWhiteSpace(t.Hero)));
        }

        [Fact]
        public void The_web_spans_friendships_and_rivalries_across_many_houses()
        {
            Assert.True(HistoricalCast.Relations.Count(r => r.Relation > 0) >= 20, "friendships");
            Assert.True(HistoricalCast.Relations.Count(r => r.Relation < 0) >= 20, "rivalries");
            int distinctHeroes = HistoricalCast.Relations.SelectMany(r => new[] { r.A, r.B }).Distinct().Count();
            Assert.True(distinctHeroes >= 50, $"web spans only {distinctHeroes} lords");
        }
    }
}
