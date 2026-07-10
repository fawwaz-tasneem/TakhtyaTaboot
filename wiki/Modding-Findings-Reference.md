# Modding Findings Reference — Empirical, Verified

> Hard-won facts about how Bannerlord v1.4.6 actually behaves, discovered by reflection,
> IL decompilation, and in-game debug logging during the Phase-1 renaming work. Each entry
> is **verified**, not assumed. Read this before touching localization, names, or mod setup
> so the same dead-ends aren't repeated.

**[Home](Home.md)**

---

## 1. Localization — the single most important finding

### 1.1 There are TWO string stores; English uses only one

| Store | Type | Used for | Key |
|-------|------|----------|-----|
| `GameTexts._gameTextManager._gameTexts` | `Dictionary<string, GameText>` (in `TaleWorlds.Core`) | **English (default) display text** | outer semantic id + variation |
| `LocalizedTextManager._gameTextDictionary` | `Dictionary<string, string>` (in `TaleWorlds.Localization`) | **non-English translations only** | inner `{=}` key |

- **Proven:** at runtime in an English game, `_gameTextDictionary` is **empty** (logged `dict 0->84`). It is the translation layer; it stays empty unless a non-English language is loaded.
- **Therefore:** to change English text you must overwrite the `GameText` in `GameTexts._gameTextManager`. Writing to `_gameTextDictionary`, or patching `LocalizedTextManager.GetTranslatedText`, does nothing in English. Both were tried and both failed.

### 1.2 Inner `{=}` key vs outer id — the trap that cost the most time

A vanilla string line looks like:
```xml
<string id="str_faction_noble_name_with_title.empire"
        text="{=nAaT19CC}{?RULER.GENDER}Archoness{?}Archon{\?} {RULER.NAME}" />
```
- **Outer id** = `str_faction_noble_name_with_title.empire` — what the renderer looks up. The English value is the inline text (`…Archon…`).
- **Inner key** = `nAaT19CC` — only a translation-lookup key for non-English. **Not retained at runtime** (it is not stored on `GameText`, `GameTextVariation`, or `TextObject` — `TextObject` only keeps `Value`). So you cannot recover it after load; you must map it from Native's XML offline.
- The mod's `std_module_strings_xml.xml` was keyed entirely by **inner keys**, so in English nothing it defined was ever read. This is THE root cause of every "rename didn't work" symptom (titles, descriptions, world refs).

### 1.3 GameText id + variation storage

- `<string id="A.B">` is stored as `GameText` with **id `A`** and a **variation `B`**. Lookup is `GameTexts.FindText("A", "B")`.
- So `str_faction_noble_name_with_title.empire` ⇒ `GetGameText("str_faction_noble_name_with_title")`, variation `"empire"`.
- `GameTextManager.GetGameText(id)` returns **null** if the id isn't a stored base id (it does not auto-create — `AddGameText` creates). Resolution rule we use: try `GetGameText(fullId)`; if null and the id contains `.`, split at the **last** `.` and `GetGameText(base)` with the remainder as variation; if still null, `AddGameText(fullId)` (for keys consumed inline in code).
- **Never** `Clear()` a `GameText._variationList` — sibling cultures share one `GameText` (e.g. `str_faction_noble_name_with_title` holds a variation per culture). Use `GameText.SetVariationWithId(variationId, TextObject, List<GameTextManager.ChoiceTag>)` to replace just one variation.

### 1.4 Verified API

```csharp
// TaleWorlds.Core
GameTexts._gameTextManager                                  // private static field -> GameTextManager
GameTextManager.GetGameText(string id) -> GameText          // null if absent
GameTextManager.AddGameText(string id) -> GameText          // creates/registers
GameTexts.FindText(string id, string variation) -> TextObject
GameText.SetVariationWithId(string variationId, TextObject text, List<GameTextManager.ChoiceTag> tags)
GameText._variationList                                     // List<GameText.GameTextVariation>; do not clear blindly
// GameTextVariation fields: Id (variation id), Text (TextObject), Tags (ChoiceTag[])
// TextObject fields: Value (string) + caching; NO inner-key field
```

