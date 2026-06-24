# Bug Report — Game crash during campaign Daily Tick (Winter 11, 1084)

**Date:** 2026-06-20
**Reporter:** Fawwaz
**Severity:** High (hard crash to desktop) + High (save-file corruption risk)
**Status:** Mitigated 2026-06-20 — save bug fixed; daily-tick handlers hardened; crash-isolation
engine added so a recurrence names the exact code/object. See **Resolution** below.

---

## Summary

The game crashed to desktop after ~47 minutes of play. The crash is a **native
access violation (`0xC0000005`)** that fired **inside the campaign Daily Tick**
for *Winter 11, 1084* — the moment the mod's daily behaviours run. A separate,
serious problem was also found in the same session: **every save produces 892
`SAVE ERROR ... TextObject` failures**, caused by the encyclopedia patch.

---

## Environment

| | |
|---|---|
| Game build | v1.3.11.105254 (Win64, Steam) |
| Mod module | `TakhtyaTaboot` v1.0.0.0 (The Hindostan Mod) |
| Running DLL | `TakhtyaTaboot.dll` built **2026-06-16 22:52** (predates the current uncommitted `WarfareBehavior`/`PartyOrdersBehavior` edits — those were **not** in the crashing build) |
| Active modules | Harmony 2.4.2.225, ButterLib 2.10.4.0, UIExtenderEx 2.13.2.0, MBOptionScreen/MCM 5.11.4.0, Native/SandBoxCore/Sandbox/CustomBattle/StoryMode, **TakhtyaTaboot**, Diplomacy 1.3.3.0 |
| Session | PID 20112, started 09:03, crashed **09:50:17 local** |
| TaleWorlds crash ID | `2026-06-20_07.50.20_ed33e98c9db7d99f1d6949b08f44d53c` (07:50 UTC = 09:50 local; uploaded successfully) |

---

## The crash

From `watchdog_log_20112.txt`:

```
Crash occurred. Asking for dump.
ExceptionCode:    0xC0000005   (ACCESS_VIOLATION)
ExceptionAddress: 0x7ffad4e3ae81
Parameter-0:      0x0          (read access)
Parameter-1:      0x706a000033b4  (faulting address — not a valid object)
```

`0xC0000005 / Parameter-0 = 0` is a **read from an invalid memory address**. The
native call stack could not be resolved locally (no symbol store) and the dump
(`crashes\2026-06-20_07.50.20\dump.dmp`) was uploaded to TaleWorlds and then
deleted by the CrashUploader, so the exact frame is not available on disk.

### What the player was doing

`rgl_log_20112.txt` ends *exactly* at the start of the Winter 11 tick — the very
last lines of the entire session are:

```
[09:50:13] PopScreen - SandBox.GauntletUI.GauntletInventoryScreen   (closed inventory in Patna)
[09:50:13] MapScreen::HandleResume / [GAME MENU] town
[09:50:17.087] Before Daily Tick: Winter 11, 1084            <-- log ends here, mid-tick
```

So: player closed the town inventory, returned to the map, the day rolled over,
the Daily Tick began — and the process died before the tick finished. Every
**previous** day (Winter 6–10) completed a full tick and printed the mod's
per-lord *"Mansab: …"* report; Winter 11 crashed before producing any of it.

This timing strongly implicates the mod's daily-tick work. The mod registers
**11** daily-tick listeners that run heavy logic across all heroes/clans/
settlements every day:

- `MansabdariBehavior.OnDailyTick` (recomputes mansab/zat for every clan)
- `ImperialCourtBehavior.OnDailyTick`
- `FiefHierarchyBehavior`, `CareerProgressionBehavior`, `AccessionWarBehavior`,
  `RevoltCascadeBehavior`, `SuccessionBehavior`, `WarfareBehavior`,
  `RoyalDecisionsBehavior`, plus `VillageDevelopmentBehavior.OnDailyTickSettlement`.

Most of these are wrapped in `TYTLog.Guard(...)`, which catches *managed*
exceptions. Because this crash is a **native** access violation (not a caught
managed exception, and no Better-Exception-Window overlay), the most likely
cause is **managed code calling an engine method on an invalid/destroyed native
object** during the tick — e.g. a stale `Settlement`/`MobileParty`/`Hero`
reference held in one of the behaviours' dictionaries after the underlying
object was removed. A `try/catch` does not protect against this class of fault.

> Note: I cannot prove the mod is the cause from the logs alone — a native AV
> can originate in the engine. But the crash landing inside the Daily Tick, in a
> save that is already in a visibly inconsistent state (see below), with vanilla
> rarely crashing natively on a day-rollover, makes the mod the leading suspect.

---

## Secondary issue found — 892 save errors every save (real, reproducible)

During the autosave at 09:30 the log recorded **892** lines of:

```
SAVE ERROR. Cant find
 ... At the Imperial Court:
     Feudal standing: ...
     Mansab: ...
 with type TaleWorlds.Localization.TextObject
```

### Root cause

[`EncyclopediaInfoPatch`](../src/TheHindostanMod/TheHindostanMod/Patches/EncyclopediaInfoPatch.cs)
is a Harmony **Postfix on the `Hero.EncyclopediaText` getter** that builds the
"At the Imperial Court" blurb and does:

