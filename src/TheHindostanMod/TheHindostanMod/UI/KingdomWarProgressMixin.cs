using System;
using System.Collections.Generic;
using System.Linq;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot.UI
{
    // THE WAR SCREEN TELLS THE TRUTH (wiki ch.30 §3a). Every war in the kingdom tab's diplomacy list now
    // carries a PROGRESS BAR toward its aim — and hovering it says exactly what moved it.
    //
    // The rule the whole design rests on: if a contribution cannot be named in the tooltip, it must not
    // silently move the bar. So this renders WarProgressMath's breakdown verbatim — the itemized ledger
    // (battles won x14 -> +140), the named targets of a war of conquest and which are taken, and — for a
    // war of subjugation — all three collapse conditions with their LIVE values, so the player never has
    // to guess why "demand submission" is out of reach.
    //
    // The bar is a plain filled widget scaled by a float property, not a real ProgressBar: the vanilla
    // war tuple has no bar brush to reuse, and a scaled Widget cannot get the sizing wrong.
    [ViewModelMixin("RefreshValues")]
    internal sealed class KingdomWarProgressMixin : BaseViewModelMixin<KingdomWarItemVM>
    {
        private string _text = "";
        private float _fill;
        private HintViewModel _hint = new HintViewModel();

        public KingdomWarProgressMixin(KingdomWarItemVM vm) : base(vm) { }

        [DataSourceProperty]
        public string HindWarProgressText
        {
            get => _text;
            set { if (_text != value) { _text = value; ViewModel?.OnPropertyChangedWithValue(value, nameof(HindWarProgressText)); } }
        }

        // 0..1 — the bar's width as a fraction of the track.
        [DataSourceProperty]
        public float HindWarProgressFill
        {
            get => _fill;
            set { if (Math.Abs(_fill - value) > 0.001f) { _fill = value; ViewModel?.OnPropertyChangedWithValue(value, nameof(HindWarProgressFill)); } }
        }

        [DataSourceProperty]
        public HintViewModel HindWarProgressHint
        {
            get => _hint;
            set { if (_hint != value) { _hint = value; ViewModel?.OnPropertyChangedWithValue(value, nameof(HindWarProgressHint)); } }
        }

        public override void OnRefresh()
        {
            try
            {
                Kingdom mine = Hero.MainHero?.Clan?.Kingdom;
                Kingdom theirs = FactionOf(ViewModel) as Kingdom;
                var wf = WarfareBehavior.Instance;

                if (mine == null || theirs == null || wf == null || ThroneWar.IsRebelKingdom(theirs))
                {
                    // A throne war has no bar: it is won or lost, never measured.
                    HindWarProgressText = ThroneWar.IsRebelKingdom(theirs)
                        ? "A war for the throne — won or lost, never settled"
                        : "";
                    HindWarProgressFill = 0f;
                    HindWarProgressHint = new HintViewModel();
                    return;
                }

                var snap = wf.SnapshotFor(mine, theirs);
                float pct = WarProgressMath.Percent(snap);

                HindWarProgressFill = pct / 100f;
                HindWarProgressText =
                    $"{WarProgressMath.AimName(snap.Aim)}{(snap.IsDefender ? " (we defend)" : "")} — " +
                    $"{pct:0}%, {WarProgressMath.Headline(snap)}";

                var lines = wf.ProgressBreakdown(mine, theirs);
                string tip = string.Join("\n", lines.Select(l =>
                    $"{l.Label}: {l.Value}" + (string.IsNullOrEmpty(l.Detail) ? "" : $"  ({l.Detail})")));

                // Exhaustion explains itself too: it always accrued from named sources, and always threw
                // the provenance away. Not any more.
                var wex = WarExhaustionBehavior.Instance;
                if (wex != null)
                {
                    float ours = wex.Exhaustion(mine, theirs);
                    float thm = wex.Exhaustion(theirs, mine);
                    tip += $"\n\nOur realm is {WarExhaustionMath.Tier(ours)} ({ours:0}); theirs is {WarExhaustionMath.Tier(thm)} ({thm:0}).";
                    var rows = wex.BreakdownOf(mine, theirs);
                    if (rows.Count > 0)
                        tip += "\nWhat has worn us down — " +
                               string.Join(" · ", rows.Select(r => $"{r.label} {r.value:0}"));
                }

                int truce = wf.TruceDaysLeft(mine, theirs);
                if (truce > 0) tip += $"\n\nA truce stands for {truce} more days.";

                HindWarProgressHint = new HintViewModel(new TextObject(string.IsNullOrWhiteSpace(tip)
                    ? "Nothing has yet moved this war."
                    : tip));
            }
            catch (Exception e)
            {
                TYTLog.Error("KingdomWarProgressMixin.OnRefresh failed", e);
                HindWarProgressText = "";
                HindWarProgressFill = 0f;
                HindWarProgressHint = new HintViewModel();
            }
        }

        // KingdomWarItemVM keeps the other realm in a private field; its public surface exposes only the
        // visual/name. Read it reflectively rather than guessing at a property that may be renamed.
        private static IFaction FactionOf(KingdomWarItemVM vm)
        {
            if (vm == null) return null;
            try
            {
                var t = vm.GetType();
                foreach (string name in new[] { "Faction2", "_faction2", "Faction2Object", "_opponentFaction" })
                {
                    var p = t.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (p?.GetValue(vm) is IFaction pf) return pf;
                    var f = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (f?.GetValue(vm) is IFaction ff) return ff;
                }
                // Last resort: any IFaction-typed field that is not our own realm.
                Kingdom mine = Hero.MainHero?.Clan?.Kingdom;
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                    if (typeof(IFaction).IsAssignableFrom(f.FieldType) && f.GetValue(vm) is IFaction cand && cand != (IFaction)mine)
                        return cand;
            }
            catch (Exception e) { TYTLog.Error("KingdomWarProgressMixin: could not read the war's other realm", e); }
            return null;
        }
    }

    // The bar itself, appended under the war tuple's name. The vanilla tuple is a fixed-height button with
    // a horizontal ListPanel; we append a vertical strip so the bar sits beneath the realm's name without
    // disturbing the banner or the crossed-swords icon.
    //
    // NOTE (Modding-Findings ch.18): vertical stacks must use VerticalBottomToTop to read top-down. The
    // track is a Widget with a fixed width and a child scaled by HindWarProgressFill.
    [PrefabExtension("WarTuple", "descendant::TextWidget[@Text='@Faction2Name']")]
    internal sealed class KingdomWarProgressBar : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText]
        public string Content =>
            "<ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" " +
            "VerticalAlignment=\"Center\" MarginRight=\"20\" MarginTop=\"2\" " +
            "StackLayout.LayoutMethod=\"VerticalBottomToTop\" HintWidget=\"@HindWarProgressHint\">" +
            "<Children>" +

            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"20\" " +
            "Brush=\"ArmyManagement.Army.Tuple.Name\" Brush.FontSize=\"15\" IsDisabled=\"true\" " +
            "Text=\"@HindWarProgressText\" />" +

            // The track, and the fill scaled inside it.
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"6\" " +
            "MarginTop=\"2\" Sprite=\"BlankWhiteSquare_9\" Color=\"#2A2118FF\" AlphaFactor=\"0.9\">" +
            "<Children>" +
            "<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
            "HorizontalAlignment=\"Left\" Sprite=\"BlankWhiteSquare_9\" Color=\"#D4AF37FF\" " +
            "ScaleToFitWidth=\"true\" WidthFactor=\"@HindWarProgressFill\" />" +
            "</Children>" +
            "</Widget>" +

            "</Children>" +
            "</ListPanel>";
    }
}
