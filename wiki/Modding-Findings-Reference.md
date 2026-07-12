# Modding Findings Reference â€” Empirical, Verified

> Hard-won facts about how Bannerlord v1.4.6 actually behaves, discovered by reflection,
> IL decompilation, and in-game debug logging during the Phase-1 renaming work. Each entry
> is **verified**, not assumed. Read this before touching localization, names, or mod setup
> so the same dead-ends aren't repeated.

**[Home](Home.md)**

---

## 1. Localization â€” the single most important finding

### 1.1 There are TWO string stores; English uses only one

| Store | Type | Used for | Key |
|-------|------|----------|-----|
| `GameTexts._gameTextManager._gameTexts` | `Dictionary<string, GameText>` (in `TaleWorlds.Core`) | **English (default) display text** | outer semantic id + variation |
| `LocalizedTextManager._gameTextDictionary` | `Dictionary<string, string>` (in `TaleWorlds.Localization`) | **non-English translations only** | inner `{=}` key |

- **Proven:** at runtime in an English game, `_gameTextDictionary` is **empty** (logged `dict 0->84`). It is the translation layer; it stays empty unless a non-English language is loaded.
- **Therefore:** to change English text you must overwrite the `GameText` in `GameTexts._gameTextManager`. Writing to `_gameTextDictionary`, or patching `LocalizedTextManager.GetTranslatedText`, does nothing in English. Both were tried and both failed.
- **ROUND-8 SUPERSEDING NOTE (see ch.21):** there IS a second working path, found later —
  a Harmony prefix on `MBTextManager.GetLocalizedText` (upstream of `GetTranslatedText`, the
  point where English short-circuits to the inline text). It covers what GameText-poking
  cannot: inline code TextObjects (quests, dialogue, backstories) and strings baked into
  saves. The two mechanisms now coexist: `LocalizationOverride` for GameText variations,
  `EnglishTextOverridePatch` for everything `{=key}`-driven at render. Nuance to §1.2's
  "inner key not retained": the key has no dedicated field, but it IS embedded in the raw
  `TextObject.Value` (`"{=key}text"`) and re-parsed on every render — which is exactly what
  makes the render-time override (and its save-healing) possible.

### 1.2 Inner `{=}` key vs outer id â€” the trap that cost the most time

A vanilla string line looks like:
```xml
<string id="str_faction_noble_name_with_title.empire"
        text="{=nAaT19CC}{?RULER.GENDER}Archoness{?}Archon{\?} {RULER.NAME}" />
```
- **Outer id** = `str_faction_noble_name_with_title.empire` â€” what the renderer looks up. The English value is the inline text (`â€¦Archonâ€¦`).
- **Inner key** = `nAaT19CC` â€” only a translation-lookup key for non-English. **Not retained at runtime** (it is not stored on `GameText`, `GameTextVariation`, or `TextObject` â€” `TextObject` only keeps `Value`). So you cannot recover it after load; you must map it from Native's XML offline.
- The mod's `std_module_strings_xml.xml` was keyed entirely by **inner keys**, so in English nothing it defined was ever read. This is THE root cause of every "rename didn't work" symptom (titles, descriptions, world refs).

### 1.3 GameText id + variation storage

- `<string id="A.B">` is stored as `GameText` with **id `A`** and a **variation `B`**. Lookup is `GameTexts.FindText("A", "B")`.
- So `str_faction_noble_name_with_title.empire` â‡’ `GetGameText("str_faction_noble_name_with_title")`, variation `"empire"`.
- `GameTextManager.GetGameText(id)` returns **null** if the id isn't a stored base id (it does not auto-create â€” `AddGameText` creates). Resolution rule we use: try `GetGameText(fullId)`; if null and the id contains `.`, split at the **last** `.` and `GetGameText(base)` with the remainder as variation; if still null, `AddGameText(fullId)` (for keys consumed inline in code).
- **Never** `Clear()` a `GameText._variationList` â€” sibling cultures share one `GameText` (e.g. `str_faction_noble_name_with_title` holds a variation per culture). Use `GameText.SetVariationWithId(variationId, TextObject, List<GameTextManager.ChoiceTag>)` to replace just one variation.

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

- `ModuleData/hindostan_string_map.xml` â€” generated from Native's files: `inner key -> outer id(s)`. One inner key can map to several outer ids (e.g. a culture adjective appears in `str_adjective_for_faction.*` and `str_adjective_for_culture.*`); each is overwritten.
- `Patches/LocalizationOverride.cs` â€” reads `std_module_strings_xml.xml` (text, single source of truth) + the map, resolves each to outer id+variation, and overwrites via `SetVariationWithId`. Applied from `NameGenerator.InitializePersonNames` postfix (a hook proven to run at campaign init).
- Timing caveat: this hook runs **after** character creation, so the char-creation culture description panel is not covered by it (separate handling needed there).

---

## 2. Name generation â€” Calradic NPC names

### 2.1 Three independent name sources

