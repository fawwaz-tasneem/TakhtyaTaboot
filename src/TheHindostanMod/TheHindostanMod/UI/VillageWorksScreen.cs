using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace TakhtyaTaboot.UI
{
    // Hosts the village works ledger as a focus layer over the current screen
    // (same hosting pattern as HierarchyScreen).
    public class VillageWorksScreen : ScreenBase
    {
        private readonly Settlement _village;
        private GauntletLayer _layer;
        private VillageWorksVM _vm;
        private bool _closing;

        private VillageWorksScreen(Settlement village) { _village = village; }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            _vm = new VillageWorksVM(_village, Close);
            _layer = new GauntletLayer("HindostanVillageWorks", 1000, false);
            _layer.LoadMovie("HindostanVillageWorks", _vm);
            _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            AddLayer(_layer);
            _layer.IsFocusLayer = true;
            ScreenManager.TrySetFocus(_layer);
        }

        protected override void OnFrameTick(float dt)
        {
            base.OnFrameTick(dt);
            if (_layer != null && _layer.Input.IsKeyReleased(InputKey.Escape))
                Close();
        }

        public void Close()
        {
            if (_closing) return;
            _closing = true;
            ScreenManager.PopScreen();
        }

        protected override void OnFinalize()
        {
            base.OnFinalize();
            if (_layer != null)
            {
                _layer.IsFocusLayer = false;
                RemoveLayer(_layer);
                _layer = null;
            }
            _vm?.OnFinalize();
            _vm = null;
        }

        public static void Open(Settlement village)
        {
            if (Campaign.Current == null || village == null || !village.IsVillage) return;
            ScreenManager.PushScreen(new VillageWorksScreen(village));
        }
    }
}
