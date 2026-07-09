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
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => { CampaignReady = true; IsSaving = false; });
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, _ => { CampaignReady = true; IsSaving = false; });

            // Drain any farmaan that was queued during a settlement tick. This fires on the campaign
            // tick — outside the engine's settlement iteration — so pushing the focus layer here is
            // safe, unlike pushing it synchronously from inside the tick (which native-crashed).
            // Also a stuck-flag backstop: campaign ticks never run during synchronous serialization,
            // so a tick observed with IsSaving still set means OnSaveOverEvent was missed (an
            // errored/aborted save) — clear it, or every guarded getter stays degraded forever.
            CampaignEvents.TickEvent.AddNonSerializedListener(this, _ =>
            {
                if (IsSaving) { IsSaving = false; TYTLog.Warn("save: IsSaving was still set on a campaign tick — cleared (missed OnSaveOverEvent)."); }
                UI.RoyalFarmaan.Pump();
            });
        }

        public override void SyncData(IDataStore dataStore) { }
    }
}
