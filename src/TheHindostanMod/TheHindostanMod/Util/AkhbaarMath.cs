using System;
using System.Collections.Generic;
using System.Linq;

namespace TakhtyaTaboot.Util
{
    // The harkara's arithmetic, PURE and unit-tested: what a scout costs, how long the road
    // takes him, and how his report reads. Deliberately deterministic — the player is quoted
    // a price and a rough delay up front, and the report always comes (the runner always finds
    // his man, or finds out what became of him). The uncertainty lives in the CONTENT: counts
    // are rounded to hearsay steps and composition is given in words, never exact rosters.
    // AkhbaarScoutBehavior owns the engine side.
    public static class AkhbaarMath
    {
        // ── The fee ──────────────────────────────────────────────────────────────────
        // An obscure lord costs more to trace than a famous one — everyone on the road can
        // point to the Nizam's camp; nobody has heard of a tier-1 sardar. Hunting outside
        // your own realm costs half again: your court's registers don't cover foreign lords.
        public const int BaseCost = 300;
        public const int ObscurityFeePerTier = 50;
        public const float ForeignRealmFactor = 1.5f;
        public const int MaxClanTier = 6;

        public static int DispatchCost(int clanTier, bool sameRealm)
        {
            int tier = Math.Max(0, Math.Min(MaxClanTier, clanTier));
            float cost = BaseCost + (MaxClanTier - tier) * ObscurityFeePerTier;
            if (!sameRealm) cost *= ForeignRealmFactor;
            return (int)cost;
        }

        // ── The road ─────────────────────────────────────────────────────────────────
        // A harkara relay outrides any army, but he still has to get there, ask around and
        // ride back. Two days minimum even for a lord camped outside the gates; twelve at
        // the far end of Hindostan.
        public const float UnitsPerDay = 100f;
        public const float MinDays = 2f;
        public const float MaxDays = 12f;

        public static float DaysToLocate(float distance)
        {
            float d = MinDays + Math.Max(0f, distance) / UnitsPerDay;
            return Math.Max(MinDays, Math.Min(MaxDays, d));
        }

        // ── The report's language ────────────────────────────────────────────────────
        // Hearsay rounding: a scout counts a handful exactly, estimates a war band to the
        // nearest score-and-five, and a host only to the nearest fifty.
        public static int RoughCount(int actual)
        {
            if (actual <= 0) return 0;
            if (actual < 10) return actual;
            int step = actual < 50 ? 10 : actual < 200 ? 25 : 50;
            return (actual + step / 2) / step * step;
        }

        public static string StrengthWord(int men)
            => men <= 0 ? "no men under arms"
             : men < 25 ? "a meagre escort"
             : men < 80 ? "a modest war band"
             : men < 200 ? "a strong force"
             : men < 400 ? "a formidable host"
             : "a great host";

        // Composition in words, arms ordered by share, anything under a tenth of the whole
        // beneath the scout's notice. E.g. "chiefly horse, with foot and bows".
        public static string CompositionLine(int foot, int bows, int horse)
        {
            int total = foot + bows + horse;
            if (total <= 0) return "no fighting men to speak of";

            var arms = new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(foot, "foot"),
                new KeyValuePair<int, string>(bows, "bows"),
                new KeyValuePair<int, string>(horse, "horse"),
            }.Where(a => a.Key * 10 >= total).OrderByDescending(a => a.Key).ToList();

            float leadShare = arms[0].Key / (float)total;
            string head = leadShare >= 0.65f ? "almost wholly " + arms[0].Value
                        : leadShare >= 0.45f ? "chiefly " + arms[0].Value
                        : "a mixed force, " + arms[0].Value + " foremost";
            if (arms.Count == 1) return head;
            return head + ", with " + string.Join(" and ", arms.Skip(1).Select(a => a.Value));
        }
    }
}
