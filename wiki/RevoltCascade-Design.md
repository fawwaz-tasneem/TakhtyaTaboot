# Revolt Cascade — Design Specification
## For implementation (decisions locked 2026-06-13)

The keystone that turns the political meters into a visible collapse: mismanagement →
provincial unrest → rebellion → provisional rebel kingdoms that either **consolidate into
permanent states** or are **crushed** (rebel lords imprisoned, the most hostile executed).
This is the 1719 fragmentation — Bengal, Awadh, Hyderabad, the Jat/Sikh/Maratha risings.

It consumes systems already built: `ImperialAuthorityBehavior`, `LegitimacyBehavior`,
`FiefHierarchyBehavior` (feudal standing), `VillageDevelopmentBehavior` (bandit threat),
`ReligionBehavior`. Communications use the Royal Farmaan (`RoyalFarmaan`).

---

## 1. Locked design decisions

- **Outcome model — provisional rebel kingdoms.** When a revolt fires, the disloyal clan
  secedes into a **real but provisional** `Kingdom` at war with its parent.
  - If **not crushed** within a consolidation window → the rebel kingdom becomes
    **established** (permanent, recognized; provisional flag cleared).
  - If **crushed** (loses all its settlements, or its leader is captured) → the rebel
    kingdom is **dissolved**; all rebel lords are **captured and imprisoned**; rebel lords
    whose relation to the sovereign is very low (≤ −30) may be **executed**.
- **Player as emperor** — when a province brews revolt, a Farmaan offers a response:
  **muster an army**, **grant autonomy** (trade Imperial Authority for instant calm), or
  **ignore it**. On crushing a rebellion, a Farmaan lets the player decide the rebel lords'
  fate (imprison vs. execute the worst), reusing the succession kill/banish/pardon pattern.
- **Player as vassal** — the player can **raise the standard of revolt**: secede with their
  own fiefs into a provisional rebel kingdom they lead, then must survive to consolidate.

---

## 2. Diplomacy mod integration (playable wars)

**Goal:** revolts and the wars they spawn must be fully playable. The clean way is to make
rebel factions **real `Kingdom` objects** — then all war/peace systems (vanilla *and* the
Diplomacy mod) treat them as first-class belligerents automatically.

- **Diplomacy is NOT a hard dependency.** It is not currently installed, though its full
  stack is (`Bannerlord.ButterLib`, `Bannerlord.UIExtenderEx`, `Bannerlord.MBOptionScreen`,
  `Bannerlord.Harmony`). The system works standalone: real rebel kingdoms mean the player
  can fight them and, as a faction leader, make peace through vanilla.
- **With Diplomacy installed**, the player gets the rich playable layer for free — declare
  war/peace as influence-spending vassal, negotiate terms, war exhaustion, fiefs/tribute in
  treaties — applied to rebel kingdoms because they are ordinary kingdoms.
- **Soft load-order only.** In `SubModule.xml` add `<DependedModuleMetadatas>` with
  `<DependedModuleMetadata id="Diplomacy" order="LoadBeforeThis" optional="true" />` so we
  load after Diplomacy when present but never require it.
- **Prerequisite for the user:** install Bannerlord **Diplomacy** (Nexus/Steam Workshop) to
  get the full playable war/peace UI. Recorded in MEMORY.

---

## 3. Revolt pressure model

`RevoltCascadeBehavior` tracks **pressure (0–100) per settlement** (towns + villages).

### Weekly accrual (per settlement)
| Source | Δ/week | Reads |
|--------|--------|-------|
| Imperial Authority < 25 (realm-wide) | +5 | `ImperialAuthorityBehavior.GetAuthority` |
| Imperial Authority < 50 | +2 | " |
| Owning clan disloyal (relation to ruler ≤ −20) | +4 | `CharacterRelationManager` |
| Owner in poor feudal standing | +3 | `FiefHierarchyBehavior.GetDaysInPoorStanding` |
| Village bandit threat > 70 | +2 | `VillageDevelopmentBehavior.GetThreat` |
| Religious mismatch (settlement culture faith ≠ ruling clan faith) | +3 | `ReligionBehavior.GetReligion` |
| Ruler legitimacy < 40 | +2 | `LegitimacyBehavior.GetLegitimacy` |
| Kingdom fighting ≥ 2 wars (armies away) | +2 | `FactionManager`/`IsAtWarWith` |
| **Mitigators** | | |
| Strong garrison (> 50 strength) | −4 | `settlement.Town.GarrisonParty` |
| Owner/player party present in settlement | −5 | `MobileParty.CurrentSettlement` |
| Ruler legitimacy ≥ 70 | −2 | `LegitimacyBehavior` |

