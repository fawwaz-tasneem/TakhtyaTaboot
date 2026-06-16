using TaleWorlds.Library;

namespace TakhtyaTaboot.UI
{
    // One column of the tree gutter to the left of a node, drawing the connector lines.
    // A column is either an ancestor "pass-through" vertical line, or the node's own
    // elbow (vertical down from the parent + a horizontal stub to the node). Each is
    // composed of three optional segments so it renders correctly at a fixed row height.
    public class TreeGuideCellVM : ViewModel
    {
        private bool _topVert;
        private bool _bottomVert;
        private bool _horiz;

        public TreeGuideCellVM(bool topVert, bool bottomVert, bool horiz)
        {
            _topVert = topVert;
            _bottomVert = bottomVert;
            _horiz = horiz;
        }

        // Vertical line over the upper half of the row (connects up to the parent line).
        [DataSourceProperty]
        public bool TopVert { get => _topVert; set { if (_topVert != value) { _topVert = value; OnPropertyChangedWithValue(value); } } }

        // Vertical line over the lower half (the branch continues to a sibling below).
        [DataSourceProperty]
        public bool BottomVert { get => _bottomVert; set { if (_bottomVert != value) { _bottomVert = value; OnPropertyChangedWithValue(value); } } }

        // Horizontal stub from the line out to the node (only on the node's own column).
        [DataSourceProperty]
        public bool Horiz { get => _horiz; set { if (_horiz != value) { _horiz = value; OnPropertyChangedWithValue(value); } } }
    }
}
