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
        public static float ValourPerKill => S?.ValourPerKill ?? 0.5f;
        public static float ValourTournamentWin => S?.ValourTournamentWin ?? 3f;
        public static float ValourOutnumberedMultiplier => S?.ValourOutnumberedMultiplier ?? 2f;
        public static float ValourPerRankStep => S?.ValourPerRankStep ?? 30f;
        public static float RenownPerRankStep => S?.RenownPerRankStep ?? 60f;
        public static int MinRelationForElevation => S?.MinRelationForElevation ?? 0;

        // Career: muster, demotion, stipend
        public static float TroopCapacityMultiplier => S?.TroopCapacityMultiplier ?? 1f;
        public static float RetentionFraction => S?.RetentionFraction ?? 0.8f;
        public static int DemoteGraceDays => S?.DemoteGraceDays ?? 30;
        public static float StipendPerTroop => S?.StipendPerTroop ?? 2f;

        // Tenure edict (Feudal <-> Mansabdari)
        public static float TenureLegitimacyFloor => S?.TenureLegitimacyFloor ?? 50f;
        public static float TenureEdictBaseInfluence => S?.TenureEdictBaseInfluence ?? 150f;
        public static float TenureEdictBaseGold => S?.TenureEdictBaseGold ?? 5000f;
        public static float TenureResistThreshold => S?.TenureResistThreshold ?? 0.5f;
        public static int TenureRotationIntervalDays => S?.TenureRotationIntervalDays ?? 1080;

        // Succession law (per-kingdom constitution: primogeniture / election / appointed Wali Ahd)
        public static float SuccLawLegitimacyFloor => S?.SuccLawLegitimacyFloor ?? 50f;
        public static float SuccLawBaseInfluence => S?.SuccLawBaseInfluence ?? 150f;
        public static float HeirSupportBoost => S?.HeirSupportBoost ?? 25f;
        public static float MagnateElectionDecisiveMargin => S?.MagnateElectionDecisiveMargin ?? 1.25f;
        public static int AiLawReviewIntervalDays => S?.AiLawReviewIntervalDays ?? 360;
        public static float SuccessionContestFloor => S?.SuccessionContestFloor ?? 0.20f;

        // Council & capital
        public static int KingCouncilsPerYear => S?.KingCouncilsPerYear ?? 4;
        public static int LordCouncilsPerYear => S?.LordCouncilsPerYear ?? 1;
        public static int MoveCapitalCost => S?.MoveCapitalCost ?? 200000;

        // Villages & patrol
        public static float PatrolOverwhelmChance => S?.PatrolOverwhelmChance ?? 0.05f;
        public static float MilitiaDefenceWeight => S?.MilitiaDefenceWeight ?? 1f;
        public static int ReliefDetachmentSize => S?.ReliefDetachmentSize ?? 40;

        // Village fiefs (development, treasury, AI zamindars)
        public static float VillageTaxPerHearth => S?.VillageTaxPerHearth ?? 0.004f;
        public static float VillageDevelopmentPace => S?.VillageDevelopmentPace ?? 1f;
        public static bool AiVillageDevelopment => S?.AiVillageDevelopment ?? true;
        public static int AiVillageBuildsPerWeek => S?.AiVillageBuildsPerWeek ?? 10;
        public static bool AiZamindarEngineOwnership => S?.AiZamindarEngineOwnership ?? false;
        public static int VillageTaxCollectRelationPenalty => S?.VillageTaxCollectRelationPenalty ?? 2;

        // Bonded labour (battle captives set to work in a village fief)
        public static bool SlaveLabourEnabled => S?.SlaveLabourEnabled ?? true;

        // Nazrana (the courtly gift cycle)
        public static bool NazranaEnabled => S?.NazranaEnabled ?? true;
        public static int NazranaCycleDays => S?.NazranaCycleDays ?? 90;
        public static float NazranaBaseScale => S?.NazranaBaseScale ?? 1f;

        // Seasons
        public static bool MonsoonEnabled => S?.MonsoonEnabled ?? true;
        public static bool MonsoonHarvestEnabled => S?.MonsoonHarvestEnabled ?? true;

        // Farmaans (royal decrees)
        public static bool FarmaanPausesTime => S?.FarmaanPausesTime ?? true;
        public static bool FarmaanDigest => S?.FarmaanDigest ?? true;

        // Dynasty & cadet houses
        public static int CadetGoldCost => S?.CadetGoldCost ?? 25000;
        public static int CadetAiRenownFloor => S?.CadetAiRenownFloor ?? 900;
        public static int CadetMaxHouses => S?.CadetMaxHouses ?? 8;
        public static bool CadetAllowFemale => S?.CadetAllowFemale ?? false;

        // Debug
        public static bool EnableDebugVerification => S?.EnableDebugVerification ?? false;
    }
}
