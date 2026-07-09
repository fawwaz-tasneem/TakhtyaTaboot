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
    }
}
