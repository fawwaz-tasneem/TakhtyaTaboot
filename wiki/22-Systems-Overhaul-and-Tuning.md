# Chapter 22 — Systems Overhaul, MCM Tuning & Crash Logging

> The v1.x overhaul of the mansabdari career, the council & capital, the feudal placement
> of new vassals, conquest justice, village defence — plus the MCM tuning layer that drives
> them and the crash log that helps diagnose them.

**[← Chapter 21](21-Main-Quest-Reunification-and-Peace.md)** | **[Home](Home.md)**

---

## Contents

- [MCM tuning layer (`TYTSettings` / `Tune`)](#mcm-tuning-layer)
- [Mansabdari troop stability](#mansabdari-troop-stability)
- [Career ladder: valour, elevation, demotion, stipend](#career-ladder)
- [Capital & the council overhaul](#capital--council)
- [Feudal placement: liege, village grant, mercenaries](#feudal-placement)
- [Conquest: the fate of the notables](#conquest-fates)
- [Village defence & the call for help](#village-defence)
- [Party command system](#party-commands)
- [Crash logging (`TYTLog`)](#crash-logging)
- [Console commands](#console-commands)
- [Save-data keys added](#save-data-keys)

---

## MCM Tuning Layer

Every tunable number is exposed in-game through **MCM** (Mod Configuration Menu → *Takht ya
Taboot*). Behaviours never read MCM directly — they read `Config.Tune`, which pulls from the
live settings and **falls back to the same compiled defaults** if MCM is missing, so the mod
always runs.

- `Config/TYTSettings.cs` — `AttributeGlobalSettings<TYTSettings>`, grouped `[SettingProperty…]` sliders.
- `Config/Tune.cs` — `Tune.X` accessors, each wrapped so the settings layer can never throw into gameplay.
- csproj references the ILRepacked `MCMv5.dll` (compile-only, `Private=False` — never shipped).

| Setting (group) | Default | Drives |
|---|---|---|
| Valour per battle won (Career) | 4 | Valour gained per field win |
| Siege valour multiplier | 2 | Siege vs field battle worth |
| Valour for capturing/routing an enemy king | 80 | One-time bonus when the enemy sovereign is on the losing side |
| Valour needed per rank step | 30 | Elevation valour gate = step × next rank |
| Renown needed per rank step | 60 | Elevation renown gate = step × next rank |
| Minimum relation with king to be elevated | 0 | The "favour not bad" gate |
| Troop capacity multiplier | 1.0 | Scales every rank's troop target |
| Assumed vanilla base party size | 30 | Subtracted so the cap lands on the target |
| Muster retention fraction | 0.80 | Floor you must keep (× rank target) |
| Days below muster before demotion | 30 | Grace window |
| Stipend per required troop (per 30 days) | 2 | Treasury stipend size |
| Councils a king must hold per year (Council) | 4 | King cadence |
| Councils a lord must hold per year | 1 | Lord cadence |
| Cost to move the capital | 200000 | Deliberate relocation cost |
| Daily chance bandits overwhelm a village (Villages) | 0.05 | Base raid chance at full threat |
| Militia & zamindar defence weight | 1.0 | Strength of passive village defence |
| Troops a dispatched commander takes | 40 | Relief detachment size |

---

## Mansabdari Troop Stability

**Problem fixed:** a promotion could demand more troops than the new party cap allowed, causing
a promote → over-cap → instant-demote spiral.

Each rank now carries an **absolute troop target** (`MansabdariBehavior.RequiredTroopsForIndex`,
= the rank's `SawarRequired` × `Tune.TroopCapacityMultiplier`). `PartySizeMansabPatch` adds exactly
`target − Tune.BaseTroopCapacity` to the clan leader's party-size limit, so the cap **lands on the
target** (plus any genuine perk bonuses). The retention floor you must keep is a fraction
(`Tune.RetentionFraction`) of that same target — so a rank can never demand more than it lets you field.

- `MansabdariBehavior.GetRequiredTroops(clan)` / `GetRetentionFloor(clan)` — reuse these for any
  "how big should my army be" logic.

---

## Career Ladder

All in `MansabdariBehavior` + `WarfareBehavior`.

- **Valour** — a serialized per-clan score (`_valour`). Earned in `WarfareBehavior.OnPlayerBattleEnd`:
  `Tune.ValourPerWin` × (siege ? `Tune.ValourSiegeMultiplier` : 1), plus `Tune.ValourKingCapture` when
  the enemy sovereign was on the **losing side** of the battle (`RoutedOrCapturedKing` checks the losing
  `MapEventSide` parties for `enemy.Leader`).
- **Elevation** (`MeetsElevationCriteria`) weighs three things: valour ≥ `ValourPerRankStep × next`,
  clan **renown** ≥ `RenownPerRankStep × next`, and relation with the king ≥ `MinRelationForElevation`.
  When all are met the court elevates you automatically (`TryAutoElevatePlayer`, called on valour gain
  and weekly) and spends the valour; the menu/dialog petition uses the same gate.
- **Demotion clock** — `OnDailyTick` counts consecutive days the **main party** is below the retention
  floor. A clan **companion speaks up** (named, styled like the vanilla disagreement line) on first
  breach and again at 7 and 3 days left; at `Tune.DemoteGraceDays` you drop one rank by farman; recover
  and the clock clears.
- **Stipend** — every 30 days the **imperial treasury** (the sovereign's gold) pays
  `Tune.StipendPerTroop × target`, by farman (a "treasury is bare" farman if it can't pay). Player-kings
  don't pay themselves.

---

## Capital & Council

### Four culture-named offices
`CouncilBehavior.Post` is now **`PrimeMinister, Commander, Treasurer, Spymaster`**. Titles resolve by
**culture id** and register (king's court vs a lord's council) in `CouncilTitles`:

| Culture (faction) | King's council | Lord's council |
|---|---|---|
| empire/aserai/sturgia (Mughal, Mysore, Durrani) | Wazir-e-Azam · Sipah-Salar · Diwan-i-Kul · Daroga-e-Khufia | Diwan · Bakshi · Mustaufi · Harkara |
| vlandia (Rajput) | Pradhan · Senapati · Bhandari · Mukhbir-Pramukh | Diwan · Senani · Khazanchi · Mukhbir |
| battania (Maratha) | Peshwa · Sar-e-Naubat · Amatya · Fadnavis | Karbhari · Senapati · Phadnis · Harkara |
| khuzait (Sikh) | Diwan · Jathedar-e-Fauj · Khazanchi · Khufia-Nawis | Diwan · Jathedar · Toshakhana · Harkara |

`PostTitle(post, holder)` chooses the register from the holder; save data migrated 3→4 slots
(`GetCouncillor`/`Set` tolerate old 3-slot arrays). The Spymaster is scored on Roguery + Scouting.

### Capital & convening (`ImperialCourtBehavior`)
- **Capital** = a fixed town held by each realm's sovereign (`GetCapital`, the ruler's best town).
  Auto-reassigned with a farman if captured; a ruler may **move it deliberately** for
  `Tune.MoveCapitalCost` (town loyalty/prosperity hit) via the "Seat the imperial capital here" town menu.
- **Convene** — a town menu option, available only when you are physically **in your capital** (king) or
  at your own seat (landed lord). Opens the convene menu:
  - **Put a matter to the council (vote)** — *ruler only*. Builds proposals (declare war / make peace /
    levies / tax remission); attendee lords vote weighted by tier + relation (`RunVote`); a passed vote is
    enacted (`Enact`).
  - **Grant / revoke a vassal's mansab** — *ruler only* (`MansabdariBehavior.AdjustRank`).
  - **Appoint your council offices** — opens the council screen.
  - **Take counsel with your vassals** — *lord only*, a modest influence/relation boon.
- **Cadence** — `OnDailyTick` enforces a deadline (`365 / quota` days). Miss it and a king loses
  authority + legitimacy, a lord loses influence, with a warning farman.

---

## Feudal Placement

In `FeudalTitlesBehavior`:

- **Liege override** — a serialized `heroId → liegeId` bond. `GetFeudalLiege` consults it first (validated:
  liege alive, landed, same realm), else the default bound-lord/sovereign rule.
- **On joining a realm** (`OnClanChangedKingdom` → `EnsurePlayerPlacement`, also run weekly to catch the
  mercenary→vassal transition) a landless player-vassal is **placed beneath a castle lord** (preferring a
  castle lord, then a town lord, then the sovereign) and **granted a village** beneath that liege. You can
  then petition that liege for a council seat.
- **Player zamindari is a real fief** — `AssignZamindar` transfers actual village ownership to the player
  (`ChangeOwnerOfSettlementAction.ApplyByGift`), so the village appears in the clan's fiefs screen and
  encyclopedia and is administered via the "Oversee your fief" village menu. **AI zamindars remain a
  flavour layer** over a local notable (no transfer).
- **Mercenaries hold no land** — guarded in `AssignZamindar`, `EnsurePlayerPlacement`, and
  `CareerProgressionBehavior` (claim option). A merc who swears as a full vassal is then placed by the
  weekly tick.

---

## Conquest Fates

After you storm a town and choose sack-or-mercy (`WarfareBehavior.OfferSpoils`), you **judge its
notables** one by one (`JudgeNotables`, top 6 by power). Per notable, with role and relation shown:

| Fate | Effect |
|---|---|
| Pardon | relation +10, slight loyalty gain |
| Fine | seize gold (∝ power), relation −10, slight unrest |
| Banish | strip ~85% of his power, loyalty/prosperity dip, revolt pressure, enmity with him + kin + peers |
| Execute | kill him; heavy loyalty/prosperity loss, large revolt pressure, big kin/peer relation loss, legitimacy hit if ruler |

A **"Pardon the rest"** button bulk-clears the remainder. Unrest feeds `RevoltCascadeBehavior` pressure.

---

## Village Defence

In `VillageDevelopmentBehavior` (runs for player-owned villages):

- **Passive defence** (`DefenceStrength`) — the village militia (`settlement.Militia`) and the zamindar's
  Steward skill suppress threat daily, scaled by `Tune.MilitiaDefenceWeight`.
- **Overwhelm** (`MaybeOverwhelm`) — when threat ≥ 50, a daily roll (`Tune.PatrolOverwhelmChance`, scaled
  by threat and by how weak the village's own defence is) can break the militia. The **zamindar sends a
  plea** (farman) offering two answers:
  - **Send a commander with N men** (`DispatchRelief`) — removes `Tune.ReliefDetachmentSize` regulars from
    your party, suppresses the threat, and returns the men after `ReliefDays` (6).
  - **See to it yourself** — ride there and use the existing **Patrol** action.
- A 12-day cooldown prevents plea spam. Relief state is serialized.

---

## Party Commands

`PartyOrdersBehavior` lets you command parties from any settlement menu ("Command your parties").

- **Commandable parties**: your **clan** parties (companions/kin, via `Clan.WarPartyComponents`) and your
  **vassals** (clans whose `GetFeudalLiege` is you, or all kingdom clans if you are king).
- **Orders**: Follow me · Find & attack an enemy · Support a lord · Reinforce a holding · Defend a village ·
  Stand down. Applied through `MobileParty.SetMove…` (`SetMoveEscortParty`, `SetMoveEngageParty`,
  `SetMoveGoToSettlement`, `SetMoveDefendSettlement`, `SetMoveModeHold`) — note this build's overloads take
  a `MobileParty.NavigationType`.
- **Clan parties** are held to the order: it is re-asserted every `HourlyTickPartyEvent` until it expires
  (30 days) or the target is gone. **Vassals** are a relation-gated **request** (relation + authority + roll);
  on accept the order is applied once (20-day record) — they keep their own AI and may drift; on refuse, a
  small relation hit. Orders are serialized; `hindostan.orders` lists them.

---

## Crash Logging

`Util/TYTLog.cs` writes a timestamped (and in-campaign, in-game dated) line per entry to:

```
<module folder>\Logs\tyt_log.txt        (falls back to the Desktop)
```

- Opened in `HindostanSubModule.OnSubModuleLoad`; Harmony patch results and behaviour registration are logged.
- `AppDomain.UnhandledException` is captured with its **full stack trace** — a fatal crash leaves a
  mod-specific trail showing what the mod was last doing.
- `TYTLog.Guard("context", () => …)` runs a risky handler in try/catch and **logs the exception with
  context instead of crashing**. The hot new handlers are wrapped: `Mansabdari.OnDailyTick`,
  `ImperialCourt.OnDailyTick`, `Warfare.OnPlayerBattleEnd`, `VillageDev.DailyTick`.

When diagnosing a crash, read `tyt_log.txt` bottom-up: the last `INFO` lines show context, an `ERROR`
line shows a guarded handler that threw, and `UNHANDLED EXCEPTION` shows a fatal stack trace.

---

## Console Commands

All under the `hindostan.` namespace (open the console with the developer console mod / Alt+~):

| Command | What it shows / does |
|---|---|
| `hindostan.career_status` | Mansab, fiefs beyond rank, claim availability |
| `hindostan.valour` | Valour, renown, elevation eligibility, muster floor, days under muster |
| `hindostan.add_valour <n>` | Add valour (test elevation/stipend) |
| `hindostan.council` | Your (or your liege's) council, 4 culture-named offices |
| `hindostan.capital` | Each realm's capital |
| `hindostan.war_status` | Valour + war goals/scores |
| `hindostan.village_lords` | Village zamindars and their lieges |
| `hindostan.village_status` | Your villages' hearth & threat |
| `hindostan.set_village_threat <n>` | Force threat (test the overwhelm plea) |
| `hindostan.orders` | List parties currently under your orders |

---

## Save-Data Keys

New `SyncData` keys (a new campaign is recommended; old saves degrade gracefully):

- Mansabdari: `hind_mansab_valIds/valVals`, `hind_mansab_daysUnder`, `hind_mansab_warnedUnder`, `hind_mansab_lastStipend`
- Council: `hind_council_spymaster` (4th slot)
- Imperial court: `hind_capital_kingdoms/settlements`, `hind_court_cadenceDeadline`
- Feudal: `hind_liege_heroes/lieges`
- Village relief: `hind_vil_reliefIds/reliefVals/reliefTIds/reliefTVals`
- Party orders: `hind_orders_pids/types/tgts/vass/exps`
