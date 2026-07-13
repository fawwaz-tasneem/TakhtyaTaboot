# Claims, War Aims & the Complete Diplomacy Layer — Design

The mandate (2026-07-13): a **complete native diplomacy layer**, no Diplomacy mod. Wars are fought for a
declared reason, the reason *is* the win condition, and the reason is grounded in a **claim** — a house's
standing pretension to a place, remembered across generations.

This chapter supersedes the war-aim half of [[27-War-Aims-and-Casus-Belli-Design]] (Pillar 2/3 stay; the
"scope note" that limited aims to the player's realm is retired).

---

## 0. Why the shipped system feels wrong

Three defects, all confirmed in the source:

1. **The war goal is a label, not a mechanism.** `WarfareBehavior.WarGoal` (Conquest/Tribute/Humble/Defense)
   is a *second* enum, entirely disconnected from the tested `Util.WarAimMath.WarAim`. `OfferTerms` gates
   the peace terms on **war score alone** — the aim you chose never constrains anything. `Humble` does nothing.
2. **The aim prompt fires in civil wars.** `OnWarDeclared` only checks `other is Kingdom`, and a `hind_rebel_*`
   breakaway *is* a Kingdom. So an accession war pops "War Aim against the Challenger's Claim: Conquest /
   Tribute / Chastisement" — when the aim of a throne war is, by construction, the throne.
3. **A war has no target.** `DemandProvince` annexes `ok.Settlements.Where(IsTown).FirstOrDefault()` — an
   arbitrary town. Nothing ties the reason for the war to its progress, so a war can never be *won*, only
   scored.

The fix is a claim ledger and a per-war record. Everything below hangs off those two.

---

## 1. The claim ledger (`ClaimsBehavior` + `ClaimMath`)

A **claim** is held by a **clan** (never a hero — a successor inherits his house's pretensions) over a
**settlement**, and carries a single scalar: **claim strength**, denominated in *years of standing*.

### Accrual — governance
While a clan **holds** a fief, its claim on that fief grows with tenure, at 1.0 per year held, capped at
`MaxClaimYears` (20). This runs under **both** tenure laws — the law changes the *consequence*, not the
accrual (see §2).

### Accrual — the external claim (the companion agent)
A clan may manufacture a claim on a fief it has never held, by **leaving a companion in the town as its
agent** — a *wakil* cultivating the merchant houses.

- The player leaves a companion via a town menu option; the hero exits the party and stays in the settlement.
- Each week the agent raises the clan leader's relation with the town's **merchant notables**. Pace scales
  with the agent's **Charm** and **Intelligence** (`ClaimMath.AgentWeeklyRelationGain`).
- When **two-thirds of the town's merchants stand at ≥ +40** to the clan leader, the clan **gains a claim**
  on that town.
- That external claim is **live for 2 years**. Within the window the clan may act on it — a landed clan by
  its own war, a vassal house by **petitioning the crown** to take it up (`DarbarPetitionBehavior`). Unacted,
  it lapses to nothing.
- AI clans run the same machinery (an AI lord dispatches a companion; same thresholds, same window), so
  AI-declared wars can be claim-backed too.

### Decay — the grudge
When a clan **loses** a fief, the claim is *not* erased: it is frozen at its accrued value and begins to
**decay** at `DecayPerYear` (0.5/yr — half the accrual rate, so a grudge outlives the holding that made it).
A claim that decays to zero is forgotten, and the conquest is legitimate at last.

This is the grudge engine: lose Bijapur after thirty years and your house carries a claim on it for decades,
and any king you serve may take that claim up as his casus belli.

### Seeding at 1707
No clan has tenure history at game start, so the ledger is **seeded**: every clan that holds a settlement at
world-gen receives a claim on it drawn from a **normal distribution over 0–10 years** (`ClaimMath.SeedYears`,
mean 5, σ 2.5, clamped). Historically-rooted holders therefore start with real, varied pretensions rather
than a flat zero, and the map has grudges to act on from the first campaign year.

### Ownership rules
- **A secession that graduates keeps its claims.** A breakaway that wins independence (`ThroneWar.Graduate`)
  retains its clans' claims on every fief left behind — a permanent, self-generating grudge on the map.
- **Rebel (`hind_rebel_*`) kingdoms accrue nothing while the throne war runs.** Those wars are binary and
  settle by their own deadlines; the claim layer never touches them (the same guard `WarExhaustionBehavior`
  already uses).

---

## 2. Claims under the two tenure laws

The resolved fork (this was the sharpest requirement conflict — a growing, heritable claim is a *feudal*
idea, and Mansabdari law exists to deny exactly that):

**Claims accrue identically under both laws. The law changes what the claim *does*.**

| | Feudal | Mansabdari |
|---|---|---|
| **Conquest awarded** | Directly to the clan whose claim was acted upon | Through the normal mansabdari channel (re-granted by rank) — the claim does not bind the crown |
| **Rotation** | n/a (fiefs are hereditary) | A high claim makes the holder **expensive to rotate** |

### Rotation friction
`MansabTenureMath.EnforcementInfluenceCost` gains a **claim multiplier**: the deeper a house's claim on the
fief it sits in, the more influence the crown must spend to move it on.

- The sovereign is **shown the price and chooses** — rotate this entrenched house, or leave it and spend the
  influence elsewhere. Rotation becomes a judgement, not an automatic tick.
- If the ruling clan's influence is **insufficient**, the rotation is **impossible**: the order cannot be
  issued, and the holder sits where he is. A house that has held Golconda for twenty years is, in practice,
  beyond the crown's reach — which is precisely how the empire actually decayed.

This makes Mansabdari law a *race*: rotate early and cheaply, or watch the jagirdars entrench past recall.

---

## 3. War records, aims and targets (rewrite of `WarfareBehavior` state)

### The data model
The current `Dictionary<otherKingdomId, goal>` with the player's realm implicit **cannot represent an
AI-vs-AI war, nor record who the aggressor was**. Replace with a per-war record covering *every* war on the map:

```
WarRecord { aggressorId, defenderId, aim, targetSettlementIds[], startDay, score }
```

A war has **one** aim, owned by the **aggressor**. The defender does not choose an aim — his aim is derivative
(§5). Save-compatible: on load, any war without a record is reconstructed with sensible defaults (aim inferred
from the aggressor's best claim, or `Chastisement` if none; targets empty; score from the legacy `hind_war_sVals`).

### The aims

| Aim | Targets | Complete when | Terms unlocked |
|---|---|---|---|
| **ProvincialConquest** | 1..n specific towns/castles the aggressor's clans hold claims on | **every target is held** by the aggressor | Annex the targets — nothing else |
| **Tribute** | none | war score ≥ `DecisiveScore` | Indemnity, tributary submission |
| **Chastisement** (punish) | none | war score ≥ `DecisiveScore` | Indemnity, surrender the culprit |
| **TotalSubjugation** | the enemy realm entire | see §4 | Absorb the whole realm |
| **Succession** | n/a — throne wars, untouched by this layer | | |

Aim now **gates the terms** (via the existing, tested `WarAimMath.Allows*`), replacing the score-only gate.
A war for Bijapur can take Bijapur — not a nazrana, not a random province.

### Casus belli — what licenses which aim
- **ProvincialConquest** — the aggressor's realm holds a clan claim on the target(s). *(Kingdoms act on their
  clans' claims; the fief goes to the claimant clan on victory — under Feudal law directly, under Mansabdari
  through the channel.)*
- **Chastisement** — an affront (the shipped `WarAimsBehavior` kidnap/caravan CB), **or a tributary withholding
  its nazrana** (new: a tributary that misses payment hands its overlord a CB).
- **Tribute** — always available, but a war without a CB carries an authority/legitimacy penalty for declaring
  it (naked aggression is *possible*, merely costly).
- **TotalSubjugation** — §4.

### Completion → the offer to end it
When a war's aim is **complete**, the aggressor is offered to **conclude it**: a **forced truce** on the loser.

- The truce is a real, dated bar on re-declaration (§6), not a suggestion.
- The loser's clans that lost fiefs **keep their claims** on them, now decaying — the grudge that fuels the
  next war. This is the loop the whole design exists to produce.

---

## 3a. War progress must be legible (`WarProgressMath` + the itemized ledger)

**The rule: every number the player sees must be able to explain itself.** A war carries a **progress bar**
(0–100% toward its aim), and **hovering it lists exactly what moved it, itemized.** If a contribution cannot
be named in the tooltip, it must not silently move the bar.

This is an architectural constraint, not a UI feature. The shipped `AddScore(id, delta)` accumulates an
anonymous float — a tooltip cannot be reverse-engineered out of it. **Scoring becomes an itemized ledger.**

### The ledger
Each war record carries a list of **contributions**:

```
Contribution { day, kind, delta, subjectId }   // kind: BattleWon, BattleLost, SiegeTaken, FiefTaken,
                                               // FiefLost, VillageRaided, KingCaptured, TargetTaken,
                                               // TargetRetaken, TributeWithheld, AffrontAvenged, TimeGrind
```

The bar is derived from the ledger; the ledger is never derived from the bar. Contributions are **rolled up by
kind** for display, so a ten-year war reads as *"Battles won ×14 → +140"*, not fourteen lines — but the raw
entries stay, so any figure can be audited. Cap the stored list (roll old entries into a per-kind running
total beyond ~200) so a long war can't bloat the save.

### Progress is computed *by the aim* — the bar means something different in each war
| Aim | Progress | The tooltip breaks down |
|---|---|---|
| **ProvincialConquest** | targets held ÷ targets total | each target by name and its state — *taken*, *still theirs*, *taken then lost back* |
| **Tribute / Chastisement** | war score ÷ `DecisiveScore` | the score ledger, rolled up by kind |
| **TotalSubjugation** | the *better* of the two paths (§4): collapse-gate readiness, or fiefs taken ÷ their fiefs total | **all three collapse conditions with live values** — is their king captured or dead? his legitimacy, to the point? what % of their lords stand with me? — *and* the fief count |
| **Defensive** | denial: 100% − the aggressor's progress toward his aim | the aggressor's aim named openly, and what he still needs |

So a defender's bar fills as he *denies*, and a subjugation bar honestly shows the player which of the two
roads is nearer. This is the same computation the AI resolves its wars on — one model, two consumers, no
chance of the bar disagreeing with the outcome.

### Where it lives
`Util/WarProgressMath.cs` — **pure and unit-tested**: takes the war record, its ledger and the live gate
inputs, returns `(percent, complete, List<(label, value, detail)>)`. The behaviour layer gathers inputs; the UI
renders. Because it is pure, the tooltip text is testable — which is the only way "the bar explains itself"
stays true after six more waves of tuning.

### The UI
A `HintViewModel` on the progress bar in the kingdom-tab war screen (§4's panel — the same mixin renders
both the subjugation conditions and this bar). The tooltip is the `WarProgressMath` breakdown, verbatim.

**Exhaustion gets the same treatment.** `WarExhaustionBehavior` already accrues from named sources
(casualties, fiefs lost, raids, daily creep) but throws the provenance away into one float. Hovering the
exhaustion figure should say *"Casualties 34 · Fiefs lost 16 · Villages raided 9 · The grind of time 12"*.
Same itemization, same pure-model treatment.

---

## 4. Total subjugation

Two paths, both surfaced to the player (they were previously invisible, which is half of why the system felt
arbitrary):

1. **Collapse (the default, existing gate).** `WarAimMath.SubjugationAllowed`: the enemy king is captured or
   dead **AND** his legitimacy < 60 **AND** ≥ 30% of his lords stand at ≥ +10 with the conquering king. The
   realm may be absorbed without taking every stone.
2. **Total conquest (new).** If the collapse gate is *not* met, subjugation is still possible — but only when
   **every last fief has fallen**. No shortcuts.

### The war screen must show this
Add to the **kingdom tab's war screen** (UIExtenderEx mixin on the vanilla diplomacy VM, following the
`ClanFiefsZamindariMixin` precedent) a live panel per war:
- the aim, the targets and which are taken;
- the three subjugation conditions with their **current state** — is their king captured or killed? what is
  his legitimacy, exactly? what percentage of their lords stand with me?

The player should never have to guess why an option is greyed out.

### Absorption, not destruction
When the last fief falls and the kingdom still exists: **transfer every clan to the subjugating kingdom**, then
retire the husk. Vanilla's `FactionDiscontinuationCampaignBehavior` would otherwise scatter them the moment
they hit zero settlements — so a realm mid-subjugation is **vetoed** through `CanKingdomBeDiscontinuedEvent`
(the hook `ClanSafetyNetBehavior` already owns).

The absorbed lords are **not** destroyed and do not leave. They carry a **−50 grudge, in the opinion ledger,
not raw relation** — a new `OpinionType.Subjugated`, magnitude −50, half-life ≈ 270 days, so it decays to
nothing over ~3–4 years exactly as specified, using the decay machinery `OpinionMath` already provides.

**Consequence, accepted deliberately:** `CivilWarMath.BidFires` and `DisaffectionBehavior` both read
`EffectiveOpinion`. Swallow a realm and its lords are, for the next few years, a slate of live civil-war
challengers and cabal recruits. A conquered empire *should* seethe. This is a feature; it is also the reason
subjugation must stay hard to reach.

---

## 5. Defensive wars

The defender never picks an aim — **his aim is to deny the aggressor's**.

- A defender "wins" by **holding**: the war is a defensive victory when the aggressor's aim becomes
  unachievable (his targets retaken, or his exhaustion spends him) — at which point the *defender* may
  dictate the truce.
- Retaking a target flips it out of the aggressor's completed set, so a conquest war can be un-won.
- This applies to **AI realms identically** — the record is per-war, not per-player, so an AI defender fights
  to deny and sues on the same rules.

---

## 6. Forced truce

A dated, enforced non-aggression bar (`truceUntil`) between two realms.

- **Enforcement:** a Harmony prefix on `DeclareWarAction.ApplyInternal`. `NoMughalCivilWarPatch` already
  prefixes that method — **merge both rules into one gatekeeper** (`WarDeclarationGate`) so the ordering is
  explicit rather than incidental.
- Breaking a truce (when it later lapses, or by an override) is legitimate; declaring *during* one is barred.
- Truces never bind a `hind_rebel_*` throne war.

### The Maratha war stops being unconditional
`FactionRelationsBehavior.EnsureWarsAndPeace` currently re-declares the Maratha–Mughal war on every
`ReassertStance()` — which would silently evaporate any truce the player wins. Change to: the war is declared
**once, at campaign start, when the empire is unified under Aurangzeb**, and thereafter the realms are free to
make war and peace like anyone else. Intra-Mughal peace enforcement is unaffected.

---

## 7. Build order

1. **`ClaimMath`** (pure, unit-tested): accrual, decay, seed distribution, agent relation pace, rotation-cost
   multiplier, claim comparison. No engine types.
2. **`ClaimsBehavior`**: the ledger (clan × settlement → strength, dated), seeding at world-gen, daily/weekly
   accrual + decay, ownership-change hooks. Console: `hindostan.claims`, `hindostan.grant_claim`.
3. **Tenure integration**: claim multiplier into `EnforcementInfluenceCost`; the sovereign's rotate/don't-rotate
   choice; the hard block when influence is short.
4. **`WarProgressMath`** (pure, unit-tested): the itemized contribution ledger, per-aim progress, completion,
   and the tooltip breakdown (§3a). Built *before* the behaviour so the rewrite has nowhere to hide an
   anonymous `+= delta`.
5. **`WarfareBehavior` rewrite**: `WarRecord` model, the contribution ledger replacing `AddScore`, aim-gated
   terms via `WarAimMath`, target tracking, completion detection, **the `ThroneWar.IsRebelKingdom` guard on
   `OnWarDeclared`** (kills the civil-war bug).
6. **`WarDeclarationGate`**: merged truce + Mughal-kinship prefix. Truce state, forced-truce application.
7. **Subjugation**: total-conquest path, discontinuation veto, clan absorption, `OpinionType.Subjugated`.
8. **The wakil (companion agent)**: town menu, weekly merchant cultivation, the 2-year external claim window,
   the crown petition. *De-risk first:* leaving a hero in a settlement outside a party is the API risk here.
9. **War screen UI**: the mixin — per war, the **progress bar with its hover breakdown**, the aim, the targets
   and their state, the live subjugation conditions, and the itemized exhaustion hint (§3a, §4).
10. **AI aim assignment (cheap path)**: let vanilla `DeclareWarDecision` declare as it does today, then assign
    the aim **post-hoc** in `OnWarDeclared` from the aggressor's best claim against the defender (falling back to
    Chastisement/Tribute). *Full claim-driven AI war scoring is deferred — see ROADMAP block E.3.*

## 8. Open risks

- **Save compatibility.** The `WarfareBehavior` state model changes shape. Legacy keys must load into
  reconstructed records rather than being dropped (§3).
- **Two prefixes on `DeclareWarAction.ApplyInternal`** — merge, don't stack.
- **Companion-in-settlement API** (`Hero.StayingInSettlement`) is the one genuinely unproven engine surface in
  this design. Console-test it in isolation before wiring the wakil into live claims, per the standing rule for
  hero/clan restructuring ([[Modding-Findings-Reference]]).
- **Mass subjugation → instant civil war** (§4) is intended, but wants a playtest pass to confirm it is drama
  and not farce.
- **The progress bar needs a prefab extension**, not just a mixin — vanilla's war item has nowhere to put a
  bar. UIExtenderEx prefab patching is proven here (`EncyclopediaHeroAkhbaarMixin` uses `Prefabs2`), but
  remember the standing trap: **GUI prefabs must be deployed game-side alongside the DLL** ([[module-deployment]]),
  and vertical stacks need `VerticalBottomToTop` (Modding-Findings ch.18).
- **The ledger must stay bounded.** A decade-long war accrues thousands of contributions; roll old entries
  into per-kind totals so the save cannot bloat (§3a).
