using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the fief-petition court: below a floor of the sovereign's regard the petition is
    // refused outright, a lavish well-backed suit is granted faster than a meagre one, and a
    // greater fief tier asks a greater gift and stake.
    public class FiefPetitionMathTests
    {
        [Fact]
        public void A_despised_petitioner_is_below_the_floor()
        {
            Assert.True(FiefPetitionMath.BelowRelationFloor(-40f));
            Assert.False(FiefPetitionMath.BelowRelationFloor(0f));
            Assert.False(FiefPetitionMath.BelowRelationFloor(FiefPetitionMath.RelationFloor));
        }

        [Fact]
        public void A_lavish_well_liked_suit_is_granted_faster_than_a_meagre_resented_one()
        {
            float rich = FiefPetitionMath.ApprovalChancePerWeek(40000, 150, 60f);
            float poor = FiefPetitionMath.ApprovalChancePerWeek(500, 10, -5f);
            Assert.True(rich > poor);
        }

        [Fact]
        public void Each_lever_raises_the_chance_on_its_own()
        {
            float baseline = FiefPetitionMath.ApprovalChancePerWeek(2000, 25, 0f);
            Assert.True(FiefPetitionMath.ApprovalChancePerWeek(40000, 25, 0f) > baseline); // more gold
            Assert.True(FiefPetitionMath.ApprovalChancePerWeek(2000, 200, 0f) > baseline); // more influence
            Assert.True(FiefPetitionMath.ApprovalChancePerWeek(2000, 25, 80f) > baseline); // warmer regard
        }

        [Fact]
        public void The_chance_stays_within_bounds()
        {
            Assert.InRange(FiefPetitionMath.ApprovalChancePerWeek(9999999, 9999, 100f), 0.03f, 0.95f);
            Assert.InRange(FiefPetitionMath.ApprovalChancePerWeek(0, 0, -100f), 0.03f, 0.95f);
        }

        [Fact]
        public void The_roll_decides_the_grant()
        {
            // A strong suit (~high chance): a low roll grants, a near-1 roll does not.
            Assert.True(FiefPetitionMath.CourtGrants(40000, 150, 60f, 0.01f));
            Assert.False(FiefPetitionMath.CourtGrants(40000, 150, 60f, 0.99f));
        }

        [Fact]
        public void A_greater_fief_asks_a_greater_gift_and_stake()
        {
            Assert.True(FiefPetitionMath.TierGiftBase(2) > FiefPetitionMath.TierGiftBase(1));
            Assert.True(FiefPetitionMath.TierGiftBase(1) > FiefPetitionMath.TierGiftBase(0));
            Assert.True(FiefPetitionMath.TierInfluenceBase(2) > FiefPetitionMath.TierInfluenceBase(0));
        }
    }
}
