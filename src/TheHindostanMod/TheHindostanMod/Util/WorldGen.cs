namespace TakhtyaTaboot.Util
{
    // New-game world generation runs the engine's hero/clan/settlement creation in PARALLEL, and it
    // raises CampaignEvents (OnSettlementOwnerChanged, OnClanChangedKingdom, ...) for every object as
    // it goes. A behavior handler that mutates non-thread-safe state (a Dictionary/List) from one of
    // those events is then called concurrently from many threads -> the managed heap corrupts and the
    // game native-crashes (0xC0000005) with no managed exception and no tyt_crash file. This has bitten
    // us repeatedly and intermittently (the race only sometimes collides), so the fix is a single shared
    // gate rather than per-behavior flags.
    //
    // Contract: Ready is false during world-gen and true once we are on the live map.
    //  - Reset to false in HindostanSubModule.OnGameStart, which runs BEFORE world-gen for every campaign
    //    (new game AND save load), so it is campaign-safe (a per-process static would otherwise stay
    //    stale-true when starting a second campaign without restarting the game).
    //  - Set to true at OnSessionLaunched (fires after world-gen / after a save finishes loading), by the
    //    guard behavior registered first in OnGameStart.
    //
    // Every handler that fires during world-gen and touches shared state must early-return on !Ready.
    // Skipping the initial world-gen assignment is also semantically correct: those handlers are meant to
    // react to settlements/clans CHANGING hands in play, not to the one-time initial distribution.
    public static class WorldGen
    {
        public static volatile bool Ready;
    }

    // Registered first so its OnSessionLaunched publishes WorldGen.Ready once the live map is up.
    public class WorldGenGuardBehavior : TaleWorlds.CampaignSystem.CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            // RegisterEvents runs inside OnGameStart, before the parallel world-gen storm.
            WorldGen.Ready = false;
            TaleWorlds.CampaignSystem.CampaignEvents.OnSessionLaunchedEvent
                .AddNonSerializedListener(this, _ => WorldGen.Ready = true);
        }

        public override void SyncData(TaleWorlds.CampaignSystem.IDataStore dataStore) { }
    }
}
