namespace TakhtyaTaboot.Config
{
    // The single point every behaviour reads tuning values through. It pulls from the
    // live MCM settings when present and falls back to the same compiled defaults if MCM
    // has not initialised (or fails), so the mod always runs. Each getter is wrapped so a
    // problem in the settings layer can never throw into gameplay code.
    public static class Tune
    {
        private static TYTSettings S
        {
            get { try { return TYTSettings.Instance; } catch { return null; } }
        }

        // Career: valour & elevation
        public static float ValourPerWin => S?.ValourPerWin ?? 4f;
        public static float ValourSiegeMultiplier => S?.ValourSiegeMultiplier ?? 2f;
        public static float ValourKingCapture => S?.ValourKingCapture ?? 80f;
        public static float ValourPerRankStep => S?.ValourPerRankStep ?? 30f;
        public static float RenownPerRankStep => S?.RenownPerRankStep ?? 60f;
        public static int MinRelationForElevation => S?.MinRelationForElevation ?? 0;

        // Career: muster, demotion, stipend
        public static float TroopCapacityMultiplier => S?.TroopCapacityMultiplier ?? 1f;
        public static int BaseTroopCapacity => S?.BaseTroopCapacity ?? 30;
        public static float RetentionFraction => S?.RetentionFraction ?? 0.8f;
        public static int DemoteGraceDays => S?.DemoteGraceDays ?? 30;
        public static float StipendPerTroop => S?.StipendPerTroop ?? 2f;

        // Council & capital
        public static int KingCouncilsPerYear => S?.KingCouncilsPerYear ?? 4;
        public static int LordCouncilsPerYear => S?.LordCouncilsPerYear ?? 1;
        public static int MoveCapitalCost => S?.MoveCapitalCost ?? 200000;

        // Villages & patrol
        public static float PatrolOverwhelmChance => S?.PatrolOverwhelmChance ?? 0.05f;
        public static float MilitiaDefenceWeight => S?.MilitiaDefenceWeight ?? 1f;
        public static int ReliefDetachmentSize => S?.ReliefDetachmentSize ?? 40;
    }
}