### 1.5 The implemented fix

- `ModuleData/hindostan_string_map.xml` — generated from Native's files: `inner key -> outer id(s)`. One inner key can map to several outer ids (e.g. a culture adjective appears in `str_adjective_for_faction.*` and `str_adjective_for_culture.*`); each is overwritten.
- `Patches/LocalizationOverride.cs` — reads `std_module_strings_xml.xml` (text, single source of truth) + the map, resolves each to outer id+variation, and overwrites via `SetVariationWithId`. Applied from `NameGenerator.InitializePersonNames` postfix (a hook proven to run at campaign init).
- Timing caveat: this hook runs **after** character creation, so the char-creation culture description panel is not covered by it (separate handling needed there).

---

## 2. Name generation — Calradic NPC names

### 2.1 Three independent name sources

| Source | Holds | Used by |
|--------|-------|---------|
| `CultureObject._maleNameList` / `_femaleNameList` / `_clanNameList` (`MBList<TextObject>`) | per-culture names from `<male_names>` etc. | most generated heroes |
| `NameGenerator._imperialNamesMale` / `_imperialNamesFemale` (`MBList<TextObject>`) | empire/Mughal names | empire culture |
| `NameGenerator._merchantNames` / `_artisanNames` / `_preacherNames` / `_gangLeaderNames` | town notable pools | traders, gang leaders, etc. — **culture-agnostic** |

- **Proven cause:** modded `tyt_spcultures.xml` name lists **merge** with Native's rather than replacing, so Calradic names persist. And the notable/imperial pools are never fed by culture XML at all.
- **Fix:** `Patches/NameOverridePatch.cs` postfixes `NameGenerator.InitializePersonNames()` and **replaces** every `CultureObject` list and every `NameGenerator` pool from `tyt_spcultures.xml`. Confirmed in-game: "Cultures fixed: 18; merged pool: 550".

### 2.2 Verified API

```csharp
NameGenerator.Current                                       // static instance
NameGenerator.GetNameListForCulture(CultureObject, bool isFemale)
MBObjectManager.Instance.GetObjectTypeList<CultureObject>() // all cultures
CultureObject.MaleNameList / FemaleNameList / ClanNameList  // readonly props; backing fields _maleNameList etc.
```

---

## 3. Character-creation culture screen

