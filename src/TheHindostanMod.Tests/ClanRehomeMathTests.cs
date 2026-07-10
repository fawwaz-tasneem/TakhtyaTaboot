using System.Collections.Generic;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the safety net's promise: a masterless noble house always finds a realm within days,
    // and it picks by faith, then friendship, then nearness — never a court across the map.
    public class ClanRehomeMathTests
    {
        [Theory]
        [InlineData(-1, 100, false)]  // never seen -> not due
        [InlineData(10, 12, false)]   // inside grace
        [InlineData(10, 13, true)]    // grace elapsed
        [InlineData(10, 50, true)]
        public void Grace_period_gates_the_rehoming(int firstSeen, int today, bool due)
            => Assert.Equal(due, ClanRehomeMath.DueForRehome(firstSeen, today));

        [Fact]
        public void Faith_outweighs_a_middling_friendship()
        {
            // Same distance: co-religionist ruler at relation 0 beats an infidel friend at +40.
            float faithful = ClanRehomeMath.Score(true, 0f, 100f);
            float friendly = ClanRehomeMath.Score(false, 40f, 100f);
            Assert.True(faithful > friendly);
        }

        [Fact]
        public void A_far_realm_loses_to_a_near_one()
        {
            // Identical courts: 500 map units of distance costs 50 points.
            float near = ClanRehomeMath.Score(true, 10f, 50f);
            float far = ClanRehomeMath.Score(true, 10f, 550f);
            Assert.Equal(50f, near - far, 3);
            Assert.True(near > far);
        }

        [Fact]
        public void A_deep_friendship_can_overcome_a_faith_gap()
        {
            // +80 relation vs +10 relation of the same faith at equal distance: 60-point faith
            // swing < 70-point relation swing.
            float friend = ClanRehomeMath.Score(false, 80f, 100f);
            float coReligionist = ClanRehomeMath.Score(true, 10f, 100f);
            Assert.True(friend > coReligionist);
        }

        [Fact]
        public void PickBest_returns_the_top_scorer_and_survives_junk()
        {
            var candidates = new List<ClanRehomeMath.Candidate>
            {
                null,
                new ClanRehomeMath.Candidate("", true, 100f, 0f),          // blank id ignored
                new ClanRehomeMath.Candidate("far_faithful", true, 20f, 800f),
                new ClanRehomeMath.Candidate("near_neutral", false, 0f, 30f),
                new ClanRehomeMath.Candidate("near_faithful", true, 20f, 60f),
            };
            Assert.Equal("near_faithful", ClanRehomeMath.PickBest(candidates));
        }

        [Fact]
        public void PickBest_of_nothing_is_null()
        {
            Assert.Null(ClanRehomeMath.PickBest(null));
            Assert.Null(ClanRehomeMath.PickBest(new List<ClanRehomeMath.Candidate>()));
        }
    }
}
