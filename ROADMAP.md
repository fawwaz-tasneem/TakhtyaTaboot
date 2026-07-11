# Roadmap — Takht ya Taboot

The single source of truth for what comes next. Ordered within each block; blocks A → D.
Design details for many items exist in wiki ch.13–27; as-built ground truth is ch.28.
Update this file when an item ships (move it to "Shipped") or when priorities change.

## A. Near-term (from playtest rounds 1–2, July 2026)

1. **Culture-keyed dialogue register + Calradia purge.** Persianate Urdu honorifics for
   Muslim courts (Zill-e-Ilahi, Jahanpanah, bismillah invocations), distinct registers for
   Rajput/Maratha/Sikh courts; sweep remaining vanilla Calradia prose through the
   LocalizationOverride pipeline.
2. **Clan-screen fiefs visibility.** The engine cannot give a village an owner separate
   from its bound town, so zamindari villages never appear in the vanilla Fiefs tab —
   inject a "Zamindari" block into the clan screen via UIExtenderEx instead.

## C. Deepening what already ships

1. **Festivals** (Eid, Diwali, Nauroz, Baisakhi — culture/faith-keyed like royal styles):
   seasonal Ceremonial farmaan, a court gathering that batches nazrana presentation and
   opinion gains; natural stage for petitions and betrothals. Builds on SeasonMath + the
   court menu.
2. **Women's court influence.** Official darbar posts are men-only (period rule, shipped);
   model the Nur Jahan pattern properly: influential women act through the intrigue layer —
   whispers that shift the holder's decisions, opinion records, faction ties.

## D. The big new arcs

1. **Marriage alliances with negotiation.** Dowry/mehr terms, inter-faith matches
   interacting with the tolerance system, `KinBond` records binding houses, cadet houses
   giving younger sons something to be married into.
2. **Princely intrigue.** Make the princes' succession answers real: resentful princes
   (low `EffectiveOpinion` of the heir) quietly gather supporters; charter them into cadet
   houses to defuse them — or watch them fester into the challenger when the throne
   passes. Turns the accession war into the payoff of a visible, influenceable buildup.
3. **Firangi factor.** European trading companies as late-game texture for 1719: envoys
   seeking trade concessions at ports, artillery mercenaries for hire, and a slowly
   overstaying welcome.

## Longer-term designs on the shelf (wiki ch.13–27)

Estates, trade-route network, epidemics, cultural patronage/caravanserai, traits earned
from actions, ulema fatwa network, pilgrimage (Hajj/Kumbh/Amritsar), Great Works,
assassination/blackmail/spy schemes, main quest (Restoration vs Domination),
waqai-nawis LLM news layer (ch.18).

## Shipped (for orientation)

- Zat/sawar split ranks (was C.2) — 2026-07-11. The mansab is now presented as the historical
  DUAL rank it always encoded: ZAT (the mansab number — personal rank/status, gates fiefs and
  now sets the stipend) and SAWAR (the cavalry obligation — the muster target). Both numbers were
  already tracked; this names them (`MansabdariBehavior.GetZat/GetSawar`, shown as "zat X / sawar
  Y" in the mansab menu, encyclopedia, stipend farmaan) and re-bases the stipend on ZAT rather
  than headcount (new `MansabRankMath.StipendForZat`, MCM "Stipend per point of zat"). Fief
  eligibility and the muster target already keyed on these, so the rest was labelling. 4 tests.
- Darbar petition court (was B.1) — 2026-07-11. `DarbarPetitionBehavior`: a "Hear a petition and
  render judgment" sitting in the sovereign's Darbar, drawing GROUNDED cases from the live realm
  — a boundary dispute between two village zamindars, a raided village pleading for justice, a
  quarrel between two notables. The ruling (for plaintiff / for defendant / compromise / dismiss)
  writes a signed `CourtRuling` opinion record into each party's regard for the sovereign (the
  ledger hook that was defined but unused), and moves the crown's influence and legitimacy per the
  tested `DarbarCourtMath`. 3-day docket cooldown so it's a periodic act, not a grind. (Traits are
  read-only in this codebase, so trait nudges were left out.) Console: `hindostan.darbar_petition`.
- Fief petitions replacing instant claim (was B.2) — 2026-07-11. `FiefPetitionBehavior`: the
  mansab menu's "claim your due" is no longer an instant grant for a flat fee. The player FILES
  a petition staking a gold gift (nazrana, non-refundable) and influence (refunded on
  withdrawal), choosing modest/handsome/lavish. The weekly court is the queue engine: while a
  qualifying fief is free it rolls the court's approval (rising with gift, stake, and the
  sovereign's regard — `FiefPetitionMath`), and BELOW a floor of regard it refuses outright and
  keeps the stake. Eligibility + the grant reuse `CareerProgressionBehavior` (no logic
  duplicated). Fixes the "Kanpur/Lucknow instantly claimable" playtest complaint. 6 tests.
  Console: `hindostan.petition_status / petition_resolve`.
