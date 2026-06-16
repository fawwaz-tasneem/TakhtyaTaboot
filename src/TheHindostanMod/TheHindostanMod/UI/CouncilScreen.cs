using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace TheHindostanMod.UI
{
    // Hosts the Council ("Darbar") screen as a focus layer over the current screen.
    public class CouncilScreen : ScreenBase
    {
        private GauntletLayer _layer;
        private CouncilVM _vm;
        private bool _closing;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            _vm = new CouncilVM(Close);
            _layer = new GauntletLayer("HindostanCouncil", 1000, false);
            _layer.LoadMovie("HindostanCouncil", _vm);
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

        public static void Open()
        {
            if (Campaign.Current == null) return;
            ScreenManager.PushScreen(new CouncilScreen());
        }
    }
}
