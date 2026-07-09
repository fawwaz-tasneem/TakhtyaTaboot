using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // The court's master of decrees. RoyalFarmaan consults this behavior before showing
    // anything: it remembers when each dedupe-key was last put before the player (so a
    // seasonal demand cannot stack five deep), and it collects DOWNGRADED routine notices
    // — stipend receipts, monthly summaries — into a weekly "Court Circular" digest, so
    // the decrees that DO interrupt the player are the ones that matter.
    // Decision rules live in Util/FarmaanFlow (unit-tested).
    public class FarmaanDirectorBehavior : CampaignBehaviorBase
    {
        public static FarmaanDirectorBehavior Instance { get; private set; }

        private const int CircularCap = 20;

        private Dictionary<string, int> _lastShown = new Dictionary<string, int>(); // dedupeKey -> day
        private List<string> _circular = new List<string>();                         // pending digest lines

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("FarmaanDirector.WeeklyTick", OnWeeklyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            var keys = _lastShown.Keys.ToList();
            var days = _lastShown.Values.ToList();
            dataStore.SyncData("hind_far_keys", ref keys);
            dataStore.SyncData("hind_far_days", ref days);
            dataStore.SyncData("hind_far_circular", ref _circular);
            if (!dataStore.IsSaving)
            {
                _lastShown = new Dictionary<string, int>();
                for (int i = 0; i < keys.Count && i < days.Count; i++) _lastShown[keys[i]] = days[i];
                if (_circular == null) _circular = new List<string>();
            }
        }

        // ── Contract with RoyalFarmaan ───────────────────────────────────────────────
        public int LastShownDay(string dedupeKey)
            => !string.IsNullOrEmpty(dedupeKey) && _lastShown.TryGetValue(dedupeKey, out int day) ? day : -1;

        public void RecordShown(string dedupeKey, int day)
        {
            if (!string.IsNullOrEmpty(dedupeKey)) _lastShown[dedupeKey] = day;
        }

        // A routine notice that will not interrupt: one line in the message log now, one
        // entry in the weekly Court Circular later.
        public void Downgrade(string title, string body)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"The court records: {title}.", Color.FromUint(0xFF9A8866)));

            string line = string.IsNullOrEmpty(body) ? title
                : $"{title} — {(body.Length > 110 ? body.Substring(0, 110).TrimEnd() + "…" : body)}";
            _circular.Add(line);
            while (_circular.Count > CircularCap) _circular.RemoveAt(0);
        }

        // ── The weekly Court Circular ────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            if (!Config.Tune.FarmaanDigest) { _circular.Clear(); return; }
            if (_circular.Count < 3) return; // too thin to bother the court with

            string bodyText = "The waqai-nawis sets down the week's lesser notices of the court:\n\n  • "
                              + string.Join("\n  • ", _circular);
            _circular.Clear();
            RoyalFarmaan.Issue("The Court Circular", "From the office of the waqai-nawis (court diarist)",
                bodyText, seal: "Entered in the registers, " + RoyalFarmaan.CurrentDate(),
                primary: "Noted", dedupeKey: "court_circular", cooldownDays: 6);
        }
    }
}