- Village-jagir tenure rotation + opinion records (was C.1) — 2026-07-11. Under Mansabdari
  law, `MansabdariTenureBehavior` now rotates VILLAGE jagirs too (through the zamindari feudal
  layer, not engine ownership): one town/castle and one village review per realm per week on
  the same term clock. AI village zamindars run the full comply/reprimand/dismissal ladder (a
  village holder can't secede, so "traitor" collapses to a forced, grudge-laden removal); the
  player gets the comply/defy farmaan for his own village jagirs. Every rotation now writes
  `Favor` (to the new holder) / `Grudge` (to the displaced, in the friction cases) opinion
  records feeding the grievance dialogue — town/castle paths too. Console:
  `hindostan.tenure_rotate_village`; `tenure` status lists overdue village jagirs.
- Monsoon beyond speed (was C.4) — 2026-07-11. `MonsoonBehavior`: once a year the rains draw
  a good/bad quality roll (announced by farmaan only when notably bountiful or failed). Post-
  monsoon village tax scales with it (fat after good rains, thin after bad), read into
  `VillageDevelopmentBehavior`'s accrual via `HarvestMultiplier()`. A failed monsoon can tip a
  hard-pressed player village into FAMINE — a plea farmaan: open the granaries at a price
  (hearth steadies, threat falls, gentry warm) or let them starve (hearth and loyalty bleed,
  threat climbs). Pure math in `SeasonMath` (harvest multiplier, famine odds, verdict; 11 new
  tests). MCM "Monsoon drives the harvest"; console `hindostan.monsoon_status / set_monsoon`.
- Coronation ceremonies (was B.1) — 2026-07-11. `CoronationBehavior`: on any accession, a
  darbar. If the PLAYER accedes, AI house heads attend or leave an empty place by their
  `EffectiveOpinion` of him (attendees → `SworeFealty` + relation & legitimacy; absentees →
  `MissedCeremony`, and he may DEMAND a late oath — harder to win, `CoronationMath`); if the
  player is a VASSAL of the new sovereign he is summoned to travel and swear or deliberately
  stay away. AI-only accessions resolve silently. Building blocks (opinion records, Ceremonial
  farmaan, grievance dialogue) pre-existed; pure `CoronationMath` (8 tests). Console:
  `hindostan.coronation_test`.
- Slave labour in villages (was A.2, user-requested) — 2026-07-11. `SlaveLabourBehavior`:
  in a village you hold, bind common battle captives from your prison train to forced
  labour (begar). The gang (capped by hearth, ~5–60) raises the village's tax and the bound
  town's prosperity, but adds a daily UNREST term to bandit threat that watchtowers can't
  police, and thins over time as men escape (fugitives feed the local banditry) or die at
  the work. Free them for a threat drop + notable goodwill. Village menu options + status
  line; pure `SlaveLabourMath` (12 tests); `VillageFiefMath.ThreatStep` gained an `unrest`
  term. Console: `hindostan.labour_status / settle_labour`. MCM toggle under Village Fiefs.
- Akhbaar scouts (was A.1) — 2026-07-11. `AkhbaarScoutBehavior`: from any court, pay a
  harkara to trail a named lord; his *akhbaar* (newsletter) arrives in the farmaan layer
  N days later — location, what he's about, and strength in hearsay terms (rounded count +
  worded composition, never an exact roster). Handles no-party / captive / died-on-the-road
  lords. Pure fee/delay/prose logic in `AkhbaarMath` (tested, 23 cases). Seed of the wider
  akhbarat espionage layer (wiki ch.17). Console: `hindostan.akhbaar_status / _arrive / _send`.
- Wave of 2026-07-10 (was A.0–A.3): imperial colours while unified (folded clans dress in
  empire colours, ancestral colours restored at the breakaway); succession-crisis economy
  rework (incumbent price scales with reign years via the dynasty accession roll — a
  49-year Alamgir costs millions; pressing a secure king ≥3:1 risks a TREACHERY
  declaration → banishment + war; captured traitor faces execute / heavy fine / imprison
  with a 200k–1M ransom or monthly death-in-the-fort risk); hierarchy screen rebuilt as a
  troop-tree board (sovereign card → one column per direct vassal, both-axis scroll);
  village works ledger as a Gauntlet screen (progress bar, coffer + collect, tax estimate,
  queue) replacing the text construction menu.
- Playtest round 2 fixes & features — July 2026. `ClanSafetyNetBehavior`: no noble house
  stands masterless (claim-kingdom scatter vetoed, scattered houses fold home, orphans
  re-home by faith/relations/nearness — `ClanRehomeMath`). Sovereign levers: grand darbar
  (+authority) and charitable endowments (+legitimacy) in the empire-survey menu, with a
  "how to raise it" guide. `SiegeParleyBehavior`: the attacker's envoy at the walls —
  bribe the qiladar or offer terms, then honour or defy them (`SiegeParleyMath`).
  White claim-kingdom banner fixed (shell clans now carry real colors).
- Unified Empire until Aurangzeb dies (was A.1) — July 2026. `UnifiedEmpireBehavior` folds
  Bengal (`empire_w`) and Hyderabad (`empire_s`) into the empire on a fresh campaign
  (dormant kingdom shells keep their ids); the accession cascade's first death sunders the
  fold — clans return, the Nawab/Nizam are re-seated, recorded wars resume, farmaans
  announce both beats. Pure logic in `UnifiedEmpireMath` (tested); ground truth in ch.28.
- Stability pass, zamindari unification, village fiefs + projects, core wave (nazrana,
  civil war, tolerance, court factions, monsoon) — July 2026.
- Foundations pass: farmaan director (pause/dedupe/digest), personal opinion ledger,
  dynasties/royal styles, cadet houses, dialogue pack, court-menu consolidation.
- Mansabdari tenure law with jagir rotation for towns/castles (pre-dates the passes).
- Playtest round 1 fixes: exile-house crash, honourable abdication, village menu dedupe,
  men-only councils, tournament/outnumbered valour, zamindar on village encyclopedia.
