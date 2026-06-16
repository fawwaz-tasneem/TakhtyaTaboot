# Chapter 11 — XML and C# Interaction

> How XML loads, how C# reads it, the StringId contract, and what XML cannot do.

**[← Chapter 10](10-Debugging.md)** | **[Home](Home.md)** | **[Next: Worked Example →](12-Worked-Example-Chauth.md)**

---

## Contents

- [XML loads first, C# reads it](#xml-loads-first-c-reads-it)
- [Accessing XML-defined objects from C#](#accessing-xml-defined-objects-from-c)
- [The StringId contract](#the-stringid-contract)
- [What XML cannot do (C# required)](#what-xml-cannot-do-c-required)
- [This mod's XML files and what they control](#this-mods-xml-files-and-what-they-control)

---

## XML Loads First, C# Reads It

When a campaign starts, Bannerlord's loading pipeline runs in this order:

```
1. All XML files from all modules are loaded (in dependency order)
2. Objects are merged into MBObjectManager (the game's database)
3. All CampaignBehaviorBase.RegisterEvents() calls run
4. OnNewGameCreatedEvent fires
```

Your C# code runs in step 4, **after all XML is in memory**. Every kingdom, hero, settlement, and troop type you defined in XML is available via `MBObjectManager` by the time your behavior's `OnNewGameCreated` runs.

---

## Accessing XML-Defined Objects from C#

```csharp
// The id= attribute from your XML is the StringId:

// From spkingdoms.xml: <Kingdom id="empire" ...>
Kingdom mughals = MBObjectManager.Instance.GetObject<Kingdom>("empire");

// From spnpccharacters.xml: <NPCCharacter id="lord_1_1" ...>
Hero muhammadShah = MBObjectManager.Instance.GetObject<Hero>("lord_1_1");

// From settlements.xml: <Settlement id="town_EN2" ...>  (Shahjahanabad/Delhi)
Settlement delhi = MBObjectManager.Instance.GetObject<Settlement>("town_EN2");

// From tyt_spcultures.xml: <Culture id="empire" ...>
CultureObject mughalCulture = MBObjectManager.Instance.GetObject<CultureObject>("empire");

// From your custom NPCCharacters entry: <NPCCharacter id="war_elephant" ...>
CharacterObject elephant = MBObjectManager.Instance.GetObject<CharacterObject>("war_elephant");
```

All of these can return `null` if the object was not found. Always null-check.

---

## The StringId Contract

The `StringId` (the `id=` in XML) is the **permanent, save-game-safe identifier**. 

- **Never change a StringId after release.** Save files reference objects by StringId. Changing one breaks every existing save.
- The **display name** (`name=`, `short_name=`) can change freely in any version.
- The `{=!}` prefix on names is display-only — it never affects the StringId.

```xml
<!-- StringId is "empire" — permanent, never changes -->
<Kingdom id="empire"
    name="{=!}Gurkani Alamgir"        <!-- can change freely -->
    short_name="{=!}Mughliya Sultanat" <!-- can change freely -->
    ...
```

---

## What XML Cannot Do (C# Required)

| Task | XML | C# |
|------|-----|----|
| Override character creation culture display name | ✗ | ✓ Harmony postfix on `RefreshCultureDetails` |
| React to events (hero killed, siege won) | ✗ | ✓ `CampaignEvents` |
| Change game math (speed, damage, income) | ✗ | ✓ `GameModel` overrides |
| Spawn parties at runtime | ✗ | ✓ `MobileParty.CreateParty` |
| Show dialogs and player choices | ✗ | ✓ `AddGameMenuOption` + `InquiryData` |
| Persist custom state in save files | ✗ | ✓ `SyncData` |
| Conditional logic (if monsoon, if at war) | ✗ | ✓ Behavior code |
| Modify culture-specific stat bonuses | ✗ | ✓ `GameModel` overrides |
| Fire timed historical events | ✗ | ✓ `YearlyTickEvent` |

**Rule of thumb:** XML defines the *data* (names, starting values, relationships). C# defines *behavior* (what changes over time, what happens in response to events, what calculations produce what numbers).

---

## This Mod's XML Files and What They Control

| File | Controls |
|------|----------|
| [tyt_spcultures.xml](../ModuleData/tyt_spcultures.xml) | Culture definitions — IDs, names, troop trees, culture flags |
| [spkingdoms.xml](../ModuleData/spkingdoms.xml) | Kingdom definitions — names, short_names, starting wars, banner colors |
| [tyt_spclans.xml](../ModuleData/tyt_spclans.xml) | Clan definitions — allegiances, home settlements, banner keys |
| [spnpccharacters.xml](../ModuleData/spnpccharacters.xml) | All lords and notables — their names, skills, equipment |
| [settlements.xml](../ModuleData/settlements.xml) | Settlement names and initial owners |
| Languages/std_module_strings_xml.xml | Overrides vanilla string IDs → Hindostan names (Mughal, Mysore, etc.) |

### How the language file overrides work

The mod's `Languages/std_module_strings_xml.xml` overrides specific vanilla string IDs:

```xml
<string id="empirefaction"  text="Mughal" />   <!-- displayed when showing the empire faction -->
<string id="aseraifaction"  text="Mysore" />
<string id="PjO7oY16"       text="Afghan" />   <!-- vanilla Sturgia faction name string -->
<string id="FjwRsf1C"       text="Rajput" />   <!-- vanilla Vlandia faction name string -->
<string id="0B27RrYJ"       text="Maratha" />  <!-- vanilla Battania faction name string -->
<string id="sZLd6VHi"       text="Sikh" />     <!-- vanilla Khuzait faction name string -->
```

The engine looks up these IDs from various UI contexts. Our file intercepts those lookups and returns Hindostan names instead of Calradian ones.

**Character creation specifically** reads `kingdom.short_name` for non-empire cultures. Our `spkingdoms.xml` sets these with `{=!}` to bypass the lookup system entirely:

```xml
<Kingdom id="vlandia" short_name="{=!}Rajputs" ...>
<Kingdom id="battania" short_name="{=!}Marathas" ...>
<Kingdom id="khuzait" short_name="{=!}Sikhs" ...>
```

The `{=!}` prefix means "show this literal text, skip all string ID lookup". This is the most reliable way to set text that must always show correctly regardless of language file state.

---

**[← Chapter 10](10-Debugging.md)** | **[Home](Home.md)** | **[Next: Worked Example →](12-Worked-Example-Chauth.md)**
