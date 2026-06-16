# Takht ya Taboot — v1.0

*Takht ya Taboot ("the Throne or the Coffin") — a 1719 Mughal-India overhaul for Mount & Blade II: Bannerlord.*

> **Early / work-in-progress release.** This is the systems-and-flavour foundation of an intended total conversion. It currently re-themes the **vanilla Calradia map** through renaming plus a stack of custom C# campaign systems. There is **no custom world map and no custom 3D art yet.**

## Compatibility

| | Version |
|---|---|
| **Mount & Blade II: Bannerlord** | **v1.4.6** (game build `v1.4.6.115439`) |
| Harmony | `v2.4.2.225` |
| ButterLib | `v2.10.3` |
| UIExtenderEx | `v2.13.2` |
| Mod Configuration Menu (MCM) | `v5.11.3` |
| Diplomacy *(optional)* | supported if installed |

The four library mods are **required** and must load **before** Takht ya Taboot.

## What's in this release

- **Re-themed world** — Mughal-India names for factions, clans, settlements, and cultures over the vanilla map; historical faction hostilities (Sikhs vs. Mughals, Marathas vs. Mughals, peace within the Mughal realms).
- **Mansabdari career ladder** — rank gates your holdings: unlanded → village → castle → town → throne, with party size scaling to mansab.
- **Feudal hierarchy** — every village has its own lord beneath its castle/town lord beneath the sovereign; appoint/remove fief-holders at a cost; graphical hierarchy-tree screen.
- **The Darbar (council)** — landed lords keep a council of vassals, kin, and companions; petition your liege for a seat.
- **Imperial Authority & Legitimacy** meters shown inline in the map info bar.
- **Royal farmaans** — wars, votes, and summons delivered as sealed, dated decrees naming the parties with kingdom-appropriate honorifics.
- **Meaningful warfare** — war goals, war score, dictated peace terms (nazrana tribute / province cession / tributary status), war-weariness, battlefield deeds raising mansab, sack-or-spare, ransom & hostages, and a "call the banners" muster.
- **Succession crisis, revolt cascade, and War of Accession** — contested successions, seceding rebel kingdoms, and a player path to seize the throne.

## Installation

1. Install the required library mods listed above.
2. Download `TakhtyaTaboot-v1.0.zip` below.
3. Extract so that `...\Mount & Blade II Bannerlord\Modules\TakhtyaTaboot\SubModule.xml` exists.
4. In the launcher's **Mods** tab, enable the library mods **and** *Takht ya Taboot* (libraries above it).
5. Start a **new** campaign.

## Known limitations

- No custom map (plays on Calradia) and no custom 3D models/textures/scenes yet.
- Several systems are newly built and not exhaustively playtested; expect rough edges.
- Existing saves from pre-rename builds are not compatible.
