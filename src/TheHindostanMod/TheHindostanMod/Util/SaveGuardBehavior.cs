using TaleWorlds.CampaignSystem;

namespace TakhtyaTaboot.Util
{
    // Tracks whether a save is in progress. The game's save serializer reads
    // Hero.EncyclopediaText for every hero while it writes the campaign; any Harmony
    // patch that returns a freshly-built TextObject from that getter makes the serializer
    // choke ("SAVE ERROR. Cant find … with type TextObject") once per lord. While a save
    // is running we let such getters fall back to the vanilla, serializer-known value.
    // See EncyclopediaInfoPatch.
    public class SaveGuardBehavior : CampaignBehaviorBase
    {
        public static bool IsSaving { get; private set; }

        // False until the campaign is fully built and running. During world generation the engine
        // mass-creates heroes whose clan/family relations are only half-wired; anything that reads
        // a hero's EncyclopediaText then (e.g. our biography generator, which walks Father/Spouse/
        // Children) can native-crash on a not-yet-valid relation. We compute nothing until ready.
        public static bool CampaignReady { get; private set; }

        public override void RegisterEvents()
        {
            CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener(this, () => { IsSaving = true; TYTLog.Crumb("save: serialization started"); });
            CampaignEvents.OnSaveOverEvent.AddNonSerializedListener(this, (_, __) => { IsSaving = false; TYTLog.Crumb("save: serialization finished"); });
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => { CampaignReady = true; });
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, _ => { CampaignReady = true; });

            // Drain any farmaan that was queued during a settlement tick. This fires on the campaign
            // tick — outside the engine's settlement iteration — so pushing the focus layer here is
            // safe, unlike pushing it synchronously from inside the tick (which native-crashed).
            CampaignEvents.TickEvent.AddNonSerializedListener(this, _ => UI.RoyalFarmaan.Pump());
        }

        public override void SyncData(IDataStore dataStore) { }
    }
}
