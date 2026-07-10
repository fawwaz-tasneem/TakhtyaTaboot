using System.Collections.Generic;

namespace TakhtyaTaboot.Util
{
    // A noble house without a realm is a wound in the world: vanilla lets landless independent
    // clans wither and die after 28 days, and a scattered claim kingdom dumps its houses to
    // nobody. This PURE logic (unit-tested) decides where a masterless house swears next:
    // faith first, then friendship, then a realm whose lands are near its own — never a realm
    // half the map away. ClanSafetyNetBehavior owns the engine actions.
    public static class ClanRehomeMath
    {
        // Days a clan may stand alone before the sweep finds it a realm. Long enough for the
        // mod's own transitions (fold-backs, dissolutions) to finish; far shorter than the
        // engine's 28-day destruction clock.
        public const int GraceDays = 3;

        public sealed class Candidate
        {
            public readonly string KingdomId;
            public readonly bool FaithMatch;    // clan leader's faith == realm ruler's faith
            public readonly float Relation;     // leader-to-ruler effective opinion (~-100..100)
            public readonly float Distance;     // clan's seat to the realm's nearest settlement (map units)
            public Candidate(string kingdomId, bool faithMatch, float relation, float distance)
            { KingdomId = kingdomId; FaithMatch = faithMatch; Relation = relation; Distance = distance; }
        }

        public static bool DueForRehome(int firstSeenDay, int today)
            => firstSeenDay >= 0 && today - firstSeenDay >= GraceDays;

        // Faith outweighs a middling friendship; distance dominates at range (a realm 500 units
        // away loses 50 points — no house rides across Hindostan for a marginally better court).
        public static float Score(bool faithMatch, float relation, float distance)
            => (faithMatch ? 40f : -20f) + relation - 0.1f * distance;

        // The realm this house swears to, or null if there are no candidates at all.
        public static string PickBest(IEnumerable<Candidate> candidates)
        {
            string best = null;
            float bestScore = float.MinValue;
            if (candidates == null) return null;
            foreach (Candidate c in candidates)
            {
                if (c == null || string.IsNullOrEmpty(c.KingdomId)) continue;
                float s = Score(c.FaithMatch, c.Relation, c.Distance);
                if (s > bestScore) { bestScore = s; best = c.KingdomId; }
            }
            return best;
        }
    }
}