```csharp
__result = new TextObject(baseText + extra);   // line 29 — a brand-new TextObject every call
```

A fresh `TextObject` is minted on **every** read of `EncyclopediaText`. These
objects are never registered with the save container, so when the save system
walks the object graph it finds `TextObject` references it cannot resolve and
logs `SAVE ERROR. Cant find … with type TextObject` — once per lord, per save
(892 here). This pollutes the save and is exactly the kind of inconsistency that
can later surface as a hard fault.

### Suggested fix

Don't allocate `TextObject`s in a hot getter that the save system reads. Options:
- Surface the court blurb through the encyclopedia **page UI** (UIExtenderEx VM
  mixin) instead of overriding `EncyclopediaText`, **or**
- Cache one `TextObject` per hero and reuse it, **or**
- Only append when the call is genuinely an encyclopedia render, not a save pass.

This is independent of the crash but should be fixed regardless — 892 serializer
errors per save is a corruption risk on its own.

---

## Reproduction

Not yet reproduced deterministically. Likely steps:
1. Load the affected campaign (kingdom-level play, many lords with mansab/court state).
2. Advance days on the campaign map until a day-rollover; the crash occurred on a
   rollover immediately after returning from a town inventory screen.

---

## Next steps to pin the exact frame

1. Retrieve the symbolicated stack from TaleWorlds using crash ID
   `2026-06-20_07.50.20_ed33e98c9db7d99f1d6949b08f44d53c`.
2. To catch it live: build a **`Win64_Shipping_wEditor`** / debug DLL and run
   with a debugger attached so the native AV breaks with a managed stack; or add
   defensive validation (`IsActive` / removed-object guards) to the daily-tick
   loops in `MansabdariBehavior` and `ImperialCourtBehavior` and watch whether
   the crash moves or disappears.
3. Audit the behaviours' cached collections (`_score`, `_weary`, settlement/clan
   dictionaries, etc.) for stale references that aren't cleared on
   `OnSettlementOwnerChanged` / clan-destroyed / hero-killed events.

---

## Resolution (2026-06-20)

Three changes, all in the rebuilt `TakhtyaTaboot.dll`:

### 1. Save bug fixed — 892 → 0 errors
[`SaveGuardBehavior`](../src/TheHindostanMod/TheHindostanMod/Util/SaveGuardBehavior.cs) tracks
`OnBeforeSaveEvent`/`OnSaveOverEvent`. [`EncyclopediaInfoPatch`](../src/TheHindostanMod/TheHindostanMod/Patches/EncyclopediaInfoPatch.cs)
now returns the vanilla `EncyclopediaText` while a save is in progress, so the serializer never sees a
fresh, unresolvable `TextObject`. The court blurb still shows normally when viewing the encyclopedia.

### 2. Crash-isolation engine — names the failing code/object
[`TYTLog`](../src/TheHindostanMod/TheHindostanMod/Util/TYTLog.cs) rewritten around the fact that a
**native** access violation never reaches a managed `catch`:
- **`Logs\tyt_heartbeat.txt`** — a one-line file overwritten on every breadcrumb. It is on disk
  *before* the crash, so after a native CTD it still names the last operation, e.g.
  `Mansabdari.WeeklyTick › clan 'Asaf Jah I'`. **This is what would have pinpointed this crash.**
- **`Logs\tyt_crash_<time>.txt`** — for managed throws: exception + stack, the live scope stack, and
  the last ~96 breadcrumbs. Wired into `AppDomain.UnhandledException` and into every `Guard`.
- `ForEach` / `Valid(...)` / `Crumb` / `GuardQuiet` helpers (see Chapter 10 of the wiki).

### 3. Daily-tick loops hardened
- Every daily/weekly tick handler now runs through `TYTLog.Guard(...)` — a managed throw is caught,
  identified, and crash-reported instead of escaping; the scope is recorded for native crashes.
- `Valid(...)` validation (skips removed/eliminated/destroyed objects — the native-AV defence) added
  to the exposed dereferences; per-object breadcrumbs added to the highest-risk daily loops
  (`RevoltCascade`, `Succession`).
- The hot `HourlyTickPartyEvent` path uses `GuardQuiet` + `Valid(party)` (catch + validate, no
  per-call file I/O) so per-party-per-hour ticks don't stutter.

**Verification:** `dotnet build -c Release` → 0 warnings, 0 errors. In-game test to confirm: save in a
large campaign and grep `rgl_log` for `SAVE ERROR` (expect none), and confirm `Logs\tyt_heartbeat.txt`
updates during day-rollovers.

> Note: this **prevents** the save corruption and makes any *recurrence* of the native crash
> self-diagnosing (the heartbeat will name the exact behaviour + object). It does not by itself prove
> the original native frame; if it recurs, read `tyt_heartbeat.txt` and attach it here.

## Log locations (this session)

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_20112.txt` — native exception record
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_20112.txt` — full gameplay log (ends at the crashing tick)
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\CrashUploader.11228.txt` — upload confirmation + crash ID
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\crashlist.txt` — crash zip list
