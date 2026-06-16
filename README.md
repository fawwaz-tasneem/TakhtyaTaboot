# The Hindostan Mod

A total-conversion mod for **Mount & Blade II: Bannerlord** (v1.4.6) set in **1719 Mughal India** — the twilight of imperial unity, when the Padshah's authority frays and zamindars, mansabdars, Sikhs, and the Marathas contend for the soul of Hindostan.

> This is a singleplayer overhaul: a new map of the subcontinent, new factions and lords, and a layer of Mughal-era political systems built on top of Bannerlord's campaign.

---

## Features

- **Hindostan setting** — a map of the Indian subcontinent with new kingdoms, clans, settlements, and cultures replacing the vanilla world.
- **Mansabdari career ladder** — rank (*mansab*) gates what you may hold: rise from an unlanded noble through a village, then a castle, then a town, then the throne. You cannot leap the ladder.
- **Feudal hierarchy** — every village has its own lord, vassal to the lord of its castle/town, beneath the sovereign. Appoint and remove fief-holders (at a cost), with a graphical hierarchy tree.
- **The Darbar (council)** — every landed lord keeps a council of vassals, kin, and companions. Petition your liege for a seat and its perks.
- **Royal farmaans** — declarations of war, votes, and summons arrive as sealed, dated imperial decrees naming the parties involved, with kingdom-appropriate honorifics.
- **Meaningful warfare** — war goals, a running war score, dictated peace terms (*nazrana* tribute, province cession, tributary status), and war-weariness; battlefield deeds raise your mansab; sack-or-spare, ransom, and hostage choices; call your banners to muster vassals to your army.
- **Succession & revolt** — the War of Princes succession crisis, provisional rebel kingdoms that must consolidate or be crushed, player-led revolts, and a War of Accession in which you secede with your supporters into a temporary rebel kingdom to challenge the emperor for the throne.

## Requirements

- **Mount & Blade II: Bannerlord** `v1.4.6`
- The four standard library mods (load **before** this mod):
  - [Harmony](https://www.nexusmods.com/mountandblade2bannerlord/mods/2006) `v2.4.2+`
  - [ButterLib](https://www.nexusmods.com/mountandblade2bannerlord/mods/2018) `v2.10.3+`
  - [UIExtenderEx](https://www.nexusmods.com/mountandblade2bannerlord/mods/2102) `v2.13.2+`
  - [Mod Configuration Menu (MCM)](https://www.nexusmods.com/mountandblade2bannerlord/mods/612) `v5.11.3+`
- *(Optional)* [Diplomacy](https://www.nexusmods.com/mountandblade2bannerlord/mods/2018) — supported if present.

## Installation

1. Install the required library mods above.
2. Download the latest `TheHindostanMod-vX.X.X.zip` from the [**Releases**](../../releases) page.
3. Extract it so that the `TheHindostanMod` folder sits inside your Bannerlord `Modules` directory:
   ```
   Mount & Blade II Bannerlord\Modules\TheHindostanMod\SubModule.xml
   ```
4. Launch the game, open the **Mods** tab, and enable the library mods **and** *The Hindostan Mod* (the libraries must be ordered above it).
5. Start a new campaign.

## Building from source

The compiled assembly and large map/scene/asset binaries are **not** kept in this repository — only the source, game-data XML, and GUI prefabs are versioned. The runtime binaries are distributed via Releases.

```sh
"C:/Program Files/dotnet/dotnet.exe" build src/TheHindostanMod/TheHindostanMod/TheHindostanMod.csproj -c Release
```

The build outputs `TheHindostanMod.dll` into `bin/Win64_Shipping_Client/`.

To assemble a distributable, runtime-only zip (source, wiki, and tooling excluded):

```powershell
powershell -ExecutionPolicy Bypass -File .\package-release.ps1
```

## Repository layout

| Path | Contents |
|------|----------|
| `src/` | C# source (campaign behaviours, Harmony patches, Gauntlet UI) |
| `ModuleData/` | Game-data XML — cultures, clans, kingdoms, settlements, heroes, concepts, localization |
| `GUI/` | Custom Gauntlet prefabs and sprite data |
| `SubModule.xml` | Module manifest and dependencies |
| `wiki/` | Developer documentation and modding notes |
| `package-release.ps1` | Builds a clean runtime-only release zip |

> Map, scene, and compiled-asset binaries (`SceneObj/`, `SceneEditData/`, `Assets/`, `RuntimeDataCache/`, `AssetSources/`) are git-ignored — they are not human-editable source and are shipped only in releases.

## License

All rights reserved unless stated otherwise. *Mount & Blade II: Bannerlord* is a trademark of TaleWorlds Entertainment; this is an unofficial fan modification.
