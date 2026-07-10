# 29 — Crash Forensics: From "Object reference not set" to Root Cause

How the July 2026 character-creation crash was solved, generalized into a repeatable
workflow. Use this chapter when the game crashes and the logs tell you nothing.
Companion facts: [Findings §17](Modding-Findings-Reference.md) (the specific trap that
crash exposed), [Ch.10](10-Debugging.md) (everyday logging).

---

## 1. Where the evidence lives

| Artifact | Path | What it holds |
|---|---|---|
| Crash folder | `C:\ProgramData\Mount and Blade II Bannerlord\crashes\<yyyy-MM-dd_HH.mm.ss>\` | Everything below, snapshotted at crash time |
| `crash_tags.txt` | in crash folder | Game version, module list + versions, launch arguments |
| `rgl_log_<pid>.txt` | in crash folder | The engine log of the crashed session — **read its tail first** |
| `dump.dmp` | in crash folder | Full minidump **including the managed heap** (~900 MB) — the ground truth |
| `module_list.txt` | in crash folder | Every native/managed image loaded, with file paths |
| Mod log | `<install>\Modules\The Hindostan Mod\Logs\tyt_log.txt` | `TYTLog` output; any `[ERR]` line is a finding |
| Heartbeat | `...\Logs\tyt_heartbeat.txt` | The LAST mod operation before death — decisive for native crashes with no managed trace |
| Crash capture | `...\Logs\tyt_crash_*.txt` | Written only when a managed exception passed through our handlers |

Multiple `rgl_log_*.txt` files in one crash folder = earlier sessions the reporter swept
up; match the PID and timestamps to pick the crashed one.

## 2. Read the engine log tail — and know its limits

The Gauntlet UI wraps every button command in reflection. When a click blows up you get
only:

```
Exception occurred inside invoke: OnNextStage
Target type: ...CharacterCreationOptionsStageVM
Inner message: Object reference not set to an instance of an object.
```

**No stack trace, and the named VM is just the button that was clicked** — the real
failure can be arbitrarily deep below it (in our case: five frames of UI plumbing, then
`MapScreen..ctor` → first party-speed calculation → a static initializer that had been
poisoned 100 seconds earlier). Never stop at this message; go to the dump.

Also check the heartbeat: if it names a mod tick, the crash was probably ours; if it
shows something old (e.g. world-gen seeding) while the crash is minutes later, the mod's
ticks were idle and the failure came through an engine path.

## 3. Mining dump.dmp (WinDbg + SOS)

One-time setup: `winget install Microsoft.WinDbg`. The Store app exposes a CLI alias,
`WinDbgX`, which can run fully scripted:

```
WinDbgX -z "C:\ProgramData\...\crashes\<ts>\dump.dmp" ^
  -c ".logopen C:\temp\pass1.log; !analyze -v; .loadby sos clr; !threads; .logclose; qd"
```

Each pass takes 1–3 minutes (symbol loading on a 900 MB dump); write a log file and read
that — don't fight the GUI. The command cookbook, in the order that solved the real case:

| Command | Question it answers |
|---|---|
| `!analyze -v` | Failure bucket. Gave us `CLR_EXCEPTION_System.NullReferenceException ... GameTexts.FindText` + the `TypeInitializationException` type name — the case was half-solved right here |
| `!threads` | Which thread holds the managed exception (`Exception` column, "nested exceptions") |
| `!pe -nested <addr>` | Full exception chain with **generated stack traces** — including the *stored* stack of a cached TypeInitializationException's inner exception, i.e. a stack from an EARLIER moment than the crash |
| `!dumpheap -type <T> -short` | Find exception/model objects on the heap |
| `!name2ee *!Full.Type.Name` | How many copies of a type the CLR actually has (see red herring below), plus its MethodTable/EEClass |
| `!dumpclass <EEClass>` | **Static field values at crash time** — this proved `_gameTextManager` was non-null at the crash, forcing the "poisoned earlier" conclusion |
| `~~[<osid>]s; !clrstack` | Full managed stack of a specific thread |
| `lmf m <pattern>` | Loaded-module file paths |

## 4. Red herrings to not chase

- **Duplicate TaleWorlds modules** (`TaleWorlds_Core_<addr>` twice in `lm`): the BUTR
  crash reporter maps DLLs as files to hash them; they show up as modules. `!name2ee`
  returning ONE MethodTable proves there is no double assembly load.
- **`PROCESS_NAME: TaleWorlds.MountAndBlade.Launcher.exe`**: normal — the launcher
  process hosts the whole game (and BLSE injects via an AppDomainManager; you'll see
  `Bannerlord.BLSE.*` assemblies).
- **"Dump integrity is compromised due to cheat usage"**: only means the dev console was
  used; not related to the crash.
- **A Harmony patch "applied" ≠ harmless.** Patch application can run the target class's
  static initializer at a time when it cannot succeed, poison the type, and still report
  success (Findings §17). Absence of a patch-failure log line proves nothing.

## 5. Decompiling the game — answer engine questions with the engine

`dotnet tool install --global ilspycmd`, then against
`<install>\bin\Win64_Shipping_Client\`:

```
ilspycmd -t Full.Type.Name TaleWorlds.CampaignSystem.dll     # one type
ilspycmd -l c TaleWorlds.CampaignSystem.dll | grep <name>    # list classes
ilspycmd <dll> -o outdir                                     # whole assembly, grep-able
```

What this settled in one session: the exact statics in a model's initializer, that
`GameTextManager.TryGetText` is null-safe (so the NRE had to be the manager field), that
`Game.Initialize()` runs `GameTexts.Initialize` **before** `Campaign.OnInitialize` fires
`GameManager.OnGameStart` (making OnGameStart the safe patch point), and the v1.3.11
`MakePeaceAction` surface (the 4-arg `Apply` overload is gone; everything funnels through
the private 5-arg `ApplyInternal`). **Verify patch targets against the decompile after
every game update** — the per-class patch loop logs binding failures at startup
(`Harmony patch FAILED for ...`), and each one means a signature to re-check here.

## 6. The worked example, compressed

Symptom: crash clicking the last "Next" of sandbox character creation, message only
"Object reference not set". Chain of deductions:

1. rgl tail → NRE inside `OnNextStage` invoke; no stack. Heartbeat idle since world gen.
2. `!analyze -v` → `TypeInitializationException` for `DefaultPartySpeedCalculatingModel`,
   inner NRE in `GameTexts.FindText` called from the **cctor**.
3. Decompile → cctor line: `_culture = GameTexts.FindText("str_culture")`; `FindText`
   derefs `_gameTextManager` unguarded.
4. `!dumpclass` → `_gameTextManager` NON-null at crash ⇒ the cctor must have run (and
   failed) earlier, in the only null window: before any `Game` exists ⇒ during
   `OnSubModuleLoad` — where our Harmony patch prepares `CalculateFinalSpeed`, whose body
   reads the class statics.
5. Why dormant for 100s: the campaign registers a *subclass*, and instantiating a derived
   type does not run a `beforefieldinit` base cctor. First base-method call = first
   party-speed calc = map screen spawn ⇒ cached TIE thrown there.
6. Fix: two-phase patch application (`GameStartPatches` set in `HindostanSubModule`);
   audit found the same bomb armed on `DefaultArmyManagementCalculationModel` (would have
   fired at the first army).

The general lesson: **a TypeInitializationException is a fossil** — its inner stack is
from the first failed access, which can be minutes before and causally unrelated to the
moment it surfaces. Date the fossil (what was null, and WHEN could it have been null?)
instead of staring at the crash site.

---

**[Home](Home.md)**
