# Takht ya Taboot

*(Takht ya Taboot — "the Throne or the Coffin")*

A work-in-progress overhaul mod for **Mount & Blade II: Bannerlord** (`v1.3.11`) that reimagines the campaign as **Mughal Hindostan, 1707** — the campaign opens at the death of Aurangzeb Alamgir, and the age unfolds from there: the rapid-fire succession of emperors, the rise of the Marathas and the misls, the coup in Mysore, Nadir Shah at the gates. Zamindars, mansabdars, Sikhs, Rajputs, Afghans, and the Maratha Confederacy contend for the soul of Hindostan.

> **Status: work in progress.** This is *intended* to become a full total conversion, but it is **not there yet**. Today the mod is built from **a fully re-themed vanilla world plus a deep stack of C# gameplay systems** — there is **no custom map and no custom 3D art**. It plays on Bannerlord's existing map with everything renamed, re-written, and re-wired to Mughal India. Treat it as a systems-and-setting overhaul, not a finished new world.

---

## What this mod is right now

1. **A complete re-theming** — kingdoms, clans, settlements, cultures, lords, the main quest, character creation, and every stray line of vanilla text are rewritten to Mughal-India equivalents (a runtime localization layer guarantees the English text actually changes — including in existing saves).
2. **A historical world** — every house head in all eight kingdoms has a researched, cross-referencing encyclopedia biography; friendships, rivalries, and personality traits are seeded from real history; scripted events fire on the real timeline.
3. **Custom C# campaign systems** — a deep layer of Mughal-era political, feudal, military, and court mechanics via campaign behaviours, Harmony patches, and custom Gauntlet UI. **400+ unit tests** cover the mod's decision logic.

### What is *not* done yet

- ❌ **No custom world map** — plays on the stock map, renamed (a Hindostan map exists in working files but is not wired in).
- ❌ **No custom 3D assets** — no new models, textures, or scenes; visuals are vanilla.
- ❌ Not content-complete — this is the systems/setting foundation.

---

## Features

### The historical world
- **1707 start, real timeline** — the campaign opens with Aurangzeb dying in the Deccan; the scripted **emperor cascade** runs the 1707–1719 successions (Bahadur Shah → Jahandar → Farrukhsiyar → the Sayyid puppets → Muhammad Shah), and **scripted history** fires the age's real events by year: Banda Singh's rising (1709), the Deccan treaty (1714), **Hyder Ali's coup on the Wodeyars** (1724 — Mysore's throne passes to his house; when Tipu succeeds, the kingdom raises the yellow-and-black lion standard), Bajirao's dash on Delhi (1737), **Nadir Shah's sack of Delhi** (1739), the Durrani proclamation (1747).
- **The historical cast** — 66 cross-referencing encyclopedia biographies covering every clan leader in all eight kingdoms (the Sayyid kingmakers vs. the Turani party, Rohilla vs. Bangash, the Sidi of Janjira vs. the Angre admiralty, the misl feuds…), backed by a seeded web of ~60 starting relations and per-lord personality traits.
- **The main quest, re-themed onto real history** — *Neretzes' Folly* is now **Alamgir's Folly**: the veterans' tales retell the Deccan war and the storming of Wagingera (1705); the banner you assemble is the **Alam of Timur**; your mentors are Zinat-un-Nissa Begum (restore the Raj) and Khando Ballal (break it).
- **Mughal texture everywhere** — farmaans dated in AD *and* Hijri with the regnal year; the **rupee** as currency; Urdu/Persian court vocabulary; culture-keyed names for the player's own family; road-news anecdotes ("What word do the roads carry?") retelling real events; faith layer over heroes and factions; monsoon seasons that swing harvests, mire armies in the rains, and can bring famine.

### Feudal & career systems
- **Mansabdari career ladder** — your **zat/sawar** rank gates what you may hold: village → castle → town → throne, no skipping rungs; party size scales with mansab; battlefield valour raises it.
- **Feudal hierarchy** — every village has its own zamindar, vassal to the lord of its bound seat, beneath the sovereign — one liege chain driving tribute and the call-to-arms, drawn on a graphical hierarchy tree screen. Kingdoms have capitals; the clan screen shows zamindari holdings.
- **Tenure law & rotation** — fiefs held at the crown's pleasure or hereditarily (a per-kingdom court edict); periodic rotation with a defiance ladder, down to village jagirs, leaving **Favor/Grudge** records with the winners and losers.
- **Fief petitions** — no instant claims: stake gold and influence on a petition and let the court weigh it weekly against your standing.
- **Village development** — works, bandit-threat dynamics, watch, a Gauntlet works ledger — plus **bonded labour** (settle captives as a begar gang: revenue up, unrest up).

