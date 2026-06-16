# Chapter 15 — Lord Progression and the Mansabdari System

> A complete career system for the player: from landless mercenary to town lord to council member, modelled on the Mughal mansabdari and adapted for all eight factions.

**[← Chapter 14](14-Feudal-Hierarchy.md)** | **[Home](Home.md)**

---

## Contents

- [Correcting Chapter 14 — what lords are NOT](#correcting-chapter-14--what-lords-are-not)
- [The fief holder system](#the-fief-holder-system)
- [Historical mansabdari — what it actually was](#historical-mansabdari--what-it-actually-was)
- [Mansabdari as a game system](#mansabdari-as-a-game-system)
- [Career progression path](#career-progression-path)
- [Village lord responsibilities](#village-lord-responsibilities)
- [Village development menu](#village-development-menu)
- [Bandit threat system](#bandit-threat-system)
- [Call to arms](#call-to-arms)
- [Valour — the new metric](#valour--the-new-metric)
- [Performance evaluation and dismissal](#performance-evaluation-and-dismissal)
- [Getting a fief — the player's first assignment](#getting-a-fief--the-players-first-assignment)
- [Implementation: FiefRecord and FiefHolderBehavior](#implementation-fiefrecord-and-fiefholderbehavior)
- [Implementation: VillageDevelopmentBehavior](#implementation-villagedevelopmentbehavior)
- [Implementation: CallToArmsBehavior](#implementation-calltoarmsbehavior)
- [Implementation: ValourBehavior](#implementation-valourbehavior)
- [Cultural variants](#cultural-variants)

---

## Correcting Chapter 14 — What Lords Are NOT

[Chapter 14](14-Feudal-Hierarchy.md) made two errors. They are corrected here:

### Error 1 — Village Notables are not lords

Village **Notables** (headmen, landowners) are commoner NPCs. They:
- Have no clan
- Cannot lead armies
- Cannot own property in any legal sense
- Are quest-givers and recruitment sources, nothing more

A **Headman** notable is the village elder — the player speaks with him. He is not the lord. The lord is a **noble hero** who owns the village as a fief. These are different things.

### Error 2 — Settlement.Governor is not the lord

The `Governor` system in Bannerlord is a **settlement management role** — the hero assigned there gets perk bonuses affecting food, security, and prosperity. It is NOT a political ownership title.

In a castle, the `Governor` is the administrator. The *lord* who politically owns the castle is separate. A powerful lord might appoint a trusted companion as governor while he himself commands an army elsewhere.

### The corrected model

We maintain our own separate fief ownership record:

```
settlement.Governor  →  the administrator (vanilla, unchanged)
FiefRecord.Holder    →  the noble lord who politically owns the fief (our system)
settlement.Notables  →  commoner notables, unchanged
```

For the player: they are the `FiefRecord.Holder` of their assigned village. They are NOT the `Governor` (they are out fighting bandits and answering calls to arms). The village Headman notable manages day-to-day life.

---

## The Fief Holder System

Each settlement has at most one noble fief holder tracked in our system:

```csharp
public class FiefRecord
{
    public string SettlementId;
    public Hero   Holder;               // the noble lord who owns this fief
    public float  TaxRateToLiege;      // fraction of income paid upward (0.0–1.0)
    public int    DaysInPoorStanding;  // counter toward dismissal
    public int    DaysCallToArmsActive;// 0 if no active call, else countdown
    public bool   CallToArmsAnswered;  // did the holder respond this call?
}
```

The liege is NOT stored in FiefRecord — it is derived from the settlement hierarchy exactly as described in Chapter 14:
- Village lord's liege = `FiefRecord.Holder` of `Village.Bound` settlement
- Castle lord's liege = `FiefRecord.Holder` of the nearest town (via CastleToTown map)
- Town lord's liege = Kingdom.Leader

For the player:
```csharp
string _playerFiefId;  // null = player has no fief
```

The player's current rank, their obligations, and their valour are tracked separately in `ValourBehavior` and `MansabBehavior`.

---

## Historical Mansabdari — What It Actually Was

The mansabdari was created by Akbar around 1570 and formed the backbone of Mughal administration until the empire's collapse.

**Zat and Sawar:**
Every noble held two numbers. The *zat* was his personal rank (500 zat, 1000 zat, etc.) and determined his salary. The *sawar* was how many cavalry he was obligated to maintain for imperial service. A "2000/1500 mansabdar" had personal rank 2000 and maintained 1500 cavalry.

**The jagir:**
The salary was almost never paid in cash. Instead, the Emperor assigned a *jagir* — a territory whose tax revenues were earmarked for the mansabdar. He collected them directly. The jagir was not a personal possession; it was a revenue assignment. The Emperor could move, shrink, or revoke it at will.

**By 1719 — the breakdown:**
By our setting, the system was in serious decline. Mansabdars were converting jagirs into hereditary estates. The central treasury was bankrupt. Nobles held rank without paying their sawar. The Mughals in our mod exist in this twilight state: the forms of the mansabdari are intact but the substance has eroded.

**For the game, this gives us:**
- A progression ladder with defined ranks
- A troop maintenance obligation that scales with rank (if you fall below your sawar, you lose standing)
- Fiefs are revocable by the Emperor at any time (unlike Rajput hereditary lands)
- The system is explicitly Mughal; other kingdoms use different names and rules

---

## Mansabdari as a Game System

### The ranks (simplified for gameplay)

> **Corrected in [Chapter 16](16-Civil-War-and-Imprisonment.md):** sawar = total regular troops across all clan field parties, not cavalry only. Max is 600 at Rank 5000.

| Rank | Title | Fief tier | Field troops required | Tax rate paid upward |
|------|-------|-----------|----------------------|----------------------|
| 100 | Zamindar | 1 village | 25 | 15% |
| 500 | Mansabdar-e-Panjsad | 1 castle | 100 | 12% |
| 1000 | Qiledar | 1 castle (major) | 200 | 10% |
| 2000 | Faujdar | 1 town | 350 | 8% |
| 3000 | Subahdar | multiple settlements | 500 | 6% |
| 5000 | Amir-ul-Umara | Council seat + largest fief | 600 | 5% |

### Promotion requirements

Promotion from one rank to the next requires ALL of the following to be true:
1. **Renown threshold** — the vanilla `Clan.Renown` value
2. **Valour threshold** — the new `ValourBehavior.Valour` value
3. **Sawar maintained** — your party has at least `requiredCavalry` cavalry in its roster
4. **Standing is Good** — you are not currently in Poor Standing (see performance evaluation)
5. **Liege approval** — for player kingdoms, the king must approve (via a game menu petition); for AI, it happens automatically when criteria are met

### The promotion table

> **Corrected in [Chapter 16](16-Civil-War-and-Imprisonment.md):** troop counts are total regular troops across all clan field parties (see `GetClanTotalTroops()`), not cavalry only.

| Current → Next | Required Renown | Required Valour | Required Troops |
|----------------|----------------|-----------------|-----------------|
| 100 → 500 | 200 | 100 | 100 |
| 500 → 1000 | 600 | 300 | 200 |
| 1000 → 2000 | 1200 | 700 | 350 |
| 2000 → 3000 | 2500 | 1500 | 500 |
| 3000 → 5000 | 4000 | 2500 | 600 |

### Sawar check

Weekly tick verifies total field troop count across all clan parties. If below sawar requirement for more than 30 days:
- Days 1–30 below sawar: warning notification
- Day 31–60: Poor Standing begins
- Day 61+: Liege is notified; risk of rank reduction

```csharp
// Use GetClanTotalTroops() from MansabdariBehavior (Chapter 16) — counts ALL regular troops,
// not cavalry only.
private int GetPlayerClanTotalTroops()
{
    return MansabdariBehavior.GetClanTotalTroops(Hero.MainHero?.Clan);
}
```

---

## Career Progression Path

```
[Landless — joins a kingdom]
        ↓ Kingdom conquers territory
        ↓ Player petitions king for fief
[RANK 100 — Village Zamindar]
        ↓ Renown 200 + Valour 100 + 10 cavalry + Good Standing
        ↓ Player petitions king for promotion
[RANK 500 — Castle Mansabdar]
        ↓ Renown 600 + Valour 300 + 50 cavalry + Good Standing
[RANK 1000 — Senior Qiledar]
        ↓ Renown 1200 + Valour 700 + 100 cavalry + Good Standing
[RANK 2000 — Town Faujdar]
        ↓ Renown 2500 + Valour 1500 + 250 cavalry + Good Standing
[RANK 3000 — Subahdar (Regional)]
        ↓ Renown 4000 + Valour 2500 + 500 cavalry + Good Standing
[RANK 5000 — Diwan / Council Seat]
```

Each promotion:
1. **New fief assigned** — player's `_playerFiefId` updated to a better settlement
2. **New immediate liege** — derived from the new fief's position in the hierarchy
3. **New tax rate and sawar obligation** apply immediately
4. **Relation bonus** with the king (+15) and with the former fief's new lord (we "gift" it to someone else: -5 from them, since they wanted it)

---

## Village Lord Responsibilities

As the holder of Rank 100 (a single village), the player has four obligations:

### 1. Tax payment
- Every 28 days (once per season), `15%` of the village's income is automatically deducted from the player's gold and sent to the immediate liege's clan
- If the player has less gold than the tax owed, they enter **Tax Default**:
  - Partial amount is taken
  - `DaysInPoorStanding` counter starts incrementing
  - Immediate liege gets a notification

### 2. Bandit threat clearance
- The village has a `BanditThreat` level (0–100)
- When threat > 60, player gets a HUD notification
- When threat > 80, village Hearth stops growing
- When threat > 90, village Hearth begins declining
- Player clears threat by patrolling (see Village Menu)
- If threat stays above 80 for 14 days: Poor Standing tick

### 3. Village development
- Player is expected to invest in the village over time
- Each development project takes gold and real (in-game) days
- Projects improve Hearth growth, income, or bandit resistance
- A village with zero development after 1 in-game year: -2 relation with liege

### 4. Call to arms
- When the kingdom goes to war and the king raises an army, a call to arms fires
- Player has **14 days** to join the army containing their immediate liege, or the king's army
- Failure: −5 relation with immediate liege, −3 with king, +5 BanditThreat (no lord = bandits get bolder)
- Success: Valour earned per battle (see below)

---

## Village Development Menu

When the player visits their assigned village, a custom menu item appears: **"Oversee your fief"**.

This opens a sub-menu with:

```
Your Fief: [Village Name]
  Hearth: 342   Bandit Threat: 24   Income today: ~47 gold

  [1] Review village status      (always available)
  [2] Patrol village territory   (reduces BanditThreat by 15–35, costs 1 day)
  [3] Construction projects      (see projects list)
  [4] Speak with headman         (goes to vanilla notable conversation)
  [5] Leave
```

### Construction projects

```csharp
public enum VillageProject
{
    Granary,         // Hearth growth +20%, cost 500g, 30 days
    Watchtower,      // BanditThreat growth rate ×0.5, cost 300g, 20 days
    IrrigationCanal, // village income +25%, cost 800g, 45 days
    MilitiaPost,     // starts a village militia (passive bandit threat reduction), cost 600g, 25 days
    TradePost,       // boosts nearby town's prosperity slightly, cost 400g, 30 days
}
```

The project takes `buildDays` in real campaign time. During construction, the gold is spent immediately. On completion, the project's effect activates.

**Implementation note:** There is no vanilla village building system. We maintain:
```csharp
Dictionary<string, HashSet<VillageProject>> _completedProjects;  // settlementId → what's built
Dictionary<string, (VillageProject proj, int daysRemaining)> _underConstruction;
```

Effects applied in model overrides (see `VillageDevelopmentBehavior`).

---

## Bandit Threat System

Rather than spawning new bandit parties (complex, fragile), we track a **threat level** that simulates the bandit presence near the player's village.

```csharp
Dictionary<string, float> _banditThreat; // settlementId → 0.0–100.0
```

### Threat growth

Daily tick:
- +1.0 per day baseline (bandits always come back)
- +2.0 if the owning kingdom is currently at war (armies are elsewhere)
- +1.5 if there is no watchtower built
- −5.0 if the player is currently within 2 map units of the village (presence deters)
- +0.5 per nearby bandit party visible on the map (counted within radius)

```csharp
private void UpdateBanditThreat(string settlementId)
{
    Settlement s = Settlement.Find(settlementId);
    if (s == null) return;

    float threat = _banditThreat.GetValueOrDefault(settlementId, 0f);

    // Baseline growth
    threat += 1.0f;

    // War multiplier
    if (s.OwnerClan?.Kingdom?.IsAtWarWith(
        Kingdom.All.FirstOrDefault(k => k != s.OwnerClan.Kingdom)) == true)
        threat += 2.0f;

    // Player proximity deterrent
    if (MobileParty.MainParty.Position2D.DistanceSquared(s.Position2D) < 4f)
        threat -= 5.0f;

    // Watchtower built
    if (_completedProjects.GetValueOrDefault(settlementId)?.Contains(VillageProject.Watchtower) == true)
        threat *= 0.7f;

    // Nearby bandit parties
    int nearbyBandits = MobileParty.All.Count(p =>
        p.IsBandit &&
        p.Position2D.DistanceSquared(s.Position2D) < 9f);
    threat += nearbyBandits * 0.5f;

    _banditThreat[settlementId] = Math.Clamp(threat, 0f, 100f);
}
```

### Threat consequences

| Threat | Effect |
|--------|--------|
| 0–40 | Normal |
| 40–60 | Notification to player |
| 60–80 | Village income −15% |
| 80–90 | Village Hearth growth stops |
| 90–100 | Village Hearth actively declines; liege notified |

### Patrolling

"Patrol village territory" in the village menu costs 1 in-game day and reduces threat:
- Base reduction: 20
- +10 if player's party strength > 100
- +5 per Roguery skill tier (every 100 points)

```csharp
private float CalculatePatrolReduction()
{
    float base_reduction = 20f;
    float strengthBonus  = MobileParty.MainParty.MemberRoster.TotalManCount > 100 ? 10f : 0f;
    float scoutingBonus  = Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) / 100f * 5f;
    return base_reduction + strengthBonus + scoutingBonus;
}
```

---

## Call to Arms

When a kingdom goes to war, all fief holders are called to serve.

### Detection

Bannerlord has no `OnWarDeclared` event. Instead, poll in the weekly tick:

```csharp
private HashSet<string> _activeWars = new HashSet<string>();  // "k1_vs_k2" keys

private void OnWeeklyTick()
{
    Kingdom playerKingdom = Hero.MainHero?.Clan?.Kingdom;
    if (playerKingdom == null) return;

    // Check for newly started wars involving the player's kingdom
    foreach (Kingdom enemy in Kingdom.All)
    {
        if (enemy == playerKingdom) continue;
        string warKey = string.Compare(playerKingdom.StringId, enemy.StringId) < 0
            ? $"{playerKingdom.StringId}_vs_{enemy.StringId}"
            : $"{enemy.StringId}_vs_{playerKingdom.StringId}";

        bool atWarNow = playerKingdom.IsAtWarWith(enemy);

        if (atWarNow && !_activeWars.Contains(warKey))
        {
            _activeWars.Add(warKey);
            TriggerCallToArms(playerKingdom, enemy);
        }
        else if (!atWarNow && _activeWars.Contains(warKey))
        {
            _activeWars.Remove(warKey);
            _callToArmsDeadlineDay = -1;
            _callToArmsAnswered    = false;
        }
    }
}
```

### Call to arms logic

```csharp
private int   _callToArmsDeadlineDay = -1;  // in-game absolute day; -1 = no active call
private bool  _callToArmsAnswered    = false;

private void TriggerCallToArms(Kingdom kingdom, Kingdom enemy)
{
    if (_playerFiefId == null) return;  // player has no fief, no obligation

    int currentDay = (int)CampaignTime.Now.ToDays;
    _callToArmsDeadlineDay = currentDay + 14;
    _callToArmsAnswered    = false;

    InformationManager.DisplayMessage(new InformationMessage(
        $"Your liege has called you to war against {enemy.Name}! " +
        $"You have 14 days to join the army.",
        Color.FromUint(0xFFCC4400)));
}

private void CheckCallToArmsCompliance()
{
    if (_callToArmsDeadlineDay < 0 || _callToArmsAnswered) return;

    int currentDay = (int)CampaignTime.Now.ToDays;

    // Check if player is in their liege's army
    Hero liege = FiefHierarchy.GetLiege(Hero.MainHero);
    bool inLiegeArmy =
        MobileParty.MainParty.Army != null &&
        (MobileParty.MainParty.Army.LeaderParty?.LeaderHero == liege ||
         MobileParty.MainParty.Army.Parties.Any(p => p.LeaderHero == liege));

    if (inLiegeArmy)
    {
        _callToArmsAnswered = true;
        ValourBehavior.Instance?.AddValour(10f, "Answered call to arms");
        InformationManager.DisplayMessage(new InformationMessage(
            "You have answered the call to arms. Your liege is pleased."));
        return;
    }

    if (currentDay >= _callToArmsDeadlineDay && !_callToArmsAnswered)
    {
        _callToArmsAnswered = true;  // mark as processed (failed)

        if (liege != null)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                Hero.MainHero, liege, -5);

        Hero king = Hero.MainHero?.Clan?.Kingdom?.Leader;
        if (king != null && king != liege)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                Hero.MainHero, king, -3);

        // Bandits take advantage of the undefended village
        if (_playerFiefId != null && _banditThreat.ContainsKey(_playerFiefId))
            _banditThreat[_playerFiefId] = Math.Min(
                _banditThreat[_playerFiefId] + 15f, 100f);

        InformationManager.DisplayMessage(new InformationMessage(
            "You failed to answer the call to arms. Your liege is displeased.",
            Color.FromUint(0xFFCC2200)));
    }
}
```

---

## Valour — the New Metric

Renown already exists in Bannerlord and measures fame. **Valour** is distinct: it measures proven military service to your liege specifically. A famous outlaw can have high renown; a loyal soldier who fights every war has high valour.

```csharp
// In ValourBehavior:
private float _valour = 0f;

public float Valour => _valour;
public static ValourBehavior Instance { get; private set; }

public void AddValour(float amount, string reason = "")
{
    _valour = Math.Max(0f, _valour + amount);
    if (!string.IsNullOrEmpty(reason))
        Debug.Print($"[Hindostan] Valour +{amount}: {reason} → total {_valour:F0}");
}

public void RemoveValour(float amount, string reason = "")
{
    _valour = Math.Max(0f, _valour - amount);
    Debug.Print($"[Hindostan] Valour -{amount}: {reason} → total {_valour:F0}");
}
```

### Sources of valour gain

| Action | Amount |
|--------|--------|
| Answering call to arms | +10 |
| Battle victory while in liege's army | +5 to +20 (scales with enemy strength) |
| Killing an enemy hero (lord) in battle | +15 |
| Capturing an enemy hero in battle | +20 |
| Defending your village from a raid | +20 |
| Successfully patrolling bandit threat below 30 | +5 |
| Tax paid on time (each cycle) | +3 |

### Sources of valour loss

| Action | Amount |
|--------|--------|
| Missing a call to arms | −15 |
| Battle defeat (while in liege's army) | −5 |
| Village raided while you were absent | −10 |
| Tax default (each cycle) | −8 |
| Dismissed from fief | −30 |

### Hook into battle events

```csharp
public override void RegisterEvents()
{
    Instance = this;
    CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
    CampaignEvents.OnHeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
}

private void OnBattleEnded(MapEvent mapEvent)
{
    if (!mapEvent.IsPlayerSergeant && !mapEvent.IsPlayerMapEvent) return;

    Hero liege = FiefHierarchy.GetLiege(Hero.MainHero);
    bool inLiegeArmy = liege != null &&
        mapEvent.AttackerSide.Parties.Any(p => p.Party?.LeaderHero == liege);

    if (!inLiegeArmy) return;  // not a liege-army battle, no valour from this

    if (mapEvent.AttackerSide == mapEvent.Winner)
    {
        float valourGain = Math.Min(20f,
            mapEvent.DefenderSide.ReciprocatedStrength / 100f);
        AddValour(5f + valourGain, "Battle victory with liege");
    }
    else
    {
        RemoveValour(5f, "Battle defeat with liege");
    }
}

private void OnHeroKilled(Hero victim, Hero killer,
    KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
{
    if (killer != Hero.MainHero) return;
    if (victim.IsLord && victim.Clan?.Kingdom != Hero.MainHero?.Clan?.Kingdom)
        AddValour(15f, $"Slew {victim.Name} in battle");
}
```

---

## Performance Evaluation and Dismissal

Every 28 days (one season), the system evaluates each fief holder — AI and player alike. A "performance score" is computed:

```csharp
private int CalculatePerformanceScore(FiefRecord record, Hero holder)
{
    int score = 50;  // neutral baseline

    Settlement s = Settlement.Find(record.SettlementId);
    if (s == null) return score;

    // Paid tax on time
    score += record.LastTaxPaid >= record.LastTaxDue ? 15 : -20;

    // Bandit threat under control
    float threat = _banditThreat.GetValueOrDefault(record.SettlementId, 0f);
    if (threat < 40) score += 10;
    else if (threat > 80) score -= 20;

    // Village development (at least one project completed)
    bool hasDevelopment = _completedProjects
        .GetValueOrDefault(record.SettlementId)?.Count > 0;
    score += hasDevelopment ? 5 : 0;

    // Answered last call to arms (if there was one)
    if (record.CallToArmsAnswered) score += 15;
    else if (record.DaysCallToArmsActive > 0) score -= 15;

    return score;
}
```

### Standing thresholds

| Score | Standing |
|-------|----------|
| 60+ | Good Standing |
| 30–59 | Neutral |
| 0–29 | Caution |
| < 0 | Poor Standing |

### Dismissal pipeline

```
Poor Standing begins
    ↓ 90 days of Poor Standing
Warning notification to player: "Your liege is displeased with your performance."
    ↓ 90 more days still in Poor Standing
DISMISSED: fief revoked, valour -30, relation with liege -25
```

```csharp
private void ProcessPerformanceEvaluation(FiefRecord record)
{
    Hero holder = record.Holder;
    if (holder == null) return;

    int score = CalculatePerformanceScore(record, holder);

    if (score < 0)
    {
        record.DaysInPoorStanding += 28;

        if (record.DaysInPoorStanding == 90 && holder == Hero.MainHero)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "WARNING: Your liege is considering revoking your fief. " +
                "Improve your performance within the next season.",
                Color.FromUint(0xFFFF6600)));
        }
        else if (record.DaysInPoorStanding >= 180)
        {
            DismissFiefHolder(record, reason: "poor performance");
        }
    }
    else
    {
        // Recovering from poor standing
        record.DaysInPoorStanding = Math.Max(0, record.DaysInPoorStanding - 14);
    }
}

private void DismissFiefHolder(FiefRecord record, string reason)
{
    Hero dismissed = record.Holder;
    Hero liege     = FiefHierarchy.GetLiegeOfSettlement(
                        Settlement.Find(record.SettlementId));

    record.Holder            = null;
    record.DaysInPoorStanding = 0;

    if (dismissed == Hero.MainHero)
    {
        _playerFiefId = null;
        ValourBehavior.Instance?.RemoveValour(30f, "Dismissed from fief");
        InformationManager.DisplayMessage(new InformationMessage(
            $"Your fief has been revoked for {reason}. " +
            $"You must petition your liege for a new assignment.",
            Color.FromUint(0xFFCC2200)));
    }
    else if (dismissed != null && liege != null)
    {
        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(liege, dismissed, -20);
    }

    Debug.Print($"[Hindostan] {dismissed?.Name} dismissed from " +
                $"{record.SettlementId}: {reason}");
}
```

---

## Getting a Fief — the Player's First Assignment

After the player joins a kingdom (`OnClanChangedKingdom`), a new option appears at the throne settlement:

```csharp
starter.AddGameMenuOption(
    "castle_outside", "petition_for_fief",
    "{=!}Petition the lord for a village fief",
    condition: args =>
    {
        bool eligible = Hero.MainHero?.Clan?.Kingdom != null
            && _playerFiefId == null
            && GetAvailableVillageForPlayer() != null;

        args.IsEnabled = eligible;
        args.Tooltip = eligible
            ? new TextObject("{=!}Request a village to hold as your fief")
            : _playerFiefId != null
                ? new TextObject("{=!}You already hold a fief")
                : new TextObject("{=!}No villages are currently available");
        return true;
    },
    consequence: args => PetitionForFief()
);

private Settlement GetAvailableVillageForPlayer()
{
    Kingdom kingdom = Hero.MainHero?.Clan?.Kingdom;
    if (kingdom == null) return null;

    return kingdom.Settlements
        .Where(s => s.IsVillage
                 && !_fiefRecords.ContainsKey(s.StringId))
        .OrderBy(s => s.Village.Hearth)  // assign a modest village first
        .FirstOrDefault();
}

private void PetitionForFief()
{
    Settlement village = GetAvailableVillageForPlayer();
    if (village == null) return;

    Hero king = Hero.MainHero?.Clan?.Kingdom?.Leader;
    int kingRelation = king != null
        ? CharacterRelationManager.GetHeroRelation(Hero.MainHero, king)
        : 0;

    // Better relation = better (higher hearth) village
    if (kingRelation >= 20)
    {
        // Give a slightly better village
        village = Hero.MainHero?.Clan?.Kingdom?.Settlements
            .Where(s => s.IsVillage && !_fiefRecords.ContainsKey(s.StringId))
            .OrderByDescending(s => s.Village.Hearth)
            .Skip(1).FirstOrDefault() ?? village;
    }

    AssignFief(Hero.MainHero, village);

    if (king != null)
        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, king, 5);

    InformationManager.ShowInquiry(new InquiryData(
        "Fief Granted",
        $"The lord grants you the village of {village.Name}. " +
        $"You are now its Zamindar. Pay your taxes, hunt the bandits, " +
        $"and answer the call when war comes.",
        false, true, "", "I am honored", null, () => { }
    ));
}

private void AssignFief(Hero hero, Settlement settlement)
{
    var record = new FiefRecord
    {
        SettlementId = settlement.StringId,
        Holder       = hero,
        TaxRateToLiege = GetTaxRateForRank(GetRankOf(hero)),
        DaysInPoorStanding = 0
    };
    _fiefRecords[settlement.StringId] = record;

    if (hero == Hero.MainHero)
        _playerFiefId = settlement.StringId;

    Debug.Print($"[Hindostan] {hero.Name} assigned fief: {settlement.Name}");
}
```

---

## Implementation: FiefHolderBehavior

This is the master behavior that owns all the above state and ties it together.

```csharp
public class FiefHolderBehavior : CampaignBehaviorBase
{
    // ── State ─────────────────────────────────────────────────────────────────
    private Dictionary<string, FiefRecord> _fiefRecords  = new Dictionary<string, FiefRecord>();
    private Dictionary<string, float>      _banditThreat = new Dictionary<string, float>();
    private Dictionary<string, HashSet<VillageProject>> _completedProjects
        = new Dictionary<string, HashSet<VillageProject>>();
    private Dictionary<string, (VillageProject proj, int completionDay)> _underConstruction
        = new Dictionary<string, (VillageProject, int)>();

    private string _playerFiefId          = null;
    private int    _callToArmsDeadlineDay  = -1;
    private bool   _callToArmsAnswered     = false;
    private int    _lastTaxDay             = 0;
    private HashSet<string> _activeWars    = new HashSet<string>();

    public static FiefHolderBehavior Instance { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────
    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.OnClanChangedKingdom.AddNonSerializedListener(this, OnClanChangedKingdom);
        CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(
            this, OnSettlementOwnerChanged);
        CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
    }

    // ── SyncData ──────────────────────────────────────────────────────────────
    public override void SyncData(IDataStore dataStore)
    {
        // FiefRecords — save as parallel lists (DataStore can't handle Dictionary<string, class>)
        var fiefIds      = _fiefRecords.Keys.ToList();
        var fiefHolders  = _fiefRecords.Values.Select(r => r.Holder).ToList();
        var fiefTaxRates = _fiefRecords.Values.Select(r => r.TaxRateToLiege).ToList();
        var fiefBadDays  = _fiefRecords.Values.Select(r => r.DaysInPoorStanding).ToList();

        dataStore.SyncData("hind_fief_ids",       ref fiefIds);
        dataStore.SyncData("hind_fief_holders",   ref fiefHolders);
        dataStore.SyncData("hind_fief_taxrates",  ref fiefTaxRates);
        dataStore.SyncData("hind_fief_baddays",   ref fiefBadDays);

        dataStore.SyncData("hind_player_fief",    ref _playerFiefId);
        dataStore.SyncData("hind_cta_deadline",   ref _callToArmsDeadlineDay);
        dataStore.SyncData("hind_cta_answered",   ref _callToArmsAnswered);
        dataStore.SyncData("hind_last_tax_day",   ref _lastTaxDay);

        // Bandit threat
        var threatIds    = _banditThreat.Keys.ToList();
        var threatValues = _banditThreat.Values.ToList();
        dataStore.SyncData("hind_threat_ids",   ref threatIds);
        dataStore.SyncData("hind_threat_vals",  ref threatValues);

        if (!dataStore.IsSaving)
        {
            // Reconstruct from parallel lists
            _fiefRecords.Clear();
            for (int i = 0; i < fiefIds.Count; i++)
            {
                if (i < fiefHolders.Count)
                    _fiefRecords[fiefIds[i]] = new FiefRecord
                    {
                        SettlementId       = fiefIds[i],
                        Holder             = fiefHolders[i],
                        TaxRateToLiege     = i < fiefTaxRates.Count ? fiefTaxRates[i] : 0.15f,
                        DaysInPoorStanding = i < fiefBadDays.Count  ? fiefBadDays[i]  : 0
                    };
            }

            _banditThreat.Clear();
            for (int i = 0; i < threatIds.Count; i++)
                if (i < threatValues.Count)
                    _banditThreat[threatIds[i]] = threatValues[i];
        }
    }

    // ── Ticks ─────────────────────────────────────────────────────────────────
    private void OnDailyTick()
    {
        if (_playerFiefId != null)
            UpdateBanditThreat(_playerFiefId);

        CheckConstructionCompletion();
        CheckCallToArmsCompliance();
    }

    private void OnWeeklyTick()
    {
        CheckForNewWars();

        // Tax collection every 28 days
        int currentDay = (int)CampaignTime.Now.ToDays;
        if (currentDay - _lastTaxDay >= 28)
        {
            _lastTaxDay = currentDay;
            CollectAllTaxes();
            EvaluateAllPerformance();
        }
    }

    private void OnNewGameCreated(CampaignGameStarter starter)
    {
        // AI kingdoms auto-assign their lords to fiefs
        AutoAssignAIFiefs();
    }

    private void OnGameLoaded(CampaignGameStarter starter)
    {
        Instance = this;
        if (_fiefRecords.Count == 0)
            AutoAssignAIFiefs();
    }

    private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
        ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
    {
        // Revoke fiefs from lords who leave the kingdom
        if (oldKingdom != null)
        {
            foreach (Hero hero in clan.Heroes)
            {
                foreach (var record in _fiefRecords.Values
                    .Where(r => r.Holder == hero).ToList())
                {
                    DismissFiefHolder(record, "left the kingdom");
                }
            }
        }
    }

    private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim,
        Hero newOwner, Hero oldOwner, Hero capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        // When a settlement is captured, its fief record is cleared
        if (_fiefRecords.TryGetValue(settlement.StringId, out var record))
        {
            if (record.Holder == Hero.MainHero)
            {
                _playerFiefId = null;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Your fief {settlement.Name} has been captured by the enemy.",
                    Color.FromUint(0xFFCC2200)));
            }
            _fiefRecords.Remove(settlement.StringId);
        }
    }

    private void OnBattleEnded(MapEvent mapEvent)
    {
        // Valour is handled in ValourBehavior — see that class
        // Here we check if the player's village was raided
        if (_playerFiefId == null) return;
        if (mapEvent.MapEventSettlement?.StringId != _playerFiefId) return;

        if (mapEvent.BattleState == BattleState.DefenderVictory)
        {
            ValourBehavior.Instance?.AddValour(20f, "Defended your village");
        }
        else if (mapEvent.BattleState == BattleState.AttackerVictory)
        {
            ValourBehavior.Instance?.RemoveValour(10f, "Village raided under your watch");
            _banditThreat[_playerFiefId] =
                Math.Min((_banditThreat.GetValueOrDefault(_playerFiefId) + 20f), 100f);
        }
    }

    // ── Tax collection ────────────────────────────────────────────────────────
    private void CollectAllTaxes()
    {
        foreach (var record in _fiefRecords.Values.ToList())
        {
            if (record.Holder == null) continue;

            Settlement s = Settlement.Find(record.SettlementId);
            if (s == null || !s.IsVillage) continue;

            Hero liege = FiefHierarchy.GetLiegeOfSettlement(s);
            if (liege?.Clan == null || liege.Clan == record.Holder.Clan) continue;

            int villageIncome = EstimateVillageIncome(s);
            int taxDue        = (int)(villageIncome * record.TaxRateToLiege * 28);
            int available     = record.Holder.Clan?.Gold ?? 0;
            int actual        = Math.Min(taxDue, available);

            record.LastTaxDue  = taxDue;
            record.LastTaxPaid = actual;

            if (record.Holder.Clan != null) record.Holder.Clan.Gold -= actual;
            if (liege.Clan != null)         liege.Clan.Gold         += actual;

            if (record.Holder == Hero.MainHero && actual < taxDue)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Tax default: you owed {taxDue} gold but only paid {actual}. " +
                    $"Your liege {liege.Name} is displeased.",
                    Color.FromUint(0xFFFF6600)));
                ValourBehavior.Instance?.RemoveValour(8f, "Tax default");
            }
        }
    }

    private int EstimateVillageIncome(Settlement village)
    {
        // Approximate: Hearth * 0.15 gold per day
        float base_income = village.Village.Hearth * 0.15f;

        // Irrigation bonus
        if (_completedProjects.GetValueOrDefault(village.StringId)
                ?.Contains(VillageProject.IrrigationCanal) == true)
            base_income *= 1.25f;

        // Trade post bonus
        if (_completedProjects.GetValueOrDefault(village.StringId)
                ?.Contains(VillageProject.TradePost) == true)
            base_income *= 1.15f;

        return (int)base_income;
    }
}
```

---

## Implementation: VillageDevelopmentBehavior

Handles the custom village menu and construction timers. Wire up the game menu option:

```csharp
// In HindostanSubModule.AddCustomMenuOptions:
starter.AddGameMenuOption(
    "village", "oversee_fief",
    "{=!}Oversee your fief",
    condition: args =>
    {
        bool isPlayerFief = Settlement.CurrentSettlement?.StringId ==
                            FiefHolderBehavior.Instance?._playerFiefId;
        args.IsEnabled = isPlayerFief;
        args.Tooltip   = new TextObject(isPlayerFief
            ? "{=!}Manage your village fief"
            : "{=!}This is not your fief");
        return isPlayerFief;
    },
    consequence: args => ShowVillageFiefMenu()
);

private void ShowVillageFiefMenu()
{
    string fiefId    = FiefHolderBehavior.Instance?._playerFiefId;
    Settlement s     = Settlement.Find(fiefId);
    if (s == null) return;

    float threat  = FiefHolderBehavior.Instance._banditThreat.GetValueOrDefault(fiefId, 0f);
    int   income  = FiefHolderBehavior.Instance.EstimateVillageIncome(s);
    int   hearth  = (int)s.Village.Hearth;

    string body =
        $"Village: {s.Name}\n" +
        $"Hearth: {hearth}    Bandit Threat: {threat:F0}/100\n" +
        $"Estimated daily income: ~{income} gold\n\n" +
        "What would you like to do?";

    // Show options via a multi-step inquiry chain...
    // (Full implementation requires chaining multiple InquiryData calls)
}
```

---

## Implementation: CallToArmsBehavior

See the full code in the [Call to Arms](#call-to-arms) section above. Register with:

```csharp
starter.AddBehavior(new FiefHolderBehavior());
starter.AddBehavior(new ValourBehavior());
starter.AddBehavior(new MansabdariBehavior());  // Mughal kingdoms only
```

---

## Implementation: ValourBehavior

```csharp
public class ValourBehavior : CampaignBehaviorBase
{
    private float _valour = 0f;
    public float  Valour  => _valour;
    public static ValourBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
        CampaignEvents.OnHeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("hind_valour", ref _valour);
    }

    public void AddValour(float amount, string reason = "")    => _valour = Math.Max(0f, _valour + amount);
    public void RemoveValour(float amount, string reason = "") => _valour = Math.Max(0f, _valour - amount);

    private void OnBattleEnded(MapEvent mapEvent)
    {
        if (!mapEvent.IsPlayerMapEvent) return;
        Hero liege = FiefHierarchy.GetLiege(Hero.MainHero);
        bool inLiegeArmy = liege != null &&
            (mapEvent.AttackerSide.Parties.Any(p => p.Party?.LeaderHero == liege) ||
             mapEvent.DefenderSide.Parties.Any(p => p.Party?.LeaderHero == liege));
        if (!inLiegeArmy) return;

        if (mapEvent.Winner?.LeaderParty == MobileParty.MainParty ||
            mapEvent.Winner?.Parties.Any(p => p == MobileParty.MainParty.Party) == true)
            AddValour(5f + Math.Min(15f, mapEvent.StrengthOfSide(BattleSideEnum.Attacker) / 200f),
                "Battle victory with liege");
        else
            RemoveValour(5f, "Battle defeat with liege");
    }

    private void OnHeroKilled(Hero victim, Hero killer,
        KillCharacterAction.KillCharacterActionDetail detail, bool _)
    {
        if (killer != Hero.MainHero || !victim.IsLord) return;
        if (victim.Clan?.Kingdom == Hero.MainHero?.Clan?.Kingdom) return;
        AddValour(15f, $"Slew {victim.Name}");
    }
}
```

---

## Cultural Variants

| Kingdom | System name | Fief revocable? | Dismissal threshold | Promotion mechanic |
|---------|-------------|-----------------|---------------------|--------------------|
| Mughal (empire, empire_w, empire_s) | Mansabdari / Jagirdari | Yes — Emperor can reassign freely | 90 days poor standing | Petition to Emperor, based on zat/sawar |
| Rajput (vlandia) | Hereditary clan territory | No — family lineage | 365 days poor standing (much harder to dismiss) | Proven in battle; elder clan recognition |
| Maratha (battania) | Sardar appointment by Peshwa | Partially — sardars can petition against each other | 120 days | Valour + horizontal clan approval vote |
| Afghan (sturgia) | Tribal chieftainship | Strongest wins | No standing system — dismissal only by military defeat | Defeat the current holder in a duel/battle |
| Mysore (aserai) | Palegar system | Partially | 120 days | Military service + Maharaja approval |
| Sikh (khuzait) | Misl territory | No — Misl's collective property | Not applicable — Misl governs collectively | Become Misl leader (via death of current) |

---

**[← Chapter 14](14-Feudal-Hierarchy.md)** | **[Home](Home.md)**
