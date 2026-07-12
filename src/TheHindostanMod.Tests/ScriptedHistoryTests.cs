using System.Linq;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the scripted timeline: chronological order, unique ids, known kinds, and every
    // event inside the mod's playable era.
    public class ScriptedHistoryTests
    {
        [Fact]
        public void The_timeline_is_chronological()
        {
            for (int i = 1; i < ScriptedHistory.Events.Length; i++)
                Assert.True(ScriptedHistory.Events[i].Year >= ScriptedHistory.Events[i - 1].Year);
        }

        [Fact]
        public void Ids_are_unique_and_nonempty()
        {
            var ids = ScriptedHistory.Events.Select(e => e.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        }

        [Fact]
        public void Every_kind_is_known()
            => Assert.All(ScriptedHistory.Events, e => Assert.Contains(e.Kind, ScriptedHistory.ValidKinds));

        [Fact]
        public void Every_event_sits_in_the_playable_era()
            => Assert.All(ScriptedHistory.Events, e => Assert.InRange(e.Year, HistoricalCalendar.BaseADYear, 1800));
    }
}
