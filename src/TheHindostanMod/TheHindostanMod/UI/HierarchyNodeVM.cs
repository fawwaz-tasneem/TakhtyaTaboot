using System;
using TaleWorlds.Library;

namespace TakhtyaTaboot.UI
{
    // One node in the feudal tree: a person with a name and a one-line description,
    // preceded by a gutter of connector-line columns ({Guides}) that draw the branches
    // back to their liege. Clicking opens the noble's encyclopedia entry.
    public class HierarchyNodeVM : ViewModel
    {
        private readonly string _encyclopediaLink;
        private readonly Action<string> _onActivate;
        private string _name;
        private string _subtitle;
        private bool _isClickable;
        private MBBindingList<TreeGuideCellVM> _guides;

        public HierarchyNodeVM(string name, string subtitle, bool isClickable,
            string encyclopediaLink, MBBindingList<TreeGuideCellVM> guides, Action<string> onActivate)
        {
            _name = name ?? "";
            _subtitle = subtitle ?? "";
            _isClickable = isClickable;
            _encyclopediaLink = encyclopediaLink;
            _guides = guides ?? new MBBindingList<TreeGuideCellVM>();
            _onActivate = onActivate;
        }

        [DataSourceProperty]
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string Subtitle { get => _subtitle; set { if (_subtitle != value) { _subtitle = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool IsClickable { get => _isClickable; set { if (_isClickable != value) { _isClickable = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public MBBindingList<TreeGuideCellVM> Guides { get => _guides; set { if (_guides != value) { _guides = value; OnPropertyChangedWithValue(value); } } }

        public void ExecuteOpenEncyclopedia()
        {
            if (_isClickable) _onActivate?.Invoke(_encyclopediaLink);
        }
    }
}
