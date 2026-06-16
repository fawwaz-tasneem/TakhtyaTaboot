# War of Princes — Succession Crisis System
## Design Specification — IMPLEMENTED 2026-06-13

**Status: built and compiling.** `Behaviours/SuccessionBehavior.cs` implements the full
system below; `SuccessionCheats.cs` adds console commands for testing. Registered in
`HindostanSubModule.OnGameStart`. No new XML data files or Harmony patches were needed —
all in-world text uses inline strings, sidestepping the inner-key/outer-id localization trap.

### Testing via the developer console (enable cheats, press tilde `~`):
- `hindostan.force_succession_crisis` — force a crisis in the player's kingdom
- `hindostan.force_succession_crisis empire` — force one in a named kingdom (StringId)
- `hindostan.succession_status` — list every active crisis and live support %
- `hindostan.advance_succession 30` — fast-forward active crises N days (runs the real state machine)

A crisis needs **≥2 eligible agnatic claimants** (sons/brothers/nephews/clan-leader). If the
ruling dynasty has only one heir, the throne passes quietly with no crisis — as designed.

---

## Original Design Specification

---

## 1. Historical Context

The year is 1719. The Mughal emperor Farrukhsiyar has just been blinded and deposed by the Sayyid Brothers, kingmakers who installed three puppet emperors in quick succession that same year. This is the **defining crisis** the mod is built around. A Mughal emperor who is weak, illegitimate, or dies without a secure heir triggers a cascade: princes contest the throne, lords pick sides, and the empire fractures — or consolidates under a strong successor.

**Key historical figures modelled by the system:**
- The Sayyid Brothers (Hussain Ali Khan Barha + Abdullah Khan Barha) — kingmakers; the Amir-ul-Umara rank holders who actually control succession
- Ahmad Shah Durrani — Afghan invader who exploits Mughal succession crises
- Maratha Peshwas — benefit from Mughal fragmentation

---

## 2. Feature Overview

A **Succession Crisis** triggers when specific conditions are met. Once triggered, claimant factions form, lords align, and the crisis resolves through civil war or brokered succession. The system reads Imperial Authority and Legitimacy meters built in previous phases.

---

## 3. Trigger Conditions

A succession crisis begins when ANY of these happen to a kingdom:

| Trigger | Threshold |
|---------|-----------|
| Ruler dies with legitimacy < 60 | (OnHeroDied event) |
| Ruler dies with no direct male heir alive | (OnHeroDied event) |
| Imperial Authority collapses below 15 while legitimacy < 40 | (weekly tick) |
| Ruler is captured and held prisoner for > 30 days with authority < 30 | (daily tick) |

Only one crisis per kingdom at a time. If the kingdom has no living ruler (eliminated), skip.

---

## 4. Claimant Selection

When a crisis triggers, enumerate **claimants** from the ruling clan's eligible heirs:

```
Priority order:
1. Living sons of the current/last ruler (eldest first by age)
2. Living brothers of the ruler
3. Nephews (sons of brothers)
4. Clan leader of the ruling clan (if none of the above)
```

Cap at 3 claimants. If 0 found → no crisis, just a quiet succession to clan leader.

Each claimant gets a **starting support score (0-100)**:
- Eldest son: 60 base
- Other sons: 45 − (5 × birth_rank) base
- Brothers: 30 base
- Nephews: 20 base
- Clan leader fallback: 40 base

Modifiers:
- +10 if claimant has relation > 20 with the player kingdom's top Amir
- +15 if claimant's clan has mansabdari rank ≥ Subahdar
- −10 per war the kingdom is currently losing
- +5 per town they personally own

---

## 5. Lord Alignment

Each non-ruler, non-minor lord in the crisis kingdom aligns to a claimant or stays neutral:

```
Alignment weight for lord L toward claimant C =
  base_relation(L, C)                          // Hero.GetRelation
  + religion_bonus (same religion = +15)       // ReligionBehavior.GetReligion
  + rank_bonus (lord's mansabdari rank × 3)    // MansabdariBehavior
  + random_noise (−10 to +10)                  // MBRandom
```

Lord aligns to the claimant with highest weight. If max_weight < 0, lord is neutral.

Alignment is stored as: `_lordAlignment[heroId] = claimantHeroId`

---

## 6. Crisis States

```
enum CrisisState { None, Brewing, Active, CivilWar, Resolved }
```

**Brewing** (days 0–21): Lords quietly align. Player informed "whispers of a succession dispute." No military action yet.

**Active** (days 22–60): Factions declared. Player can intervene. Kingmaker action available. Each claimant faction gains/loses support weekly.

**CivilWar** (if no claimant reaches 55% support by day 60): Top two claimant factions go to war. Implemented by making their clans hostile to each other via `DeclareWarAction`. The kingdom stays one entity on the map — this is an **internal war** flag, not a full kingdom split. The winning side is determined when one claimant captures/kills the other, OR accumulates 70% lord support.

**Resolved**: Winning claimant is crowned. `ChangeKingdomAction` sets new ruler. Losing claimants: exiled (clan sent neutral) or killed (if captured during civil war). Authority penalty: −20 on crisis resolution (legitimacy crisis leaves scars). New ruler starts with legitimacy = 45 + (support_percentage × 0.4).

---

## 7. Kingmaker Mechanic (Amir-ul-Umara only)

The lord holding Amir-ul-Umara rank (mansabdari rank 6) can **broker a succession** during the Active phase, ending the crisis without civil war:

**Conditions to broker:**
- Kingmaker must have influence ≥ 200
- One claimant must have ≥ 45% lord support
- It must be the Active phase (days 22–60)