Clamp 0–100. (Wiki's famine/disease sources are stubbed — `FoodSecurityBehavior` /
`EpidemicBehavior` don't exist yet; add their hooks when built.)

### Thresholds
- **≥ 50** — first crossing: Farmaan warning to the emperor-player ("the country around X
  seethes with unrest").
- **≥ 80** — each week, 12% chance the settlement's region **ignites** (see §4). Higher if
  the owning clan is strongly disloyal.

---

## 4. Rebellion lifecycle

### Ignition → provisional rebel kingdom
1. Pick the **rebel leader**: the disloyal owning clan's leader (or a regional notable if
   the owner is loyal — a popular/peasant revolt led by a spawned clan via
   `Clan.CreateSettlementRebelClan(settlement, hero, troopCount)`).
2. Create the kingdom:
   ```csharp
   var k = MBObjectManager.Instance.CreateObject<Kingdom>("rebel_" + settlement.StringId);
   k.InitializeKingdom(name, informalName, culture, banner, color, color2,
                       initialHomeSettlement, rulerTitle, /*text*/ null, /*…*/ null);
   ChangeKingdomAction.ApplyByCreateKingdom(rebelClan, k, showNotification: true);
   ```
   Mark provisional: track `_provisionalUntilDay[k.StringId]` (e.g. now + 60 days).
3. War with the parent kingdom is automatic (rebellion). Confiscated settlement(s) go to the
   rebel clan. Authority of the parent: **−5** per settlement lost (feedback loop).
4. **Farmaan to the emperor-player** (if it's their realm): *muster army* / *grant autonomy*
   (recognise the breakaway now: parent Authority −10 but the rebel kingdom is immediately
   established and at peace) / *ignore*.

### Consolidation (not crushed)
- Each daily tick, if a provisional rebel kingdom still holds ≥ 1 settlement at
  `_provisionalUntilDay`, it becomes **established**: clear the provisional flag, Farmaan/
  notification ("the breakaway state of X is now a recognised power"), small Legitimacy hit
  to the parent ruler.

### Crushing
- A provisional rebel kingdom is **crushed** when it holds 0 settlements OR its leader is
  captured. On crush:
  - `KillCharacterAction`/imprison every rebel clan leader: `TakePrisonerAction.Apply(captor,
    rebelLord)` — captor = the parent ruler's party or the settlement that retook it.
  - For rebel lords with relation ≤ −30 to the sovereign → eligible for **execution**.
  - If the **player is the sovereign**, a Farmaan presents the captives' fate
    (`MultiSelectionInquiryData`: imprison all / execute the ringleaders / pardon) — reuse
    `SuccessionBehavior`'s deposed-fate pattern.
  - Dissolve the rebel kingdom (`DestroyKingdomAction.Apply` if available, else leave empty
    and eliminated). Parent Authority **+8** (order restored).

### Player-led revolt
- Menu option + console `hindostan.secede`: if the player holds ≥ 1 fief and is not the
  ruler, they secede into a provisional rebel kingdom they lead (same creation path, player
  clan as rebel clan). They then live the same consolidate-or-be-crushed timer. With
  Diplomacy installed, they negotiate their own survival/peace through its UI.

---

## 5. Technical reference (verified APIs, v1.4.6)

```csharp
// Create a kingdom at runtime
Kingdom k = MBObjectManager.Instance.CreateObject<Kingdom>(stringId);
k.InitializeKingdom(TextObject name, TextObject informalName, CultureObject culture,
                    Banner banner, uint color, uint color2, Settlement initialHomeSettlement,
                    TextObject rulerTitle, TextObject t1, TextObject t2);

// Secede / form rebel kingdom
ChangeKingdomAction.ApplyByCreateKingdom(Clan clan, Kingdom newKingdom, bool showNotification);
ChangeKingdomAction.ApplyByLeaveWithRebellionAgainstKingdom(Clan clan, bool showNotification);

// Vanilla settlement rebel clan (peasant/popular revolt)
Clan rebel = Clan.CreateSettlementRebelClan(Settlement s, Hero leader, int seed);

// Crush consequences
TakePrisonerAction.Apply(PartyBase capturerParty, Hero prisoner);
KillCharacterAction.ApplyByExecution(Hero victim, Hero executer, bool showNotification, bool isForced);

// Reads
ImperialAuthorityBehavior.Instance.GetAuthority(kingdom) / ModifyAuthority(k, d, why)
LegitimacyBehavior.Instance.GetLegitimacy(ruler) / ModifyLegitimacy(...)
FiefHierarchyBehavior.Instance.GetDaysInPoorStanding()
VillageDevelopmentBehavior.Instance.GetThreat(settlement)
ReligionBehavior.Instance.GetReligion(hero)
RoyalFarmaan.FromRuler(kingdom, title, body, primary, onPrimary, secondary, onSecondary)
```

**Confirm at build time** (not yet reflected): `DestroyKingdomAction` existence/signature;
exact `InitializeKingdom` argument meanings (informal name, the trailing TextObjects);
whether `Banner.CreateOneColoredBannerWithOneIcon` or copying the rebel clan's banner is the
simplest banner source. A rebel kingdom can reuse the rebel clan's culture, banner, and
colours to avoid asset work.

---

## 6. Files

| File | Change |
|------|--------|
| `Behaviours/RevoltCascadeBehavior.cs` | **New** — full system |
| `RevoltCheats.cs` | **New** — `set_revolt_pressure`, `trigger_revolt`, `list_unrest`, `secede`, `crush` |
| `HindostanSubModule.cs` | register `RevoltCascadeBehavior`; uncomment Phase 6 |
| `SubModule.xml` | optional soft dependency on `Diplomacy` (LoadBeforeThis, optional) |

No new XML data or prefabs (uses Farmaan UI + `MultiSelectionInquiryData`).

---

## 7. Cross-system interactions (the cascade)

```
Authority < 25 ─► revolt pressure +5 realm-wide ─► ignition ─► settlement lost ─► Authority −5 ─► (loops)
Legitimacy < 40 ─► +pressure, and rebel kingdoms more likely to consolidate
Feudal poor standing / defied summons / withheld tribute ─► owner disloyal ─► +pressure on their fiefs
Village bandit threat (unpatrolled) ─► +pressure
Religious mismatch ─► +pressure (Hindu/Sikh provinces under a strict Muslim ruler, etc.)
Succession crisis (Authority −20) ─► spikes revolt risk during a War of Princes
Crushed rebellion ─► Authority +8, captives judged ─► deters further revolt
```