| Source | Holds | Used by |
|--------|-------|---------|
| `CultureObject._maleNameList` / `_femaleNameList` / `_clanNameList` (`MBList<TextObject>`) | per-culture names from `<male_names>` etc. | most generated heroes |
| `NameGenerator._imperialNamesMale` / `_imperialNamesFemale` (`MBList<TextObject>`) | empire/Mughal names | empire culture |
| `NameGenerator._merchantNames` / `_artisanNames` / `_preacherNames` / `_gangLeaderNames` | town notable pools | traders, gang leaders, etc. â€” **culture-agnostic** |

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

- The culture **name** on the selection list is `CharacterCreationCultureVM.NameText` (a property), set in the VM constructor. It reads `str_culture_rich_name.{id}` â€” **not** `CultureObject.Name` (that is encyclopedia/dialogue).
- Fixed deterministically by a Harmony postfix on the VM constructor (`Patches/CultureNamePatch.cs`) setting `NameText`/`ShortenedNameText`. The VM also exposes `DescriptionText` (use this to fix the description panel, which the campaign-time localization overlay can't reach).
- The selection list shows **only the 6 hard-coded main vanilla cultures** (empire, sturgia, vlandia, aserai, khuzait, battania). `empire_w`/`empire_s` (Bengal/Hyderabad) never appear there.

---

## 4. Mod setup / loading

- **Culture screen names DON'T come from `CultureObject.Name`.** Renaming via the `name="{=!}â€¦"` attribute in `tyt_spcultures.xml` updates encyclopedia/dialogue only.
- **English language files auto-load** from `ModuleData/Languages/*.xml` when their root is `<base type="string">`; a `language_data.xml` listing them is optional for English (Native's own `std_*.xml` load without being listed). This was NOT the blocker â€” the inner/outer key mismatch (Â§1.2) was.
- **Junction install:** the dev folder is linked into the game via a directory junction (`Modules\The Hindostan Mod` â†’ `Desktop\TakhtyaTaboot`). The game reads `bin\Win64_Shipping_Client\` live; no copy step. A duplicate real folder with the same module `Id` causes two launcher entries and the game may load the stale one â€” keep exactly one.
- **Harmony** is provided by the `Bannerlord.Harmony` module (v2.4.2). Declare it as `<DependedModule Id="Bannerlord.Harmony" .../>`; do **not** also ship `0Harmony.dll` (version conflict). `Lib.Harmony` PackageReference uses `<ExcludeAssets>runtime</ExcludeAssets>` so it compiles but isn't copied.
- **Project:** `net481`, `x64`, output straight to `..\..\..\bin\Win64_Shipping_Client\`. Reference DLLs with `<Private>False</Private>`. Note `TaleWorlds.MountAndBlade.View.dll` lives in `Modules\Native\bin\â€¦` while `TaleWorlds.MountAndBlade.ViewModelCollection.dll` and `TaleWorlds.CampaignSystem.ViewModelCollection.dll` live in the **main** `bin\â€¦`.

---

## 5. Character data model â€” names vs bios vs families vs clans

Four different files own different parts of a lord, and confusing them wastes time:

| What you want to change | File | Mechanism |
|--------------------------|------|-----------|
| Hero **display name** | `spnpccharacters.xml` (`<NPCCharacter id name="{=!}â€¦">`) | mod overrides by id; attribute-merge |
| Hero **encyclopedia bio** | **`heroes.xml`** (`<Hero id text="{=key}â€¦">`) â€” in `SandBox`, **not** spnpccharacters | mod must register `<XmlName id="Heroes" path="heroes"/>` and override by id |
| Hero **family** (`father`/`mother`/`spouse`) | **`heroes.xml`** | siblings = shared `father` OR `mother`; there is no "brother" attribute |
| Hero **clan membership** | **`heroes.xml`** (`faction="Faction.clan_â€¦"`) | â€” |
| **Clan** name / owner / banner | `tyt_spclans.xml` (`<Faction id name owner super_faction>`) | mod loads it via `<XmlName id="Factions" path="tyt_spclans"/>` |

- **The trap that produced "Lucon" text on Muhammad Shah:** the bio is `heroes.xml`'s `text=`, and the mod only overrode `spnpccharacters.xml` (`text` there is **not** the encyclopedia bio). Phase-1 renamed names but left every bio, family link, and clan assignment vanilla.
- **Merge is attribute-level** (proven: Phase-1 name-only overrides kept faction/spouse). Still, anchor critical attributes (`faction`) in hero overrides so clan membership can't be lost even if a future loader does element-replace.
- **Restructuring clans is risky:** a clan owns settlements via `owner=` + `initial_home_settlement`. Removing/reassigning an owner orphans settlements. Prefer fixing the *relationship* (e.g. set Tipu's `father=Hyder` and keep his cadet clan) over moving a leader out of a clan.
- **Gender** is set on the base `CharacterObject`, not the name override. The mod author's name choice signals it (names come from the culture's `<male_names>`/`<female_names>` lists â€” cross-check there, e.g. "Roshan" is in empire male names, "Ruqaiya" in female).
- Non-Muslim ruling clans were already correctly named (Kachwaha/Rajput, Peshwa/Maratha, Sukerchakia/Sikh, Sadozai/Afghan); the one mis-named ruling clan was `clan_aserai_1` "House of Sultani" â†’ **Wodeyar** (its owner is Krishnaraja Wodeyar).

## 6. Harmony patching â€” what applies and what doesn't

- Confirmed applied (logged via `harmony.GetPatchedMethods()`): `CharacterCreationCultureVM..ctor`, `NameGenerator.InitializePersonNames`. Constructor patches resolve cleanly with `TargetMethod() => typeof(T).GetConstructors()[0]`.
- `PatchAll` is wrapped in try/catch in `OnSubModuleLoad`, writing the patched-method list and any exception to `Desktop\hindostan_patch_debug.txt` â€” keep this; it instantly answers "did my patch apply?".
- Patching `LocalizedTextManager.GetTranslatedText` "applied" but was useless because that method is off the English path (Â§1.1). Lesson: confirm a method is actually on the live code path before relying on a patch â€” a clean patch on a dead method looks like success.

---

## 7. Diagnostic methodology that worked (reuse this)

1. **Offline reflection** with an `AssemblyResolve` handler pointing at the game `bin` (guard against recursion: cache resolved names, return null on miss) to read private fields, method signatures, and accessibility without launching the game.
2. **IL decompilation** via `MethodBody.GetILAsByteArray()` + `Module.ResolveMethod/ResolveString` to see what a method calls (e.g. proving `GetTranslatedText` reads `_gameTextDictionary` with a plain-id `TryGetValue`, and finding `MBTextManager.GetLocalizedText` as its only caller).
3. **In-game debug files written to Desktop** from inside patches (`hindostan_loc_debug.txt`, `hindostan_name_debug.txt`, `hindostan_patch_debug.txt`). Include a live read-back (e.g. `FindText(...)` after an overwrite) â€” that one line distinguished "wrong store" from "wrong id" from "didn't run".
4. **Map innerâ†’outer keys from Native's XML offline** (`grep`/regex for `{=key}`), because the inner key is gone at runtime.

---

## 8. Parallel world-gen native crash class (NO managed exception, NO tyt_crash log)

- **Symptom:** new game crashes to desktop with `0xC0000005` (access violation). **No** managed stack, **no** `TYTLog`/`tyt_crash` file â€” because the fault is in native code corrupting a half-built object, not a C# throw.
- **Root cause (proven by reading handler bodies, not theorising):** at new-game start the engine creates heroes/clans/settlements **in parallel (multi-threaded)**. Any `CampaignEvent` handler that fires *during* world-gen and mutates non-thread-safe managed state (a `Dictionary`, an `MBList`, a shared field) races the engine's own writes â†’ heap corruption â†’ native crash. Confirmed writers were `OnSettlementOwnerChanged`/`OnClanChangedKingdom` handlers writing per-kingdom/per-settlement dictionaries (e.g. `ImperialAuthorityBehavior._authority[]`, MansabdariTenure appointedDay).
- **The trap:** fixing ONE handler (we first fixed only MansabdariTenure) doesn't help â€” it's a **class** of bug. Every handler that can fire during world-gen must be gated.
- **The fix â€” one shared gate:** `Util/WorldGen.cs` â†’ `public static volatile bool Ready;` plus `WorldGenGuardBehavior` that sets `Ready=false` in `RegisterEvents` and `Ready=true` at `OnSessionLaunched`. **Register it FIRST** in `OnGameStart`. Then every world-gen-firing handler opens with `if (!Util.WorldGen.Ready) return;`. Currently gated: ImperialAuthority, RevoltCascade, ImperialCourt, FiefHierarchy, CareerProgression, Warfare, FeudalTitles, MansabdariTenure.
- **Rule going forward:** any NEW `OnSettlementOwnerChanged` / `OnClanChangedKingdom` / `OnHeroCreated` / owner-or-kingdom-mutating handler **must** start with the `WorldGen.Ready` guard unless it provably only reads.

## 9. Clan creation at runtime â€” there is no `InitializeClan`

- `Clan.CreateClan(stringId)` returns an **empty shell**: no leader, culture, banner, home, name; `IsInitialized` false. There is **no public `InitializeClan`** on this engine build, and the generic reflection `PropertySetter("Leader", â€¦)` **silently no-ops** (Leader has no usable setter that way).
- **An uninitialised clan native-crashes the encyclopedia** the moment it's referenced (e.g. a temp claimant clan shown in a notification).
- **Working recipe (all public unless noted):**
  ```csharp
  var clan = Clan.CreateClan("tyt_claim_" + id);
  clan.SetLeader(hero);                       // public â€” the ONLY reliable leader setter
  clan.SetInitialHomeSettlement(settlement);  // public
  clan.Culture = culture;  clan.Banner = banner;   // public
  // Name / InformalName: non-public setters via reflection
  // IsInitialized: force true via reflection
  // move a hero into the clan: non-public Hero.Clan setter via reflection
  ```
- Implemented in `Util/ClaimantClan.cs` (`BuildShell` + `Create` + `CreateExileHouse`). Reuse `BuildShell` for any future runtime clan (rebels, exile houses, cadet branches).

## 10. War state â€” `IsAtWarWith` is truth; your own `_score` dict is not

- **Symptom:** "Direct the war effort" reported *"the realm is at peace"* while two wars were active, and war score barely moved after a big battle.
- **Cause:** the behaviour tracked wars only in its own `_score` dictionary, populated solely by `OnWarDeclared`. Wars that began before the behaviour loaded, or via other paths, were never tracked â†’ invisible.
- **Fix / rule:** never treat your own tracking dict as the source of war existence. Read engine truth: `kingdom.IsAtWarWith(other)` (or `FactionManager.IsAtWarAgainstFaction`). Add `EnsureWarTracked(kingdom)` that lazily creates a score entry from the engine's actual war list before reading/showing it. Score deltas now: battle 10 / siege 18 / enemy king captured +25.
- **Regicide (`HeroKilledEvent`, killed hero is an enemy ruler):** `MakePeaceAction` (temporary peace from the loser), âˆ’30 relation with all their lords, register a revenge casus belli, âˆ’3 legitimacy for the perpetrator. Big consequences, as designed.

## 11. Complete vanilla-name purge â€” culture-keyed outer ids are settable DIRECTLY

- Extends Â§1/Â§3. The culture/faction/ruler strings in Native `module_strings.xml` are keyed by their **outer** id (`str_<base>.<culture>`), so `LocalizationOverride` sets them **without** needing the innerâ†’outer `hindostan_string_map.xml` (that map is only for `std_*.xml` inner-keyed strings).
- **The gap that leaked "Principality of Sturgia" / "King of the Vlandians":** coverage was *uneven* â€” some families/cultures had overrides, others didn't. The fix is **exhaustive generation**, not hand-editing. Two re-runnable generators:
  - `tools/gen_faction_names.py` â†’ `Languages/hindostan_faction_names.xml` (~115 rows): for **all 8 cultures**, every family â€” `str_faction_ruler[_f]`, `str_faction_official[_f]`, `str_faction_ruler_name_with_title`, `str_faction_ruler_term_in_speech`, `str_adjective_for_culture/faction`, `str_neutral_term_for_culture`, `str_short_term_for_faction`, `str_culture_rich_name`, `str_faction_formal_name_for_culture`, `str_faction_informal_name_for_culture`, `str_kingdom_formal_name` + `str_culture_description` for the 3 imperial cultures.
  - `tools/gen_prose_overrides.py` â†’ `Languages/hindostan_prose_overrides.xml` (~12 rows): proper-noun swaps (Calradiaâ†’Hindostan, Vlandiaâ†’Rajputana, Sturgiaâ†’Afghanistan, Battaniaâ†’Maharashtra, Khuzaitâ†’Sikh, Aseraiâ†’Mysorean, Nordâ†’Pathan) across all semantic-id prose; **excludes** culture-keyed ids (owned by the faction-names generator).
- **Load order matters:** `LocalizationOverride.EnsureParsed` loads faction_names **LAST** so it wins. Adding a new term = one map entry + re-run the generator.
- **Known remaining gap:** dialogue prose stored in `std_*.xml` under **inner `{=}` keys** is NOT covered by either generator â€” that needs `hindostan_string_map.xml` innerâ†’outer entries (Â§1.5). If a vanilla name appears *in conversation*, it's almost certainly here.

## 12. World generation is PARALLEL â€” `OnNewGameCreatedEvent` is a trap

- **Proven crash class** (`0xC0000005`, no managed exception, documented in `bug-reports/2026-06-20-daily-tick-crash.md`): any handler on `OnNewGameCreatedEvent` that iterates `Clan.All` / `clan.Settlements` / `Settlement.All` / notables races the engine's parallel hero/clan/settlement creation. Seven behaviors did this and were fixed (Council, ImperialCourt, ImperialAuthority, Legitimacy, FeudalTitles, FactionRelations, Succession).
- **Rule:** do NOT hook `OnNewGameCreatedEvent` for collection work at all. Seed **idempotently in `OnSessionLaunched`** (only write values that aren't already stored, so loads don't clobber saves). Gate mid-game event handlers (`OnSettlementOwnerChanged`, `HeroKilledEvent`, `OnClanChangedKingdom`) with `Util.WorldGen.Ready` â€” those events also fire during world-gen setup.
- Same class: `NameGenerator` patches run on parallel hero creation â€” any lazily-built shared structure they touch needs double-checked locking (see `ReligionBehavior.EnsurePools`).

## 13. Harmony â€” one bad patch target used to kill EVERY patch

- **Trap:** a single `harmony.PatchAll(assembly)` inside `try/catch` means one patch whose target signature changed on a game update throws during discovery and **silently disables all ~15 patches** (bios, renames, map bar, party caps â€” everything).
- **Fix (as-built):** `HindostanSubModule` loops the assembly's `[HarmonyPatch]` types and calls `harmony.CreateClassProcessor(type).Patch()` each in its own try/catch, logging failures individually plus a summary count. A broken patch now costs only itself.
- **Verified against the current game DLLs** (reflection, 2026-07): `DeclareWarAction.ApplyInternal` exists (private). `MakePeaceAction.Apply` lost its 4-arg overload in v1.3.11 â€” `NoThroneWarPeacePatch` now targets the private 5-arg `ApplyInternal` (see Â§17). The per-class isolation proved itself here: the stale target failed alone and logged itself while the other 12 patch classes applied.
- **Patch application is two-phase** since the Â§17 finding: GameComponents-model patches apply at `OnGameStart`; everything else at `OnSubModuleLoad`.
- **Namespace traps that cost a compile:** `MBTextManager` lives in **`TaleWorlds.Localization`** â€” not CampaignSystem, not Core. A file calling `MBTextManager.SetTextVariable` without that using fails with CS0103. `MathF` is **`TaleWorlds.Library.MathF`** (the BCL MathF doesn't exist on net481) â€” fine in engine code, forbidden in the engine-free `Util/*Math.cs` files (use `System.Math` there, or the tests won't build).

## 14. Save/load â€” the SyncData conventions that keep saves alive

- **Parallel primitive lists only.** Every behavior serializes dictionaries as parallel key/value lists and rebuilds on load (`!IsSaving`) with **index-guarded** loops. Derived indexes (e.g. FeudalTitles' reverse heroâ†’villages map) are never serialized â€” rebuilt on load.
- **Append-only keys.** Loading an old save leaves new keys absent â†’ the locals stay empty â†’ defaults. Never rename or reorder anything serialized by value: enum ints (`OpinionType`, tolerance stances), the `VillageProject` display names inside the completed-works CSV.
- **The encyclopedia SAVE ERROR root cause:** the save serializer reads `Hero.EncyclopediaText` for every hero mid-save; returning a **fresh `TextObject`** from a patched getter makes it choke ("SAVE ERROR. Cant find â€¦ with type TextObject", once per lord). Fix: cache ONE `TextObject` per hero (`EncyclopediaInfoPatch._bioCache`) and always return the same instance; the `IsSaving` guard remains only as a first-read fallback. Related: `SaveGuardBehavior.IsSaving` can stick if a save errors before `OnSaveOverEvent` â€” a campaign tick observed with the flag set clears it (ticks never run during synchronous serialization).
- **Static helpers don't persist themselves.** `ClaimantClan._origin` (heroâ†’origin clan) is a static dict; `SuccessionBehavior.SyncData` exports/imports it. Any static helper holding campaign state needs a behavior to own its persistence.

## 15. The farmaan layer â€” what it actually is, and its two safety mechanisms

- `RoyalFarmaan` is **not a screen**: it adds a `GauntletLayer("HindostanFarmaan", 1010)` onto `ScreenManager.TopScreen` (above the Council/Hierarchy screens at 1000) with full input restriction + focus. One popup at a time from a static queue.
- **Re-entrancy (native crash):** pushing a focus layer synchronously while the engine iterates settlements (a daily settlement tick) re-enters the screen system and crashes. `SuppressImmediate` (set by settlement-tick callers) makes `Issue` enqueue-only; `Pump()` â€” driven from `SaveGuardBehavior`'s campaign `TickEvent`, outside the iteration â€” drains the queue. Don't bypass this.
- **Pause (as-built, compiles on current DLLs):** `ShowNext` stores `Campaign.Current.TimeControlMode` and sets `CampaignTimeControlMode.Stop`; `Close` restores it only when the queue is empty AND the farmaan system itself did the pausing (a player already on pause stays paused).
- **Dedup/downgrade rules** live in pure `Util/FarmaanFlow.Decide` (unit-tested): Ceremonial is never suppressed; an identical live `dedupeKey` drops; a Routine notice in cooldown (or with none) downgrades to a log line + the weekly "Court Circular" digest (`FarmaanDirectorBehavior`); **choice-bearing farmaans are never downgraded** â€” dedupe only, or the player's choices silently vanish.

## 16. Creating clans (and the missing party primitive)

- **Recipe** (one factory: `Util/CadetHouse.BuildShell`, delegated to by `ClaimantClan`): `Clan.CreateClan(id)` registers an EMPTY shell; then Name/InformalName via non-public setters (`AccessTools.PropertySetter`), `Culture`, `Banner.CreateRandomClanBanner` (**mandatory** â€” the encyclopedia dereferences it), `SetInitialHomeSettlement`, force `IsInitialized`, `clan.SetLeader(hero)` (public â€” historically the missed step), and move heroes via the **non-public `Hero.Clan` setter** (it maintains both clans' member lists; a raw field write corrupts them).
- **The party gap:** the mod never created a `MobileParty` for a new lord before cadet houses. Verified present in the current DLLs (reflection): `Helpers.MobilePartyHelper.SpawnLordParty` (public, 2 overloads) and `LordPartyComponent.CreateLordParty` (public). `CadetHouse.TrySpawnLordParty` tries both via reflection with parameter matching; fallback = teleport the leader home and let the engine's clan-party AI raise one. Exact overload behavior is a **runtime** verification item (PLAYTEST.md Â§G2).

## 17. Patching a GameComponents model at OnSubModuleLoad poisons its static initializer

*(Root-caused from a real crash dump with WinDbg+SOS, 2026-07: "Exception occurred inside invoke: OnNextStage â€¦ Object reference not set" when finishing sandbox character creation.)*

- **The mechanism.** TaleWorlds' model classes (v1.3.11+) initialize static `TextObject` fields via `GameTexts.FindText(...)` â€” e.g. `DefaultPartySpeedCalculatingModel`'s `_culture = GameTexts.FindText("str_culture")`, and four statics on `DefaultArmyManagementCalculationModel`. `GameTexts._gameTextManager` is only set inside `Game.Initialize()` â€” it is **null** during `OnSubModuleLoad`. Applying a Harmony patch prepares (JIT-compiles) the target method; if that method's body reads the class's statics, the runtime may run the `beforefieldinit` static initializer **right then** â†’ `NullReferenceException` inside the cctor â†’ the type is **permanently poisoned** for the process. Harmony still reports the patch as applied, campaign creation succeeds (the registered model is a subclass, which does NOT force the base cctor), and the cached `TypeInitializationException` finally fires at the FIRST call of a base method â€” the first party-speed calculation when the map screen spawns. The crash site is nowhere near the cause and nothing appears in any log until the throw.
- **The rule.** Patches on `TaleWorlds.CampaignSystem.GameComponents.*` (any class whose statics could call `GameTexts`/need a live `Game`) are applied in `OnGameStart`, never `OnSubModuleLoad`. `HindostanSubModule.GameStartPatches` is the explicit set; `Campaign.OnInitialize` calls `GameManager.OnGameStart` strictly **after** `Game.Initialize()` has run `GameTexts.Initialize`, so the cctor succeeds there (verified in the v1.3.11 decompile). UI/VM, `NameGenerator`, `Hero`, and Actions targets have no such statics and stay at load time.
- **Diagnosing this class of crash.** The engine log's "Exception occurred inside invoke" wrapper prints only the inner message â€” no stack. The `dump.dmp` in `C:\ProgramData\Mount and Blade II Bannerlord\crashes\<timestamp>\` contains the full managed heap: `WinDbgX -z dump.dmp -c ".loadby sos clr; !threads; !pe -nested <addr>"` gives the exception chain; a `TypeInitializationException` whose inner stack shows `..cctor â†’ GameTexts.FindText` is this finding. Note `!analyze` may show duplicate TaleWorlds modules (`TaleWorlds_Core_<addr>`) â€” those are file mappings by the BUTR crash reporter, **not** a double assembly load (`!name2ee *!<type>` proves one MethodTable).
- **Same-dump bonus finding:** v1.3.11 removed the 4-arg `MakePeaceAction.Apply` overload; all peace paths funnel through the private 5-arg `ApplyInternal(IFaction, IFaction, int, int, MakePeaceDetail)` â€” `NoThroneWarPeacePatch` targets that now.

## 18. Gauntlet ListPanel vertical stacking is named BACKWARDS

*(Root-caused from a playtest screenshot, 2026-07: the hierarchy board rendered upside down - title at the bottom, lords below their zamindars.)*

- **The quirk.** Gauntlet's layout Y-axis is bottom-origin, so `StackLayout.LayoutMethod="VerticalBottomToTop"` renders children **visually top-to-bottom** (first child at the top), and `VerticalTopToBottom` renders them bottom-up. Vanilla prefabs confirm it: Native uses `VerticalBottomToTop` 50x vs `VerticalTopToBottom` 7x, including the character-creation culture list that plainly reads downward in game.
- **The rule.** For a normal top-down stack, always write `StackLayout.LayoutMethod="VerticalBottomToTop"`. Horizontal methods are named sanely (`HorizontalLeftToRight` renders left to right).
- **History.** Every mod prefab written before this finding used `VerticalTopToBottom` and therefore rendered reversed; the hierarchy board and village works ledger are fixed. `HindostanCouncil.xml` / `HindostanFarmaan.xml` still carry the old value - flip them the next time either screen is touched (their layouts have been accepted as-is for weeks, so flipping blind is riskier than leaving them).
- **Faces on cards:** v1.3 split `ImageIdentifierVM` into typed classes under `TaleWorlds.Core.ViewModelCollection.ImageIdentifiers` - use `new CharacterImageIdentifierVM(CharacterCode.CreateFrom(hero.CharacterObject))` and bind `<ImageIdentifierWidget DataSource="{Visual}" ImageId="@Id" AdditionalArgs="@AdditionalArgs" TextureProviderName="@TextureProviderName" />`.

## 19. Gauntlet layout bindings write BACK into the VM — the property type must match exactly

*(Root-caused from the mosque-build crash, 2026-07-12: `Exception occurred inside invoke: set_BarWidth … Object of type 'System.Single' cannot be converted to type 'System.Int32'`, then the same conversion error broke `ExecuteAct`, then a native crash took the game down. Heartbeat showed an unrelated crumb — the exception fired in the UI invoke path, which writes no crumbs.)*

- **The mechanism.** Binding a VM property to a widget LAYOUT attribute (`SuggestedWidth="@BarWidth"`, margins, offsets) is not one-way: Gauntlet's binding system also pushes the widget-side value back into the VM property via reflection invoke. Widget layout attributes are `float`. If the VM property is `int`, the write-back invoke throws `Single → Int32` **inside Gauntlet's invoke wrapper** — no managed crash report (it is swallowed and logged only to `rgl_log`), the screen is left half-broken, and the next interaction (a button `ExecuteAct`) cascades into a native crash.
- **The rule.** Any `[DataSourceProperty]` bound to a widget layout attribute must be **`float`**, even when the value is conceptually integral. Text/bool/list bindings are unaffected.
- **Diagnosing.** `rgl_log_<pid>.txt` in the crash folder prints the `Exception occurred inside invoke: set_<Property>` lines with the target VM type — grep for `inside invoke` before reaching for WinDbg. The tyt heartbeat is NOT useful here: UI-thread invokes write no crumbs, so the last crumb points at whatever campaign tick ran last.
- **History.** `VillageWorksVM.BarWidth` (the crash) and `CouncilEntryVM.IndentWidth` (same latent bug) are fixed to `float`.

## 20. Materialising arbitrary heroes in a keep hall (the ceremony recipe)

*(Built for the round-5 in-hall coronation, verified against the v1.3.11 decompile of
`HeroAgentSpawnCampaignBehavior` and `PlayerTownVisitCampaignBehavior`.)*

- **Placing a hero bodily in the lord's hall without moving his map party:** build a
  `LocationCharacter` exactly the way the native keep-notable path does and add it to the
  location — a stand-in agent, not a teleport. The verified recipe (all from
  `TaleWorlds.CampaignSystem`, no SandBox.dll reference needed — `SandBoxManager` lives in
  CampaignSystem):
  `new AgentData(new SimpleAgentOrigin(h.CharacterObject)).Monster(FaceGen.GetMonsterWithSuffix(race, "_settlement")).NoHorses(true)` + faction clothing colours, then
  `new LocationCharacter(agentData, SandBoxManager.Instance.AgentBehaviorManager.AddFixedCharacterBehaviors, "sp_notable", true, CharacterRelations.Neutral, ActionSetCode.GenerateActionSetNameWithSuffix(agentData.AgentMonster, h.IsFemale, "_lord"), useCivilianEquipment: true)` →
  `LocationComplex.Current.GetLocationWithId("lordshall").AddCharacter(lc)`. Skip heroes who
  already have a `LocationCharacter` in the complex (`GetLocationOfCharacter(h) != null`) or you
  get twins. Remove your stand-ins afterwards with `RemoveCharacterIfExists(hero)`.
- **Opening the hall mission from a menu, the native way** (this is what "Go to the lord's hall"
  does): set `GameMenuManager.NextLocation` to the hall and `PreviousLocation` to `"center"`,
  call `PlayerEncounter.LocationEncounter.CreateAndOpenMissionController(hall)`, then null both.
  Works from any menu while inside the settlement (`LocationComplex.Current` non-null).
- **Dialogue works unchanged:** agents spawned this way are real heroes — campaign conversations
  start at token `"start"` when the player talks to them, so a priority-200 `AddDialogLine` with
  a state-gated condition owns the encounter (same trick as the darbar dialogue court, ch.15's
  farmaan rules unaffected).
- **State discipline:** anything scoped to the live mission (who attended, who swore) can stay
  unserialized — the game cannot save inside a mission — but the *summons* that leads to it must
  be serialized, with a deadline fallback so it can never dangle (`CoronationBehavior` pattern).

## 21. English NEVER loads language files — override strings at render time

*(Root-caused in round 8 after the round-7 quest re-theme silently failed: "Neretzes' Folly"
and the vanilla family names survived ~250 language-file overrides. Verified against the
v1.3.11 decompile of TaleWorlds.Localization.)*

- **The mechanism.** `LocalizedTextManager.LoadLanguage` wraps its string deserialization in
  `if (languageId != "English")` — for English, NO language file's `<string>` entries are ever
  read, from any module, vanilla or mod. And `MBTextManager.GetLocalizedText` (the choke point
  every `{=key}text` passes through at render) short-circuits: `if (_activeTextLanguageId ==
  "English") return inline text;` — it never consults the translation dictionary. A
  `<LanguageData id="English">` override file is therefore **dead on arrival**, no matter how
  it is ordered or registered.
- **The three string pipelines** (know which one your target string uses):
  1. **Inline TextObjects** (`new TextObject("{=key}text")` in code; also XML attributes) —
     the vast majority: quests, dialogue, backstories. Overridable ONLY by intercepting
     render (see the fix).
  2. **GameTexts** (`GameTexts.FindText(outerId, variation)` — flat dotted-id strings in
     `module_strings.xml` files, e.g. `str_player_father_name.empire`) — overridable by
     poking `GameTextManager` at runtime: that is what `Patches/LocalizationOverride.cs`
     does via `hindostan_string_map.xml` (inner→outer id map). Note the *variation
     TextObjects* still carry `{=key}` inline text, so the render-time fix covers these too.
  3. **Names/titles already baked into a SAVE** (hero names set at character creation, quest
     journal titles) — stored as the raw `"{=key}text"` value and re-resolved every frame,
     so a render-time override HEALS EXISTING SAVES with no migration.
- **The fix.** `Patches/EnglishTextOverridePatch`: a Harmony **prefix on
  `MBTextManager.GetLocalizedText`** that extracts the `{=key}` and serves the mod's override
  first. It lazily loads every `<string id= text=>` from `ModuleData/Languages/*.xml`
  (excluding `language_data.xml`; ids ≤ 64 chars — the very long snake_case ids are outer
  GameText ids that never appear inside `{=…}`). One dictionary lookup per call; the native
  path already allocates more.
- **The rule.** Any English-facing string override goes in a `ModuleData/Languages/*.xml`
  file and just works — no registration needed beyond the file existing. The
  `language_data.xml` LanguageFile listing is now only ceremony (kept for non-English
  players, for whom the engine DOES read the files).
- **Diagnosing.** `tyt_log` prints `EnglishTextOverride: N inner-key overrides live` at first
  render. If a vanilla string survives: it either isn't `{=key}`-driven (rare), its key is
  missing from the override files, or it is a GameText read *before* first render.

## 22. Banner codes, the palette, and repainting a kingdom at runtime

*(From the round-8 Tipu lion-standard work; ids verified against the v1.3.11 data files.)*

- **Banner code anatomy**: dot-separated fields, 10 per layer —
  `mesh.color1.color2.sizeX.sizeY.posX.posY.drawStroke.mirror.rotation`, layers concatenated
  (layer 1 = background, layer 2+ = icons). Vanilla kingdom examples in SandBox
  `spkingdoms.xml`; the mod's clans in `tyt_spclans.xml` (`banner_key`).
- **Verified ids** (Native `banner_icons.xml`): the palette holds 229 colors — **84 = bright
  yellow** (0xFDE217), **116 = near-black** (0x0B0C11), 131 = the mod's common gold, 15/39/
  121/143/171 = other golds. Icon **160 = the lion** (vanilla Vlandia's device). Background
  meshes are only named `banner_background_test_N` — stripe patterns are NOT identifiable
  from data; iterate visually (the mod ships `hindostan.mysore_banner [code]` exactly for
  this).
- **Repainting a kingdom**: `Kingdom.Banner` has a public setter — `k.Banner = new
  Banner(code)` just works. `Color`, `Color2`, `PrimaryBannerColor`, `SecondaryBannerColor`
  have **private setters** → set via reflection (`GetProperty(...).GetSetMethod(true)`); the
  map nameplates and shields read them. `Clan.Color/.Color2` are public (that is how
  UnifiedEmpireBehavior recolours folded clans). Pattern lives in
  `ScriptedHistoryBehavior.ApplyBanner`.

## 23. Scripted dynastic surgery (moving heroes between clans, changing ruling clans)

- `Hero.Clan` has a **public setter** — assigning it moves a lord between clans mid-campaign
  (used by the `mysore_house` migration folding Tipu's family into Hyder Ali's house).
  Move the CLAN LEADER only after seating a successor: `ChangeClanLeaderAction
  .ApplyWithSelectedNewLeader(clan, hero)` first, then reassign.
- `ChangeRulingClanAction.Apply(kingdom, clan)` swaps a kingdom's ruling clan cleanly
  (Hyder's 1724 coup). The leader-change is picked up by CoronationBehavior's daily snapshot
  like any accession — coronation/accession-day registry updates follow automatically.
- Scripted-history rule: reference **hero ids** (`lord_3_5`), never clan ids, in timeline
  effects — clan composition is itself mutable history (the Mysore restructure moved five
  heroes across clans; any clan-id assumption would have silently broken).

---

**[Home](Home.md)**
