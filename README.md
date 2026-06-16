# Takht ya Taboot

*(Takht ya Taboot — "the Throne or the Coffin")*

A work-in-progress overhaul mod for **Mount & Blade II: Bannerlord** (`v1.4.6`) that reimagines the campaign as **1719 Mughal India** — the twilight of imperial unity, when the Padshah's authority frays and zamindars, mansabdars, Sikhs, and the Marathas contend for the soul of Hindostan.

> **Status: early / work in progress.** This is *intended* to become a full total conversion, but it is **not there yet**. Today the mod is built entirely from **renaming the vanilla world and a stack of C# gameplay systems** — there is **no custom map and no custom 3D art**. It plays on Bannerlord's existing Calradia map with everything renamed to a Hindostan theme. Treat it as a systems-and-flavour overhaul, not a finished new world.

---

## What this mod is right now

The mod currently delivers its setting through two means:

1. **Renaming & re-theming** — vanilla kingdoms, clans, settlements, cultures, and lords are renamed to Mughal-India equivalents via game-data XML plus runtime text/name patches. No vanilla map geometry, scenes, or models are replaced.
2. **Custom C# campaign systems** — a layer of Mughal-era political, feudal, and military mechanics added on top of the campaign through Harmony patches, campaign behaviours, and custom Gauntlet UI.

### What is *not* done yet

- ❌ **No custom world map** — plays on the stock Calradia map (a Hindostan map exists in the repo's working files but is **not wired into the game**).
- ❌ **No custom 3D assets** — no new models, textures, scenes, troops, or items meshes; visuals are vanilla.
- ❌ Not a content-complete total conversion — this is the systems/flavour foundation.

---

## Features implemented today

All of the following are working C# systems (or data/text changes), independent of any custom map or art:

### Setting & flavour
- **Re-themed world** — Mughal-India names for factions, clans, settlements, and cultures, applied over the vanilla map.
- **Faith** — a religion/faith layer over heroes and factions.
- **Faction relations** — seeded hostilities so the world behaves historically: Sikhs vs. the Mughal realms (bad), Marathas vs. the Mughals (very bad), and concord *within* the Mughal kingdoms so they don't constantly war each other.
- **Encyclopedia & concepts** — custom encyclopedia concept entries and injected hero/empire info explaining the mod's systems.
- **Empire stats in the bottom bar** — Authority, Legitimacy, Mansab, and Unrest shown inline in the vanilla map info bar.

### Feudal & career systems
- **Mansabdari career ladder** — your rank (*mansab*) gates what you may hold: rise from an unlanded noble through a **village → castle → town → throne**, with no skipping rungs. Party size scales with mansab.
- **Feudal hierarchy** — every village has its own lord, vassal to the lord of its castle/town, beneath the sovereign; fief-holders can be appointed and removed (at a cost), viewed in a graphical hierarchy tree screen.
- **The Darbar (council)** — every landed lord keeps a council of vassals, kin, and companions, with a dedicated council screen; petition your liege for a seat and its perks.
- **Village development** — villages develop over time (works, bandit-threat dynamics).

### Power, war & succession
- **Imperial Authority & Legitimacy** — empire-wide meters that shift with events and shape the campaign.
- **Royal farmaans** — war declarations, votes, and summons arrive as sealed, **dated imperial decrees** that name the parties and use kingdom-appropriate honorifics, shown in a custom decree popup.
- **Meaningful warfare** — war goals, a running war score, dictated peace terms (*nazrana* tribute, province cession, tributary status), war-weariness, battlefield deeds that raise your mansab, sack-or-spare choices, ransom and hostages, and a "call the banners" muster that brings vassals with their troops into your army.
- **Succession crisis (War of Princes)** — contested successions that brew, turn active, and can erupt into civil war, with a kingmaker mechanic.
- **Revolt cascade** — provisional rebel kingdoms that must consolidate or be crushed; rebelling lords secede with their own house; emperor responses delivered as farmaans.
- **War of Accession** — secede with your supporters into a temporary rebel kingdom and challenge the emperor for the throne; win to take the crown, lose and be stripped of rank.

### Developer tools
- Console cheat commands for the feudal, revolt, and succession systems (see `*Cheats.cs` in the source).

## Requirements

- **Mount & Blade II: Bannerlord** `v1.4.6`
- The four standard library mods (must load **before** this mod):
  - [Harmony](https://www.nexusmods.com/mountandblade2bannerlord/mods/2006) `v2.4.2+`
  - [ButterLib](https://www.nexusmods.com/mountandblade2bannerlord/mods/2018) `v2.10.3+`
  - [UIExtenderEx](https://www.nexusmods.com/mountandblade2bannerlord/mods/2102) `v2.13.2+`
  - [Mod Configuration Menu (MCM)](https://www.nexusmods.com/mountandblade2bannerlord/mods/612) `v5.11.3+`
- *(Optional)* [Diplomacy](https://www.nexusmods.com/mountandblade2bannerlord/mods/2018) — supported if present.

## Installation

1. Install the required library mods above.
2. Download the latest `TakhtyaTaboot-vX.X.X.zip` from the [**Releases**](../../releases) page.
3. Extract it so the `TakhtyaTaboot` folder sits inside your Bannerlord `Modules` directory:
   ```
   Mount & Blade II Bannerlord\Modules\TakhtyaTaboot\SubModule.xml
   ```
4. Launch the game, open the **Mods** tab, enable the library mods **and** *Takht ya Taboot* (libraries ordered above it).
5. Start a new campaign.

## Building from source

Only source, game-data XML, and GUI prefabs are kept in this repository. The compiled assembly is produced by building, and the large map/scene/asset binaries are not version-controlled.

```sh
"C:/Program Files/dotnet/dotnet.exe" build src/TheHindostanMod/TheHindostanMod/TheHindostanMod.csproj -c Release
```

This outputs `TakhtyaTaboot.dll` into `bin/Win64_Shipping_Client/`.

To assemble a clean, runtime-only release zip (source, wiki, and tooling excluded):

```powershell
powershell -ExecutionPolicy Bypass -File .\package-release.ps1
```

## Repository layout

| Path | Contents |
|------|----------|
| `src/` | C# source — campaign behaviours, Harmony patches, custom Gauntlet UI |
| `ModuleData/` | Game-data XML — cultures, clans, kingdoms, settlements, heroes, concepts, localization |
| `GUI/` | Custom Gauntlet prefabs and sprite data |
| `SubModule.xml` | Module manifest and dependencies |
| `wiki/` | Developer documentation and modding notes |
| `package-release.ps1` | Builds a clean runtime-only release zip |

> Map, scene, and compiled-asset binaries (`SceneObj/`, `SceneEditData/`, `Assets/`, `RuntimeDataCache/`, `AssetSources/`) are git-ignored — they are not human-editable source, are not currently wired into the game, and are not published to GitHub.

## License

All rights reserved unless stated otherwise. *Mount & Blade II: Bannerlord* is a trademark of TaleWorlds Entertainment; this is an unofficial, in-development fan modification.