### The court
- **The Darbar** — every landed lord keeps a council of vassals, kin, and companions (dedicated screen); petition your liege for a seat and its perks.
- **The petition court** — sit in judgment as sovereign: plaintiff, defendant, and your advisor each speak **in dialogue**, then you rule — opinion, influence, and legitimacy ride on the verdict.
- **Coronation ceremonies in the hall** — acceding summons the darbar of accession: travel to your keep, take the throne, and the attending house heads come to you **one by one, physically present in the lord's hall**, each swearing fealty in his own culture's voice (7 variations × 8 cultures); absentees are noted and a late oath can be demanded. Vassals are summoned to swear to new sovereigns in person.
- **Court honours** — bestow the **khil'at** (a robe of royal silk, or the char-aina breastplate for feats of arms); grant permanent **titles** (Bahadur, Jang, ud-Daula, ul-Mulk) that the court appends to a lord's name; show yourself monthly at the **jharokha darshan**.
- **Personal opinions** — a ledger of oaths sworn, ceremonies missed, favours and grudges that lords actually remember, surfaced in the encyclopedia and a grievance dialogue.
- **Royal farmaans** — wars, votes, summons, and news arrive as sealed, dated imperial decrees in a custom popup, with a director that dedupes court spam into a weekly circular.
- **Akhbaar scouts & the qasid** — pay a harkara to trail any lord (his report updates the encyclopedia's "last seen"), or send a **qasid** messenger: when he arrives, you speak with the lord **as if you stood before him**.
- **A dialogue pack** — fealty, nazrana presented by hand, grievances, princes addressed by style, council invitations, "ride with me" orders to your parties, the news of the roads.

### Power, war & succession
- **Imperial Authority & Legitimacy** — empire-wide meters shown in the map bar, shifting with events and shaping everything from tenure to rebellion.
- **Meaningful warfare** — war aims, a running war score, dictated peace terms (nazrana tribute, cession, tributary status), sack-or-spare, ransom and hostages, muster of the banners, and **siege parley** with the qiladar.
- **War exhaustion** — every side of every war tires with casualties, lost fiefs, and time; a spent AI realm sues for peace; a spent player-ruled realm bleeds authority until its sovereign makes peace himself.
- **Succession laws (per kingdom)** — primogeniture, election of princes, magnate election, or an appointed Wali Ahd; rewritable as a legitimacy- and influence-gated edict; the law shapes who may claim and how contests resolve.
- **Succession crisis (War of Princes)** — contested successions brew and erupt; back a claimant, buy a rival out, or fight the civil war. Wars for a throne are **binary** — no white peace, no half-outcomes.
- **War of Accession & revolt cascade** — secede into a provisional rebel kingdom and challenge for the crown; rebel kingdoms must consolidate or be crushed; a winning breakaway **graduates** into a true kingdom with a founding darbar.
- **Secession & abdication conspiracies** — personally disaffected houses form cabals and serve ultimatums: abdicate in favour of the lawful heir, or let them depart — or fight them. A seated spymaster warns you early.
- **The unified empire** — the Mughal realm opens folded under one banner and fractures on the timeline, with a clan safety net so no house is ever left masterless.

### Configuration & developer tools
- **MCM settings** — tunable factors for promotion, succession, monsoon, labour, and more.
- **Console commands** for nearly every system (`hindostan.*` — e.g. `history_status`, `coronation_test`, `exhaustion_status`, `disaffection_status`, `qasid_status`, `mysore_banner`, `cast_reapply`).
- **401+ unit tests** over the pure decision logic (`src/TheHindostanMod.Tests`).

## Requirements

- **Mount & Blade II: Bannerlord** `v1.3.11`
- The four standard library mods (must load **before** this mod):
  - [Harmony](https://www.nexusmods.com/mountandblade2bannerlord/mods/2006) `v2.2.2+`
  - [ButterLib](https://www.nexusmods.com/mountandblade2bannerlord/mods/2018) `v2.10.2+`
  - [UIExtenderEx](https://www.nexusmods.com/mountandblade2bannerlord/mods/2102) `v2.13.1+`
  - [Mod Configuration Menu (MCM)](https://www.nexusmods.com/mountandblade2bannerlord/mods/612) `v5.11.4+`
- **Diplomacy is NOT recommended.** The mod integrates its own native equivalents (war exhaustion, messengers, secession/abdication civil wars) designed around its throne-war rules; running Diplomacy alongside will double up mechanics.

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

Only source, game-data XML, and GUI prefabs are kept in this repository. The compiled assembly is produced by building; large map/scene/asset binaries are not version-controlled. The game-install path comes from a gitignored `BannerlordDir.local.props` (or `-p:BannerlordDir=...`).

```sh
dotnet build src/TheHindostanMod/TheHindostanMod/TheHindostanMod.csproj -c Release
dotnet test src/TheHindostanMod.Tests/TheHindostanMod.Tests.csproj
```

This outputs `TakhtyaTaboot.dll` into `bin/Win64_Shipping_Client/`.

To assemble a clean, runtime-only release zip (source, wiki, and tooling excluded):

```powershell
powershell -ExecutionPolicy Bypass -File .\package-release.ps1
```

## Repository layout

| Path | Contents |
|------|----------|
| `src/` | C# source — campaign behaviours, Harmony patches, custom Gauntlet UI, unit tests |
| `ModuleData/` | Game-data XML — cultures, clans, kingdoms, settlements, heroes & biographies, concepts, localization overrides |
| `GUI/` | Custom Gauntlet prefabs and sprite data |
| `SubModule.xml` | Module manifest and dependencies |
| `wiki/` | Developer documentation — ch.28 is the as-built systems reference; Modding-Findings holds the verified engine lore |
| `PLAYTEST.md` / `ROADMAP.md` | The live test checklist and what's next |
| `package-release.ps1` | Builds a clean runtime-only release zip |

> Map, scene, and compiled-asset binaries (`SceneObj/`, `SceneEditData/`, `Assets/`, `RuntimeDataCache/`, `AssetSources/`) are git-ignored — they are not human-editable source, are not currently wired into the game, and are not published to GitHub.

## License

All rights reserved unless stated otherwise. *Mount & Blade II: Bannerlord* is a trademark of TaleWorlds Entertainment; this is an unofficial, in-development fan modification.
