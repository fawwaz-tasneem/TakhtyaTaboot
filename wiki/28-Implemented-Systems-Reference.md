# 28 — Implemented Systems Reference (As-Built)

> The **as-built** state of the mod's C# systems after the two big July 2026 passes
> (stability/zamindari/village-fiefs/core-wave, then farmaans/opinions/dynasties/dialogue).
> Chapters 13–27 are *design* documents — some describe features that shipped differently
> or not at all. **This chapter describes what is actually in the code**, the invariants
> that hold it together, and how to extend it without breaking them.

**[Home](Home.md)**

---

## 1. Architecture ground rules (every behavior follows these)

1. **One behavior per system**, `CampaignBehaviorBase` with `public static X Instance` set in `RegisterEvents`. Registered in `HindostanSubModule.OnGameStart` — registration order matters (see §5).
2. **Persistence is parallel primitive lists only** in `SyncData`, keyed by `StringId`. No `SaveableTypeDefiner`, no object references, no dictionaries directly (the serializer can't take them). New keys are **append-only**: an old save missing a key must load to an empty default without crashing (guard every `Zip`/rebuild with index bounds).
3. **Never do collection work in `OnNewGameCreatedEvent`** — the engine is still creating heroes/clans/settlements on parallel threads there (native `0xC0000005`). Seed idempotently in `OnSessionLaunched`; gate mid-game event handlers with `Util.WorldGen.Ready`.
4. **Every tick handler is wrapped** in `TYTLog.Guard("Name", ...)`; per-step `TYTLog.Crumb` inside hot settlement ticks (a native crash's only witness is the last crumb).
5. **Tunables** go MCM (`Config/TYTSettings.cs`) → `Config/Tune.cs` with compiled fallbacks. Behaviors read `Tune.X` only.
6. **Non-trivial logic is pure math** in `Util/*.cs` with **no TaleWorlds types**, `<Compile>`-linked into `src/TheHindostanMod.Tests` (xUnit). If you can't unit-test the formula, split it until you can.
7. **Harmony patches are applied per class** (a loop in `HindostanSubModule`), so one bad target on a new game version disables only itself and logs, instead of silently killing every patch (which a single `PatchAll` in try/catch did).
8. **UX charter** — every feature names its surface *before* implementation:
   - person-to-person acts → **dialogue** (`HindostanDialogsBehavior`)
   - place-bound acts → **settlement menus**, consolidated under the ONE `hindostan_court` submenu (`CourtMenuBehavior`); villages keep `hindostan_village`
   - realm overviews → **Gauntlet screens** (Council, Hierarchy)
   - events & decrees → **farmaans** (paused + deduped, §3)
   - ambient info → the message log, never a popup

## 2. System map

| System | Behavior (Behaviours/) | Pure math (Util/) | SyncData prefix | Console (`hindostan.*`) |
|---|---|---|---|---|
| Feudal layer / zamindari | `FeudalTitlesBehavior` | — | `hind_zamindar_*`, `hind_liege_*` | `village_lords` |
| Tribute & call-to-arms | `FiefHierarchyBehavior` | — | `hind_fh_*` | `summon`, `tax_now`, `feudal_status` |
| Village fiefs (projects, coffer, threat) | `VillageDevelopmentBehavior` + `UI/VillageWorksScreen` (works ledger) | `VillageFiefMath` | `hind_vil_*` | `village_status`, `set_village_threat` |
| Bonded labour (captives → begar gang: +tax/+prosperity/+unrest, attrition) | `SlaveLabourBehavior` (owns gang; `VillageDevelopmentBehavior` reads yields) | `SlaveLabourMath` (+ `VillageFiefMath.ThreatStep` `unrest` term) | `hind_labour_*` | `labour_status`, `settle_labour` |
| Mansabdari ladder | `MansabdariBehavior`, `CareerProgressionBehavior` | `MansabTenureMath` | `hind_mansab_*` etc. | `set_rank`, `mansab_status`, `add_valour` |
| Tenure law | `MansabdariTenureBehavior` | `MansabTenureMath` | — | — |
| Legitimacy / authority | `LegitimacyBehavior`, `ImperialAuthorityBehavior` | — | `hind_legit_*`, `hind_authority_*` | — |
| Warfare (aims, score, terms, tributaries) | `WarfareBehavior`, `WarAimsBehavior` | `WarAimMath` | `hind_war_*` | `war_status` |
| Succession (crisis, laws, scripted 1707 cascade, treachery arc) | `SuccessionBehavior`, `SuccessionLawBehavior`, `ImperialSuccessionEventBehavior` | `SuccessionLawMath` (incl. incumbent price / treachery / fates), `ImperialSuccessionPlan` | `suc_*` (incl. `suc_treach*`), `tyt_imp_succ_*` | `accession_status` |
| Coronation darbar (accession summons: attend/snub, late oath) | `CoronationBehavior` (own ruler snapshot; player-sovereign & player-vassal beats; AI silent) | `CoronationMath` | `hind_coron_*` | `coronation_test` |
| Unified empire until Aurangzeb dies (fold + breakaway) | `UnifiedEmpireBehavior` | `UnifiedEmpireMath` | `tyt_unified_*` | `unified_status` |
| Clan safety net (no masterless houses) | `ClanSafetyNetBehavior` | `ClanRehomeMath` | `tyt_orphan_*` | — |
| Siege parley (bribe / terms / honour-or-defy) | `SiegeParleyBehavior` | `SiegeParleyMath` | — | — |
| Akhbaar scouts (dispatch → timed akhbaar report) | `AkhbaarScoutBehavior` | `AkhbaarMath` | `hind_akhbaar_*` | `akhbaar_status`, `akhbaar_arrive`, `akhbaar_send` |
| Accession war (player) / AI civil war | `AccessionWarBehavior`, `CivilWarBehavior` | `CivilWarMath` | `hind_acc_*`, `hind_cw_*` | `force_accession_win`, `force_ai_civil_war`, `civil_war_status` |
| Revolts | `RevoltCascadeBehavior` | — | — | — |
| Nazrana gift cycle | `NazranaBehavior` | `NazranaMath` | `hind_naz_*` | — |
| Religious tolerance / jizya | `ReligiousToleranceBehavior` + `Patches/ToleranceTaxPatch` | `ToleranceMath` | `hind_tol_*` | — |
| Court factions | `CourtFactionsBehavior` | `CourtFactionMath` | `hind_cf_*` | — |
| Councils & Darbar | `CouncilBehavior`, `ImperialCourtBehavior` | — | — | — |
| Monsoon | `Patches/MonsoonSpeedPatch` | `SeasonMath` | — | — |
| Farmaan director | `FarmaanDirectorBehavior` + `UI/FarmaanScreen` | `FarmaanFlow` | `hind_far_*` | `farmaan_test` |
| Personal opinions | `OpinionBehavior` | `OpinionMath` | `hind_op_*` | — |
| Dynasties, royal styles, cadet houses | `DynastyBehavior` | `CadetHouse` (engine helper) | `hind_dyn_*` | — |
| Dialogue pack | `HindostanDialogsBehavior` | — | — | — |
| Court submenu | `CourtMenuBehavior` | — | — | — |
| Rusukh (entrenchment) | `RusukhBehavior` | `RusukhMath` | — | — |
| Party orders | `PartyOrdersBehavior` | — | — | — |

## 3. Load-bearing invariants (break these and "it feels buggy" comes back)

**One liege chain.** `FeudalTitlesBehavior.GetFeudalLiege` is the ONLY liege resolution.
The rungs, top to bottom (playtest round 3): explicit `_liegeOverride` bond → village
zamindar answers the bound town/castle's lord → a CASTLE-only lord answers the NEAREST
town lord of his realm → town lords and unlanded nobles answer the sovereign.
`FiefHierarchyBehavior.GetLiege` — which drives tribute and the call-to-arms — *delegates*
to it, and the hierarchy board draws it. Before this, the hierarchy screen showed one
liege while taxes were paid to another. If you need "who does X answer to", call
`GetFeudalLiege`; never re-derive it from engine ownership.

**All zamindar writes go through `SetZamindarEntry`/`RemoveZamindarEntry`** (they maintain
the reverse hero→villages index). A weekly `Reconcile()` scrubs dead heroes, cross-realm
lords, stale liege bonds and cycles; event handlers (`OnClanDestroyed`, `KingdomDestroyed`,
`OnSettlementOwnerChanged`, `OnClanChangedKingdom` for **every** clan) prune eagerly.
`SetLiege` refuses bonds that would close a cycle — a cycled pair silently vanishes from
the hierarchy tree's top-down walk.

**Prescriptive vs descriptive.** `MansabdariBehavior.CanHold(clan, settlement)` is THE
gate for who *may* hold a fief (villages ≥ rank 1, castles ≥ 3, towns ≥ 5) — checked by
`AssignZamindar` for lords and enforced by `CareerProgressionBehavior` (grace period, then
reclamation). `FeudalTitlesBehavior.GetTier` is purely *descriptive* (what a hero actually
holds, including engine-owned villages). The mansab rank-1 title is **"Mansabdar-e-Sad"** —
the word *Zamindar* is reserved for the feudal village-lord tier; don't reintroduce the
collision.

**`AssignZamindar` returns `bool`.** It can legitimately refuse (mercenary service, rank
gate). Callers that charge influence or announce a grant MUST check it — the old `void`
version silently no-oped while `ClaimFief` still spent influence and proclaimed success.

**Farmaan flow (`Util/FarmaanFlow.Decide`).** Every `RoyalFarmaan.Issue/FromRuler/FromLiege`
takes optional `dedupeKey`, `priority` (Ceremonial/Urgent/Routine), `cooldownDays`.
Rules: Ceremonial is never suppressed; an identical live key drops; a Routine notice inside
its cooldown (or with none) becomes a log line + weekly "Court Circular" digest item; **a
farmaan carrying choice callbacks is never downgraded** (the choices would be lost). The
screen pauses campaign time (`TimeControlMode = Stop`) and restores it only if *it* paused
— a player already paused stays paused. `SuppressImmediate`/`Pump()` (re-entrancy guard for
settlement ticks) is a separate mechanism; leave it alone.

**Opinions ride on vanilla relation.** `OpinionBehavior.EffectiveOpinion(a, b)` = vanilla
`GetHeroRelation` + Σ live typed records (half-life decay in `OpinionMath`). Vanilla stays
the base so untouched systems keep working; records may target ANY hero (princes, notables),
which is the "individuals, not clans" unlock. Where an *individual's* judgment matters
(vassal orders, civil-war side-picking, council scoring, nazrana withholding), read
`EffectiveOpinion` — not raw relation.

**Dynasty registry is the source of truth** for cadet/parent links (`DynastyBehavior`);
the `tyt_claim_`/`tyt_exile_`/`tyt_cadet_` StringId prefixes are kept only as back-compat
detection. The roll of past sovereigns (per kingdom) is what makes royal styles
(Shahzada/Shahzadi, Yuvraj/Rajkumari, Kanwar/Bibi, Mirza) survive a ruler's death.

**Clan creation happens in exactly one place**: `Util/CadetHouse.BuildShell` (name/banner/
home/`IsInitialized`/`SetLeader`/`MoveHero` — every field the encyclopedia dereferences,
or the clan page native-crashes). `ClaimantClan` (temp claimant clans, exile houses)
delegates to it. `ClaimantClan._origin` is persisted through `SuccessionBehavior.SyncData`.

**Dormant shells are real kingdoms.** On a fresh campaign `UnifiedEmpireBehavior` folds
Bengal (`empire_w`) and Hyderabad (`empire_s`) into the empire; the emptied kingdom objects
stay alive (never `IsEliminated`) so the breakaway on Aurangzeb's death can repopulate them
with their StringIds — and every id-keyed system (`SuccessionLawBehavior` seeds,
`FactionRelationsBehavior.MughalIds`, `NoMughalCivilWarPatch`) — intact. Rules: a shell is
detected by `UnifiedEmpireBehavior.IsDormant(k)` (alive kingdom, zero living clans), and any
pass that enumerates kingdoms for *political* content (relations, sovereign rolls, hierarchy
UI) must skip dormant realms; the engine's own cull (`FactionDiscontinuationCampaignBehavior`)
is vetoed via `CanKingdomBeDiscontinuedEvent` only while the Unified phase lasts. Never
declare war on / make peace with a shell — `UnifiedEmpireBehavior` keeps them quiet weekly.

## 4. Extension recipes

- **New farmaan**: pick a priority; give recurring ones a `dedupeKey` + cooldown; never
  make a Routine farmaan that carries callbacks and expect it to digest.
- **New opinion type**: append to `OpinionMath.OpinionType` (NEVER reorder — serialized as
  int), add its `(magnitude, halfLife)` row + a `Describe` label, write records via
  `OpinionBehavior.Instance.AddOpinion`.
- **New dialogue flow**: copy a flow in `HindostanDialogsBehavior` — `AddPlayerLine` on
  `hero_main_options` (priority 104–115), condition side-effect-free (text variables via
  `MBTextManager` are fine), consequence does the work, end at `close_window`.
- **New court action**: `AddGameMenuOption(CourtMenuBehavior.MenuId, ...)` — never add mod
  options to the raw `town`/`castle` roots. `CourtMenuBehavior` must stay registered before
  any behavior that targets its menu.
- **New village project**: append to the `VillageProject` enum (names of the first five are
  serialized in the completed-works CSV — never rename), add its `ProjectDef` row (cost,
  days, effects, optional `Requires` prereq), and slot it into one of the AI chains
  (Defence/Food/Economy). The menu picks it up automatically.
- **New MCM setting**: property in `TYTSettings` + fallback getter in `Tune` — behaviors
  read only `Tune`.

## 5. Registration order (`HindostanSubModule.OnGameStart`) — the parts that MUST stay ordered

1. `WorldGenGuardBehavior` — **first**, closes the world-gen gate.
2. `SaveGuardBehavior` — save-flag + farmaan `Pump()` driver.
3. `FarmaanDirectorBehavior` — before anything that issues farmaans.
4. `CourtMenuBehavior` — before every behavior that adds options to `hindostan_court`.
5. `UnifiedEmpireBehavior` — right after the farmaan director and before every political
   session-launch pass (dynasty sovereign roll, faction stance): on a new campaign the fold
   (Bengal/Hyderabad into the empire) must happen before those passes see the world.
6. `OpinionBehavior`, `DynastyBehavior`, `HindostanDialogsBehavior` — before their consumers.
7. Everything else in the existing phase order.

## 6. Known gaps & deferred work

- **Cadet warband spawn** (`CadetHouse.TrySpawnLordParty`) is reflection-based
  (`MobilePartyHelper.SpawnLordParty`, then `LordPartyComponent.CreateLordParty` — both
  verified present in the current game DLLs, exact overload resolved at runtime). Fallback:
  founder teleports home and the engine's clan-party AI raises a party. Verify in-game.
- **AI zamindars hold villages in the stored layer only** by default. Engine ownership for
  AI (`Tune.AiZamindarEngineOwnership`, restart-flagged, default off) distorts fief votes /
  clan income and is reverted by conquest of the bound seat — it exists for experiments.
- **Deferred by scope decision**: EstatesBehavior, trade routes, famine, epidemics,
  festivals, character traits, ulema fatwa, pilgrimage, great works, intrigue, war-score/
  peace-negotiation/suzerainty behaviors, main quest, LLM news reporter (chapters 17–21
  describe their designs). Coronation ceremonies and princely intrigue are designed
  (roadmap discussion) and now have their foundations (opinions, dynasties, dialogue).

## 7. Verifying changes

- `dotnet test src\TheHindostanMod.Tests` — 216+ tests, pure math only, no game needed.
- `dotnet build src\...\TheHindostanMod.csproj -c Release` — needs the game DLLs; the path
  comes from gitignored `BannerlordDir.local.props` (see the contributor guide).
- In-game: `PLAYTEST.md` at the repo root is the full per-system checklist; `tyt_log.txt`
  is the mod's own log — any `[ERR]` line is a bug.

---

**[Home](Home.md)**
