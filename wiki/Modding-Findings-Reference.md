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

**[Home](Home.md)**
