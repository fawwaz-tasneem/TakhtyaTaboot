using System;
using System.Collections.Generic;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Library;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot.UI
{
    // The akhbaar scout, dispatched from the lord's own encyclopedia page (playtest: hunting a
    // name in the court-menu list was tedious — you look a lord UP, then send the runner). A
    // button under the bookmark star dispatches after the page's hero; the "Akhbaar" stats row
    // (EncyclopediaHeroStatsPatch) shows the scout on the road and his last reported whereabouts.
    // The court-menu list remains for dispatching without opening the encyclopedia.
    [ViewModelMixin("RefreshValues")]
    internal sealed class EncyclopediaHeroAkhbaarMixin : BaseViewModelMixin<EncyclopediaHeroPageVM>
    {
        private string _text = "";
        private bool _visible;

        public EncyclopediaHeroAkhbaarMixin(EncyclopediaHeroPageVM vm) : base(vm) { }

        [DataSourceProperty]
        public string HindAkhbaarText
        {
            get => _text;
            set { if (_text != value) { _text = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public bool HindAkhbaarVisible
        {
            get => _visible;
            set { if (_visible != value) { _visible = value; OnPropertyChangedWithValue(value); } }
        }

        public override void OnRefresh()
        {
            try
            {
                var behavior = AkhbaarScoutBehavior.Instance;
                Hero hero = ViewModel?.Obj as Hero;
                if (behavior == null || hero == null || Campaign.Current == null)
                { HindAkhbaarVisible = false; return; }

                if (behavior.CanDispatchFor(hero, out string label, out string reason))
                { HindAkhbaarText = label; HindAkhbaarVisible = true; }
                else if (behavior.IsTracked(hero))
                { HindAkhbaarText = "A scout is on his trail…"; HindAkhbaarVisible = true; }
                else HindAkhbaarVisible = false;
            }
            catch (Exception e) { TYTLog.Error("AkhbaarMixin.OnRefresh failed", e); HindAkhbaarVisible = false; }
        }

        [DataSourceMethod]
        public void ExecuteHindAkhbaarDispatch()
        {
            TYTLog.Guard("Akhbaar.EncyclopediaButton", () =>
            {
                Hero hero = ViewModel?.Obj as Hero;
                if (hero == null) return;
                AkhbaarScoutBehavior.Instance?.DispatchFromEncyclopedia(hero);
                OnRefresh();
                ViewModel?.RefreshValues(); // pull the new "Akhbaar" stats row / button label in
            });
        }
    }

    // The button itself, inserted after the bookmark star at the top-left of the hero page.
    // Reuses the farmaan popup's proven button brushes.
    [PrefabExtension("EncyclopediaHeroPage", "//ButtonWidget[@Id='BookmarkButton']")]
    internal sealed class EncyclopediaHeroAkhbaarButton : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText]
        public string Content =>
            "<ButtonWidget DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"Fixed\" " +
            "SuggestedHeight=\"44\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" MarginLeft=\"8\" MarginTop=\"62\" " +
            "Brush=\"Popup.Done.Button.NineGrid\" Command.Click=\"ExecuteHindAkhbaarDispatch\" IsVisible=\"@HindAkhbaarVisible\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"34\" " +
            "MarginLeft=\"18\" MarginRight=\"18\" VerticalAlignment=\"Center\" Brush=\"Popup.Button.Text\" " +
            "Brush.FontSize=\"22\" Text=\"@HindAkhbaarText\" IsDisabled=\"true\" />" +
            "</Children>" +
            "</ButtonWidget>";
    }
}
