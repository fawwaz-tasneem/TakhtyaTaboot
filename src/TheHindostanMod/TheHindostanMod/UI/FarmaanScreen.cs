using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot.UI
{
    // Issues royal farmaans as a focus layer over whatever screen is current
    // (usually the map). Farmaans are queued so only one shows at a time, campaign time
    // is PAUSED while one is up (restored on dismissal unless the player had paused
    // himself), and the FarmaanDirectorBehavior dedupes/downgrades recurring notices so
    // each decree that does appear feels real (see Util/FarmaanFlow for the rules).
    //
    // IMPORTANT: pushing the focus layer must NOT happen synchronously while the engine is
    // iterating settlements (inside a daily settlement tick) — that re-enters the screen system
    // mid-iteration and native-crashes. Callers on a settlement tick set SuppressImmediate and
    // rely on Pump() (driven by the campaign TickEvent, off the iteration) to show the popup.
    public static class RoyalFarmaan
    {
        private class Pending
        {
            public string Title, Sender, Body, Seal, Primary, Secondary;
            public Action OnPrimary, OnSecondary;
            public string DedupeKey;
        }

        private static readonly Queue<Pending> _queue = new Queue<Pending>();
        private static bool _active;
        private static string _activeKey;

        // Time-pause bookkeeping: we restore the pre-farmaan speed only if WE paused —
        // a player who had already stopped the clock stays stopped.
        private static bool _wePaused;
        private static CampaignTimeControlMode _prevMode;

        // When set, Issue() only enqueues and does NOT push the layer synchronously. Adding a
        // focus layer while the engine is iterating settlements (a daily settlement tick) is a
        // native re-entrancy crash. Settlement ticks set this; Pump() (called off the iteration,
        // from a campaign tick) drains the queue safely. Menu/dialog callers leave it false and
        // still get an immediate popup.
        public static bool SuppressImmediate;

        // Show the next queued farmaan if one is waiting and none is on screen. Safe to call
        // every tick; it no-ops when idle.
        public static void Pump()
        {
            if (!_active && _queue.Count > 0) ShowNext();
        }
        private static GauntletLayer _layer;
        private static ScreenBase _host;
        private static FarmaanVM _vm;

        // sender: e.g. "By order of Shahenshah Muhammad Shah". seal: short attribution.
        // dedupeKey/priority/cooldownDays: the director's suppression contract — an identical
        // key already queued/on screen is dropped; a Routine notice inside its cooldown (or
        // with none) becomes a log line + Court Circular digest item instead of a popup.
        // Defaults keep every pre-existing call site compiling and behaving as before.
        public static void Issue(string title, string sender, string body, string seal = null,
            string primary = "Receive the decree", Action onPrimary = null,
            string secondary = null, Action onSecondary = null,
            string dedupeKey = null, FarmaanPriority priority = FarmaanPriority.Urgent, int cooldownDays = 0)
        {
            if (Campaign.Current == null) return;

            bool hasActions = onPrimary != null || onSecondary != null;
            if (!string.IsNullOrEmpty(dedupeKey) || priority != FarmaanPriority.Urgent)
            {
                var director = FarmaanDirectorBehavior.Instance;
                int today = (int)CampaignTime.Now.ToDays;
                bool sameKeyLive = !string.IsNullOrEmpty(dedupeKey)
                    && ((_active && _activeKey == dedupeKey) || _queue.Any(q => q.DedupeKey == dedupeKey));
                int lastShown = director?.LastShownDay(dedupeKey) ?? -1;

                switch (FarmaanFlow.Decide(sameKeyLive, lastShown, today, cooldownDays, priority, hasActions))
                {
                    case FarmaanDecision.Drop:
                        return;
                    case FarmaanDecision.Downgrade:
                        director?.Downgrade(title, body);
                        return;
                }
                director?.RecordShown(dedupeKey, today);
            }

            _queue.Enqueue(new Pending
            {
                Title = title, Sender = sender, Body = body, Seal = seal,
                Primary = primary, Secondary = secondary,
                OnPrimary = onPrimary, OnSecondary = onSecondary,
                DedupeKey = dedupeKey,
            });
            if (!SuppressImmediate && !_active) ShowNext();
        }

        // A decree from a kingdom's sovereign.
        public static void FromRuler(Kingdom kingdom, string title, string body,
            string primary = "As you command", Action onPrimary = null,
            string secondary = null, Action onSecondary = null,
            string dedupeKey = null, FarmaanPriority priority = FarmaanPriority.Urgent, int cooldownDays = 0)
        {
            Hero r = kingdom?.Leader;
            string sender = r != null
                ? $"By order of {kingdom.EncyclopediaRulerTitle} {r.Name}, sovereign of {kingdom.Name}"
                : "By order of the Imperial Court";
            Issue(title, sender, body, $"Sealed at the court of {kingdom?.Name}, {CurrentDate()}{RegnalLine(kingdom)}",
                primary, onPrimary, secondary, onSecondary, dedupeKey, priority, cooldownDays);
        }

        // A summons or message from a hero's immediate liege.
        public static void FromLiege(Hero liege, string title, string body,
            string primary = "I obey", Action onPrimary = null,
            string secondary = null, Action onSecondary = null,
            string dedupeKey = null, FarmaanPriority priority = FarmaanPriority.Urgent, int cooldownDays = 0)
        {
            string honorific = liege != null ? Honorific(liege.Clan?.Kingdom) : "";
            string named = liege != null ? (string.IsNullOrEmpty(honorific) ? liege.Name.ToString() : $"{honorific} {liege.Name}") : "your liege lord";
            string sender = $"By the hand of {named}, your liege lord";
            Issue(title, sender, body, $"By the seal of your liege, {CurrentDate()}",
                primary, onPrimary, secondary, onSecondary, dedupeKey, priority, cooldownDays);
        }

        // The in-game date as a Mughal chancery would write it: AD through the historical
        // calendar (campaign opens 1707, Aurangzeb's death) with the Hijri year beside it —
        // the dating every real farmaan actually carried.
        public static string CurrentDate()
        {
            try
            {
                if (Campaign.Current == null) return "";
                int ad = Util.HistoricalCalendar.ToADYear(CampaignTime.Now.GetYear);
                int ah = Util.HistoricalCalendar.HijriYear(ad);
                return $"the {CampaignTime.Now.GetDayOfSeason + 1}th day of {CampaignTime.Now.GetSeasonOfYear}, {ad} AD ({ah} AH)";
            }
            catch { return ""; }
        }

        // "in the 5th year of the reign of Padshah X", when the accession is on record.
        public static string RegnalLine(Kingdom kingdom)
        {
            try
            {
                int? year = CoronationBehavior.Instance?.RegnalYear(kingdom);
                if (year == null || kingdom?.Leader == null) return "";
                return $", in the {Ordinal(year.Value)} year of the reign of {NameWithHonorific(kingdom.Leader)}";
            }
            catch { return ""; }
        }

        private static string Ordinal(int n)
            => n % 10 == 1 && n % 100 != 11 ? n + "st"
             : n % 10 == 2 && n % 100 != 12 ? n + "nd"
             : n % 10 == 3 && n % 100 != 13 ? n + "rd" : n + "th";

        // A kingdom's ruler-title honorific (Padshah, Maharajadhiraja, …), or "".
        public static string Honorific(Kingdom kingdom) => kingdom?.EncyclopediaRulerTitle?.ToString() ?? "";

        // A hero named with their realm's honorific and any court-granted title, e.g.
        // "Padshah Muhammad Shah", "Najaf Khan Bahadur".
        public static string NameWithHonorific(Hero hero)
        {
            if (hero == null) return "";
            string h = Honorific(hero.Clan?.Kingdom);
            string name = string.IsNullOrEmpty(h) || hero.Clan?.Kingdom?.Leader != hero
                ? hero.Name.ToString() : $"{h} {hero.Name}";
            string granted = CourtHonoursBehavior.Instance?.TitleOf(hero);
            return string.IsNullOrEmpty(granted) ? name : $"{name} {granted}";
        }

        private static void ShowNext()
        {
            if (_queue.Count == 0) { _active = false; _activeKey = null; MaybeResumeTime(); return; }
            ScreenBase top = ScreenManager.TopScreen;
            if (top == null) { _active = false; _activeKey = null; MaybeResumeTime(); return; }

            _active = true;
            Pending p = _queue.Dequeue();
            _activeKey = p.DedupeKey;
            try
            {
                _vm = new FarmaanVM(p.Title, p.Sender, p.Body, p.Seal,
                    p.Primary, p.OnPrimary, p.Secondary, p.OnSecondary, Close);
                _layer = new GauntletLayer("HindostanFarmaan", 1010, false);
                _layer.LoadMovie("HindostanFarmaan", _vm);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                top.AddLayer(_layer);
                _layer.IsFocusLayer = true;
                ScreenManager.TrySetFocus(_layer);
                _host = top;
                PauseTime();
            }
            catch
            {
                // If the layer cannot be shown, don't stall the queue.
                CleanupLayer();
                _active = false;
                _activeKey = null;
                MaybeResumeTime();
            }
        }

        private static void Close()
        {
            CleanupLayer();
            ShowNext(); // drains the chain; time resumes only once the queue is empty
        }

        // A decree stops the world: campaign time halts while a farmaan is up. We remember
        // what the clock was doing beforehand, and only put it back if WE stopped it — a
        // player reading decrees from his own pause stays paused.
        private static void PauseTime()
        {
            if (!Config.Tune.FarmaanPausesTime) return;
            try
            {
                if (Campaign.Current == null) return;
                if (!_wePaused)
                {
                    _prevMode = Campaign.Current.TimeControlMode;
                    _wePaused = _prevMode != CampaignTimeControlMode.Stop;
                }
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            }
            catch (Exception e) { Util.TYTLog.Error("Farmaan pause failed", e); }
        }

        private static void MaybeResumeTime()
        {
            try
            {
                if (!_wePaused || Campaign.Current == null) { _wePaused = false; return; }
                _wePaused = false;
                Campaign.Current.TimeControlMode = _prevMode;
            }
            catch (Exception e) { Util.TYTLog.Error("Farmaan resume failed", e); }
        }

        private static void CleanupLayer()
        {
            if (_layer != null && _host != null)
            {
                _layer.IsFocusLayer = false;
                _host.RemoveLayer(_layer);
            }
            _layer = null;
            _host = null;
            FarmaanVM vm = _vm;
            _vm = null;
            vm?.OnFinalize();
        }
    }
}
