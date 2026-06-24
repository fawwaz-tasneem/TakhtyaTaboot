namespace TakhtyaTaboot.Util
{
    // Rusukh (رسوخ) — a fief-holder's ENTRENCHMENT with the local notables: 0..100 per
    // (holder, fief). Grows the longer he holds and the better he stands with the notables;
    // decays FASTER once he is removed. High Rusukh buys local backing (influence, money,
    // bigger levies) and lets a holder defy a transfer order.
    //
    // This is the PURE, engine-free core (unit-tested in TheHindostanMod.Tests). RusukhBehavior
    // owns the per-fief state and the engine effects; all the curves live here so they can be
    // proven in isolation. Deliberately NOT vanilla hero relation — a separate, slower-moving bond.
    public static class RusukhMath
    {
        public const float Max = 100f;

        private static float Clamp(float v) => v < 0f ? 0f : (v > Max ? Max : v);
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // Daily growth while the holder keeps the fief. Scales with stewardship and his standing
        // with the local notables (relationFactor 0..1). At baseRate 0.30 a capable, well-liked
        // governor reaches deep roots (~80) in roughly two in-game years; a poor one far slower.
        public static float Growth(float current, float baseRate, int stewardSkill, float relationFactor)
        {
            float g = baseRate * (1f + stewardSkill / 200f) * (0.5f + Clamp01(relationFactor));
            return Clamp(current + g);
        }

        // Daily decay once the holder no longer holds the fief — deliberately faster than it grew,
        // because influence fades quickly once you are gone.
        public static float Decay(float current, float decayRate) => Clamp(current - decayRate);

        // Probability (0..1) that a holder can DEFY a crown transfer order: his roots against the
        // crown's reach (authority + legitimacy). The crown's grip is weighted heavily, so only a
        // genuinely entrenched magnate facing a weak throne can resist.
        public static float DefianceChance(float rusukh, float crownAuthority, float crownLegitimacy)
        {
            float crownGrip = Clamp01((crownAuthority + crownLegitimacy) / 200f);
            float roots = Clamp01(rusukh / Max);
            return Clamp01(roots - 0.8f * crownGrip);
        }

        // ── Backing the notables provide, all gated behind a minimum footing (25) ──
        // Periodic influence trickle (per application).
        public static int InfluenceBonus(float rusukh) => rusukh < 25f ? 0 : (int)(rusukh / 25f); // 1..4

        // Periodic gold backing from the local elite (per application).
        public static int GoldBacking(float rusukh) => rusukh < 25f ? 0 : (int)(rusukh / 25f) * 250; // 250..1000

        // Levy size multiplier — local boys rally to an entrenched lord (1.0 .. 1.5).
        public static float LevyMultiplier(float rusukh) => 1f + Clamp01(rusukh / Max) * 0.5f;

        // Coarse tier label for UI/tooltips.
        public static string Tier(float rusukh)
            => rusukh >= 75f ? "Entrenched"
             : rusukh >= 50f ? "Established"
             : rusukh >= 25f ? "Rooted"
             : "Newcomer";
    }
}
