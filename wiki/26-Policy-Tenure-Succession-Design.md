# Policy, Tenure, Entrenchment & Succession — Design (NOT yet implemented)

Giving the realm's *constitution* real choices and consequences. Four interlocking subsystems:

- **A. Tenure policy** — Feudal (hereditary) ⇄ **Mansabdari** (Akbar's temporary, rotational,
  non-hereditary assignment).
- **B. Rusukh** — a holder's *local entrenchment* with the notables of his fief (a NEW stat,
  not vanilla relation): grows with tenure, lets a strong holder defy transfer orders.
- **C. Succession laws** — CK3-style: primogeniture, election, appointed Wali Ahd / Naib Wali Ahd.
- **D. Pretender's fate** — the new king's first act: pardon, banish, or execute the losers.

### How they interlock (the central tension)
The crown wants **Mansabdari (A)** so fiefs rotate on merit and no governor takes root. Powerful
governors accumulate **Rusukh (B)** and resist rotation — so the gold cost of imposing Mansabdari
is *proportional to the entrenched opposition (B)*. **Succession law (C)** decides who wields the
crown's authority at all; and when a reign changes hands, **pretender's fate (D)** resets the board.
Each leans on existing stats: `LegitimacyBehavior` (legitimacy), `ImperialAuthorityBehavior`
(authority), `MansabdariBehavior` (rank), `FeudalTitlesBehavior` (the tenure layer).

---

## A. Tenure Policy — Feudal ⇄ Mansabdari

**Today:** tenure is feudal — vanilla passes a town/castle to the clan heir when its leader dies.

**New:** a realm-level **edict** (set by the sovereign through the existing darbar/decree UI in
`ImperialCourtBehavior`) choosing the tenure law for the whole kingdom:

| Law | Behaviour |
|---|---|
| **Feudal** (default) | fiefs are hereditary; heir keeps them on death (vanilla). |
| **Mansabdari (Akbar)** | fiefs are crown grants tied to a *mansab*, not the bloodline: **non-hereditary** (revert to the crown on death/disgrace for reassignment) and **rotational** (the crown periodically issues transfer orders, moving a mansabdar to a different province). |

**Switching to Mansabdari costs** (the "influence their decisions" model):
- a **legitimacy floor** — `LegitimacyBehavior.GetLegitimacy(ruler)` must clear a threshold (only a
  secure throne can rewrite tenure);
- a flat **influence** cost;
- a **gold** cost **proportional to the opposition** — summed over the realm's nobles weighted by
  each one's power × how much he loses (i.e. his **Rusukh (B)** and his held fiefs). Entrenched
  magnates are expensive to buy off; a realm of rootless clients is cheap to convert.
- *Opposition that can't be bought* (very high Rusukh / very low relation) doesn't just add cost —
  it can refuse, pushing those clans toward an `AccessionWarBehavior` challenge or
  `RevoltCascadeBehavior` secession. Reform is genuinely risky.

**Once Mansabdari is active:**
- **On a holder's death:** instead of vanilla inheritance, the fief reverts to the crown and is
  re-granted (by mansab rank via `MansabdariBehavior` eligibility) — AI auto, player by petition.
- **Rotation orders:** on a clock (e.g. every few years, scaled by authority), the crown issues a
  **transfer order** (`RoyalFarmaan`) moving a mansabdar from fief X to fief Y. This is the hook
  Rusukh (B) plays against.
- Reverting to Feudal later is itself an edict (cheaper; the nobles *like* it).

