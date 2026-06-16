# The Hindostan Mod — C# Modding Wiki

A complete guide to writing C# code for Bannerlord, with all examples drawn from the Hindostan Mod project.

---

## Table of Contents

| Chapter | Topic |
|---------|-------|
| [01 — C# Crash Course](01-CSharp-Crash-Course.md) | Properties, LINQ, lambdas, null safety, delegates — everything you need from C# that you don't already know |
| [02 — Bannerlord Architecture](02-Bannerlord-Architecture.md) | How the engine is structured, the object hierarchy, load order, and the two main extension points |
| [03 — Project Setup](03-Project-Setup.md) | Visual Studio, .NET 4.8.1, DLL references, Harmony, SubModule.xml wiring, auto-deploy |
| [04 — Campaign Behaviors](04-Campaign-Behaviors.md) | `CampaignBehaviorBase` deep dive — events, daily ticks, state management, full Hindostan examples |
| [05 — Game Model Overrides](05-Game-Model-Overrides.md) | Overriding speed, damage, income calculations with `ExplainedNumber` — monsoon and culture bonuses |
| [06 — Harmony Patching](06-Harmony-Patching.md) | Prefix, postfix, how to find patch targets, `AccessTools`, debugging failed patches |
| [07 — Game Menus and Dialogues](07-Game-Menus-and-Dialogues.md) | Settlement menus, modal inquiry dialogs, HUD notifications — elephant recruitment example |
| [08 — Working With Game Objects](08-Game-Objects.md) | Heroes, clans, kingdoms, settlements, mobile parties — all the properties and actions you'll use |
| [09 — Save and Load](09-Save-Load.md) | `SyncData` pattern, what to persist, null-reference traps, save key naming |
| [10 — Debugging](10-Debugging.md) | Log file, in-game messages, common exceptions, real-time log streaming, iterative workflow |
| [11 — XML and C# Interaction](11-XML-and-CSharp.md) | How XML loads, how C# reads it, StringId contract, what XML cannot do |
| [12 — Worked Example: Maratha Chauth](12-Worked-Example-Chauth.md) | Complete feature from scratch — yearly tribute demand with player popup, fully annotated |
| [Quick Reference](Quick-Reference.md) | One-page cheat sheet — lookups, actions, time, display, culture IDs |
| [Modding Findings Reference](Modding-Findings-Reference.md) | **Verified empirical facts** — the two string stores (English vs translation), inner-vs-outer id trap, name-generation pools, Harmony/patch gotchas, diagnostic methodology. Read before touching localization or names. |
| [13 — Council System](13-Council-System.md) | CK3-style royal council — positions, competence, effects, AI appointments, decision voting integration |
| [14 — Feudal Hierarchy](14-Feudal-Hierarchy.md) | Four-tier liege-vassal chain — Emperor → Town Lord → Castle Lord → Village Headman; first approach using `settlement.Governor` (superseded by `FiefRecord.Holder` in Chapter 15) |
| [15 — Lord Progression and Mansabdari](15-Lord-Progression-and-Mansabdari.md) | Player career ladder (village → castle → town → council), Valour metric, call to arms, village development menu, Mughal mansabdari system |
| [16 — Civil War and Imprisonment](16-Civil-War-and-Imprisonment.md) | Corrected sawar (total clan troops, max 600), demotion, leadership challenges, civil war mechanics, noble imprisonment, troop desertion, Diplomacy mod integration |
| [17 — Quality of Life and Depth Systems](17-Quality-of-Life-and-Depth-Systems.md) | Monsoon calendar, Nazrana gifts, trade route network, famine, epidemic, religious tolerance, festivals, Akhbarat intelligence, cultural patronage, caravanserai |
| [18 — News Reporter and Personal Biographer](18-News-Reporter-and-Biographer.md) | AI-generated news dispatches (waqai-nawis) and player biography (tazkira); event capture schema, C# async LLM client, Ollama setup, Qwen 2.5 1.5B fine-tuning with Unsloth, template fallback |
| [19 — Imperial Depth: Political Systems](19-Imperial-Depth-Political-Systems.md) | Mughal succession (War of Princes), Imperial Authority decay, Three Estates, internal court factions, legitimacy, revolt cascade |
| [20 — Character Depth: Traits, Intrigue, Pilgrimage, Great Works](20-Character-Depth-and-Intrigue.md) | Character traits (earned from actions), Ulema fatwa network, assassination/blackmail/spy schemes, pilgrimage (Hajj/Kumbh Mela/Amritsar), Great Works (Taj Mahal etc.), cultural innovations |
| [21 — Main Quest, Reunification, War Score, Peace Negotiation, Suzerainty](21-Main-Quest-Reunification-and-Peace.md) | Two-path win condition (Restoration vs Domination), war score accumulation, peace negotiation menu with demand tiers, suzerainty vassal system, Shahanshah coronation |

---

## Project Facts

| Item | Value |
|------|-------|
| Mod ID | `TheHindostanMod` |
| Desktop source | `C:\Users\tasne\Desktop\TakhtyaTaboot\` |
| Installed copy | `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\The Hindostan Mod\` |
| Game version | v1.4.6 |
| .NET target | Framework 4.8.1 |
| Harmony version | 2.2.2 |

**CRITICAL:** The Bannerlord install at `D:\SteamLibrary\...\Mount & Blade II Bannerlord` is **READ ONLY**. Never modify any file there. All authoring happens in `C:\Users\tasne\Desktop\TakhtyaTaboot`.

---

## Culture → Kingdom ID Map

| Display Name | Engine ID | Short Name |
|--------------|-----------|------------|
| Mughal Empire | `empire` | Mughliya Sultanat |
| Bengal | `empire_w` | Bangaal |
| Hyderabad | `empire_s` | Hyderabad |
| Afghan/Durrani | `sturgia` | Afghans |
| Mysore | `aserai` | Mysore |
| Rajput | `vlandia` | Rajputs |
| Maratha | `battania` | Marathas |
| Sikh | `khuzait` | Sikhs |