- The culture **name** on the selection list is `CharacterCreationCultureVM.NameText` (a property), set in the VM constructor. It reads `str_culture_rich_name.{id}` — **not** `CultureObject.Name` (that is encyclopedia/dialogue).
- Fixed deterministically by a Harmony postfix on the VM constructor (`Patches/CultureNamePatch.cs`) setting `NameText`/`ShortenedNameText`. The VM also exposes `DescriptionText` (use this to fix the description panel, which the campaign-time localization overlay can't reach).
- The selection list shows **only the 6 hard-coded main vanilla cultures** (empire, sturgia, vlandia, aserai, khuzait, battania). `empire_w`/`empire_s` (Bengal/Hyderabad) never appear there.

---

## 4. Mod setup / loading

- **Culture screen names DON'T come from `CultureObject.Name`.** Renaming via the `name="{=!}…"` attribute in `tyt_spcultures.xml` updates encyclopedia/dialogue only.
- **English language files auto-load** from `ModuleData/Languages/*.xml` when their root is `<base type="string">`; a `language_data.xml` listing them is optional for English (Native's own `std_*.xml` load without being listed). This was NOT the blocker — the inner/outer key mismatch (§1.2) was.
- **Junction install:** the dev folder is linked into the game via a directory junction (`Modules\The Hindostan Mod` → `Desktop\TakhtyaTaboot`). The game reads `bin\Win64_Shipping_Client\` live; no copy step. A duplicate real folder with the same module `Id` causes two launcher entries and the game may load the stale one — keep exactly one.
- **Harmony** is provided by the `Bannerlord.Harmony` module (v2.4.2). Declare it as `<DependedModule Id="Bannerlord.Harmony" .../>`; do **not** also ship `0Harmony.dll` (version conflict). `Lib.Harmony` PackageReference uses `<ExcludeAssets>runtime</ExcludeAssets>` so it compiles but isn't copied.
- **Project:** `net481`, `x64`, output straight to `..\..\..\bin\Win64_Shipping_Client\`. Reference DLLs with `<Private>False</Private>`. Note `TaleWorlds.MountAndBlade.View.dll` lives in `Modules\Native\bin\…` while `TaleWorlds.MountAndBlade.ViewModelCollection.dll` and `TaleWorlds.CampaignSystem.ViewModelCollection.dll` live in the **main** `bin\…`.

---

## 5. Character data model — names vs bios vs families vs clans

Four different files own different parts of a lord, and confusing them wastes time:

| What you want to change | File | Mechanism |
|--------------------------|------|-----------|
| Hero **display name** | `spnpccharacters.xml` (`<NPCCharacter id name="{=!}…">`) | mod overrides by id; attribute-merge |
| Hero **encyclopedia bio** | **`heroes.xml`** (`<Hero id text="{=key}…">`) — in `SandBox`, **not** spnpccharacters | mod must register `<XmlName id="Heroes" path="heroes"/>` and override by id |
| Hero **family** (`father`/`mother`/`spouse`) | **`heroes.xml`** | siblings = shared `father` OR `mother`; there is no "brother" attribute |
| Hero **clan membership** | **`heroes.xml`** (`faction="Faction.clan_…"`) | — |
| **Clan** name / owner / banner | `tyt_spclans.xml` (`<Faction id name owner super_faction>`) | mod loads it via `<XmlName id="Factions" path="tyt_spclans"/>` |

- **The trap that produced "Lucon" text on Muhammad Shah:** the bio is `heroes.xml`'s `text=`, and the mod only overrode `spnpccharacters.xml` (`text` there is **not** the encyclopedia bio). Phase-1 renamed names but left every bio, family link, and clan assignment vanilla.
- **Merge is attribute-level** (proven: Phase-1 name-only overrides kept faction/spouse). Still, anchor critical attributes (`faction`) in hero overrides so clan membership can't be lost even if a future loader does element-replace.
- **Restructuring clans is risky:** a clan owns settlements via `owner=` + `initial_home_settlement`. Removing/reassigning an owner orphans settlements. Prefer fixing the *relationship* (e.g. set Tipu's `father=Hyder` and keep his cadet clan) over moving a leader out of a clan.
- **Gender** is set on the base `CharacterObject`, not the name override. The mod author's name choice signals it (names come from the culture's `<male_names>`/`<female_names>` lists — cross-check there, e.g. "Roshan" is in empire male names, "Ruqaiya" in female).
- Non-Muslim ruling clans were already correctly named (Kachwaha/Rajput, Peshwa/Maratha, Sukerchakia/Sikh, Sadozai/Afghan); the one mis-named ruling clan was `clan_aserai_1` "House of Sultani" → **Wodeyar** (its owner is Krishnaraja Wodeyar).

## 6. Harmony patching — what applies and what doesn't

- Confirmed applied (logged via `harmony.GetPatchedMethods()`): `CharacterCreationCultureVM..ctor`, `NameGenerator.InitializePersonNames`. Constructor patches resolve cleanly with `TargetMethod() => typeof(T).GetConstructors()[0]`.
- `PatchAll` is wrapped in try/catch in `OnSubModuleLoad`, writing the patched-method list and any exception to `Desktop\hindostan_patch_debug.txt` — keep this; it instantly answers "did my patch apply?".
- Patching `LocalizedTextManager.GetTranslatedText` "applied" but was useless because that method is off the English path (§1.1). Lesson: confirm a method is actually on the live code path before relying on a patch — a clean patch on a dead method looks like success.

---

## 7. Diagnostic methodology that worked (reuse this)

1. **Offline reflection** with an `AssemblyResolve` handler pointing at the game `bin` (guard against recursion: cache resolved names, return null on miss) to read private fields, method signatures, and accessibility without launching the game.
2. **IL decompilation** via `MethodBody.GetILAsByteArray()` + `Module.ResolveMethod/ResolveString` to see what a method calls (e.g. proving `GetTranslatedText` reads `_gameTextDictionary` with a plain-id `TryGetValue`, and finding `MBTextManager.GetLocalizedText` as its only caller).
3. **In-game debug files written to Desktop** from inside patches (`hindostan_loc_debug.txt`, `hindostan_name_debug.txt`, `hindostan_patch_debug.txt`). Include a live read-back (e.g. `FindText(...)` after an overwrite) — that one line distinguished "wrong store" from "wrong id" from "didn't run".
4. **Map inner→outer keys from Native's XML offline** (`grep`/regex for `{=key}`), because the inner key is gone at runtime.

---

## 8. Parallel world-gen native crash class (NO managed exception, NO tyt_crash log)

- **Symptom:** new game crashes to desktop with `0xC0000005` (access violation). **No** managed stack, **no** `TYTLog`/`tyt_crash` file — because the fault is in native code corrupting a half-built object, not a C# throw.
- **Root cause (proven by reading handler bodies, not theorising):** at new-game start the engine creates heroes/clans/settlements **in parallel (multi-threaded)**. Any `CampaignEvent` handler that fires *during* world-gen and mutates non-thread-safe managed state (a `Dictionary`, an `MBList`, a shared field) races the engine's own writes → heap corruption → native crash. Confirmed writers were `OnSettlementOwnerChanged`/`OnClanChangedKingdom` handlers writing per-kingdom/per-settlement dictionaries (e.g. `ImperialAuthorityBehavior._authority[]`, MansabdariTenure appointedDay).
- **The trap:** fixing ONE handler (we first fixed only MansabdariTenure) doesn't help — it's a **class** of bug. Every handler that can fire during world-gen must be gated.
- **The fix — one shared gate:** `Util/WorldGen.cs` → `public static volatile bool Ready;` plus `WorldGenGuardBehavior` that sets `Ready=false` in `RegisterEvents` and `Ready=true` at `OnSessionLaunched`. **Register it FIRST** in `OnGameStart`. Then every world-gen-firing handler opens with `if (!Util.WorldGen.Ready) return;`. Currently gated: ImperialAuthority, RevoltCascade, ImperialCourt, FiefHierarchy, CareerProgression, Warfare, FeudalTitles, MansabdariTenure.
- **Rule going forward:** any NEW `OnSettlementOwnerChanged` / `OnClanChangedKingdom` / `OnHeroCreated` / owner-or-kingdom-mutating handler **must** start with the `WorldGen.Ready` guard unless it provably only reads.

## 9. Clan creation at runtime — there is no `InitializeClan`

- `Clan.CreateClan(stringId)` returns an **empty shell**: no leader, culture, banner, home, name; `IsInitialized` false. There is **no public `InitializeClan`** on this engine build, and the generic reflection `PropertySetter("Leader", …)` **silently no-ops** (Leader has no usable setter that way).
- **An uninitialised clan native-crashes the encyclopedia** the moment it's referenced (e.g. a temp claimant clan shown in a notification).
- **Working recipe (all public unless noted):**
  ```csharp
  var clan = Clan.CreateClan("tyt_claim_" + id);
  clan.SetLeader(hero);                       // public — the ONLY reliable leader setter
  clan.SetInitialHomeSettlement(settlement);  // public
  clan.Culture = culture;  clan.Banner = banner;   // public
  // Name / InformalName: non-public setters via reflection
  // IsInitialized: force true via reflection
  // move a hero into the clan: non-public Hero.Clan setter via reflection
  ```
- Implemented in `Util/ClaimantClan.cs` (`BuildShell` + `Create` + `CreateExileHouse`). Reuse `BuildShell` for any future runtime clan (rebels, exile houses, cadet branches).

## 10. War state — `IsAtWarWith` is truth; your own `_score` dict is not

- **Symptom:** "Direct the war effort" reported *"the realm is at peace"* while two wars were active, and war score barely moved after a big battle.
- **Cause:** the behaviour tracked wars only in its own `_score` dictionary, populated solely by `OnWarDeclared`. Wars that began before the behaviour loaded, or via other paths, were never tracked → invisible.
- **Fix / rule:** never treat your own tracking dict as the source of war existence. Read engine truth: `kingdom.IsAtWarWith(other)` (or `FactionManager.IsAtWarAgainstFaction`). Add `EnsureWarTracked(kingdom)` that lazily creates a score entry from the engine's actual war list before reading/showing it. Score deltas now: battle 10 / siege 18 / enemy king captured +25.
- **Regicide (`HeroKilledEvent`, killed hero is an enemy ruler):** `MakePeaceAction` (temporary peace from the loser), −30 relation with all their lords, register a revenge casus belli, −3 legitimacy for the perpetrator. Big consequences, as designed.

## 11. Complete vanilla-name purge — culture-keyed outer ids are settable DIRECTLY

- Extends §1/§3. The culture/faction/ruler strings in Native `module_strings.xml` are keyed by their **outer** id (`str_<base>.<culture>`), so `LocalizationOverride` sets them **without** needing the inner→outer `hindostan_string_map.xml` (that map is only for `std_*.xml` inner-keyed strings).
- **The gap that leaked "Principality of Sturgia" / "King of the Vlandians":** coverage was *uneven* — some families/cultures had overrides, others didn't. The fix is **exhaustive generation**, not hand-editing. Two re-runnable generators:
  - `tools/gen_faction_names.py` → `Languages/hindostan_faction_names.xml` (~115 rows): for **all 8 cultures**, every family — `str_faction_ruler[_f]`, `str_faction_official[_f]`, `str_faction_ruler_name_with_title`, `str_faction_ruler_term_in_speech`, `str_adjective_for_culture/faction`, `str_neutral_term_for_culture`, `str_short_term_for_faction`, `str_culture_rich_name`, `str_faction_formal_name_for_culture`, `str_faction_informal_name_for_culture`, `str_kingdom_formal_name` + `str_culture_description` for the 3 imperial cultures.
  - `tools/gen_prose_overrides.py` → `Languages/hindostan_prose_overrides.xml` (~12 rows): proper-noun swaps (Calradia→Hindostan, Vlandia→Rajputana, Sturgia→Afghanistan, Battania→Maharashtra, Khuzait→Sikh, Aserai→Mysorean, Nord→Pathan) across all semantic-id prose; **excludes** culture-keyed ids (owned by the faction-names generator).
- **Load order matters:** `LocalizationOverride.EnsureParsed` loads faction_names **LAST** so it wins. Adding a new term = one map entry + re-run the generator.
- **Known remaining gap:** dialogue prose stored in `std_*.xml` under **inner `{=}` keys** is NOT covered by either generator — that needs `hindostan_string_map.xml` inner→outer entries (§1.5). If a vanilla name appears *in conversation*, it's almost certainly here.

## 12. World generation is PARALLEL — `OnNewGameCreatedEvent` is a trap

- **Proven crash class** (`0xC0000005`, no managed exception, documented in `bug-reports/2026-06-20-daily-tick-crash.md`): any handler on `OnNewGameCreatedEvent` that iterates `Clan.All` / `clan.Settlements` / `Settlement.All` / notables races the engine's parallel hero/clan/settlement creation. Seven behaviors did this and were fixed (Council, ImperialCourt, ImperialAuthority, Legitimacy, FeudalTitles, FactionRelations, Succession).
- **Rule:** do NOT hook `OnNewGameCreatedEvent` for collection work at all. Seed **idempotently in `OnSessionLaunched`** (only write values that aren't already stored, so loads don't clobber saves). Gate mid-game event handlers (`OnSettlementOwnerChanged`, `HeroKilledEvent`, `OnClanChangedKingdom`) with `Util.WorldGen.Ready` — those events also fire during world-gen setup.
- Same class: `NameGenerator` patches run on parallel hero creation — any lazily-built shared structure they touch needs double-checked locking (see `ReligionBehavior.EnsurePools`).

## 13. Harmony — one bad patch target used to kill EVERY patch

- **Trap:** a single `harmony.PatchAll(assembly)` inside `try/catch` means one patch whose target signature changed on a game update throws during discovery and **silently disables all ~15 patches** (bios, renames, map bar, party caps — everything).
- **Fix (as-built):** `HindostanSubModule` loops the assembly's `[HarmonyPatch]` types and calls `harmony.CreateClassProcessor(type).Patch()` each in its own try/catch, logging failures individually plus a summary count. A broken patch now costs only itself.
- **Verified against the current game DLLs** (reflection, 2026-07): `DeclareWarAction.ApplyInternal` exists (private). `MakePeaceAction.Apply` lost its 4-arg overload in v1.3.11 — `NoThroneWarPeacePatch` now targets the private 5-arg `ApplyInternal` (see §17). The per-class isolation proved itself here: the stale target failed alone and logged itself while the other 12 patch classes applied.
- **Patch application is two-phase** since the §17 finding: GameComponents-model patches apply at `OnGameStart`; everything else at `OnSubModuleLoad`.
- **Namespace traps that cost a compile:** `MBTextManager` lives in **`TaleWorlds.Localization`** — not CampaignSystem, not Core. A file calling `MBTextManager.SetTextVariable` without that using fails with CS0103. `MathF` is **`TaleWorlds.Library.MathF`** (the BCL MathF doesn't exist on net481) — fine in engine code, forbidden in the engine-free `Util/*Math.cs` files (use `System.Math` there, or the tests won't build).

## 14. Save/load — the SyncData conventions that keep saves alive

- **Parallel primitive lists only.** Every behavior serializes dictionaries as parallel key/value lists and rebuilds on load (`!IsSaving`) with **index-guarded** loops. Derived indexes (e.g. FeudalTitles' reverse hero→villages map) are never serialized — rebuilt on load.
- **Append-only keys.** Loading an old save leaves new keys absent → the locals stay empty → defaults. Never rename or reorder anything serialized by value: enum ints (`OpinionType`, tolerance stances), the `VillageProject` display names inside the completed-works CSV.
- **The encyclopedia SAVE ERROR root cause:** the save serializer reads `Hero.EncyclopediaText` for every hero mid-save; returning a **fresh `TextObject`** from a patched getter makes it choke ("SAVE ERROR. Cant find … with type TextObject", once per lord). Fix: cache ONE `TextObject` per hero (`EncyclopediaInfoPatch._bioCache`) and always return the same instance; the `IsSaving` guard remains only as a first-read fallback. Related: `SaveGuardBehavior.IsSaving` can stick if a save errors before `OnSaveOverEvent` — a campaign tick observed with the flag set clears it (ticks never run during synchronous serialization).
- **Static helpers don't persist themselves.** `ClaimantClan._origin` (hero→origin clan) is a static dict; `SuccessionBehavior.SyncData` exports/imports it. Any static helper holding campaign state needs a behavior to own its persistence.

## 15. The farmaan layer — what it actually is, and its two safety mechanisms

- `RoyalFarmaan` is **not a screen**: it adds a `GauntletLayer("HindostanFarmaan", 1010)` onto `ScreenManager.TopScreen` (above the Council/Hierarchy screens at 1000) with full input restriction + focus. One popup at a time from a static queue.
- **Re-entrancy (native crash):** pushing a focus layer synchronously while the engine iterates settlements (a daily settlement tick) re-enters the screen system and crashes. `SuppressImmediate` (set by settlement-tick callers) makes `Issue` enqueue-only; `Pump()` — driven from `SaveGuardBehavior`'s campaign `TickEvent`, outside the iteration — drains the queue. Don't bypass this.
- **Pause (as-built, compiles on current DLLs):** `ShowNext` stores `Campaign.Current.TimeControlMode` and sets `CampaignTimeControlMode.Stop`; `Close` restores it only when the queue is empty AND the farmaan system itself did the pausing (a player already on pause stays paused).
- **Dedup/downgrade rules** live in pure `Util/FarmaanFlow.Decide` (unit-tested): Ceremonial is never suppressed; an identical live `dedupeKey` drops; a Routine notice in cooldown (or with none) downgrades to a log line + the weekly "Court Circular" digest (`FarmaanDirectorBehavior`); **choice-bearing farmaans are never downgraded** — dedupe only, or the player's choices silently vanish.

## 16. Creating clans (and the missing party primitive)

- **Recipe** (one factory: `Util/CadetHouse.BuildShell`, delegated to by `ClaimantClan`): `Clan.CreateClan(id)` registers an EMPTY shell; then Name/InformalName via non-public setters (`AccessTools.PropertySetter`), `Culture`, `Banner.CreateRandomClanBanner` (**mandatory** — the encyclopedia dereferences it), `SetInitialHomeSettlement`, force `IsInitialized`, `clan.SetLeader(hero)` (public — historically the missed step), and move heroes via the **non-public `Hero.Clan` setter** (it maintains both clans' member lists; a raw field write corrupts them).
- **The party gap:** the mod never created a `MobileParty` for a new lord before cadet houses. Verified present in the current DLLs (reflection): `Helpers.MobilePartyHelper.SpawnLordParty` (public, 2 overloads) and `LordPartyComponent.CreateLordParty` (public). `CadetHouse.TrySpawnLordParty` tries both via reflection with parameter matching; fallback = teleport the leader home and let the engine's clan-party AI raise one. Exact overload behavior is a **runtime** verification item (PLAYTEST.md §G2).

## 17. Patching a GameComponents model at OnSubModuleLoad poisons its static initializer

*(Root-caused from a real crash dump with WinDbg+SOS, 2026-07: "Exception occurred inside invoke: OnNextStage … Object reference not set" when finishing sandbox character creation.)*

- **The mechanism.** TaleWorlds' model classes (v1.3.11+) initialize static `TextObject` fields via `GameTexts.FindText(...)` — e.g. `DefaultPartySpeedCalculatingModel`'s `_culture = GameTexts.FindText("str_culture")`, and four statics on `DefaultArmyManagementCalculationModel`. `GameTexts._gameTextManager` is only set inside `Game.Initialize()` — it is **null** during `OnSubModuleLoad`. Applying a Harmony patch prepares (JIT-compiles) the target method; if that method's body reads the class's statics, the runtime may run the `beforefieldinit` static initializer **right then** → `NullReferenceException` inside the cctor → the type is **permanently poisoned** for the process. Harmony still reports the patch as applied, campaign creation succeeds (the registered model is a subclass, which does NOT force the base cctor), and the cached `TypeInitializationException` finally fires at the FIRST call of a base method — the first party-speed calculation when the map screen spawns. The crash site is nowhere near the cause and nothing appears in any log until the throw.
- **The rule.** Patches on `TaleWorlds.CampaignSystem.GameComponents.*` (any class whose statics could call `GameTexts`/need a live `Game`) are applied in `OnGameStart`, never `OnSubModuleLoad`. `HindostanSubModule.GameStartPatches` is the explicit set; `Campaign.OnInitialize` calls `GameManager.OnGameStart` strictly **after** `Game.Initialize()` has run `GameTexts.Initialize`, so the cctor succeeds there (verified in the v1.3.11 decompile). UI/VM, `NameGenerator`, `Hero`, and Actions targets have no such statics and stay at load time.
- **Diagnosing this class of crash.** The engine log's "Exception occurred inside invoke" wrapper prints only the inner message — no stack. The `dump.dmp` in `C:\ProgramData\Mount and Blade II Bannerlord\crashes\<timestamp>\` contains the full managed heap: `WinDbgX -z dump.dmp -c ".loadby sos clr; !threads; !pe -nested <addr>"` gives the exception chain; a `TypeInitializationException` whose inner stack shows `..cctor → GameTexts.FindText` is this finding. Note `!analyze` may show duplicate TaleWorlds modules (`TaleWorlds_Core_<addr>`) — those are file mappings by the BUTR crash reporter, **not** a double assembly load (`!name2ee *!<type>` proves one MethodTable).
- **Same-dump bonus finding:** v1.3.11 removed the 4-arg `MakePeaceAction.Apply` overload; all peace paths funnel through the private 5-arg `ApplyInternal(IFaction, IFaction, int, int, MakePeaceDetail)` — `NoThroneWarPeacePatch` targets that now.

---

**[Home](Home.md)**
