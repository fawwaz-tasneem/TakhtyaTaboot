using System;
using TaleWorlds.Library;

namespace TakhtyaTaboot.UI
{
    // One council seat row: the office, its current holder, and (when it is your own
    // council or your liege's) a click to appoint or petition for it.
    public class CouncilEntryVM : ViewModel
    {
        private readonly Action _onActivate;
        private string _name;
        private string _subtitle;
        private string _detail;
        private float _indentWidth;
        private bool _isClickable;

        public CouncilEntryVM(string name, string subtitle, string detail, bool isClickable, Action onActivate)
        {
            _name = name ?? "";
            _subtitle = subtitle ?? "";
            _detail = detail ?? "";
            _indentWidth = 20;
            _isClickable = isClickable;
            _onActivate = onActivate;
        }

        [DataSourceProperty]
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string Subtitle { get => _subtitle; set { if (_subtitle != value) { _subtitle = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string Detail { get => _detail; set { if (_detail != value) { _detail = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool HasDetail => !string.IsNullOrEmpty(_detail);

        // FLOAT, not int: bound to a widget's SuggestedWidth (a float), and Gauntlet writes the
        // value back into the VM — set_IndentWidth(Single) against an int throws inside invoke
        // (the same trap that crashed the works ledger; see VillageWorksVM.BarWidth).
        [DataSourceProperty]
        public float IndentWidth { get => _indentWidth; set { if (_indentWidth != value) { _indentWidth = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool IsClickable { get => _isClickable; set { if (_isClickable != value) { _isClickable = value; OnPropertyChangedWithValue(value); } } }

        public void ExecuteAction()
        {
            if (_isClickable) _onActivate?.Invoke();
        }
    }
}
