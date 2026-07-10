using System;

namespace TakhtyaTaboot.Util
{
    // The qiladar's arithmetic, PURE and unit-tested: whether the commander of a besieged fort
    // will treat with the besieger, and what his price is. Deliberately deterministic — the
    // player sees WHY the gates stay shut (resolve too high) and what would change it (starve
    // them, mass more strength), instead of gambling gold on a dice roll.
    // SiegeParleyBehavior owns the engine side.
    public static class SiegeParleyMath
    {
        // Resolve 0..100 — the garrison's will to hold the walls.
        //   • strength ratio carries most of it: an evenly-matched garrison (ratio 1) never
        //     treats (70 base); at 2:1 against them (ratio 0.5) resolve is 35 + food.
        //   • every day of food left stiffens the spine a little.
        public static float Resolve(float defenderStrength, float attackerStrength, float foodDays)
        {
            float ratio = attackerStrength <= 0f ? 10f : Math.Max(0f, defenderStrength) / attackerStrength;
            float r = 70f * ratio + 1.2f * Math.Max(0f, Math.Min(foodDays, 60f));
            return Math.Max(0f, Math.Min(100f, r));
        }

        // Coin opens the gates only when the defence already looks doubtful.
        public const float BribeThreshold = 55f;
        // Honourable terms (march out free, town spared) are cheaper than gold, so they take
        // real desperation — overwhelming odds or empty granaries.
        public const float TermsThreshold = 35f;

        public static bool AcceptsBribe(float resolve) => resolve < BribeThreshold;
        public static bool AcceptsTerms(float resolve) => resolve < TermsThreshold;

        // The bribe: scaled by the garrison to be paid off and the wealth of the town, and a
        // firmer commander demands more. Never trivial.
        public static int BribeCost(int garrisonCount, float prosperity, float resolve)
        {
            float raw = (Math.Max(0, garrisonCount) * 150f + Math.Max(0f, prosperity) * 3f)
                        * (0.5f + Math.Max(0f, Math.Min(100f, resolve)) / 100f);
            return Math.Max(2000, (int)raw);
        }

        public static string ResolveTier(float resolve)
            => resolve >= 70f ? "unshakeable" : resolve >= BribeThreshold ? "firm"
             : resolve >= TermsThreshold ? "wavering" : "broken";
    }
}
