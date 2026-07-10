using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace TakhtyaTaboot.UI
{
    // Root view model for the imperial hierarchy viewer. The left list holds every
    // kingdom; selecting one builds a TROOP-TREE-STYLE BOARD on the right — the sovereign's
    // card at the top centre, and beneath him one COLUMN per direct vassal (the great town
    // and castle lords), each column stacking that lord's own subtree: sub-vassals, then
    // the zamindars of his villages, indented with connector gutters (TreeGuideCellVM).
    public class HierarchyVM : ViewModel
    {
        private readonly Action _onClose;
        private MBBindingList<KingdomItemVM> _kingdoms;
        private MBBindingList<HierarchyBranchVM> _branches;
        private string _titleText;
        private string _selectedKingdomName;
        private string _hintText;
        private string _rootName;
        private string _rootSubtitle;
        private string _rootLink;
        private ImageIdentifierVM _rootVisual;
        private KingdomItemVM _selected;
        private Kingdom _currentKingdom;
        private bool _showZamindars; // off by default: the board shows the lords; toggle to see the village rung

        public HierarchyVM(Action onClose)
        {
            _onClose = onClose;
            _kingdoms = new MBBindingList<KingdomItemVM>();
            _branches = new MBBindingList<HierarchyBranchVM>();
            _titleText = "The Imperial Hierarchy of Hindostan";
            _hintText = "Select a realm. The tree branches from the sovereign down through his lords to the village zamindars. Click any noble for their encyclopedia entry.";
            _selectedKingdomName = "";
            _rootName = "";
            _rootSubtitle = "";

            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && !UnifiedEmpireBehavior.IsDormant(k))
                                          .OrderByDescending(k => k.CurrentTotalStrength))
                _kingdoms.Add(new KingdomItemVM(k, OnKingdomSelected));

            KingdomItemVM initial = _kingdoms.FirstOrDefault(i => i.Kingdom == Hero.MainHero?.Clan?.Kingdom)
                                  ?? _kingdoms.FirstOrDefault();
            if (initial != null) OnKingdomSelected(initial);
        }

        [DataSourceProperty]
        public MBBindingList<KingdomItemVM> Kingdoms { get => _kingdoms; set { if (_kingdoms != value) { _kingdoms = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public MBBindingList<HierarchyBranchVM> Branches { get => _branches; set { if (_branches != value) { _branches = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string RootName { get => _rootName; set { if (_rootName != value) { _rootName = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string RootSubtitle { get => _rootSubtitle; set { if (_rootSubtitle != value) { _rootSubtitle = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public ImageIdentifierVM RootVisual { get => _rootVisual; set { if (_rootVisual != value) { _rootVisual = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string ZamindarToggleLabel => _showZamindars ? "Hide the zamindars" : "Show the zamindars";

        public void ExecuteToggleZamindars()
        {
            _showZamindars = !_showZamindars;
            OnPropertyChanged(nameof(ZamindarToggleLabel));
            if (_currentKingdom != null) BuildTree(_currentKingdom);
        }

        [DataSourceProperty]
        public string TitleText { get => _titleText; set { if (_titleText != value) { _titleText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string SelectedKingdomName { get => _selectedKingdomName; set { if (_selectedKingdomName != value) { _selectedKingdomName = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string HintText { get => _hintText; set { if (_hintText != value) { _hintText = value; OnPropertyChangedWithValue(value); } } }

        private void OnKingdomSelected(KingdomItemVM item)
        {
            if (_selected != null) _selected.IsSelected = false;
            _selected = item;
            item.IsSelected = true;
            SelectedKingdomName = item.Name;
            BuildTree(item.Kingdom);
        }

        // ── Board construction ───────────────────────────────────────────────────────
        // The sovereign crowns the board; each of his DIRECT vassals heads a column, and the
        // column stacks that lord's own subtree (indented with connector gutters). Zamindars
        // are the lowest rung and only appear when toggled on.
        private void BuildTree(Kingdom kingdom)
        {
            _currentKingdom = kingdom;
            _branches.Clear();
            RootName = "";
            RootSubtitle = "";
            _rootLink = null;
            if (kingdom?.Leader == null) { OnPropertyChanged(nameof(Branches)); return; }
            Hero ruler = kingdom.Leader;

            RootName = ruler.Name?.ToString() ?? "—";
            RootSubtitle = RulerSubtitle(kingdom, ruler);
            _rootLink = ruler.EncyclopediaLink;
            RootVisual = FaceOf(ruler);

            var visited = new HashSet<string> { ruler.StringId };
            foreach (Hero lord in ChildrenOf(ruler, kingdom, visited))
            {
                var members = new MBBindingList<HierarchyNodeVM>();
                EmitMembers(members, ChildrenOf(lord, kingdom, visited), kingdom, visited, new List<bool>());
                _branches.Add(new HierarchyBranchVM(lord.Name?.ToString() ?? "—",
                    NodeSubtitle(lord), true, lord.EncyclopediaLink, members, ActivateLink, FaceOf(lord)));
            }
            OnPropertyChanged(nameof(Branches));
        }

        private static ImageIdentifierVM FaceOf(Hero h)
            => h?.CharacterObject == null ? null
             : new CharacterImageIdentifierVM(CharacterCode.CreateFrom(h.CharacterObject));

        private void EmitMembers(MBBindingList<HierarchyNodeVM> members, List<Hero> kids,
            Kingdom kingdom, HashSet<string> visited, List<bool> path)
        {
            for (int i = 0; i < kids.Count; i++)
            {
                Hero kid = kids[i];
                bool hasNext = i < kids.Count - 1;
                var myPath = new List<bool>(path) { hasNext };

                members.Add(new HierarchyNodeVM(kid.Name?.ToString() ?? "—",
                    NodeSubtitle(kid), true, kid.EncyclopediaLink, BuildGuides(myPath), ActivateLink, FaceOf(kid)));

                EmitMembers(members, ChildrenOf(kid, kingdom, visited), kingdom, visited, myPath);
            }
        }

        public void ExecuteOpenRoot()
        {
            if (!string.IsNullOrEmpty(_rootLink)) ActivateLink(_rootLink);
        }

        // A holder's feudal children: the lords who answer to him, then the zamindars of
        // the villages beneath his seats.
        private List<Hero> ChildrenOf(Hero holder, Kingdom kingdom, HashSet<string> visited)
        {
            var ft = FeudalTitlesBehavior.Instance;
            var lords = new List<Hero>();
            var zamindars = new List<Hero>();

            foreach (Clan c in kingdom.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != holder))
            {
                Hero h = c.Leader;
                if (visited.Contains(h.StringId)) continue;
                if (ft?.GetFeudalLiege(h) == holder) { visited.Add(h.StringId); lords.Add(h); }
            }

            // The village rung is heavy (hundreds of names on a big realm) — gathered only
            // when the toggle asks for it. Zamindars always sort after the lords, so they
            // land on the LOWEST rung of each branch.
            if (_showZamindars && ft != null && holder.Clan != null)
                foreach (Settlement seat in holder.Clan.Settlements.Where(s => s.IsTown || s.IsCastle))
                    foreach (Village v in seat.Town?.Villages ?? Enumerable.Empty<Village>())
                    {
                        Hero z = ft.GetVillageLord(v.Settlement);
                        if (z != null && z != holder && !visited.Contains(z.StringId))
                        { visited.Add(z.StringId); zamindars.Add(z); }
                    }

            lords = lords
                .OrderByDescending(h => FeudalTitlesBehavior.Instance?.GetTierRank(h) ?? 0)
                .ThenByDescending(h => MansabdariBehavior.Instance?.GetRankIndex(h.Clan) ?? 0)
                .ThenByDescending(h => h.Clan?.CurrentTotalStrength ?? 0f)
                .ToList();
            zamindars = zamindars.OrderBy(h => h.Name?.ToString()).ToList();
            return lords.Concat(zamindars).ToList();
        }

        // path[k] = "the ancestor at depth k+1 (on this node's path) has a sibling below".
        // The last entry is the node itself. Columns before the last are ancestor
        // pass-through lines; the last is the node's elbow.
        private static MBBindingList<TreeGuideCellVM> BuildGuides(List<bool> path)
        {
            var cells = new MBBindingList<TreeGuideCellVM>();
            for (int k = 0; k < path.Count; k++)
            {
                bool last = k == path.Count - 1;
                if (last) cells.Add(new TreeGuideCellVM(topVert: true, bottomVert: path[k], horiz: true));
                else cells.Add(new TreeGuideCellVM(topVert: path[k], bottomVert: path[k], horiz: false));
            }
            return cells;
        }

        // ── Node labels ──────────────────────────────────────────────────────────────
        private static string RulerSubtitle(Kingdom kingdom, Hero ruler)
        {
            string title = kingdom.EncyclopediaRulerTitle?.ToString() ?? "Sovereign";
            string legit = LegitimacyBehavior.Instance != null ? $" · Legitimacy {LegitimacyBehavior.Instance.GetTier(ruler)}" : "";
            string auth = ImperialAuthorityBehavior.Instance != null ? $" · Authority {ImperialAuthorityBehavior.Instance.GetTier(kingdom)}" : "";
            return $"{title} of {kingdom.Name}{legit}{auth}";
        }

        private static string NodeSubtitle(Hero h)
        {
            var ft = FeudalTitlesBehavior.Instance;
            string tier = ft?.GetTier(h) ?? "";

            // A prince of a reigning (or fallen) line carries the royal style first.
            string royal = DynastyBehavior.Instance?.RoyalStyle(h);
            string royalPart = string.IsNullOrEmpty(royal) ? "" : royal + " · ";

            if (ft != null && ft.IsVillageZamindar(h) && !(h.Clan?.Settlements.Any(s => s.IsTown || s.IsCastle) ?? false))
            {
                var villages = ft.GetVillagesLordedBy(h);
                string vlist = villages.Count > 0 ? string.Join(", ", villages.Select(v => v.Name)) : "";
                return $"{royalPart}Village Zamindar — {vlist}";
            }

            string rankTitle = MansabdariBehavior.Instance?.GetTitle(h.Clan) ?? "";
            int mansab = MansabdariBehavior.Instance?.GetMansab(h.Clan) ?? 0;
            string rankPart = string.IsNullOrEmpty(rankTitle) || rankTitle == "Unranked" ? "Unranked" : $"{rankTitle} (mansab {mansab})";
            var seats = h.Clan?.Settlements.Where(s => s.IsTown || s.IsCastle).Select(s => s.Name?.ToString()).ToList() ?? new List<string>();
            string seatPart = seats.Count > 0 ? $" · {string.Join(", ", seats.Take(3))}" : "";
            string tierPart = string.IsNullOrEmpty(tier) ? "" : tier + " · ";
            return $"{royalPart}{tierPart}{rankPart}{seatPart}";
        }

        private void ActivateLink(string link)
        {
            _onClose?.Invoke();
            if (!string.IsNullOrEmpty(link) && Campaign.Current != null)
            {
                try { Campaign.Current.EncyclopediaManager.GoToLink(link); }
                catch { /* stale link — ignore */ }
            }
        }

        public void ExecuteClose() => _onClose?.Invoke();
    }
}
