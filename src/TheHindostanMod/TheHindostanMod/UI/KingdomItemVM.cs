using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace TakhtyaTaboot.UI
{
    // A selectable kingdom in the left-hand list.
    public class KingdomItemVM : ViewModel
    {
        private readonly Kingdom _kingdom;
        private readonly Action<KingdomItemVM> _onSelect;
        private string _name;
        private string _rulerLine;
        private bool _isSelected;

        public Kingdom Kingdom => _kingdom;

        public KingdomItemVM(Kingdom kingdom, Action<KingdomItemVM> onSelect)
        {
            _kingdom = kingdom;
            _onSelect = onSelect;
            _name = kingdom.Name?.ToString() ?? kingdom.StringId;
            string rulerName = kingdom.Leader?.Name?.ToString() ?? "—";
            _rulerLine = $"{kingdom.EncyclopediaRulerTitle} {rulerName}".Trim();
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
