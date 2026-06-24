# War Aims, Casus Belli & Claimant Wars — Design

The vision: wars are fought for declared REASONS, and the reason constrains how the war ends. Succession
crises produce real, claimant-led breakaway states that either re-merge or permanently split the realm.

## Pillar 1 — Claimant wars with REAL temporary clans (replaces the interim "champion" model)

Today (interim, shipped): a succession `CivilWar` spawns a breakaway kingdom led by each rival claimant's
strongest *separate backer house* (champion). Safe (no clan creation) but the prince doesn't lead his own war.

Target: each non-ruling claimant who CHOOSES to fight gets a **temporary cadet clan** split off the dynasty,
which founds a breakaway kingdom (as today). A claimant may decline (stays a peaceful court claimant).

- **Create:** `Clan.CreateClan(id)` + set name/culture/banner (setters exist) + move the prince via the
  `Hero.Clan` setter. RISK: `Clan` keeps a maintained `_heroes` list and there is no clean public
  "move hero between clans" action — clan restructuring is the documented crash class
  ([[Modding-Findings-Reference]] line 120). MUST be de-risked with an isolated console test
  (create → verify → dissolve) BEFORE wiring into live succession.
- **Resolve / merge-back:** on win the victor's cadet clan is crowned and all temp clans dissolve back into
  the realm (`DestroyClanAction`, heroes returned to the dynasty). On a partial outcome the realm can
  **stay split** (a pretender keeps the provinces he holds as a permanent new kingdom). "Handle carefully":
  every create/move/dissolve guarded; never during world-gen (parallel — see [[parallel-facegen-harmony-race]]).

## Pillar 2 — War aims / casus belli (pure rules DONE in `Util/WarAimMath.cs`, tested)

A war's **aim** is fixed at declaration and gates its resolution:

| Aim | How it can end |
|---|---|
| **ProvincialConquest** | annex only the contested fief(s) |
| **Tribute** | one-off reparation and/or seasonal tribute |
| **Revenge** (affront) | reparation OR surrender the culprit for judgement |
| **TotalSubjugation** | absorb the ENTIRE realm — gated (below) |
| **Succession** | resolved by the succession system (Pillar 1) |

- **TotalSubjugation gate** (`WarAimMath.SubjugationAllowed`): losing king captured-or-killed **AND**
  legitimacy < 60 **AND** victor holds ≥ +10 relation with ≥ 30% of the realm's lords.
- **Revenge judgement** (`WarAimMath.AvailableVerdicts`): Pardon / Fine / Imprison (2–5 yrs, clamped) /
  Execute. An imprisoned lord's clan wanes (`ImprisonedClanStrengthFactor`, applied as influence/recruit drag).

## Pillar 3 — Trait-driven affronts that grant casus belli (behavior, TODO)

Random incidents create a Revenge casus belli, weighted by lord personality (Mercy/Honor/Calculating —
already surfaced via `Util.Biographies.Temper`):
- kidnapping a member of another kingdom; looting a caravan; border raids.
The wronged kingdom gains a justified war; resolution per Pillar 2 (reparation or surrender the culprit).

## Build order
1. **DONE** — `WarAimMath` pure model + 15 xUnit tests.
2. **DONE** — De-risk temp clans: `hindostan.test_tempclan` create (+`ClaimantClan.SetLeader/home/init`);
   create + encyclopedia render confirmed in-game. `hindostan.test_tempkingdom[_dissolve]` added for the
   breakaway-kingdom lifecycle.
3. **DONE** — Succession swapped to claimant-led hosts in `SuccessionBehavior.StartCivilWar`:
   - A prince who leads his own landed house secedes with it; a landless prince (e.g. the king's son)
     gets a **temporary cadet clan** (`Util.ClaimantClan`) that founds the breakaway kingdom.
   - `WillFight` lets a weak claimant decline and stay a court claimant.
   - **Player claimant** is prompted to *Raise Your Banner* (his own clan secedes) or hold at court.
   - `FoldBack` merges a realm back: backers rejoin with fiefs; a cadet clan's conquests pass to the
     throne, then the cadet dissolves into the dynasty. An embittered surviving pretender = **permanent split**.
4. **DONE (player realm)** — aim-gated peace terms in `WarfareBehavior.OfferTerms/ApplyTerms`:
   **Total subjugation** (annex the whole realm) appears only when `WarAimMath.SubjugationAllowed`
   (king fallen + legitimacy < 60 + ≥30% of their lords at ≥+10 to you); **Surrender the culprit**
   appears when a casus belli exists.
5. **DONE** — `Util.WarAimsBehavior`: weekly trait-weighted affronts (kidnap / caravan loot) grant a
   year-long **casus belli**; `JudgeCulprit` runs the verdict (Pardon / Fine / Imprison 2-5 yrs with a
   weekly clan-influence drain until release / Execute) via the tested `WarAimMath`.

## Scope note (resolved)
War aims are surfaced through the player realm's existing peace-terms UI (`WarfareBehavior`) rather than a
full hook on every AI `DeclareWarAction`, to avoid conflicting with vanilla/Diplomacy AI peace. Affronts
are generated map-wide (any kingdom can wrong any other) but consequences are realised when the player's
realm is a party. A future pass could extend aim-gated terms to pure AI-vs-AI wars if desired.
