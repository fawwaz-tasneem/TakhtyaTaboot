using System;

namespace TakhtyaTaboot.Util
{
    // What one PERSON thinks of another — the individual layer the clan-based engine
    // lacks. Every record is typed, dated, and decays with a half-life; the sum of live
    // records rides on top of vanilla relation to give an "effective opinion" between
    // any two heroes (princes, spouses, notables included — not just clan leaders).
    // NO TaleWorlds types — linked into TheHindostanMod.Tests (OpinionMathTests).
    public static class OpinionMath
    {
        // Serialized as ints in SyncData: order must never change. Append only.
        public enum OpinionType
        {
            SworeFealty = 0, MissedCeremony = 1, Grudge = 2, Favor = 3,
            CourtRuling = 4, Insult = 5, GiftReceived = 6, KinBond = 7
        }

        // (default magnitude, half-life in days). CourtRuling's magnitude is signed by
        // the caller (ruled for you / against you); its default here is the FOR case.
        private static readonly (float magnitude, int halfLifeDays)[] Table =
        {
            (+8f, 180),   // SworeFealty — an oath freshly renewed warms the liege
            (-10f, 360),  // MissedCeremony — an empty place at the coronation is long remembered
            (-12f, 540),  // Grudge — a quarrel pressed rather than mended
            (+10f, 270),  // Favor — a good turn done
            (+8f, 360),   // CourtRuling — justice rendered in one's favour (negate for against)
            (-8f, 240),   // Insult — words spoken at court
            (+6f, 180),   // GiftReceived
            (+6f, 3600),  // KinBond — blood cools very slowly
        };

        public const float DeadThreshold = 0.5f; // |value| below this = the record is forgotten
        public const float EffectiveCap = 100f;

        public static float DefaultMagnitude(OpinionType t) => Table[(int)t].magnitude;
        public static int HalfLifeDays(OpinionType t) => Table[(int)t].halfLifeDays;

        // Exponential decay: half the feeling remains after one half-life.
        public static float CurrentValue(float magnitude, int ageDays, int halfLifeDays)
        {
            if (halfLifeDays <= 0) return magnitude;
            if (ageDays <= 0) return magnitude;
            return magnitude * (float)Math.Pow(2.0, -(double)ageDays / halfLifeDays);
        }

        public static float CurrentValue(OpinionType t, float magnitude, int ageDays)
            => CurrentValue(magnitude, ageDays, HalfLifeDays(t));

        public static bool IsDead(float currentValue) => Math.Abs(currentValue) < DeadThreshold;

        // Vanilla relation stays the base; live modifiers ride on top, clamped.
        public static float Effective(int vanillaRelation, float modifierSum)
        {
            float v = vanillaRelation + modifierSum;
            return v < -EffectiveCap ? -EffectiveCap : v > EffectiveCap ? EffectiveCap : v;
        }

        // Short court-register label for display ("Disposition toward you" rows).
        public static string Describe(OpinionType t)
        {
            switch (t)
            {
                case OpinionType.SworeFealty: return "an oath sworn";
                case OpinionType.MissedCeremony: return "an empty place at the ceremony";
                case OpinionType.Grudge: return "an old grudge";
                case OpinionType.Favor: return "a favour done";
                case OpinionType.CourtRuling: return "a judgement at court";
                case OpinionType.Insult: return "an insult";
                case OpinionType.GiftReceived: return "a gift received";
                case OpinionType.KinBond: return "the bond of blood";
                default: return "an old matter";
            }
        }
    }
}
