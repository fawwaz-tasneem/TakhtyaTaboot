# Contributor Guide — The Hindostan Mod (Takht ya Taboot)

The 10-minute version of everything a new developer needs before touching the code.
Deeper material: `wiki/28-Implemented-Systems-Reference.md` (the as-built systems map and
invariants) and `wiki/Modding-Findings-Reference.md` (verified engine facts and traps).

## 1. Getting building

1. Clone; open `src/TheHindostanMod/TheHindostanMod/TheHindostanMod.csproj` (net481, x64).
2. Create `BannerlordDir.local.props` next to the csproj (it is **gitignored** — never
   commit it) pointing at YOUR game install:

   ```xml
   <Project>
     <PropertyGroup>
       <BannerlordDir>PATH\TO\Mount &amp; Blade II Bannerlord</BannerlordDir>
     </PropertyGroup>
   </Project>
   ```

   Alternatives: `-p:BannerlordDir=...` on the command line, or the `BANNERLORD_DIR`
   environment variable. The default falls back to the standard Steam location.
3. `dotnet build -c Release` → the assembly lands in `bin\Win64_Shipping_Client`
   (the repo root doubles as the module folder).
4. `dotnet test src\TheHindostanMod.Tests` — the pure-math test suite needs **no game
   install**; it must stay green at all times.

## 2. House rules (non-negotiable)

- **One behavior per system.** `CampaignBehaviorBase`, `public static X Instance`,
  registered in `HindostanSubModule.OnGameStart`. Some registrations are order-sensitive —
  read the comments there before inserting.
- **Never touch collections in `OnNewGameCreatedEvent`.** World-gen is parallel; iterating
  clans/settlements/heroes there native-crashes. Seed idempotently in `OnSessionLaunched`;
  gate event handlers with `Util.WorldGen.Ready`. (Findings §12.)
- **Persistence = parallel primitive lists in `SyncData`**, keyed by `StringId`,
  index-guarded on load, append-only keys. Never rename/reorder serialized enum values or
  serialized display names. (Findings §14.)
- **Wrap every tick** in `TYTLog.Guard`; drop `TYTLog.Crumb` breadcrumbs inside hot
  settlement ticks. `tyt_log.txt` in the module folder is the mod's log — an `[ERR]` line
  is always a bug.
- **Tunables** go `Config/TYTSettings.cs` (MCM) → `Config/Tune.cs` fallback getter.
  Behaviors read only `Tune.X`.
- **Formulas are pure.** Non-trivial math lives in `Util/*Math.cs` with zero TaleWorlds
  types (use `System.Math`, NOT `TaleWorlds.Library.MathF`), linked into the test project,
  with xUnit tests.
- **Harmony patches**: one class per target, they're applied per-class so a bad target
  only disables itself; check the startup log line `Harmony: N patch classes applied`.
- **UX charter** — pick the surface before writing the feature:
  dialogue for person-to-person acts · the `hindostan_court` submenu for place-bound acts
  (never the raw `town`/`castle` menus) · Gauntlet screens for realm overviews · farmaans
  for events (give recurring ones a `dedupeKey` + priority; see Findings §15) · the
  message log for ambient info.

## 3. The invariants most likely to bite you

- `FeudalTitlesBehavior.GetFeudalLiege` is the **only** liege resolution. Don't re-derive
  "who does X answer to" from engine ownership.
- `MansabdariBehavior.CanHold` is prescriptive (who MAY hold a fief);
  `FeudalTitlesBehavior.GetTier` is descriptive (what someone DOES hold). Keep them apart.
- `AssignZamindar` returns `bool` and can refuse — check it before charging costs or
  announcing grants.
- Individual judgment (orders, side-picking, appointments, withholding) reads
  `OpinionBehavior.EffectiveOpinion`, not raw relation.
- New clans are built ONLY through `Util/CadetHouse.BuildShell` (banner/home/leader are
  mandatory or the encyclopedia crashes; hero moves use the engine's non-public setter).

## 4. Commit hygiene

- **No machine-absolute paths** in committed content (user profiles, drive-specific
  install paths). Machine config goes in gitignored `*.local.props` files.
- Commits are authored by the project owner only — no tool/AI attribution trailers.
- Keep the test suite green and the Release build clean before committing.

## 5. Verifying a change

1. Unit tests (`dotnet test`) for any formula change.
2. Clean Release build.
3. In-game: `PLAYTEST.md` at the repo root has the per-system checklist; at minimum run
   the section for the system you touched, on a new campaign AND a pre-change save, with
   `tyt_log.txt` open.
