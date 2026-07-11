using System;

namespace TakhtyaTaboot.Util
{
    // The arithmetic of bonded labour (bandi/begar) on a village fief, PURE and unit-tested.
    // Battle captives set to work in a village raise its yield and feed the bound town's
    // market — but forced labour breeds resentment (threat) and the gang thins over time as
    // men escape or die, faster where the district is already lawless. Deliberately a real
    // trade-off the player can reason about, not a free productivity switch.
    // SlaveLabourBehavior owns the engine side; VillageDevelopmentBehavior reads the yields.
    public static class SlaveLabourMath
    {
        // ── Capacity ─────────────────────────────────────────────────────────────────
        // A village absorbs bonded hands in proportion to its hearth — a populous village
        // can house, feed and watch more of them. Floored at a handful, ceilinged so the
        // gang never dwarfs the free population.
        public const int MinCap = 5;
        public const int MaxCap = 60;
        public const float HearthPerLabourer = 40f;

        public static int LabourCap(float hearth)
            => (int)Clamp(MinCap + Math.Max(0f, hearth) / HearthPerLabourer, MinCap, MaxCap);

        // ── Yields ───────────────────────────────────────────────────────────────────
        // Each labourer adds a slice of the village's daily tax (as a % bonus, fed into the
        // same tax pipeline as the built works) and a trickle of prosperity to the bound
        // town (labour on the roads and fields feeds the market).
        public const float TaxPctPerLabourer = 0.4f;
        public const float ProsperityPerLabourer = 0.02f;

        public static float TaxBonusPct(int labourers) => Math.Max(0, labourers) * TaxPctPerLabourer;
        public static float BoundProsperityPerDay(int labourers) => Math.Max(0, labourers) * ProsperityPerLabourer;

        // ── Unrest ───────────────────────────────────────────────────────────────────
        // Daily threat ADDED by holding the gang. Resentment scales with the gang's size
        // AND how full the village is of it: a near-capacity gang (little free population to
        // hold it down) is far more dangerous per head than a few labourers among many free
        // villagers. Fed into VillageFiefMath.ThreatStep as its `unrest` term.
        public const float UnrestPerLabourer = 0.08f;

        public static float DailyUnrest(int labourers, float hearth)
        {
            if (labourers <= 0) return 0f;
            int cap = LabourCap(hearth);
            float fill = cap > 0 ? Clamp((float)labourers / cap, 0f, 1f) : 1f;
            return labourers * UnrestPerLabourer * (0.5f + fill); // 0.04..0.12 per head/day
        }

        // ── Attrition & escape ───────────────────────────────────────────────────────
        // Each day the gang thins: a base rate plus more where the district is lawless
        // (chaos covers a runaway). Deterministic expectation, with the fractional part
        // resolved by a single supplied roll so behaviour and tests stay in lockstep.
        public const float BaseLossRate = 0.01f;
        public const float ThreatLossRate = 0.04f;

        public static int DailyLoss(int labourers, float threat, float rng01)
        {
            if (labourers <= 0) return 0;
            float rate = BaseLossRate + ThreatLossRate * Clamp(threat, 0f, 100f) / 100f; // 1%..5%/day
            float expected = labourers * rate;
            int whole = (int)expected;
            if (rng01 < expected - whole) whole += 1;
            return Math.Min(labourers, whole);
        }

        // Of the men lost on a given day, how many FLED (rather than died) — fugitives swell
        // the banditry around the village, a threat spike the behaviour applies. The rest are
        // attrition. Half flee, rounded up, so a single runaway still counts as a flight.
        public static int Fugitives(int lost) => lost <= 0 ? 0 : (lost + 1) / 2;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
