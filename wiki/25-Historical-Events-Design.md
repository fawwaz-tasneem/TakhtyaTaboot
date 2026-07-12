# Scripted Historical Events — Design (FIRST WAVE IMPLEMENTED, round 8)

> **STATUS UPDATE (2026-07-12):** the first wave is BUILT — `ScriptedHistoryBehavior` +
> `Util/ScriptedHistory` (pure timeline table, tested) fire eight events by AD year through
> `HistoricalCalendar` (game 1084 = AD 1707), each an akhbar farmaan plus an engine effect,
> with stale-load guards for old saves: the continuing Deccan war (1707), Banda's rising
> (1709), the Deccan treaty (1714), **Hyder Ali's coup on the Wodeyars** (1724,
> `ChangeRulingClanAction`), Bajirao's dash on Delhi (1737), **Nadir Shah's sack** (1739),
> the Durrani proclamation (1747), plus the Mysore house restructure and the Tipu-accession
> lion-standard watcher. Console: `history_status`, `history_fire`, `mysore_banner`.
> As-built reference: wiki ch.28 system map; engine recipes in Modding-Findings ch.22–23.
> The emperor cascade (1707–1719) shipped earlier as `ImperialSuccessionEventBehavior`.
> The sections below remain the design notes for the UNBUILT remainder.

A timeline of real 18th-century events the player witnesses: the death of Aurangzeb, the
fragmentation of the empire, the rapid-fire succession of emperors, the rise of Mysore, and
Nadir Shah's invasion.

## 0. The crucial finding — most of the machinery already exists

The events are NOT built from scratch. The mod already has the systems they need; a scripted
event is mostly a **dated trigger** that calls into one of these plus a `RoyalFarmaan`:

| Existing behavior | What it already does | Which events ride on it |
|---|---|---|
| `RevoltCascadeBehavior` | secedes a clan into a **real provisional kingdom** (vanilla + Diplomacy aware) | provinces declaring independence; Sikh/Jat uprisings |
| `SuccessionBehavior` | fires on a ruler dying weak/heirless → claimants + civil war | the rapid emperor turnover (1707–1719) |
| `AccessionWarBehavior` | a challenger usurps the throne via rebel kingdom | the "kingmaker" depositions |
| `ImperialAuthorityBehavior` | per-kingdom authority 0–100; low → provinces drift independent | sets the *pressure* that makes secessions plausible |
| `ImperialCourtBehavior` | capitals, ruler, council | crownings / ruler changes |
| `RoyalFarmaan` (UI) | queued decree popups (now tick-safe) | every announcement |

So the new work is a **timeline driver** + a few **historical hero definitions**, not new mechanics.

## 1. Proposed architecture — `HistoricalTimelineBehavior`

A single `CampaignBehaviorBase` holding an ordered list of episodes:

```
Episode {
    string  Id;            // stable key, e.g. "1719_muhammad_shah_crowned"
    AD date (or game date) When;
    Func<bool>  StillApplies;   // guard: has the world already diverged past this?
    Action      Fire;           // calls existing systems + issues farmaan(s)
}
```

- **DailyTick** checks each not-yet-fired episode whose date has arrived; if `StillApplies()`
  is true it calls `Fire()` and records the `Id`.
- **Save-safe & idempotent**: the set of fired `Id`s is serialized (`SyncData`). An episode
  never fires twice, and a mid-timeline load resumes correctly.
- **Guarded** like the rest of the mod (validity checks, `TYTLog` breadcrumbs) — these touch
  heroes/kingdoms that may have died/been destroyed; never assume they exist.
- **One date authority**: centralize the AD↔game-date mapping (currently inline in
  `FarmaanScreen.CurrentDate`) so episodes can be authored in AD years.

### History bends to reality (the key design principle)
Events are **pre-timed but pre-emptible**. If the world has already diverged — the player killed
the emperor early, a province is already independent or player-held, a clan is extinct — the
episode's `StillApplies()` returns false and it is **skipped gracefully** rather than forcing an
absurd outcome. This keeps the campaign coherent and avoids the native-crash risk of acting on
stale objects.

## 2. Start date & the independence premise

