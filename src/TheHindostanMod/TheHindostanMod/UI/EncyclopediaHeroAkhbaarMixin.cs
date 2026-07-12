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
    // The lord's encyclopedia page grows two court instruments (playtest rounds 4 & 6):
    //   • the akhbaar scout — pay a harkara to trail him and bring word BACK (his report also
    //     teaches the game his native "Last seen around..." line);
    //   • the qasid — send YOUR word OUT: when the messenger reaches the lord a conversation
    //     opens as if you stood before him yourself.
    // Round 6: the buttons live IN the left column's own stack, under the name and rank —
    // the old floating button sat on top of the hero's name.
    [ViewModelMixin("RefreshValues")]
    internal sealed class EncyclopediaHeroAkhbaarMixin : BaseViewModelMixin<EncyclopediaHeroPageVM>
    {
        private string _scoutText = "";
        private bool _scoutVisible;
        private string _qasidText = "";
        private bool _qasidVisible;

        public EncyclopediaHeroAkhbaarMixin(EncyclopediaHeroPageVM vm) : base(vm) { }

        [DataSourceProperty]
        public string HindAkhbaarText
        {
            get => _scoutText;
            set { if (_scoutText != value) { _scoutText = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public bool HindAkhbaarVisible
        {
            get => _scoutVisible;
            set { if (_scoutVisible != value) { _scoutVisible = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public string HindQasidText
        {
            get => _qasidText;
            set { if (_qasidText != value) { _qasidText = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public bool HindQasidVisible
        {
            get => _qasidVisible;
            set { if (_qasidVisible != value) { _qasidVisible = value; OnPropertyChangedWithValue(value); } }
        }

        public override void OnRefresh()
        {
            try
            {
                Hero hero = ViewModel?.Obj as Hero;
                if (hero == null || Campaign.Current == null)
                { HindAkhbaarVisible = false; HindQasidVisible = false; return; }

                var scouts = AkhbaarScoutBehavior.Instance;
                if (scouts != null && scouts.CanDispatchFor(hero, out string label, out _))
                { HindAkhbaarText = label; HindAkhbaarVisible = true; }
                else if (scouts != null && scouts.IsTracked(hero))
                { HindAkhbaarText = "A scout is on his trail…"; HindAkhbaarVisible = true; }
                else HindAkhbaarVisible = false;

                var qasids = MessengerBehavior.Instance;
                if (qasids != null && qasids.CanDispatchFor(hero, out string qLabel, out _))
                { HindQasidText = qLabel; HindQasidVisible = true; }
                else if (qasids != null && qasids.IsEnRoute(hero))
                { HindQasidText = "A qasid is on the road…"; HindQasidVisible = true; }
                else HindQasidVisible = false;
            }
            catch (Exception e)
            {
                TYTLog.Error("AkhbaarMixin.OnRefresh failed", e);
                HindAkhbaarVisible = false; HindQasidVisible = false;
            }
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

        [DataSourceMethod]
        public void ExecuteHindQasidDispatch()
        {
            TYTLog.Guard("Qasid.EncyclopediaButton", () =>
            {
                Hero hero = ViewModel?.Obj as Hero;
                if (hero == null) return;
                MessengerBehavior.Instance?.DispatchFromEncyclopedia(hero);
                OnRefresh();
                ViewModel?.RefreshValues();
            });
        }
    }

    // The two buttons, inserted INTO the left column's vertical stack right after the kingdom
    // rank line — they flow with the layout instead of floating over the hero's name (the
    // round-6 complaint). The column's ListPanel uses the ch.18-correct method, so Append
    // renders them below the rank text. Reuses the farmaan popup's proven button brushes.
    [PrefabExtension("EncyclopediaHeroPage", "//TextWidget[@Text='@KingdomRankText']")]
    internal sealed class EncyclopediaHeroAkhbaarButton : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText]
        public string Content =>
            "<ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" " +
            "MarginTop=\"6\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
            "<Children>" +
            "<ButtonWidget DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"Fixed\" " +
            "SuggestedHeight=\"40\" MarginLeft=\"4\" MarginRight=\"4\" VerticalAlignment=\"Center\" " +
            "Brush=\"Popup.Done.Button.NineGrid\" Command.Click=\"ExecuteHindAkhbaarDispatch\" IsVisible=\"@HindAkhbaarVisible\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"30\" " +
            "MarginLeft=\"14\" MarginRight=\"14\" VerticalAlignment=\"Center\" Brush=\"Popup.Button.Text\" " +
            "Brush.FontSize=\"19\" Text=\"@HindAkhbaarText\" IsDisabled=\"true\" />" +
            "</Children>" +
            "</ButtonWidget>" +
            "<ButtonWidget DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"Fixed\" " +
            "SuggestedHeight=\"40\" MarginLeft=\"4\" MarginRight=\"4\" VerticalAlignment=\"Center\" " +
            "Brush=\"Popup.Done.Button.NineGrid\" Command.Click=\"ExecuteHindQasidDispatch\" IsVisible=\"@HindQasidVisible\">" +
            "<Children>" +
            "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"30\" " +
            "MarginLeft=\"14\" MarginRight=\"14\" VerticalAlignment=\"Center\" Brush=\"Popup.Button.Text\" " +
            "Brush.FontSize=\"19\" Text=\"@HindQasidText\" IsDisabled=\"true\" />" +
            "</Children>" +
            "</ButtonWidget>" +
            "</Children>" +
            "</ListPanel>";
    }
}
