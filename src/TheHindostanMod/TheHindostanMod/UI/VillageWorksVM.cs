using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace TakhtyaTaboot.UI
{
    // The village works ledger (roadmap A.3): a proper construction screen in the style of the
    // town's — the district's numbers up top (hearth, threat, militia, coffer, tax estimate),
    // the work UNDER WAY with a progress bar, the queued work, and the full project list with
    // costs, effects and one-click start/queue. All reads and acts go through
    // VillageDevelopmentBehavior's UI façade; this class holds no campaign state of its own.
    public class VillageWorksVM : ViewModel
    {
        // Width in px of a full progress bar in the prefab; BarWidth = fraction * this.
        private const int BarFullWidth = 420;

        private readonly Settlement _village;
        private readonly Action _onClose;
        private MBBindingList<VillageProjectItemVM> _projects;
        private string _titleText, _statsLine, _cofferLine, _activeLine, _queuedLine, _builtLine;
        private bool _hasActive;
        private float _barWidth;
        private bool _canCollect;

        public VillageWorksVM(Settlement village, Action onClose)
        {
            _village = village;
            _onClose = onClose;
            _projects = new MBBindingList<VillageProjectItemVM>();
            Refresh();
        }

        public void Refresh()
        {
            var dev = VillageDevelopmentBehavior.Instance;
            Settlement s = _village;
            if (dev == null || s?.Village == null) { _onClose?.Invoke(); return; }

            TitleText = $"The Works of {s.Name}";

            Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
            StatsLine = $"Hearth {(int)s.Village.Hearth}   ·   Militia {(int)s.Militia}   ·   " +
                        $"Bandit threat {dev.GetThreat(s):0}/100 ({dev.GetThreatBand(s)})   ·   " +
                        $"Zamindar: {(z != null ? z.Name.ToString() : "vacant")}";
            CofferLine = $"Coffer: {(int)dev.GetTreasury(s)} rupees   (≈ {dev.TaxPerDayEstimate(s):0.0}/day)   ·   Your purse: {Hero.MainHero.Gold:n0}";
            CanCollect = dev.GetTreasury(s) >= 1f;

            HasActive = dev.TryGetActiveWork(s, out string activeName, out float done, out int daysLeft);
            ActiveLine = HasActive
                ? $"Under construction: {activeName} — {(int)(done * 100f)}%  ({daysLeft} day(s) left)"
                : "No work is under way. The masons stand idle.";
            BarWidth = HasActive ? Math.Max(6f, done * BarFullWidth) : 0f;

            string queued = dev.GetQueuedWorkName(s);
            QueuedLine = string.IsNullOrEmpty(queued) ? "" : $"Queued next: {queued} (charged when work begins)";

            var works = dev.DescribeWorks(s);
            var built = works.Where(w => w.IsBuilt).Select(w => w.Name).ToList();
            BuiltLine = built.Count > 0 ? "Standing works: " + string.Join(", ", built) : "Standing works: none";

            _projects.Clear();
            foreach (var w in works.Where(w => !w.IsBuilt))
                _projects.Add(new VillageProjectItemVM(w, () =>
                {
                    dev.UiStartWork(s, w.Id);
                    Refresh();
                }));
            OnPropertyChanged(nameof(Projects));
        }

        public void ExecuteCollect()
        {
            VillageDevelopmentBehavior.Instance?.UiCollectTaxes(_village);
            Refresh();
        }

        public void ExecuteClose() => _onClose?.Invoke();

        [DataSourceProperty]
        public MBBindingList<VillageProjectItemVM> Projects { get => _projects; set { if (_projects != value) { _projects = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string TitleText { get => _titleText; set { if (_titleText != value) { _titleText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string StatsLine { get => _statsLine; set { if (_statsLine != value) { _statsLine = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string CofferLine { get => _cofferLine; set { if (_cofferLine != value) { _cofferLine = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string ActiveLine { get => _activeLine; set { if (_activeLine != value) { _activeLine = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string QueuedLine { get => _queuedLine; set { if (_queuedLine != value) { _queuedLine = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string BuiltLine { get => _builtLine; set { if (_builtLine != value) { _builtLine = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool HasActive { get => _hasActive; set { if (_hasActive != value) { _hasActive = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool HasQueued => !string.IsNullOrEmpty(_queuedLine);

        // FLOAT, not int: this binds to a widget's SuggestedWidth, a float on the Gauntlet side,
        // and Gauntlet also writes the value BACK into the VM. Invoking set_BarWidth(Single)
        // against an int property throws inside invoke, which then broke ExecuteAct and crashed
        // the game (the mosque-build crash of 2026-07-12). Any VM property bound to a widget
        // layout attribute must match the widget-side type exactly.
        [DataSourceProperty]
        public float BarWidth { get => _barWidth; set { if (_barWidth != value) { _barWidth = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool CanCollect { get => _canCollect; set { if (_canCollect != value) { _canCollect = value; OnPropertyChangedWithValue(value); } } }
    }

    // One buildable work in the ledger.
    public class VillageProjectItemVM : ViewModel
    {
        private readonly Action _onAct;
        private string _name, _costLine, _effect, _statusText, _actLabel, _hint;
        private bool _canAct, _showAct;

        public VillageProjectItemVM(VillageDevelopmentBehavior.WorkItem w, Action onAct)
        {
            _onAct = onAct;
            _name = w.Name;
            _costLine = $"{w.Cost:n0} rupees · {w.Days} days";
            _effect = w.Effect + (string.IsNullOrEmpty(w.PrereqName) ? "" : $"  (needs {w.PrereqName})");
            _statusText = w.IsActive ? "UNDER WAY" : w.IsQueued ? "QUEUED" : "";
            _showAct = !w.IsActive && !w.IsQueued;
            _canAct = w.CanAct;
            _actLabel = w.WillQueue ? "Queue" : "Begin";
            _hint = w.CanAct
                ? (w.WillQueue ? "Queued behind the current work; cost charged when it begins." : "")
                : w.DisabledReason;
        }

        public void ExecuteAct() { if (_canAct) _onAct?.Invoke(); }

        [DataSourceProperty]
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string CostLine { get => _costLine; set { if (_costLine != value) { _costLine = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string Effect { get => _effect; set { if (_effect != value) { _effect = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string StatusText { get => _statusText; set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string Hint { get => _hint; set { if (_hint != value) { _hint = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string ActLabel { get => _actLabel; set { if (_actLabel != value) { _actLabel = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool CanAct { get => _canAct; set { if (_canAct != value) { _canAct = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool ShowAct { get => _showAct; set { if (_showAct != value) { _showAct = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool ShowStatus => !string.IsNullOrEmpty(_statusText);
    }
}
