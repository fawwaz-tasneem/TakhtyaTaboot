using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace TheHindostanMod.UI
{
    // A selectable council in the left-hand list (Imperial / My Liege's / My Own).
    public class CouncilTabVM : ViewModel
    {
        private readonly Action<CouncilTabVM> _onSelect;
        private string _name;
        private string _rulerLine;
        private bool _isSelected;

        public Hero Holder { get; }

        public CouncilTabVM(string label, Hero holder, Action<CouncilTabVM> onSelect)
        {
            Holder = holder;
            _onSelect = onSelect;
            _name = label;
            _rulerLine = holder != null ? $"Council of {holder.Name}" : "";
        }

        [DataSourceProperty]
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string RulerLine { get => _rulerLine; set { if (_rulerLine != value) { _rulerLine = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChangedWithValue(value); } } }

        public void ExecuteSelect() => _onSelect?.Invoke(this);
    }
}