*Implementation note / difficulty:* **Moderate–Hard.** Intercepting inheritance and moving real
vanilla ownership (`ChangeOwnerOfSettlementAction`) on a rotation clock is the heavy part; grant
plumbing already exists in `MansabdariBehavior`/`FeudalTitlesBehavior`. Keep all of it guarded and
save-safe (the mod's standard).

---

## B. Rusukh (رسوخ) — local entrenchment with the notables

A **new tracked stat**, deliberately *not* vanilla hero relation. Suggested name **Rusukh**
("established footing/influence"); per **(holder hero, fief)** pair, 0–100.

- **Growth:** rises slowly each season the holder keeps the fief, faster if his Steward/relation
  with the fief's notables is high. Represents marriages, patronage, and bonds with the local
  zamindars/notables (the `FeudalTitlesBehavior` notable layer already models them).
- **Decay (asymmetric):** when the holder no longer holds the fief, Rusukh **decays faster** than it
  grew — influence fades once you're gone. (Two separate MCM rates.)
- **Benefits scale with Rusukh** (the "backing" the notables provide):
  - **Influence** — a periodic influence trickle from notable support.
  - **Money** — seasonal financial backing (gold) from the local elite.
  - **Troops** — larger, *better-quality* village levies (ties into `FeudalTitlesBehavior` levy + the
    new troop trees) — local boys rally to an entrenched lord.
- **Defiance of transfer orders (the payoff):** when a Mansabdari rotation order (A) targets a
  holder, if his Rusukh is high enough he may **refuse**. Resolution = Rusukh vs the crown's
  authority×legitimacy. Outcomes form a risk ladder:
  1. **Reprimand** — relation/legitimacy hit, order stands.
  2. **Dismissal** — stripped of the fief (and a chunk of standing).
  3. **Declared a traitor** — the crown declares him a rebel: he secedes via
     `RevoltCascadeBehavior`/`AccessionWarBehavior` and it's **war**.
  - A holder with **low** Rusukh has no choice — he obeys the transfer.
- **Player & AI:** the player sees his Rusukh per fief in the fief menu and chooses whether to defy;
  AI governors defy based on Rusukh, ambition (personality), and the crown's strength.

*Implementation note / difficulty:* **Moderate.** It's a serialized `(heroId|settlementId)->float`
dict with daily/seasonal ticks and threshold effects, plus one decision point on transfer. Mostly
bookkeeping over existing systems.

---

## C. Succession Laws (per kingdom)

Replace the single vanilla heir rule with a **succession law** edict per kingdom, hooking
`SuccessionBehavior`'s claimant/heir selection:

| Law | Who inherits |
|---|---|
| **Male primogeniture (among princes)** | eldest son of the dynasty. |
| **Seniority / election among princes** | the dynasty's princes stand; lords vote (weighted by mansab, power, relation, Rusukh). |
| **Election among all lords** | any clan leader may be raised — an open magnate election (most "republican", least stable). |
| **Appointed Wali Ahd (crown prince)** | the ruler *names* his heir; optional **Naib Wali Ahd** (deputy) as fallback if the Wali Ahd predeceases or is unfit. |

- **Changing the law** is an edict gated by legitimacy + influence (like A); some transitions anger
  the princes (primogeniture → election) or the magnates (election → appointment).
- **Wali Ahd / Naib Wali Ahd** are explicit ruler actions (court menu); naming an heir raises that
  prince's claim-support in `SuccessionBehavior` and lowers rivals'.
- On a ruler's death the law drives `SuccessionBehavior`: primogeniture → near-automatic; the
  election laws → the existing claimant/support/civil-war machinery decides; appointed → the Wali
  Ahd accedes unless his support has collapsed (then crisis).

*Implementation note / difficulty:* **Moderate.** The crisis/claimant/support engine already exists;
this adds a law enum per kingdom that *selects the candidate pool and the resolution rule*, plus the
Wali Ahd appointment data + menu.

---

## D. The Pretender's Fate (Mughal accession tradition)

When a prince/challenger **becomes king** (via `SuccessionBehavior` resolution or a won
`AccessionWarBehavior`), his **first act** is to judge the defeated rivals (other claimants / the
deposed ruler's faction). For the player-as-new-king this is a `RoyalFarmaan` choice **per
pretender**; AI kings auto-decide by personality, threat, and legitimacy:

| Verdict | Effect |
|---|---|
| **Pardon** | the pretender lives and is reconciled (relation set neutral/positive). Magnanimous: small legitimacy gain, but a living rival may rise again. |
| **Banish** | he is stripped and **exiled with his wife and children as a NEW clan** that leaves the realm (wanders / may take service abroad or found a state). Removes the immediate threat without the stain of kinslaying. |
| **Execute** | `KillCharacterAction` (execution) — ends the threat permanently but costs legitimacy, scars relations with his kin/backers, and can seed a future blood-feud / revolt. |

*Implementation note / difficulty:* **Easy–Moderate.** Pardon (relation set) and Execute
(`KillCharacterAction.ApplyByExecution`) are easy; **Banish** (create a new clan, move the
pretender + spouse + children into it, remove from the kingdom) is the one moderate piece — clan
creation/transfer API, guarded.

---

## Cross-cutting concerns

**The cost engine (shared by A & C edicts).** A single helper:
`cost = f(baseInfluence, baseGold, opposition)` where `opposition = Σ over affected nobles of
(power × stake × (1 − relationFactor) × RusukhFactor)`. Legitimacy acts as a *gate* (below the floor
the edict is unavailable) and a *discount* (high legitimacy lowers the buy-off). This makes B feed
directly into A's price, exactly as asked.

**AI.** AI rulers adopt Mansabdari when authority+legitimacy are high and the realm is fragmented;
AI governors accumulate/spend Rusukh; AI kings pick succession laws by dynasty size and judge
pretenders by personality (Mercy/Calculating traits, already used by `Util.Biographies.Temper`).

**Save-safety & robustness.** All new state in parallel-list `SyncData` (the engine can't serialize
dictionaries — see existing behaviors). Every cross-object access guarded with `TYTLog` breadcrumbs;
never act on a dead hero / destroyed kingdom / stale fief (the native-crash lesson).

**MCM tunables (`Config/Tune` + `TYTSettings`).** Rusukh growth & (faster) decay rates; Rusukh
benefit magnitudes (influence/gold/levy); Mansabdari rotation interval; legitimacy floors and
influence/gold base costs for each edict; defiance success curve; execution legitimacy penalty.

**UI.** Reuse the darbar/decree menus (`ImperialCourtBehavior`) for edicts; the fief menu
(`VillageDevelopmentBehavior`/court) for Rusukh display & defiance; `RoyalFarmaan` for transfer
orders, traitor declarations, accession judgements.

---

## Open questions for the user

1. **Scope of Mansabdari rotation:** rotate **all** fiefs (towns, castles, villages), or start with
   **villages/castles** only (lower blast radius, easier first cut)?
2. **Who can set tenure/succession law** — only the emperor for the whole empire, or each sovereign
   kingdom independently (so Mysore could be feudal while the Mughals go mansabdari)?
3. **Rusukh vs vanilla relation:** keep entirely separate (recommended), or have high Rusukh also
   nudge vanilla relation with the notables?
4. **Player-side framing:** should the *player* (as a governor) be the one resisting transfers as a
   core power-fantasy, or is this mainly an AI-realm simulation the player observes and occasionally
   participates in?
5. **Banishment destination:** exiled pretenders wander as a minor clan, or actively seek service
   with a rival kingdom (raising the stakes of pardon-vs-banish)?
6. **Build order:** which subsystem first? Suggested **B (Rusukh) → A (Mansabdari) → C (succession)
   → D (pretender's fate)**, since A's cost model and rotation-defiance depend on B existing.
