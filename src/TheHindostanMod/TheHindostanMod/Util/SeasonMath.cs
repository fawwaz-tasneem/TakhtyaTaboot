namespace TakhtyaTaboot.Util
{
    // Pure math for the seasons of Hindostan (wiki Ch.05/17 §1). The engine's four
    // seasons (0 spring, 1 summer, 2 autumn, 3 winter) are read as the subcontinent's:
    // the hot season, the monsoon (Barsaat), the bright post-monsoon, the cool season.
    // NO TaleWorlds types — linked into TheHindostanMod.Tests (SeasonMathTests).
    public static class SeasonMath
    {
        public const int HotSeason = 0;    // engine spring
        public const int Monsoon = 1;      // engine summer
        public const int PostMonsoon = 2;  // engine autumn
        public const int CoolSeason = 3;   // engine winter

        // Campaign-map movement: the rains mire armies; the dry, bright weeks after
        // them are the marching season.
        public static float MoveSpeedMultiplier(int season)
            => season == Monsoon ? 0.70f
             : season == PostMonsoon ? 1.10f
             : 1.0f;

        public static string SeasonName(int season)
            => season == HotSeason ? "the hot season (Garmi)"
             : season == Monsoon ? "the monsoon (Barsaat)"
             : season == PostMonsoon ? "the bright season (Sharad)"
             : "the cool season (Sardi)";

        // Label shown on the party-speed tooltip; null when the season moves nothing.
        public static string SpeedExplanation(int season)
            => season == Monsoon ? "Monsoon mud"
             : season == PostMonsoon ? "Dry roads of the marching season"
             : null;

        // ── The harvest (wiki Ch.17 §4 — monsoon beyond speed) ───────────────────────
        // The year's rains decide the harvest. Village tax accrues all year, but the fat
        // collection comes in after the rains: a bountiful monsoon (quality→1) swells the
        // post-monsoon harvest, a failed one (quality→0) thins it, with a milder echo into
        // the cool season. The hot season and the monsoon itself carry no harvest bonus —
        // the grain is still in the ground.
        //   quality: 0 (rains failed) .. 1 (bountiful), the year's monsoon.
        public static float HarvestTaxMultiplier(int season, float monsoonQuality)
        {
            float q = monsoonQuality < 0f ? 0f : monsoonQuality > 1f ? 1f : monsoonQuality;
            switch (season)
            {
                case PostMonsoon: return 0.5f + 0.9f * q;   // 0.50 .. 1.40
                case CoolSeason:  return 0.8f + 0.35f * q;  // 0.80 .. 1.15
                default:          return 1.0f;
            }
        }

        public const float FailedMonsoonThreshold = 0.35f;
        public const float BountifulMonsoonThreshold = 0.75f;

        public static bool IsFailedMonsoon(float quality) => quality < FailedMonsoonThreshold;
        public static bool IsBountifulMonsoon(float quality) => quality > BountifulMonsoonThreshold;

        // How the year's rains read, for the monsoon farmaan.
        public static string MonsoonVerdict(float quality)
            => IsBountifulMonsoon(quality) ? "The rains came full and timely — a bountiful year is promised."
             : IsFailedMonsoon(quality) ? "The rains failed. The land will hunger before the next monsoon."
             : "The rains were middling — neither feast nor famine, the common lot of years.";

        // Daily chance (0..1) that a village tips into famine during a failed-rains harvest
        // window. Only bites when the monsoon actually failed; then it rises as the granary
        // (hearth) thins and disorder (threat) keeps the grain from the people. Zero in a
        // fair or good year — famine is the failure of the rains, not mere banditry.
        public static float FamineDailyChance(float monsoonQuality, float hearth, float threat)
        {
            if (!IsFailedMonsoon(monsoonQuality)) return 0f;
            float shortfall = (FailedMonsoonThreshold - monsoonQuality) / FailedMonsoonThreshold; // 0..1
            float hunger = hearth <= 0f ? 1f : Clamp(1f - hearth / 600f, 0f, 1f);                 // thin hearth = hungrier
            float disorder = Clamp(threat, 0f, 100f) / 100f;
            return Clamp(0.010f * shortfall * (0.4f + hunger) * (0.5f + disorder), 0f, 0.05f);
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