**Broker action (game menu option in capital city):**
- Costs 200 influence
- The leading claimant is immediately crowned
- Kingmaker gains +15 relation with new ruler, +20 legitimacy for their own kingdom
- Authority gains +10

If the **player** is the Amir-ul-Umara, this is a player-triggered menu option.
If an AI lord is the Amir-ul-Umara, they broker automatically if conditions are met by day 45.

---

## 8. Player Interaction

Three game menu options appear in capital settlements during a crisis:

1. **"Back [Claimant Name]"** — Spend 50 influence; adds +10 support to that claimant; grants +5 relation with claimant's clan
2. **"Broker the Succession"** (Amir-ul-Umara only) — see §7
3. **"Observe the crisis"** — Tooltip showing current support percentages per claimant

Menu ID: `hindostan_succession_crisis`
Available condition: crisis is Active or Brewing, settlement is kingdom capital

---

## 9. Effects on Other Systems

| Effect | Detail |
|--------|--------|
| Authority drain | −3/week during Brewing; −5/week during CivilWar |
| Legitimacy | All claimants start at 40; new ruler gets boosted on resolution |
| Mansabdari | Ranks frozen during CivilWar (no demotions/promotions while war active) |
| AI kingdoms | Non-crisis kingdoms gain Authority +1/week if neighboring a crisis kingdom (stability premium) |

---

## 10. Technical Implementation Plan

### New file: `Behaviours/SuccessionBehavior.cs`

**Class fields (SyncData):**
```csharp
// Parallel lists for serialization (engine can't serialize Dictionary<string,T>)
List<string>    _crisisKingdomIds     // kingdoms currently in crisis
List<int>       _crisisStates         // CrisisState enum as int, parallel to above
List<float>     _crisisDays           // how many days since crisis started
List<string>    _claimantIds          // all active claimant hero ids (comma-joined per kingdom)
List<string>    _lordAlignmentHeroIds // parallel lists for lord alignment
List<string>    _lordAlignmentTargets
List<string>    _supportHeroIds       // parallel lists for claimant support scores
List<float>     _supportScores
```

**Events to register:**
```csharp
CampaignEvents.OnHeroKilled                  // detect ruler death
CampaignEvents.DailyTickHeroEvent            // prisoner duration check
CampaignEvents.WeeklyTickEvent               // crisis progression, support drift
CampaignEvents.OnGameMenuOpenedEvent         // inject menu options
CampaignEvents.OnNewGameCreatedEvent         // init
CampaignEvents.OnSessionLaunchedEvent        // restore from save
```

**Key Bannerlord APIs:**
```csharp
// Finding heirs
hero.Children                                // IEnumerable<Hero>
hero.Clan.Heroes                             // all clan members
Hero.MainHero.Clan.Kingdom.Leader            // current ruler

// Alignment / war
DeclareWarAction.Apply(IFaction, IFaction, DeclareWarDetail.Default)
MakePeaceAction.Apply(IFaction, IFaction)
ChangeKingdomAction.ApplyByLeaveByKingdomDecision(clan, false)

// Support/influence
ChangeClanInfluenceAction.Apply(clan, delta)
Hero.GetRelation(Hero other)                 // int relation score

// Menus
GameMenu.ActivateGameMenu("hindostan_succession_crisis")
campaignGameStarter.AddGameMenu(...)
campaignGameStarter.AddGameMenuOption(...)
```

**Cross-system reads:**
```csharp
ImperialAuthorityBehavior.Instance.GetAuthority(kingdom)
ImperialAuthorityBehavior.Instance.ModifyAuthority(kingdom, delta, reason)
LegitimacyBehavior.Instance.GetLegitimacy(ruler)
LegitimacyBehavior.Instance.SetLegitimacy(newRuler, startValue)
MansabdariBehavior.Instance.GetRank(clan)    // for alignment weight
ReligionBehavior.Instance.GetReligion(hero)  // for alignment weight
```

---

## 11. Files to Create / Modify

| File | Change |
|------|--------|
| `Behaviours/SuccessionBehavior.cs` | **New file** — full implementation |
| `HindostanSubModule.cs` | Uncomment `starter.AddBehavior(new SuccessionBehavior())` |
| `ModuleData/Languages/std_module_strings_xml.xml` | Add succession notification strings |

No new XML data files needed. No Harmony patches needed (pure campaign behavior).

---

## 12. String Keys Needed (add to std_module_strings_xml.xml)

```xml
<string id="succession.crisis.brewing"    text="Whispers fill the court of {KINGDOM}. The succession is not secure." />
<string id="succession.crisis.active"     text="A succession crisis grips {KINGDOM}. The throne is contested." />
<string id="succession.crisis.civilwar"   text="Civil war! The great lords of {KINGDOM} take up arms for rival claimants." />
<string id="succession.crisis.resolved"   text="{WINNER} is crowned. The succession crisis in {KINGDOM} is over." />
<string id="succession.menu.back"         text="Pledge your support to {CLAIMANT} (+10 support, costs 50 influence)" />
<string id="succession.menu.broker"       text="Broker the succession in favour of the leading claimant (costs 200 influence)" />
<string id="succession.menu.observe"      text="Observe the state of the crisis" />
<string id="succession.observe.line"      text="{CLAIMANT}: {SUPPORT}% lord support" />
```

---

## 13. Out of Scope (Next Phase)

- Kingdom splitting: the losing claimant actually forming a rebel faction that secedes → Phase 6 (Revolt Cascade)
- Foreign intervention: Afghan/Maratha player exploiting the crisis with a special diplomatic option → Phase 5.5
- Player can BE a claimant (if player is from ruling clan) → Phase 5.5
