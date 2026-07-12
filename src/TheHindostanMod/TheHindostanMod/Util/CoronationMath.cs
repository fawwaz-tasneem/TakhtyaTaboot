using System;

namespace TakhtyaTaboot.Util
{
    // The arithmetic of a coronation darbar, PURE and unit-tested. When a new sovereign
    // accedes he summons the great houses to swear; who comes and who sends an empty place
    // turns on what each house head thinks of him (his effective opinion). A snubbed ruler
    // may demand a late oath — harder to win than mere attendance, because a lord who
    // already stayed away must now be made to bend in public. Deterministic given the roll,
    // so the ceremony can be reasoned about and tested. CoronationBehavior owns the engine side.
    public static class CoronationMath
    {
        // ── Attendance ───────────────────────────────────────────────────────────────
        // Most lords attend a coronation out of duty; disposition tips the balance. At
        // neutral standing roughly two in three come; deep resentment empties the hall,
        // warm regard fills it. Never certain either way (a floor and a ceiling).
        public static float AttendanceChance(float effectiveOpinion)
            => Clamp(0.65f + effectiveOpinion / 120f, 0.03f, 0.99f);

        public static bool Attends(float effectiveOpinion, float rng01)
            => rng01 < AttendanceChance(effectiveOpinion);

        // ── The late oath ────────────────────────────────────────────────────────────
        // Demanded of those who stayed away. Harder than attendance: a lord who already
        // snubbed the throne loses face by bending now, so only warmer dispositions comply;
        // the cold ones dig in and the grudge hardens.
        public static float LateOathChance(float effectiveOpinion)
            => Clamp(0.35f + effectiveOpinion / 150f, 0.02f, 0.9f);

        public static bool AcceptsLateOath(float effectiveOpinion, float rng01)
            => rng01 < LateOathChance(effectiveOpinion);

        // ── The summons deadline ─────────────────────────────────────────────────────
        // A summoned darbar cannot wait forever: past the deadline the oaths are taken by
        // courier instead (the instant resolution) and the moment's majesty is lost.
        public const int CeremonyDeadlineDays = 14;

        public static bool SummonsLapsed(float summonDay, float nowDay)
            => summonDay >= 0f && nowDay - summonDay > CeremonyDeadlineDays;

        // ── The register of the oath ─────────────────────────────────────────────────
        // How a lord speaks his oath in the hall, by his regard for the new sovereign:
        // glad, dutiful, or through his teeth.
        public enum OathRegister { Warm, Even, Cold }

        public static OathRegister RegisterOf(float effectiveOpinion)
            => effectiveOpinion >= 20f ? OathRegister.Warm
             : effectiveOpinion <= -5f ? OathRegister.Cold
             : OathRegister.Even;

        // ── The verdict of the hall ──────────────────────────────────────────────────
        // How the gathering reads, for the coronation farmaan.
        public static string LoyaltyVerdict(int attended, int summoned)
        {
            if (summoned <= 0) return "You hold court alone; there are no great houses to summon.";
            float f = (float)attended / summoned;
            return f >= 0.999f ? "Every house bent the knee — your accession is unquestioned."
                 : f >= 0.75f ? "The great houses came in strength; your throne stands on firm ground."
                 : f >= 0.5f ? "A working majority swore, but the empty places are noted."
                 : f >= 0.25f ? "More stayed away than came — your grip on the realm is thin."
                 : "The hall stood near empty. The lords do not accept you.";
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
