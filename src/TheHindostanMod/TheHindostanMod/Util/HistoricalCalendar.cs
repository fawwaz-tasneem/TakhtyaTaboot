namespace TakhtyaTaboot.Util
{
    // Single source of truth for mapping between the engine's game-year and the historical AD
    // year. The campaign opens at the death of Aurangzeb (AD 1707) — game-year BaseGameYear —
    // and history unfolds from there (Option A: a unified empire that fractures).
    //
    // Pure integer math, no engine types, so it is unit-tested directly
    // (TheHindostanMod.Tests/HistoricalCalendarTests). The scripted-events timeline authors its
    // episodes in AD years and converts through here; FarmaanScreen renders the dated decrees
    // through here too. Change the base in ONE place and everything follows.
    public static class HistoricalCalendar
    {
        public const int BaseGameYear = 1084;  // the engine year a fresh campaign starts in
        public const int BaseADYear   = 1707;  // Aurangzeb dies; the Mughal empire begins to fracture

        // game-year -> AD year (and back).
        public static int ToADYear(int gameYear) => BaseADYear + (gameYear - BaseGameYear);
        public static int ToGameYear(int adYear) => BaseGameYear + (adYear - BaseADYear);

        // True once the campaign has reached (or passed) a given AD year — the timeline's fire test.
        public static bool HasReached(int currentGameYear, int adYear) => currentGameYear >= ToGameYear(adYear);

        // Whole game-years elapsed since the campaign opened (never negative).
        public static int YearsElapsed(int currentGameYear) => System.Math.Max(0, currentGameYear - BaseGameYear);

        // The Hijri (AH) year for an AD year — the dating every Mughal decree actually carried.
        // Lunar years run ~3% faster than solar; the linear form below lands within the correct
        // AH year across the mod's whole era (pinned by tests: Aurangzeb dies 1707 AD = 1118-19
        // AH; the Sayyids depose Farrukhsiyar 1719 AD = 1131 AH; Nadir sacks Delhi 1739 = 1151).
        public static int HijriYear(int adYear)
            => (int)System.Math.Floor((adYear - 621.5643) * 1.0306888);
    }
}
