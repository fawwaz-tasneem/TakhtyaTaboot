using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TakhtyaTaboot
{
    // Delete this file once you have verified the culture selection screen looks correct.
    public class CultureVerificationBehavior : CampaignBehaviorBase
    {
        private static readonly string[] ExpectedCultures =
        {
            "empire", "empire_w", "empire_s",
            "sturgia", "aserai", "vlandia", "battania", "khuzait"
        };

        private static readonly string[] ExpectedNames =
        {
            "Mughal", "Bengal", "Hyderabad",
            "Afghan", "Mysore", "Rajput", "Maratha", "Sikh"
        };

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
        }

        private void OnNewGame(CampaignGameStarter _)
        {
            Debug.Print("[Hindostan] CultureVerificationBehavior running...");
            bool allOk = true;
            for (int i = 0; i < ExpectedCultures.Length; i++)
            {
                string id   = ExpectedCultures[i];
                string name = ExpectedNames[i];
                var culture = Campaign.Current.ObjectManager.GetObject<CultureObject>(id);
                if (culture == null)
                {
                    Debug.Print($"[Hindostan] MISSING culture: {id}");
                    allOk = false;
                    continue;
                }
                string actual = culture.Name.ToString();
                if (actual != name)
                {
                    Debug.Print($"[Hindostan] WRONG NAME for {id}: expected '{name}', got '{actual}'");
                    allOk = false;
                }
                else
                {
                    Debug.Print($"[Hindostan] OK: {id} = '{actual}'");
                }
            }
            if (allOk)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Hindostan] All 8 culture names verified OK.",
                    Color.FromUint(0xFF44AA44)));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Hindostan] Culture verification FAILED — check the log file.",
                    Color.FromUint(0xFFCC2200)));
            }
        }

        public override void SyncData(IDataStore dataStore) { }
    }
}