**Start date.** Move the campaign opening to **1707 (Aurangzeb's death)**. Remap the date math
so game-year 1084 = AD 1707 (today it's pinned to 1719). Everything downstream uses the central
mapping helper.

**The premise problem.** The breakaway realms (Bengal, Hyderabad, Durrani, Mysore, Rajputana,
Maratha, Sikh) are *already separate kingdoms* today. "Shown as Mughal at start, then declaring
independence one by one" needs a start-state decision:

- **Option A — Unified empire at start (truest to the vision).** At new-game, the Mughal kingdom
  (`empire`) owns all the relevant fiefs and the future breakaway rulers are its **vassal clans**;
  the seven other kingdoms start **dormant/landless**. Each independence episode uses the existing
  `RevoltCascadeBehavior` secession path to move that ruler's clan + its historical fiefs out of
  `empire` into its (re)activated kingdom, with a sovereign's farmaan. The map literally shows one
  empire that fractures over time. **Cost:** a start-state reassignment of fief ownership and clan
  kingdoms (moderate, one-time), plus driving secession by script rather than by pressure.
- **Option B — Nominal subjects (easy fallback).** Keep the kingdoms separate but start them in a
  scripted *tributary* state to the empire (forced peace + a "subject" flag + suppressed AI
  aggression). Independence episodes flip them to sovereign/hostile with a farmaan. **Cost:** low.
  **Downside:** the map shows separate colors from day one, so the "all Mughal then fractures"
  visual is weaker.

Recommendation: **Option A** if the unified-then-fracture map is central to the fantasy; otherwise
**Option B** as a low-risk first cut we can upgrade later.

## 3. The requested events — mechanism, fidelity, difficulty

> Difficulty assumes the architecture above and the existing systems. **Prereq** = data that must
> exist first (usually a named hero).

**1. Aurangzeb's death → empire fragments (1707 →).**
- *Mechanism:* opening farmaan (Alamgir is dead); then a staggered series of secession episodes
  (one per province) over the following game-years, each via `RevoltCascadeBehavior` + a new
  sovereign's farmaan. Authority drop via `ImperialAuthorityBehavior` makes the AI lean the same way.
- *Fidelity/difficulty:* **Moderate** (Option A) / **Easy** (Option B).
- *Prereq:* Aurangzeb as the starting emperor hero; the historical secession date + new sovereign
  per province (see §5 table).

**2. The rapid succession (1707–1719), with on-screen deaths into the encyclopedia.**
- *Mechanism:* the emperor heroes are defined in the imperial dynasty. On each historical date,
  `KillCharacterAction` kills the reigning emperor (he remains in the encyclopedia as deceased —
  automatic), a death farmaan fires, the successor is installed as ruler (`ImperialCourtBehavior`/
  ruler-change), and a crowning farmaan fires. The 1719 "four emperors in a year" is just four such
  episodes close together (Farrukhsiyar deposed & killed → Rafi ud-Darajat → Shah Jahan II →
  Muhammad Shah).
- *Fidelity/difficulty:* **Easy–Moderate.** Encyclopedia storage is free once they're real heroes.
- *Prereq (important):* define the emperor heroes — Bahadur Shah I, Jahandar Shah, Farrukhsiyar,
  Rafi ud-Darajat, Shah Jahan II (Rafi ud-Daulah), Muhammad Shah — in the `empire` dynasty.
- *Note:* if the player has already toppled the empire by then, `StillApplies()` skips these.

**3. Crowning of Muhammad Shah (1719).** Same mechanism, the final crowning of the sequence.
- *Difficulty:* **Easy** (last step of event 2).

**4. Haider Ali takes Mysore; designates Tipu (1761 / 1782).**
- *Mechanism:* a ruler-change/coup of the Mysore (`aserai`) kingdom to Haider Ali + a farmaan; set
  Tipu as Haider's heir (clan member / designated heir). Optionally a later episode for Tipu's
  accession.
- *Fidelity/difficulty:* **Moderate.**
- *Prereq:* Haider Ali and Tipu Sultan heroes in the Mysore ruling clan.
- *Caveat:* 1761 is ~54 game-years after a 1707 start — a long campaign may never reach it. Worth
  considering compressing the timeline, or anchoring late events to *triggers* (e.g. "first time
  Mysore loses its capital") rather than hard dates.

**5. Nadir Shah's invasion (1739).** Three fidelity tiers:
- *Lite (recommended):* narrative farmaans (the Persian host crosses the Khyber → Karnal → the sack
  of Delhi) **plus effects** — empire authority crashes, the capital's prosperity/garrison are
  slashed, the imperial treasury is "looted" (gold/influence hit), a brief military shock. No army
  spawn. **Easy.**
- *Medium:* spawn a strong temporary "Persian" army near the capital that raids/besieges, then
  despawns after the window. **Moderate–Hard** (spawning, pathing, cleanup).
- *Full:* a temporary Persian kingdom that invades and withdraws. **Hard.**
- *Prereq:* Nadir Shah hero (for farmaan attribution / Medium+); the capital settlement id.

## 4. Suggested additional events (kept easy)

- **The Kingmakers — Sayyid Brothers (1713–1720).** Drive the depositions of event 2 through
  `AccessionWarBehavior` so the turnover *plays* as usurpations, not just scripted deaths. Farmaan
  flavor. **Easy–Moderate.**
- **Successor-state foundations as distinct beats** — Murshid Quli Khan / Bengal (1717), Saadat
  Khan / Awadh (1722), Asaf Jah declares Hyderabad (1724). These *are* the §3.1 independence beats;
  list them explicitly with dates. **(same difficulty as event 1).**
- **Maratha Chauth demand (recurring, player-facing).** A periodic farmaan to provincial lords —
  including the player — demanding tribute: pay gold, or face raids / war. Leans on gold + relation
  + optional war. Great recurring interactive content. **Easy.**
- **Banda Singh Bahadur — Sikh uprising (1709–1716).** A scripted `RevoltCascadeBehavior` of type
  `ReligiousUprising` (already exists) in the Punjab + farmaan. **Easy.**
- **Jat ascendancy (Churaman / Suraj Mal).** A minor scripted secession/raid around Bharatpur.
  **Easy–Moderate.**
- **Abdali's invasions & Third Panipat (1748 / 1761).** Durrani (`sturgia`) vs Maratha (`battania`):
  scripted war + set-piece. **Lite** (farmaan + authority/strength effects) **Easy**; full battle Hard.
- **Pure-flavor anniversaries** — annual imperial darbar, accession anniversaries: farmaan only.
  **Trivial**, good for ambient texture.

## 5. Prerequisites checklist (before any coding)

1. **Start date** moved to 1707; AD↔game-date mapping centralized.
2. **Historical heroes defined** (the single biggest prereq): the six emperors; Haider Ali & Tipu;
   optionally Nadir Shah / Abdali / Murshid Quli / Saadat Khan / Asaf Jah / Banda Singh. Placed in
   the correct dynasties/clans so deaths and crownings reference real encyclopedia entries.
   *(Authored biographies for these tie into the existing `Util.Biographies` system.)*
3. **Start-ownership model chosen** (Option A unified vs Option B subjects).
4. **Province → (secession date, new sovereign) table** authored (Bengal 1717, Awadh 1722,
   Hyderabad 1724, …) mapped to the existing kingdom ids:
   `empire` (Gurkani Alamgir) · `empire_w` Bengal · `empire_s` Hyderabad · `sturgia` Durrani ·
   `aserai` Mysore · `vlandia` Rajputana · `battania` Maratha · `khuzait` Sikh.

## 6. Open questions for the user

1. **Opening beat:** start with Aurangzeb *already dead* (empire intact, day-1 farmaan), or alive
   for a few days and then dying on-screen as the inciting farmaan?
2. **Independence premise:** Option A (unified empire that visibly fractures — more work) or
   Option B (separate kingdoms flavored as subjects — easy)?
3. **History vs player agency:** confirm the "pre-timed but pre-emptible" rule — events skip
   themselves if the world has already diverged. (Recommended.)
4. **Nadir Shah fidelity:** Lite (narrative + effects), Medium (spawned army), or Full?
5. **Timeline pace:** hard AD dates (late events may never be reached), or compress / anchor late
   events to game triggers so they still happen in a normal-length campaign?
6. **Interactivity:** purely observed history, or player-facing choices (Chauth demands, "submit or
   resist" at each independence)?
