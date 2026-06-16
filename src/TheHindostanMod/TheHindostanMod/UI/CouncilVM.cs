using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace TheHindostanMod.UI
{
    // Root view model for the Council screen ("the Darbar"). The left list holds the
    // councils you can see — the Imperial Darbar, your liege's council, and your own;
    // selecting one shows its three offices on the right. On your own council a seat
    // can be clicked to appoint; on your liege's, to petition.
    public class CouncilVM : ViewModel
    {
        private readonly Action _onClose;
        private MBBindingList<CouncilTabVM> _councils;
        private MBBindingList<CouncilEntryVM> _entries;
        private string _titleText;
        private string _selectedCouncilName;
        private string _hintText;
        private CouncilTabVM _selected;

        public CouncilVM(Action onClose)
        {
            _onClose = onClose;
            _councils = new MBBindingList<CouncilTabVM>();
            _entries = new MBBindingList<CouncilEntryVM>();
            _titleText = "The Councils of Hindostan";
            _hintText = "Select a council. On your own council, click a seat to appoint; on your liege's, to petition.";
            _selectedCouncilName = "";

            var cb = CouncilBehavior.Instance;
            Hero imperial = Hero.MainHero?.Clan?.Kingdom?.Leader;
            Hero liege = cb?.PlayerLiege();
            bool ownHolder = CouncilBehavior.IsCouncilHolder(Hero.MainHero);

            if (ownHolder)
                _councils.Add(new CouncilTabVM("My Own Council", Hero.MainHero, OnSelected));
            if (liege != null && liege != Hero.MainHero)
                _councils.Add(new CouncilTabVM($"My Liege's Council ({liege.Name})", liege, OnSelected));
            if (imperial != null && imperial != Hero.MainHero && imperial != liege)
                _councils.Add(new CouncilTabVM($"The Imperial Darbar ({imperial.Name})", imperial, OnSelected));
            if (imperial == Hero.MainHero && !ownHolder)
                _councils.Add(new CouncilTabVM("The Imperial Darbar", Hero.MainHero, OnSelected));

            CouncilTabVM first = _councils.Count > 0 ? _councils[0] : null;
            if (first != null) OnSelected(first);
        }

        [DataSourceProperty]
        public MBBindingList<CouncilTabVM> Councils { get => _councils; set { if (_councils != value) { _councils = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public MBBindingList<CouncilEntryVM> Entries { get => _entries; set { if (_entries != value) { _entries = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string TitleText { get => _titleText; set { if (_titleText != value) { _titleText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string SelectedCouncilName { get => _selectedCouncilName; set { if (_selectedCouncilName != value) { _selectedCouncilName = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string HintText { get => _hintText; set { if (_hintText != value) { _hintText = value; OnPropertyChangedWithValue(value); } } }

        private void OnSelected(CouncilTabVM tab)
        {
            if (_selected != null) _selected.IsSelected = false;
            _selected = tab;
            tab.IsSelected = true;
            SelectedCouncilName = tab.Name;
            BuildEntries(tab.Holder);
        }

        private void BuildEntries(Hero holder)
        {
            _entries.Clear();
            var cb = CouncilBehavior.Instance;
            if (cb == null || holder == null) return;

            bool isMine = holder == Hero.MainHero;
            bool isLiege = holder == cb.PlayerLiege();

            foreach (CouncilBehavior.Post p in new[]
                { CouncilBehavior.Post.Vizier, CouncilBehavior.Post.MirBakshi, CouncilBehavior.Post.Diwan })
            {
                CouncilBehavior.Post post = p;
                Hero seated = cb.GetCouncillor(holder, post);
                string who = seated != null ? (seated == Hero.MainHero ? "you" : seated.Name.ToString()) : "— vacant —";
                string name = $"{CouncilBehavior.PostTitle(post)}: {who}";

                string action;
                bool clickable;
                if (isMine) { action = "Click to appoint or replace."; clickable = true; }
                else if (isLiege) { action = seated == Hero.MainHero ? "You hold this seat." : "Click to petition for this seat."; clickable = seated != Hero.MainHero; }
                else { action = "Observing only."; clickable = false; }

                _entries.Add(new CouncilEntryVM(name, action, CouncilBehavior.PostPerk(post), clickable,
                    () => { _onClose?.Invoke(); cb.ScreenAction(holder, post); }));
            }
        }

        public void ExecuteClose() => _onClose?.Invoke();
    }
}
