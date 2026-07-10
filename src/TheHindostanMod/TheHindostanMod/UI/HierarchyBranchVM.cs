using System;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace TakhtyaTaboot.UI
{
    // One COLUMN of the hierarchy board: a direct vassal of the sovereign at its head, and
    // beneath him his own subtree (sub-vassals, then the zamindars of his villages) as an
    // indented member list. The board lays these columns out side by side under the
    // sovereign, troop-tree style.
    public class HierarchyBranchVM : ViewModel
    {
        private readonly string _encyclopediaLink;
        private readonly Action<string> _onActivate;
        private string _name;
        private string _subtitle;
        private bool _isClickable;
        private MBBindingList<HierarchyNodeVM> _members;
        private ImageIdentifierVM _visual;

        public HierarchyBranchVM(string name, string subtitle, bool isClickable,
            string encyclopediaLink, MBBindingList<HierarchyNodeVM> members, Action<string> onActivate,
            ImageIdentifierVM visual = null)
        {
            _name = name ?? "";
            _subtitle = subtitle ?? "";
            _isClickable = isClickable;
            _encyclopediaLink = encyclopediaLink;
            _members = members ?? new MBBindingList<HierarchyNodeVM>();
            _onActivate = onActivate;
            _visual = visual;
        }

        [DataSourceProperty]
        public ImageIdentifierVM Visual { get => _visual; set { if (_visual != value) { _visual = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string Subtitle { get => _subtitle; set { if (_subtitle != value) { _subtitle = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool IsClickable { get => _isClickable; set { if (_isClickable != value) { _isClickable = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public MBBindingList<HierarchyNodeVM> Members { get => _members; set { if (_members != value) { _members = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool HasMembers => _members != null && _members.Count > 0;

        public void ExecuteOpenEncyclopedia()
        {
            if (_isClickable) _onActivate?.Invoke(_encyclopediaLink);
        }
    }
}
