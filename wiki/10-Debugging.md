# Chapter 10 — Debugging

> Finding and fixing problems in Bannerlord mods without losing your mind.

**[← Chapter 9](09-Save-Load.md)** | **[Home](Home.md)** | **[Next: XML and C# →](11-XML-and-CSharp.md)**

---

## Contents

- [The log file](#the-log-file)
- [In-game message logging](#in-game-message-logging)
- [Stream logs in real time](#stream-logs-in-real-time)
- [Common exceptions and causes](#common-exceptions-and-causes)
- [Iterative development workflow](#iterative-development-workflow)
- [Defensive coding patterns](#defensive-coding-patterns)

---

## The Log File

Every `Debug.Print()` call — from Bannerlord and your mod — goes here:

```
C:\Users\tasne\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\default[date].log
```

Use it like `console.log` / `print`:

```csharp
Debug.Print("[Hindostan] ApplyHistoricalWars() starting");
Debug.Print($"[Hindostan] empire kingdom found: {mughals != null}");
Debug.Print($"[Hindostan] Bengal gold set to {bengal.RulingClan.Gold}");
```

**Always prefix with `[Hindostan]`** so you can Ctrl+F among thousands of engine messages.

### The mod's own crash-isolation engine (`TYTLog`)

The mod keeps its **own** diagnostic log, separate from the engine's, written to:

```
<module folder>\Logs\        (falls back to the Desktop if the module path is unavailable)
```

It is built to answer one question after any crash — **including a native access violation
(`0xC0000005`) that never reaches a managed `catch`** — "which piece of mod code, and which
game object, was executing?" Three files, read in this order:

| File | What it is | Read it when |
|------|-----------|--------------|
| `tyt_heartbeat.txt` | A **one-line, always-overwritten** record of the last mod operation, e.g. `Mansabdari.WeeklyTick › clan 'Asaf Jah I'`. Survives a native crash because it is on disk *before* the crash. | The game died with **no** managed stack (native crash, blank Better-Exception-Window, instant CTD). |
| `tyt_crash_<time>.txt` | A self-contained report written when a **managed** exception escapes: the exception + full stack, the live scope stack (what was running, nested), and the last ~96 breadcrumbs. | A guarded handler threw or `AppDomain.UnhandledException` fired. |
| `tyt_log.txt` | The running narrative: load info, tick boundaries, warnings, errors. With `TYTLog.Verbose = true`, every breadcrumb too. | General "what happened, in order". |

#### Instrumenting code

Wrap a once-per-tick handler so a throw is caught, identified, and crash-reported instead of
crashing — and so its scope shows up in the heartbeat:

```csharp
CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
    () => Util.TYTLog.Guard("MyBehaviour.DailyTick", OnDailyTick));
```

Inside a loop, iterate defensively — each item is **validated** (stale/removed objects skipped,
the native-crash defence), gets a **breadcrumb naming it**, and its body is **guarded** so one bad
object is logged-and-skipped rather than taking down the whole tick:

```csharp
Util.TYTLog.ForEach("clan", Clan.All, c => c?.Name?.ToString(), ReviewClan);
```

Other tools:

- `Util.TYTLog.Valid(hero / clan / kingdom / settlement / party)` — true only for a live engine
  object. Call it before touching a reference you have held across ticks; a stale one is the classic
  cause of the native access violation.
- `Util.TYTLog.Crumb("…")` — drop a manual breadcrumb (updates the heartbeat) at a fine point inside a loop.
- `Util.TYTLog.GuardQuiet("ctx", …)` — like `Guard` but **no** per-call breadcrumb/heartbeat I/O. Use
  on **hot paths** that fire for every party/agent many times an hour (e.g. `HourlyTickPartyEvent`).

See **[Chapter 22 — Crash Logging](22-Systems-Overhaul-and-Tuning.md#crash-logging)**.

---

## In-Game Message Logging

For quick visual feedback while testing, show messages on the HUD. Remove these before releasing.

```csharp
InformationManager.DisplayMessage(new InformationMessage(
    $"[DEBUG] Day={((int)CampaignTime.Now.ToDays % 84)} " +
    $"Monsoon={MonsoonSeasonBehavior.IsMonsoonActive}"));
```

For errors that need attention:

```csharp
InformationManager.DisplayMessage(new InformationMessage(
    "[Hindostan] WARNING: empire kingdom not found!",
    Color.FromUint(0xFFFF0000)));  // red
```

---

## Stream Logs in Real Time

Open PowerShell alongside the game and watch only your mod's output:

```powershell
Get-Content "C:\Users\tasne\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\default2026-06-12.log" -Wait | Select-String "\[Hindostan\]"
```

Each line your mod prints appears instantly. The `-Wait` flag keeps the stream open like `tail -f`.

---

## Common Exceptions and Causes

### `NullReferenceException`

**Cause:** Accessing `.Property` on a null object. The most common crash in Bannerlord mods.

```csharp
// Crashes if hero, Clan, or Kingdom is null:
string name = hero.Clan.Kingdom.Name.ToString();

// Safe:
string name = hero?.Clan?.Kingdom?.Name?.ToString() ?? "Unknown";
```

**Diagnostic:** The log shows the stack trace. Find the line in your code and add null guards.

### `InvalidOperationException: Sequence contains no elements`

**Cause:** Calling `.First()` on a collection with no matches.

```csharp
// CRASHES if no matching kingdom:
var k = Kingdom.All.First(k => k.StringId == "empire");

// SAFE:
var k = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");
if (k == null) { Debug.Print("[Hindostan] 'empire' kingdom not found"); return; }
```

### `InvalidCastException` when registering a model

**Cause:** Your model class inherits from the wrong base, or `AddModel` was passed the wrong type.

**Fix:** Verify your class declaration:
```csharp
public class HindostanPartySpeedModel : DefaultPartySpeedCalculatingModel  // correct
```

### Game crashes immediately on new campaign

**Cause:** Exception in `OnNewGameCreated` or in a model calculation method that runs at game start.

**Fix:** Wrap your `OnNewGameCreated` logic in a try/catch temporarily to see the exception:

```csharp
private void OnNewGameCreated(CampaignGameStarter starter)
{
    try
    {
        ApplyHistoricalWars();
        SetStartingTreasuries();
    }
    catch (Exception e)
    {
        Debug.Print($"[Hindostan] CRASH in OnNewGameCreated: {e}");
        InformationManager.DisplayMessage(new InformationMessage(
            "[Hindostan] startup crash — check log file", Color.FromUint(0xFFFF0000)));
    }
}
```

Once you've found and fixed the bug, remove the try/catch — swallowing exceptions silently is dangerous.

### Harmony patch not running

1. Verify the Harmony ID is unique: `new Harmony("com.hindostanmod")`
2. Verify `PatchAll` is in `OnSubModuleLoad`, not `OnGameStart`
3. Wrap `PatchAll` in try/catch and log any exceptions
4. Verify the target method name and parameter types exactly match
5. Add a smoke test: `InformationManager.DisplayMessage(new InformationMessage("patch ran"))` inside the patch

### `SyncData` values reset on load

**Cause:** The `SyncData` key string doesn't match between save and load — usually a typo.

**Fix:** Define key strings as `const string` to avoid duplicating them:

```csharp
private const string KEY_STARTED    = "hindostan_start_applied";
private const string KEY_CHAUTH_YR  = "hindostan_chauth_last_year";

public override void SyncData(IDataStore dataStore)
{
    dataStore.SyncData(KEY_STARTED,   ref _startingStateApplied);
    dataStore.SyncData(KEY_CHAUTH_YR, ref _lastChauthorYear);
}
```

---

## Iterative Development Workflow

The compile-test cycle is slow — Bannerlord takes 2–3 minutes to load to a campaign. Minimize iterations:

1. Write a batch of related logic with `Debug.Print` at each stage
2. Build (Ctrl+Shift+B) — the post-build event auto-deploys the DLL
3. Launch Bannerlord → New Campaign
4. Watch your log stream in the PowerShell window
5. If something is wrong, close the game, fix, and repeat

**Do not** test trivial changes by reloading the game. Test multiple features together per session.

**Quick iteration for pure logic** (no UI): Write a unit test or a console app that exercises your logic using mock data, before wiring it into Bannerlord. This lets you iterate in seconds instead of minutes.

---

## Defensive Coding Patterns

### Always null-check objects from global collections

```csharp
// Pattern for any "find and use" operation
var kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");
if (kingdom == null)
{
    Debug.Print("[Hindostan] WARNING: 'empire' kingdom not found in ApplyWars");
    return;
}
// kingdom is guaranteed non-null past this point
```

### Guard against eliminated kingdoms

```csharp
foreach (Kingdom target in Kingdom.All.ToList())  // .ToList() prevents iteration errors
{
    if (target.IsEliminated) continue;
    if (target.RulingClan == null) continue;
    // safe to proceed
}
```

### Guard against dead heroes

```csharp
private void DoSomethingWithHero(Hero hero)
{
    if (hero == null || !hero.IsAlive || hero.IsDisabled) return;
    // safe to use hero
}
```

### Never modify a collection while iterating it

```csharp
// WRONG — may throw InvalidOperationException:
foreach (var clan in kingdom.Clans)
    ChangeKingdomAction.ApplyByLeaveKingdom(clan);

// RIGHT — snapshot first:
foreach (var clan in kingdom.Clans.ToList())
    ChangeKingdomAction.ApplyByLeaveKingdom(clan);
```

---

**[← Chapter 9](09-Save-Load.md)** | **[Home](Home.md)** | **[Next: XML and C# →](11-XML-and-CSharp.md)**
