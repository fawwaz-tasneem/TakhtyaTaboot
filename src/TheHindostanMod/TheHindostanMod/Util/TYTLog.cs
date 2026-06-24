using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace TakhtyaTaboot.Util
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  Takht ya Taboot diagnostic log & crash-isolation engine.
    //
    //  The whole point is that when the game dies — including a *native* access
    //  violation that never reaches a managed catch — you can read, from a file,
    //  the exact piece of mod code (and the exact game object) that was executing.
    //
    //  Three layers, cheapest first:
    //
    //   1. Heartbeat  (Logs\tyt_heartbeat.txt) — a one-line file OVERWRITTEN on every
    //      breadcrumb. After a native crash the process is gone and memory is lost,
    //      but this file still holds the last operation, e.g.
    //          "Mansabdari.WeeklyTick › clan 'Asaf Jah I' › ChangeClanInfluence".
    //      This is what isolates native crashes.
    //
    //   2. Trail      (Logs\tyt_log.txt) — the running, timestamped narrative:
    //      tick boundaries, warnings, errors, and (when Verbose) every breadcrumb.
    //
    //   3. Crash report (Logs\tyt_crash_<time>.txt) — written when a managed
    //      exception escapes: the exception + stack, the live scope stack (what was
    //      running and in what nesting), and the last N breadcrumbs.
    //
    //  Use it like this in a risky handler:
    //
    //      private void OnDailyTick() => TYTLog.Guard("Mansabdari.DailyTick", DailyImpl);
    //
    //      private void DailyImpl()
    //      {
    //          using (TYTLog.Push("muster review"))
    //              TYTLog.ForEach("clan", Clan.All, c => c?.Name?.ToString(), Review);
    //      }
    //
    //  ForEach validates each item, drops a breadcrumb naming it, and guards the body
    //  so one bad object is logged-and-skipped instead of taking down the whole tick.
    // ─────────────────────────────────────────────────────────────────────────────
    public static class TYTLog
    {
        private const string ModuleId = "TakhtyaTaboot";
        private const int BreadcrumbCapacity = 96;

        private static readonly object _lock = new object();
        private static string _logDir;
        private static string _path;          // running trail
        private static string _heartbeatPath; // last-operation, overwritten each crumb
        private static bool _ready;

        // Live scope stack — the nested operations currently executing (this thread of play).
        private static readonly List<string> _scopes = new List<string>();
        // Ring buffer of the most recent breadcrumbs, dumped into a crash report.
        private static readonly string[] _crumbs = new string[BreadcrumbCapacity];
        private static int _crumbNext;
        private static long _seq;

        // When true the full breadcrumb trail is also written to tyt_log.txt (heavier,
        // but the most detailed). Off by default; the heartbeat already captures the
        // last op for native crashes. Flip on when chasing an intermittent crash.
        public static bool Verbose;

        public static string Path => _path;

        public static void Init()
        {
            try
            {
                string dir = null;
                try { dir = TaleWorlds.ModuleManager.ModuleHelper.GetModuleFullPath(ModuleId); } catch { }
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                _logDir = System.IO.Path.Combine(dir, "Logs");
                try { Directory.CreateDirectory(_logDir); } catch { _logDir = dir; }

                _path = System.IO.Path.Combine(_logDir, "tyt_log.txt");
                _heartbeatPath = System.IO.Path.Combine(_logDir, "tyt_heartbeat.txt");

                // Preserve the PREVIOUS session's log before overwriting — if the last run crashed, its
                // evidence (and heartbeat) must survive the relaunch instead of being clobbered.
                try
                {
                    if (File.Exists(_path))
                    {
                        string prev = System.IO.Path.Combine(_logDir, "tyt_log.prev.txt");
                        File.Copy(_path, prev, true);
                    }
                }
                catch { }

                File.WriteAllText(_path,
                    $"=== Takht ya Taboot log — opened {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n" +
                    "If the game crashed, read tyt_heartbeat.txt for the LAST operation, and any\n" +
                    "tyt_crash_*.txt for a managed exception with full context.\n\n");
                _ready = true;
            }
            catch { _ready = false; }
        }

        // ── Levels ──────────────────────────────────────────────────────────────────
        public static void Info(string msg) => Write("INFO ", msg);
        public static void Warn(string msg) => Write("WARN ", msg);
        public static void Error(string msg, Exception e = null)
            => Write("ERROR", e == null ? msg : msg + "\n" + Describe(e));

        // ── Breadcrumbs & scopes ────────────────────────────────────────────────────
        // A breadcrumb: record in the ring buffer and overwrite the heartbeat file so
        // it survives a native crash. Cheap enough to call per game object in a tick.
        public static void Crumb(string what)
        {
            lock (_lock)
            {
                long n = ++_seq;
                string path = ScopePath();
                string line = string.IsNullOrEmpty(path) ? what : path + " › " + what;
                _crumbs[_crumbNext] = $"{Stamp()} #{n} {line}";
                _crumbNext = (_crumbNext + 1) % BreadcrumbCapacity;
                WriteHeartbeat(n, line);
                if (Verbose) RawWrite($"{Stamp()} TRACE #{n} {line}\n");
            }
        }

        // Push an operation onto the live scope stack. Dispose to pop. Use with `using`.
        public static Scope Push(string context)
        {
            lock (_lock) { _scopes.Add(context); }
            Crumb("→ " + context);
            return new Scope(context);
        }

        public struct Scope : IDisposable
        {
            private bool _done;
            internal Scope(string ctx) { _done = false; }
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                lock (_lock) { if (_scopes.Count > 0) _scopes.RemoveAt(_scopes.Count - 1); }
            }
        }

        // ── Guarded execution ───────────────────────────────────────────────────────
        // Run an action inside a scope; a managed exception is logged with full context
        // (scope stack + recent breadcrumbs) and a crash report file, instead of crashing.
        public static void Guard(string context, Action action)
        {
            using (Push(context))
            {
                try { action(); }
                catch (Exception e) { Fail(context, e); }
            }
        }

        public static T Guard<T>(string context, Func<T> func, T fallback = default(T))
        {
            using (Push(context))
            {
                try { return func(); }
                catch (Exception e) { Fail(context, e); return fallback; }
            }
        }

        // Like Guard, but with no scope push and no per-call heartbeat write — for HOT paths
        // that fire for every party/agent many times an hour, where per-call file I/O would
        // stutter. A managed throw is still logged and crash-reported (its stack names the
        // failing method); we just don't leave a breadcrumb for every uneventful call.
        public static void GuardQuiet(string context, Action action)
        {
            try { action(); }
            catch (Exception e) { Fail(context, e); }
        }

        // Iterate a collection of game objects defensively: validate each item, drop a
        // breadcrumb naming it (so a crash points straight at it), and guard the body so
        // a single bad/stale object is logged-and-skipped rather than aborting the tick.
        public static void ForEach<T>(string label, IEnumerable<T> items, Func<T, string> name, Action<T> body)
        {
            if (items == null) return;
            List<T> snapshot;
            try { snapshot = new List<T>(items); }                 // copy: bodies may mutate the source
            catch (Exception e) { Fail(label + " (enumerate)", e); return; }

            foreach (T item in snapshot)
            {
                if (!IsLive(item)) { Crumb(label + " skip (stale/null)"); continue; }
                string id;
                try { id = name != null ? name(item) : item?.ToString(); } catch { id = "?"; }
                Crumb(label + " '" + (id ?? "?") + "'");
                try { body(item); }
                catch (Exception e) { Fail($"{label} '{id}'", e); }
            }
        }

        // ── Validation — the native-crash defence ───────────────────────────────────
        // Stale engine references (a clan eliminated, a party destroyed, a hero removed)
        // are the classic cause of a 0xC0000005 native access violation when touched in
        // a tick. These guards skip them BEFORE the engine is asked to dereference them.
        public static bool Valid(Hero h) => h != null && !h.IsDisabled && h.CharacterObject != null;
        public static bool Valid(Clan c) => c != null && !c.IsEliminated;
        public static bool Valid(Kingdom k) => k != null && !k.IsEliminated;
        public static bool Valid(Settlement s) => s != null;   // settlements are never destroyed; null is the only risk
        public static bool Valid(MobileParty p) => p != null && p.IsActive && p.Party != null;

        private static bool IsLive<T>(T item)
        {
            switch (item)
            {
                case null: return false;
                case Hero h: return Valid(h);
                case Clan c: return Valid(c);
                case Kingdom k: return Valid(k);
                case Settlement s: return Valid(s);
                case MobileParty p: return Valid(p);
                default: return true;
            }
        }

        // ── Crash report ────────────────────────────────────────────────────────────
        // Called from the AppDomain unhandled-exception handler and from Guard on a
        // managed throw. Writes a self-contained file naming the failing code path.
        public static void WriteCrashReport(string reason, Exception e)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("════════════════════════════════════════════════════════════");
                sb.AppendLine($"  TAKHT YA TABOOT — crash report  ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
                sb.AppendLine("════════════════════════════════════════════════════════════");
                sb.AppendLine("Reason: " + reason);
                try { if (Campaign.Current != null) sb.AppendLine("In-game date: " + CampaignTime.Now); } catch { }
                sb.AppendLine();

                sb.AppendLine("── What the mod was executing (innermost last) ──");
                lock (_lock)
                {
                    if (_scopes.Count == 0) sb.AppendLine("  (no open mod scope — crash likely outside a guarded handler)");
                    for (int i = 0; i < _scopes.Count; i++) sb.AppendLine($"  {new string(' ', i * 2)}› {_scopes[i]}");
                }
                sb.AppendLine();

                sb.AppendLine("── Exception ──");
                sb.AppendLine(e != null ? Describe(e) : "(none supplied)");
                sb.AppendLine();

                sb.AppendLine("── Recent breadcrumbs (oldest first) ──");
                foreach (string c in RecentCrumbs()) sb.AppendLine("  " + c);

                string file = System.IO.Path.Combine(_logDir ?? ".", $"tyt_crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(file, sb.ToString());
                Write("FATAL", $"{reason} — crash report written to {file}");
            }
            catch { /* a crash reporter must never crash */ }
        }

        // ── Internals ───────────────────────────────────────────────────────────────
        private static void Fail(string context, Exception e)
        {
            Error("Guarded [" + context + "] threw:", e);
            WriteCrashReport("Guarded handler threw in [" + context + "]", e);
        }

        private static string ScopePath()
        {
            lock (_lock) { return _scopes.Count == 0 ? "" : string.Join(" › ", _scopes); }
        }

        private static IEnumerable<string> RecentCrumbs()
        {
            lock (_lock)
            {
                var list = new List<string>(BreadcrumbCapacity);
                for (int i = 0; i < BreadcrumbCapacity; i++)
                {
                    string c = _crumbs[(_crumbNext + i) % BreadcrumbCapacity];
                    if (!string.IsNullOrEmpty(c)) list.Add(c);
                }
                return list;
            }
        }

        private static string Describe(Exception e)
        {
            var sb = new StringBuilder();
            for (Exception cur = e; cur != null; cur = cur.InnerException)
            {
                sb.AppendLine(cur.GetType().FullName + ": " + cur.Message);
                if (!string.IsNullOrEmpty(cur.StackTrace)) sb.AppendLine(cur.StackTrace);
                if (cur.InnerException != null) sb.AppendLine("  --- inner exception ---");
            }
            return sb.ToString().TrimEnd();
        }

        private static string Stamp()
        {
            string when = "";
            try { if (Campaign.Current != null) when = " {" + CampaignTime.Now + "}"; } catch { }
            return $"[{DateTime.Now:HH:mm:ss}]{when}";
        }

        private static void WriteHeartbeat(long n, string line)
        {
            if (string.IsNullOrEmpty(_heartbeatPath)) return;
            try
            {
                File.WriteAllText(_heartbeatPath,
                    $"Takht ya Taboot — last operation before this line was written.\n" +
                    $"If the game crashed (esp. a native crash with no tyt_crash_*.txt), THIS is where it was:\n\n" +
                    $"{Stamp()} #{n}\n{line}\n");
            }
            catch { /* heartbeat must never throw */ }
        }

        private static void Write(string level, string msg)
        {
            if (!_ready) return;
            lock (_lock) { RawWrite($"{Stamp()} {level} {msg}\n"); }
        }

        private static void RawWrite(string text)
        {
            try { File.AppendAllText(_path, text); }
            catch { /* logging must never throw */ }
        }
    }
}
