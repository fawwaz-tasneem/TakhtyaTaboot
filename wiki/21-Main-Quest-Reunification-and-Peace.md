# Chapter 21 — Main Quest, War Score, Peace Negotiation, and Suzerainty

> The win condition, the peace system, and the Shahanshah coronation. The goal is to reunify the subcontinent — or raze the old order and build something new. War is the instrument; negotiated peace is the craft; suzerainty is the architecture.

**[← Chapter 20](20-Character-Depth-and-Intrigue.md)** | **[Home](Home.md)**

---

## Contents

1. [The Two Paths](#1-the-two-paths)
2. [Main Quest Chain](#2-main-quest-chain)
3. [War Score](#3-war-score)
4. [Peace Negotiation System](#4-peace-negotiation-system)
5. [Suzerainty](#5-suzerainty)
6. [The Shahanshah Coronation](#6-the-shahanshah-coronation)
7. [Implementation: MainQuestBehavior](#7-implementation-mainquestbehavior)
8. [Implementation: WarScoreBehavior](#8-implementation-warscoreebehavior)
9. [Implementation: PeaceNegotiationBehavior](#9-implementation-peacenegotiationbehavior)
10. [Implementation: SuzeraintyBehavior](#10-implementation-suzeraintybehavior)

---

## 1. The Two Paths

The player has two ways to finish the game:

### Path A — Restoration (Yaksaan-e-Hind)

*"Unite what was broken. Restore the Mughal suzerainty over the subcontinent."*

- Bring 1/3 of all settlements under your direct rule or suzerain tribute
- Crown yourself **Shahanshah** at a coronation in your capital
- Suzerain kings retain their kingdoms but bow to you
- The empire is rebuilt — not by destroying every rival, but by making them acknowledge your primacy

This is the historically plausible path. The Mughal empire was not destroyed militarily; it was fragmented. A sufficiently capable ruler could have re-cohered it.

### Path B — Domination (Saltanat-e-Mutlaq)

*"One ruler, one realm, no intermediaries."*

- Eliminate (not just suzerain) every other kingdom — no rival kings remain
- Crown yourself **Padishah-e-Alam** (Lord of All the World)
- A harder, bloodier path; no diplomacy shortcuts
- The late-game is purely military elimination

Both paths use the same war score and peace negotiation systems. Path A additionally uses the suzerainty system. Path B ignores suzerainty entirely and requires full elimination.

---

## 2. Main Quest Chain

The quest chain is a sequence of **milestones**, each tracked independently. They do not need to be done in order in some cases, but earlier milestones unlock later ones.

```
Stage 1  ──  "The Landless Lord"
             Reach Mansabdar rank 500 (Mansabdar-e-Panjsad)
             Reward: Introductory fief assignment from king

Stage 2  ──  "Blooded in Service"
             Win 3 battles while a member of any kingdom's army
             Reach rank 1000 (Qiledar)
             Reward: Valour +50; king relation +15

Stage 3  ──  "The Faujdar's Road"
             Reach rank 2000 (Faujdar) and hold a town fief
             Reward: Access to the Council petition system

Stage 4  ──  "Amir Among Lords"
             Reach rank 5000 (Amir-ul-Umara) and sit on the king's council
             Reward: The "Declare Imperial Ambition" option unlocks

  ┌──────────── BRANCH ───────────────┐
  │                                   │
Stage 5A ── "The Unifying Vision"    Stage 5B ── "The Great Breaking"
  │          Declare ambition:                    Destroy (eliminate) 3 kingdoms
  │          Restoration path                     No suzerainty used
  │          Suzerainty negotiation               
  │          becomes available        
  │                                   
Stage 6A ── "One Third Under Heaven" Stage 6B ── "Sole Master"
  │          Control or suzerain               Last standing kingdom
  │          ≥ 17 towns (1/3 of ~53)          = player's kingdom
  │                                   │
  └──────────── BOTH PATHS ──────────┘

Stage 7  ──  "The Coronation"
             Crown yourself Shahanshah (Path A) or Padishah-e-Alam (Path B)
             The game's narrative endpoint; play continues after
```

The "Declare Imperial Ambition" moment is a deliberate, player-initiated declaration. Until the player makes this declaration, the suzerainty demand in peace negotiations is hidden. This stops the player accidentally triggering the end-game prematurely.

---

## 3. War Score

War score is a number from −100 to +100 representing how decisively one side is winning a specific war. Each war between two kingdoms tracks its own score independently.

**Positive = the score for the kingdom that declared war (the attacker).**
When the player attacks: positive score means player is winning.
When the player is attacked: positive score means the attacker is winning.

### Accumulation sources

| Event | Score change | Notes |
|-------|-------------|-------|
| Battle won (minor, < 150 total casualties) | +3 | Per battle |
| Battle won (significant, 150–500 casualties) | +8 | Per battle |
| Battle won (decisive, > 500 casualties) | +15 | Per battle |
| Battle lost | Mirror negatives of above | |
| Enemy commander captured | +5 | Per capture |
| Town captured from enemy | +10 | Per town |
| Castle captured from enemy | +5 | Per castle |
| Town lost to enemy | −10 | Per town |
| Duration trickle | ±0.3/week | +0.3 to the side with more settlements in the war zone |
| Enemy king captured | +20 | |
| War goal achieved (if declared) | +15 one-time bonus | |

Score naturally decays toward 0 at 1 point per week when no active military action occurs, simulating exhaustion and negotiation pressure.

### Peace offer thresholds

| Score | What the winning side CAN demand |
|-------|----------------------------------|
| ≥ 10 | White peace — no concessions from either side |
| ≥ 25 | Tribute payment (one-time gold) |
| ≥ 35 | Annual tribute (ongoing) |
| ≥ 50 | Cede one settlement |
| ≥ 65 | Cede two settlements OR suzerainty (if Imperial Ambition declared) |
| ≥ 80 | Cede three settlements + annual tribute |
| ≥ 90 | Cede four settlements + suzerainty (if declared) |
| 100  | Full annexation — all enemy settlements |

The **loser** can sue for peace at any time, but they must offer what the winner's score entitles them to — or the winner can refuse and continue.

---

## 4. Peace Negotiation System

### How negotiations open

There are three entry points:

1. **Player sues for peace** — from the campaign menu (own kingdom tab), any time the player is at war
2. **Enemy sues for peace** — AI kingdoms sue for peace when their war score vs the player reaches −40 or worse, or when their territory loss exceeds 2 settlements
3. **Captured enemy lord** — when the player captures a lord who is the enemy kingdom's ruler or council member, a dialogue option to "negotiate terms" opens

### The negotiation menu

```
┌─────────────────────────────────────────────────────────┐
│  Peace Negotiation — Mughliya Sultanat vs. Marathas     │
│  War score: +62 (in your favour)                        │
├─────────────────────────────────────────────────────────┤
│  TERMS YOU CAN DEMAND (score ≥ 62):                     │
│                                                         │
│  ○ White peace (no concessions)                         │
│  ○ One-time tribute: 5,000 gold                         │
│  ○ Annual tribute: 800 gold/year                        │
│  ○ Cede settlement: [Choose from Maratha towns]         │
│  ○ Cede two settlements                                 │
│  ◉ Suzerainty — Marathas bow to your authority          │
│    (requires Imperial Ambition declared)                │
│                                                         │
│  Estimated acceptance chance: 74%                       │
│  (Based on score, their remaining strength, their king  │
│   personality, and your legitimacy)                     │
├─────────────────────────────────────────────────────────┤
│  [Propose terms]    [Continue war]    [Accept their     │
│                                        counter-offer]   │
└─────────────────────────────────────────────────────────┘
```

### Acceptance probability

The AI kingdom's chance of accepting depends on:

```csharp
float CalculateAcceptanceProbability(Kingdom loser, PeaceDemand demand, float warScore)
{
    float base = 0f;

    // War score component: higher score = more likely to accept
    base += warScore * 0.8f;  // at score 80 = 64% base

    // Demand severity modifier
    base -= demand switch
    {
        PeaceDemand.WhitePeace      => 0f,
        PeaceDemand.OneTimeTribute  => 10f,
        PeaceDemand.AnnualTribute   => 18f,
        PeaceDemand.OneSett         => 20f,
        PeaceDemand.TwoSetts        => 35f,
        PeaceDemand.Suzerainty      => 40f,
        PeaceDemand.ThreeSetts      => 50f,
        PeaceDemand.Annexation      => 90f,
        _                           => 0f
    };

    // Their remaining strength
    float strengthRatio = (float)loser.TotalStrength /
        Math.Max(1, Hero.MainHero?.Clan?.Kingdom?.TotalStrength ?? 1);
    if (strengthRatio < 0.3f) base += 15f;  // nearly beaten, more likely to accept
    if (strengthRatio > 0.8f) base -= 20f;  // still strong, will resist

    // King personality trait
    if (TraitsBehavior.Instance?.HasTrait(loser.Leader, CharacterTrait.Reckless) == true)
        base -= 15f;  // reckless kings refuse even losing peace terms
    if (TraitsBehavior.Instance?.HasTrait(loser.Leader, CharacterTrait.Cautious) == true)
        base += 10f;  // cautious kings accept bad terms to preserve what they have

    // Player's legitimacy: high legitimacy makes suzerainty more acceptable
    if (demand == PeaceDemand.Suzerainty)
    {
        float legit = LegitimacyBehavior.Instance?.GetLegitimacy(Hero.MainHero) ?? 50f;
        base += (legit - 50f) * 0.3f;  // +15 at 100 legit, −15 at 0 legit
    }

    return Math.Clamp(base / 100f, 0.02f, 0.95f);
}
```

### Counter-offers

If the AI rejects the player's terms, they may counter-propose. Counter-proposals are always one step below what was demanded:

```
Player demanded: Suzerainty (score 62, acceptance 74%)
AI rejected.
AI counter-offers: Cede one settlement (Pune) + annual tribute of 600 gold.
```

The player can accept the counter, refuse and continue the war, or propose a different demand.

### AI-initiated offers (when AI sues for peace)

The AI will offer:
- At war score −40: White peace offer
- At war score −55: Minor tribute offer
- At war score −70: Settlement offer (whichever they can afford to lose)

If the player rejects these, the AI continues fighting. If their score deteriorates further, they try again with better terms.

---

## 5. Suzerainty

### What suzerainty means

Suzerainty is an intermediate state between independence and absorption. The suzerain kingdom retains:
- Its ruler, its title ("King" or "Peshwa" or "Khan")
- Its internal laws and culture
- Its mansabdar system and council
- The right to expand against non-suzerain kingdoms (with suzerain's permission)

The suzerain kingdom **gives up**:
- The right to declare war independently
- A percentage of its income as annual tribute (15% default, negotiable)
- The obligation to respond to the Shahanshah's call-to-arms

The relationship is formalized: the vassal king publicly kneels at a ceremony and his title is hyphenated — "King of the Marathas, under the suzerainty of the Shahanshah." His biography entry records this.

### Suzerain record

```csharp
public class SuzerainRecord
{
    public string SuzerainKingdomId;
    public string VassalKingdomId;
    public float  TributeRate;           // 0.15 = 15%
    public int    AgreementDay;
    public int    NextTributeDay;        // due every 365 days (1 in-game year)
    public bool   IsActive;
    public int    MissedTributes;        // counts consecutive missed payments
}
```

### Tribute collection

```csharp
private void CollectTribute(SuzerainRecord record)
{
    Kingdom suzerain = Kingdom.All.FirstOrDefault(k => k.StringId == record.SuzerainKingdomId);
    Kingdom vassal   = Kingdom.All.FirstOrDefault(k => k.StringId == record.VassalKingdomId);
    if (suzerain == null || vassal == null || !record.IsActive) return;

    // Estimate vassal annual income
    int vassalIncome = vassal.Clans
        .Sum(c => c.Heroes.Where(h => h.IsAlive).Sum(h => (int)(h.Gold / 10)));
    // Rough proxy — full impl reads from ClanFinanceModel

    int tributeAmount = (int)(vassalIncome * record.TributeRate);

    if (vassal.Leader?.Gold >= tributeAmount)
    {
        GiveGoldAction.ApplyBetweenCharacters(vassal.Leader, suzerain.Leader, tributeAmount);
        record.MissedTributes = 0;

        if (suzerain.Leader == Hero.MainHero)
            InformationManager.DisplayMessage(new InformationMessage(
                $"{vassal.Name} has paid their annual tribute of {tributeAmount:N0} gold.",
                Color.FromUint(0xFFD4AF37)));
    }
    else
    {
        record.MissedTributes++;
        if (record.MissedTributes >= 2)
        {
            // Vassal has defaulted — suzerainty lapses; they become independent
            record.IsActive = false;
            if (suzerain.Leader == Hero.MainHero)
                InformationManager.ShowInquiry(new InquiryData(
                    "Tribute Default",
                    $"{vassal.Name} has failed to pay tribute for two consecutive years. " +
                    $"Their suzerainty agreement is void. You may declare war to reassert control.",
                    true, false,
                    "Noted",
                    "",
                    () => { },
                    () => { }
                ));
        }
    }
}
```

### Call-to-arms obligation

When the Shahanshah's kingdom declares war, suzerain kingdoms receive a call-to-arms notification:

```csharp
private void NotifyVassalsOfWar(Kingdom suzerain, Kingdom enemy)
{
    foreach (var record in _records.Where(r => r.SuzerainKingdomId == suzerain.StringId
                                            && r.IsActive))
    {
        Kingdom vassal = Kingdom.All.FirstOrDefault(k => k.StringId == record.VassalKingdomId);
        if (vassal == null) continue;

        // Vassal AI: decide whether to join
        float compliance = LegitimacyBehavior.Instance?.GetLegitimacy(suzerain.Leader) ?? 50f;
        compliance /= 100f;  // 0–1

        if (MBRandom.RandomFloat < compliance)
        {
            // Vassal joins the war
            DeclareWarAction.Apply(vassal, enemy, DeclareWarAction.DeclareWarDetail.Default);
        }
        else
        {
            // Vassal refuses — this is a crack in the suzerain relationship
            record.MissedTributes++;  // reuse this counter for "failures to comply"
        }
    }
}
```

### Vassal can rebel

If the Shahanshah's legitimacy drops below 25, suzerain vassals gain a weekly chance to break free:

```csharp
private void CheckVassalLoyalty()
{
    foreach (var record in _records.Where(r => r.IsActive).ToList())
    {
        Kingdom suzerain = Kingdom.All.FirstOrDefault(k => k.StringId == record.SuzerainKingdomId);
        if (suzerain == null) continue;

        float legit = LegitimacyBehavior.Instance?.GetLegitimacy(suzerain.Leader) ?? 50f;
        if (legit < 25f && MBRandom.RandomFloat < 0.10f)
        {
            record.IsActive = false;
            Kingdom vassal = Kingdom.All.FirstOrDefault(k => k.StringId == record.VassalKingdomId);

            if (vassal != null && suzerain.Leader == Hero.MainHero)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{vassal.Name} has renounced their suzerainty! Your weakness has emboldened them.",
                    Color.FromUint(0xFFCC2200)));
        }
    }
}
```

---

## 6. The Shahanshah Coronation

### Conditions to crown yourself

| Condition | Value |
|-----------|-------|
| Settlements under direct control + suzerain | ≥ 17 towns (roughly 1/3 of ~53) |
| Player's own Mansabdar rank | 5000 (Amir-ul-Umara) |
| Suzerain vassals (at least) | 2 active vassal kingdoms |
| Player renown | ≥ 3000 |
| Imperial Ambition declared | Yes |
| No active civil war in own kingdom | Yes |

For Path B (Domination), suzerain condition is replaced with "≤ 1 independent rival kingdom remaining."

### Coronation ceremony

When conditions are met, an option appears in the throne capital menu:

```
[Hold the Imperial Coronation — Crown yourself Shahanshah of Hindostan]
```

This triggers a ceremonial event:

```csharp
private void TriggerCoronation(CoronationPath path)
{
    string title    = path == CoronationPath.Restoration
        ? "Shahanshah" : "Padishah-e-Alam";
    string subtitle = path == CoronationPath.Restoration
        ? "Emperor of Hindostan, Restorer of the Mughal Suzerainty"
        : "Absolute Master of All the Subcontinent";

    // Narrative display
    InformationManager.ShowInquiry(new InquiryData(
        $"The Coronation of {Hero.MainHero.Name}",
        $"In the great Diwan-i-Am, before the assembled lords of Hindostan, " +
        $"{Hero.MainHero.Name} is crowned {title} — {subtitle}. " +
        $"The ulema bless the occasion. The drums of the Naubat thunder. " +
        $"The coins are struck with your name. " +
        $"History will remember this day.",
        true, false,
        "Accept the throne",
        "",
        () => ApplyCoronationEffects(title, path),
        () => { }
    ));
}

private void ApplyCoronationEffects(string title, CoronationPath path)
{
    Hero player = Hero.MainHero;

    // Mechanical effects
    LegitimacyBehavior.Instance?.SetLegitimacy(player, 100f);
    ImperialAuthorityBehavior.Instance?.ModifyAuthority(player.Clan?.Kingdom, 30f, "Imperial coronation");
    player.Clan.Influence += 500;
    player.Clan.Renown    += 1000;

    // All suzerain vassals: loyalty +20
    foreach (var record in SuzeraintyBehavior.Instance?.GetActiveRecords(player.Clan?.Kingdom)
          ?? new List<SuzerainRecord>())
    {
        Kingdom vassal = Kingdom.All.FirstOrDefault(k => k.StringId == record.VassalKingdomId);
        if (vassal?.Leader != null)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(player, vassal.Leader, +20);
    }

    // All independent kingdoms: −20 (they fear you now)
    foreach (Kingdom k in Kingdom.All.Where(kk => !kk.IsEliminated && kk != player.Clan.Kingdom))
    {
        if (k.Leader != null)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(player, k.Leader, -20);
    }

    // Ulema fatwa of endorsement
    UlemaBehavior.Instance?.ModifyFavour(player.Clan?.Kingdom, 25f, "Imperial coronation");

    // 30-day festival bonus across all held settlements
    FestivalEffectBehavior.Instance?.ApplyEmpirewideFestival(30);

    // Traits
    TraitsBehavior.Instance?.AddTrait(player, CharacterTrait.Statesman);

    // Biographer records this as the pinnacle chapter
    EventCaptureBehavior.Instance?.RecordPlayerMilestone(
        GameEventType.Biography_Chapter,
        $"{player.Name} was crowned {title} of Hindostan in a ceremony at {player.HomeSettlement?.Name}. " +
        $"The suzerainty of the Mughal throne was restored over the subcontinent.",
        player.HomeSettlement?.Name.ToString() ?? "the capital");

    // The news reporters across the empire all publish the same headline
    NewsReporterBehavior.Instance?.BroadcastImperialAnnouncement(
        $"Coronation of the Shahanshah",
        $"On this day, {player.Name} was acclaimed {title} of all Hindostan. " +
        $"The sundered empire is restored. The throne of Akbar is occupied once more.");

    // Play continues — but the narrative goal is achieved
    InformationManager.DisplayMessage(new InformationMessage(
        $"You are now {title}. The empire is restored. Your reign continues.",
        Color.FromUint(0xFFD4AF37)));
}
```

### Post-coronation obligations

The game does not end. Ruling the empire is harder than winning it:

| Obligation | Mechanic |
|-----------|----------|
| Annual durbar (court assembly) | If skipped: all vassal loyalty −10, authority −5 |
| Nazrana from all vassals | Tracked separately from standard nazrana |
| Defending all suzerain kingdoms | Call-to-arms becomes mandatory if a vassal is attacked |
| Keeping legitimacy above 60 | Below 60, pretenders emerge (succession crisis) |
| Keeping ulema favour above 40 | Below 40, religious revolt pressure +10 empire-wide |

The Shahanshah is not an endpoint. It is the beginning of the hardest phase of the game.

---

## 7. Implementation: MainQuestBehavior

```csharp
public class MainQuestBehavior : CampaignBehaviorBase
{
    public enum QuestStage
    {
        Landless          = 0,
        BloodedInService  = 1,
        FaujdarsRoad      = 2,
        AmirAmongLords    = 3,
        AmbitionDeclared  = 4,
        PathChosen        = 5,
        ThresholdReached  = 6,
        Coronation        = 7,
    }

    public enum CoronationPath { Restoration, Domination }

    private QuestStage      _stage       = QuestStage.Landless;
    private CoronationPath  _path        = CoronationPath.Restoration;
    private bool            _pathChosen  = false;
    private bool            _crowned     = false;
    private int             _battlesWon  = 0;  // while in kingdom army

    public static MainQuestBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
    }

    private void OnWeeklyTick()
    {
        if (_crowned) return;
        CheckStageProgression();
    }

    private void CheckStageProgression()
    {
        switch (_stage)
        {
            case QuestStage.Landless:
                if (MansabdariBehavior.Instance?.GetRank(Hero.MainHero) >= 500)
                    AdvanceStage(QuestStage.BloodedInService,
                        "You have proven yourself as a Mansabdar. Now earn your place in battle.");
                break;

            case QuestStage.BloodedInService:
                int rank1000 = MansabdariBehavior.Instance?.GetRank(Hero.MainHero) ?? 0;
                if (_battlesWon >= 3 && rank1000 >= 1000)
                    AdvanceStage(QuestStage.FaujdarsRoad,
                        "Your valour is recognized. The rank of Qiledar is within reach.");
                break;

            case QuestStage.FaujdarsRoad:
                int rank = MansabdariBehavior.Instance?.GetRank(Hero.MainHero) ?? 0;
                bool holdsTown = Hero.MainHero?.Clan?.Settlements.Any(s => s.IsTown) == true;
                if (rank >= 2000 && holdsTown)
                    AdvanceStage(QuestStage.AmirAmongLords,
                        "You hold a town and command the rank of Faujdar. " +
                        "The path to the council is open.");
                break;

            case QuestStage.AmirAmongLords:
                bool isAmir    = MansabdariBehavior.Instance?.GetRank(Hero.MainHero) >= 5000;
                bool onCouncil = CouncilBehavior.Instance?.IsOnCouncil(Hero.MainHero) == true;
                if (isAmir && onCouncil)
                    AdvanceStage(QuestStage.AmbitionDeclared,
                        "You stand at the pinnacle of the mansabdari. What will you do with this power? " +
                        "Declare your imperial ambition from your capital.");
                break;

            case QuestStage.AmbitionDeclared:
                // Waiting for player to choose path via game menu
                break;

            case QuestStage.PathChosen:
                if (_path == CoronationPath.Restoration)
                    CheckRestorationThreshold();
                else
                    CheckDominationThreshold();
                break;

            case QuestStage.ThresholdReached:
                // Coronation option now available — player triggers it manually
                break;
        }
    }

    private void CheckRestorationThreshold()
    {
        Kingdom pk = Hero.MainHero?.Clan?.Kingdom;
        if (pk == null) return;

        int totalTowns = Settlement.All.Count(s => s.IsTown);
        int threshold  = totalTowns / 3;  // ~17 towns

        int controlled = pk.Settlements.Count(s => s.IsTown);
        int suzerained = SuzeraintyBehavior.Instance?
            .GetActiveRecords(pk)
            .Sum(r => Kingdom.All.FirstOrDefault(k => k.StringId == r.VassalKingdomId)
                          ?.Settlements.Count(s => s.IsTown) ?? 0) ?? 0;

        if (controlled + suzerained >= threshold)
            AdvanceStage(QuestStage.ThresholdReached,
                "One third of Hindostan acknowledges your authority. " +
                "The coronation awaits. Hold it at your capital.");
    }

    private void CheckDominationThreshold()
    {
        int rivals = Kingdom.All.Count(k => !k.IsEliminated
                                         && k != Hero.MainHero?.Clan?.Kingdom);
        if (rivals <= 1)
            AdvanceStage(QuestStage.ThresholdReached,
                "Only one rival remains. Hindostan is yours. Claim the throne.");
    }

    public void DeclareAmbition(CoronationPath chosenPath)
    {
        _path       = chosenPath;
        _pathChosen = true;
        AdvanceStage(QuestStage.PathChosen,
            chosenPath == CoronationPath.Restoration
                ? "You have declared the Restoration. Bring kingdoms under suzerainty. " +
                  "Peace negotiations now include the option to demand fealty."
                : "You have declared Domination. No rivals will be left standing. " +
                  "There is no suzerainty on this path — only elimination.");

        // Notify all other kingdoms — this changes AI behavior
        foreach (Kingdom k in Kingdom.All.Where(kk => !kk.IsEliminated))
        {
            if (k.Leader != null && k != Hero.MainHero?.Clan?.Kingdom)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                    Hero.MainHero, k.Leader, -10);  // they all distrust you a bit more now
        }
    }

    public void CompleteCoro()
    {
        _crowned = true;
        _stage   = QuestStage.Coronation;
    }

    private void AdvanceStage(QuestStage newStage, string message)
    {
        _stage = newStage;
        InformationManager.DisplayMessage(new InformationMessage(
            $"[Quest] {message}", Color.FromUint(0xFFD4AF37)));
    }

    private void OnBattleEnded(MapEvent mapEvent)
    {
        if (!mapEvent.IsPlayerMapEvent) return;
        bool playerWon = mapEvent.Winner?.LeaderParty == MobileParty.MainParty.Party;
        if (!playerWon) return;

        // Only counts if player was fighting as part of a kingdom army
        bool inKingdomArmy = MobileParty.MainParty.Army != null
                          && MobileParty.MainParty.Army.LeaderParty != MobileParty.MainParty;
        if (inKingdomArmy) _battlesWon++;
    }

    public QuestStage CurrentStage => _stage;
    public CoronationPath Path     => _path;
    public bool IsAmbitionDeclared => _stage >= QuestStage.PathChosen;
    public bool IsRestorationPath  => _path == CoronationPath.Restoration;

    public override void SyncData(IDataStore dataStore)
    {
        int stageInt = (int)_stage;
        int pathInt  = (int)_path;
        dataStore.SyncData("hind_mq_stage",      ref stageInt);
        dataStore.SyncData("hind_mq_path",       ref pathInt);
        dataStore.SyncData("hind_mq_battles",    ref _battlesWon);
        dataStore.SyncData("hind_mq_crowned",    ref _crowned);
        if (!dataStore.IsSaving)
        {
            _stage = (QuestStage)stageInt;
            _path  = (CoronationPath)pathInt;
        }
    }
}
```

---

## 8. Implementation: WarScoreBehavior

```csharp
public class WarScoreBehavior : CampaignBehaviorBase
{
    // war key = sorted kingdom id pair; value = score from attacker's perspective
    private Dictionary<string, float> _scores = new Dictionary<string, float>();
    private Dictionary<string, string> _attacker = new Dictionary<string, string>();

    public static WarScoreBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
        CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementChanged);
        CampaignEvents.OnHeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        CampaignEvents.OnWarDeclaredEvent.AddNonSerializedListener(this, OnWarDeclared);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnWarDeclared(IFaction att, IFaction def, DeclareWarAction.DeclareWarDetail d)
    {
        if (att is Kingdom ka && def is Kingdom kd)
        {
            string key = WarKey(ka, kd);
            _scores[key]   = 0f;
            _attacker[key] = ka.StringId;
        }
    }

    private void OnBattleEnded(MapEvent mapEvent)
    {
        MapEventSide winner = mapEvent.Winner;
        MapEventSide loser  = mapEvent.Loser;
        if (winner == null || loser == null) return;

        Kingdom winnerKingdom = winner.LeaderParty?.MapFaction as Kingdom;
        Kingdom loserKingdom  = loser.LeaderParty?.MapFaction as Kingdom;
        if (winnerKingdom == null || loserKingdom == null) return;

        string key = WarKey(winnerKingdom, loserKingdom);
        if (!_scores.ContainsKey(key)) return;

        int totalCasualties = (winner.Casualties ?? 0) + (loser.Casualties ?? 0);
        float delta = totalCasualties switch
        {
            > 500 => 15f,
            > 150 => 8f,
            _     => 3f
        };

        // Positive = attacker is winning
        bool attackerWon = _attacker.TryGetValue(key, out string attackerId)
                        && winnerKingdom.StringId == attackerId;

        AddScore(key, attackerWon ? delta : -delta);
    }

    private void OnSettlementChanged(Settlement s, bool openToClaim, Hero newOwner,
        Hero oldOwner, Hero capturer,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege) return;

        Kingdom newKingdom = newOwner?.Clan?.Kingdom;
        Kingdom oldKingdom = oldOwner?.Clan?.Kingdom;
        if (newKingdom == null || oldKingdom == null) return;

        string key   = WarKey(newKingdom, oldKingdom);
        float  delta = s.IsTown ? 10f : 5f;

        bool attackerGained = _attacker.TryGetValue(key, out string attackerId)
                           && newKingdom.StringId == attackerId;

        AddScore(key, attackerGained ? delta : -delta);
    }

    private void OnHeroKilled(Hero victim, Hero killer,
        KillCharacterAction.KillCharacterActionDetail d, bool show)
    {
        if (victim?.IsLord != true || killer == null) return;

        Kingdom victimKingdom = victim.Clan?.Kingdom;
        Kingdom killerKingdom = killer.Clan?.Kingdom;
        if (victimKingdom == null || killerKingdom == null) return;

        string key   = WarKey(killerKingdom, victimKingdom);
        bool captured = victim.IsPrisoner;
        float delta   = victim == victimKingdom.Leader ? 20f : captured ? 5f : 0f;

        if (delta > 0)
        {
            bool attackerKilled = _attacker.TryGetValue(key, out string attackerId)
                               && killerKingdom.StringId == attackerId;
            AddScore(key, attackerKilled ? delta : -delta);
        }
    }

    private void OnWeeklyTick()
    {
        // Decay toward 0
        foreach (var key in _scores.Keys.ToList())
            _scores[key] *= 0.98f;  // slow 2% weekly decay
    }

    private void AddScore(string key, float delta)
    {
        if (!_scores.ContainsKey(key)) _scores[key] = 0f;
        _scores[key] = Math.Clamp(_scores[key] + delta, -100f, 100f);
    }

    // Score from the PLAYER's perspective: positive = player is winning
    public float GetPlayerWarScore(Kingdom enemy)
    {
        Kingdom playerKingdom = Hero.MainHero?.Clan?.Kingdom;
        if (playerKingdom == null || enemy == null) return 0f;

        string key = WarKey(playerKingdom, enemy);
        if (!_scores.TryGetValue(key, out float score)) return 0f;

        bool playerIsAttacker = _attacker.TryGetValue(key, out string attackerId)
                             && playerKingdom.StringId == attackerId;

        return playerIsAttacker ? score : -score;
    }

    public float GetRawScore(Kingdom a, Kingdom b)
    {
        string key = WarKey(a, b);
        return _scores.GetValueOrDefault(key, 0f);
    }

    private static string WarKey(Kingdom a, Kingdom b)
        => string.Compare(a.StringId, b.StringId) < 0
            ? $"{a.StringId}|{b.StringId}"
            : $"{b.StringId}|{a.StringId}";

    public override void SyncData(IDataStore dataStore)
    {
        var keys  = _scores.Keys.ToList();
        var vals  = _scores.Values.ToList();
        var attKs = _attacker.Keys.ToList();
        var attVs = _attacker.Values.ToList();
        dataStore.SyncData("hind_ws_keys", ref keys);
        dataStore.SyncData("hind_ws_vals", ref vals);
        dataStore.SyncData("hind_ws_attk", ref attKs);
        dataStore.SyncData("hind_ws_attv", ref attVs);
        if (!dataStore.IsSaving)
        {
            _scores.Clear(); _attacker.Clear();
            for (int i = 0; i < keys.Count; i++)
                _scores[keys[i]] = i < vals.Count ? vals[i] : 0f;
            for (int i = 0; i < attKs.Count; i++)
                _attacker[attKs[i]] = i < attVs.Count ? attVs[i] : "";
        }
    }
}
```

---

## 9. Implementation: PeaceNegotiationBehavior

```csharp
public enum PeaceDemand
{
    WhitePeace, OneTimeTribute, AnnualTribute,
    OneSett, TwoSetts, Suzerainty, ThreeSetts,
    SuzeraintyPlusTribute, Annexation
}

public class PeaceNegotiationBehavior : CampaignBehaviorBase
{
    public static PeaceNegotiationBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, CheckAISuingForPeace);
    }

    private void CheckAISuingForPeace()
    {
        Kingdom playerKingdom = Hero.MainHero?.Clan?.Kingdom;
        if (playerKingdom == null) return;

        foreach (Kingdom enemy in Kingdom.All.Where(k => playerKingdom.IsAtWarWith(k)))
        {
            float score = WarScoreBehavior.Instance?.GetPlayerWarScore(enemy) ?? 0f;
            if (score >= 0) continue;  // player is winning or neutral; enemy doesn't sue yet

            float abs = Math.Abs(score);
            if (abs < 40f) continue;  // not desperate enough yet

            float sueProbability = (abs - 40f) / 120f;  // 0% at -40, 50% at -100
            if (MBRandom.RandomFloat < sueProbability / 52f) // weekly probability
                PresentEnemyPeaceOffer(enemy, score);
        }
    }

    private void PresentEnemyPeaceOffer(Kingdom enemy, float playerScore)
    {
        // enemy wants to sue — what are they willing to offer?
        PeaceDemand[] offers = GetAvailableDemandsForScore(-playerScore);
        // enemy offers from their side: at score -55, they offer what's available at +55

        string offerDescription = offers.Length > 0
            ? DescribeDemand(offers[0], enemy)
            : "White peace — no concessions from either side";

        InformationManager.ShowInquiry(new InquiryData(
            $"{enemy.Name} Sues for Peace",
            $"{enemy.Leader?.Name} sends envoys. The war goes badly for them. " +
            $"They offer: {offerDescription}.",
            true, true,
            "Accept their terms",
            "Refuse — continue the war",
            () => ExecutePeaceDeal(enemy, offers.Length > 0 ? offers[0] : PeaceDemand.WhitePeace,
                    isPlayerDemanding: true),
            () => { }
        ));
    }

    // Called from game menu when player initiates negotiation
    public void OpenNegotiationMenu(Kingdom enemy)
    {
        float score = WarScoreBehavior.Instance?.GetPlayerWarScore(enemy) ?? 0f;
        PeaceDemand[] available = GetAvailableDemandsForScore(score);

        // Build the menu options as InquiryElements
        // (Bannerlord's InquiryData supports multi-element lists)
        var options = available.Select(d => new InquiryElement(
            d.ToString(),
            DescribeDemand(d, enemy),
            null,
            true,
            $"Acceptance estimate: {CalculateAcceptanceProbability(enemy, d, score) * 100:F0}%"
        )).ToList();

        MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
            $"Negotiate Peace — {enemy.Name}",
            $"War score: {score:+0;-0} in your favour.\nSelect terms to propose:",
            options,
            true,  // canCloseWithoutSelection
            1,     // min selection
            1,     // max selection
            "Propose",
            "Cancel",
            selected =>
            {
                if (selected.Count == 0) return;
                PeaceDemand chosen = (PeaceDemand)Enum.Parse(typeof(PeaceDemand),
                    selected[0].Identifier);
                AttemptPeaceDeal(enemy, chosen, score);
            },
            _ => { }
        ));
    }

    private void AttemptPeaceDeal(Kingdom enemy, PeaceDemand demand, float score)
    {
        float chance = CalculateAcceptanceProbability(enemy, demand, score);
        bool accepted = MBRandom.RandomFloat < chance;

        if (accepted)
        {
            ExecutePeaceDeal(enemy, demand, isPlayerDemanding: true);
        }
        else
        {
            // Counter-offer
            PeaceDemand[] softer = GetAvailableDemandsForScore(score * 0.6f);
            if (softer.Length > 1)
            {
                PeaceDemand counter = softer[softer.Length / 2];
                InformationManager.ShowInquiry(new InquiryData(
                    "Terms Rejected",
                    $"{enemy.Leader?.Name} rejects your terms but offers a counter-proposal: " +
                    $"{DescribeDemand(counter, enemy)}. Do you accept?",
                    true, true,
                    "Accept the counter",
                    "Refuse",
                    () => ExecutePeaceDeal(enemy, counter, isPlayerDemanding: false),
                    () => InformationManager.DisplayMessage(new InformationMessage(
                        "Negotiations have broken down. The war continues."))
                ));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{enemy.Leader?.Name} rejects your terms outright. The war continues.",
                    Color.FromUint(0xFFCC4400)));
            }
        }
    }

    private void ExecutePeaceDeal(Kingdom enemy, PeaceDemand demand, bool isPlayerDemanding)
    {
        switch (demand)
        {
            case PeaceDemand.WhitePeace:
                MakePeaceAction.Apply(Hero.MainHero?.Clan?.Kingdom, enemy);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"White peace concluded with {enemy.Name}."));
                break;

            case PeaceDemand.OneTimeTribute:
                int tribute = (int)(enemy.Leader?.Clan?.Renown ?? 500) * 5;
                if (isPlayerDemanding && enemy.Leader != null)
                    GiveGoldAction.ApplyBetweenCharacters(enemy.Leader, Hero.MainHero, tribute);
                MakePeaceAction.Apply(Hero.MainHero?.Clan?.Kingdom, enemy);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Peace concluded. {enemy.Name} pays {tribute:N0} gold in tribute."));
                break;

            case PeaceDemand.AnnualTribute:
                SuzeraintyBehavior.Instance?.CreateTributaryRelation(
                    Hero.MainHero.Clan.Kingdom, enemy, tributeRate: 0.10f, isFull: false);
                MakePeaceAction.Apply(Hero.MainHero?.Clan?.Kingdom, enemy);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Peace concluded. {enemy.Name} agrees to pay annual tribute."));
                break;

            case PeaceDemand.Suzerainty:
            case PeaceDemand.SuzeraintyPlusTribute:
                if (!MainQuestBehavior.Instance?.IsAmbitionDeclared == true) break;
                float rate = demand == PeaceDemand.SuzeraintyPlusTribute ? 0.20f : 0.15f;
                SuzeraintyBehavior.Instance?.CreateTributaryRelation(
                    Hero.MainHero.Clan.Kingdom, enemy, tributeRate: rate, isFull: true);
                MakePeaceAction.Apply(Hero.MainHero?.Clan?.Kingdom, enemy);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{enemy.Name} acknowledges your suzerainty. Their king bows before the Shahanshah.",
                    Color.FromUint(0xFFD4AF37)));
                break;

            case PeaceDemand.OneSett:
            case PeaceDemand.TwoSetts:
            case PeaceDemand.ThreeSetts:
                int count = demand switch
                {
                    PeaceDemand.OneSett   => 1,
                    PeaceDemand.TwoSetts  => 2,
                    _                     => 3
                };
                TransferSettlements(enemy, count);
                MakePeaceAction.Apply(Hero.MainHero?.Clan?.Kingdom, enemy);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Peace concluded. {enemy.Name} cedes {count} settlement(s)."));
                break;

            case PeaceDemand.Annexation:
                // All enemy settlements transfer to player
                TransferSettlements(enemy, enemy.Settlements.Count(s => s.IsTown || s.IsCastle));
                MakePeaceAction.Apply(Hero.MainHero?.Clan?.Kingdom, enemy);
                break;
        }

        // Reset war score for this pair
        // (score resets on next war declaration — current war is over)
    }

    private void TransferSettlements(Kingdom enemy, int count)
    {
        var targets = enemy.Settlements
            .Where(s => s.IsTown || s.IsCastle)
            .OrderByDescending(s => s.IsTown)  // towns first
            .Take(count)
            .ToList();

        foreach (Settlement s in targets)
            ChangeOwnerOfSettlementAction.ApplyByDefault(Hero.MainHero, s);
    }

    private PeaceDemand[] GetAvailableDemandsForScore(float score)
    {
        bool suzeraintyUnlocked = MainQuestBehavior.Instance?.IsRestorationPath == true
                               && MainQuestBehavior.Instance?.IsAmbitionDeclared == true;

        var demands = new List<PeaceDemand> { PeaceDemand.WhitePeace };
        if (score >= 25) demands.Add(PeaceDemand.OneTimeTribute);
        if (score >= 35) demands.Add(PeaceDemand.AnnualTribute);
        if (score >= 50) demands.Add(PeaceDemand.OneSett);
        if (score >= 65 && suzeraintyUnlocked) demands.Add(PeaceDemand.Suzerainty);
        if (score >= 65) demands.Add(PeaceDemand.TwoSetts);
        if (score >= 80) demands.Add(PeaceDemand.ThreeSetts);
        if (score >= 90 && suzeraintyUnlocked) demands.Add(PeaceDemand.SuzeraintyPlusTribute);
        if (score >= 100) demands.Add(PeaceDemand.Annexation);
        return demands.ToArray();
    }

    private string DescribeDemand(PeaceDemand demand, Kingdom enemy) => demand switch
    {
        PeaceDemand.WhitePeace          => "White peace — status quo restored",
        PeaceDemand.OneTimeTribute      => $"One-time tribute payment from {enemy.Name}",
        PeaceDemand.AnnualTribute       => $"Annual tribute from {enemy.Name} (ongoing)",
        PeaceDemand.OneSett             => $"{enemy.Name} cedes one settlement",
        PeaceDemand.TwoSetts            => $"{enemy.Name} cedes two settlements",
        PeaceDemand.Suzerainty          => $"{enemy.Name} acknowledges your imperial suzerainty",
        PeaceDemand.ThreeSetts          => $"{enemy.Name} cedes three settlements",
        PeaceDemand.SuzeraintyPlusTribute => $"Suzerainty AND annual tribute from {enemy.Name}",
        PeaceDemand.Annexation          => $"Full annexation of {enemy.Name}",
        _                               => demand.ToString()
    };

    private float CalculateAcceptanceProbability(Kingdom enemy, PeaceDemand demand, float score)
    {
        float base_ = score * 0.8f;
        base_ -= demand switch
        {
            PeaceDemand.WhitePeace      => 0f,
            PeaceDemand.OneTimeTribute  => 10f,
            PeaceDemand.AnnualTribute   => 18f,
            PeaceDemand.OneSett         => 20f,
            PeaceDemand.TwoSetts        => 35f,
            PeaceDemand.Suzerainty      => 40f,
            PeaceDemand.ThreeSetts      => 50f,
            PeaceDemand.SuzeraintyPlusTribute => 55f,
            PeaceDemand.Annexation      => 90f,
            _                           => 0f
        };

        float strengthRatio = (float)(enemy.TotalStrength) /
            Math.Max(1, Hero.MainHero?.Clan?.Kingdom?.TotalStrength ?? 1);
        if (strengthRatio < 0.3f) base_ += 15f;
        if (strengthRatio > 0.8f) base_ -= 20f;

        if (TraitsBehavior.Instance?.HasTrait(enemy.Leader, CharacterTrait.Reckless) == true) base_ -= 15f;
        if (TraitsBehavior.Instance?.HasTrait(enemy.Leader, CharacterTrait.Cautious) == true) base_ += 10f;

        if (demand == PeaceDemand.Suzerainty || demand == PeaceDemand.SuzeraintyPlusTribute)
        {
            float legit = LegitimacyBehavior.Instance?.GetLegitimacy(Hero.MainHero) ?? 50f;
            base_ += (legit - 50f) * 0.3f;
        }

        return Math.Clamp(base_ / 100f, 0.02f, 0.95f);
    }

    public override void SyncData(IDataStore dataStore) { }
}
```

---

## 10. Implementation: SuzeraintyBehavior

```csharp
public class SuzeraintyBehavior : CampaignBehaviorBase
{
    private List<SuzerainRecord> _records = new List<SuzerainRecord>();

    public static SuzeraintyBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.OnWarDeclaredEvent.AddNonSerializedListener(this, OnWarDeclared);
    }

    public void CreateTributaryRelation(Kingdom suzerain, Kingdom vassal,
        float tributeRate, bool isFull)
    {
        // Remove any existing relation between these two
        _records.RemoveAll(r => r.SuzerainKingdomId == suzerain.StringId
                             && r.VassalKingdomId   == vassal.StringId);

        _records.Add(new SuzerainRecord
        {
            SuzerainKingdomId = suzerain.StringId,
            VassalKingdomId   = vassal.StringId,
            TributeRate       = tributeRate,
            AgreementDay      = (int)CampaignTime.Now.ToDays,
            NextTributeDay    = (int)CampaignTime.Now.ToDays + 365,
            IsActive          = true,
            MissedTributes    = 0,
            IsFullSuzerainty  = isFull
        });
    }

    public bool IsVassalOf(Kingdom vassal, Kingdom suzerain)
        => _records.Any(r => r.IsActive
                          && r.VassalKingdomId   == vassal.StringId
                          && r.SuzerainKingdomId == suzerain.StringId
                          && r.IsFullSuzerainty);

    public List<SuzerainRecord> GetActiveRecords(Kingdom suzerain)
        => _records.Where(r => r.IsActive && r.SuzerainKingdomId == suzerain?.StringId).ToList();

    private void OnDailyTick()
    {
        int today = (int)CampaignTime.Now.ToDays;

        foreach (var record in _records.Where(r => r.IsActive).ToList())
        {
            if (today >= record.NextTributeDay)
            {
                CollectTribute(record);
                int idx = _records.IndexOf(record);
                if (idx >= 0)
                {
                    var updated = record;
                    updated.NextTributeDay = today + 365;
                    _records[idx] = updated;
                }
            }
        }
    }

    private void OnWeeklyTick() => CheckVassalLoyalty();

    private void OnWarDeclared(IFaction att, IFaction def, DeclareWarAction.DeclareWarDetail d)
    {
        // Vassal declared war independently — violates suzerainty
        if (att is Kingdom attK)
        {
            var violated = _records.FirstOrDefault(r => r.IsActive
                && r.VassalKingdomId == attK.StringId
                && r.IsFullSuzerainty);

            if (violated.IsActive)
            {
                Kingdom suzerain = Kingdom.All.FirstOrDefault(k => k.StringId == violated.SuzerainKingdomId);
                if (suzerain?.Leader == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{attK.Name} has declared war without your sanction — a violation of their oath. " +
                        $"You may punish this breach.",
                        Color.FromUint(0xFFCC2200)));
            }
        }

        // Suzerain at war — notify vassal kingdoms
        if (att is Kingdom attKingdom)
        {
            foreach (var record in _records.Where(r => r.IsActive
                && r.SuzerainKingdomId == attKingdom.StringId
                && r.IsFullSuzerainty))
            {
                NotifyVassalOfWar(record, def as Kingdom);
            }
        }
    }

    private void CollectTribute(SuzerainRecord record)
    {
        Kingdom suzerain = Kingdom.All.FirstOrDefault(k => k.StringId == record.SuzerainKingdomId);
        Kingdom vassal   = Kingdom.All.FirstOrDefault(k => k.StringId == record.VassalKingdomId);
        if (suzerain == null || vassal == null) return;

        int vassalGold    = vassal.Leader?.Gold ?? 0;
        int tributeAmount = (int)(vassalGold * record.TributeRate);

        if (vassalGold >= tributeAmount && tributeAmount > 0)
        {
            GiveGoldAction.ApplyBetweenCharacters(vassal.Leader, suzerain.Leader, tributeAmount);

            int idx = _records.IndexOf(record);
            if (idx >= 0)
            {
                var updated = record;
                updated.MissedTributes = 0;
                _records[idx] = updated;
            }

            if (suzerain.Leader == Hero.MainHero)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{vassal.Name} pays annual tribute: {tributeAmount:N0} gold.",
                    Color.FromUint(0xFFD4AF37)));
        }
        else
        {
            int idx = _records.IndexOf(record);
            if (idx >= 0)
            {
                var updated = record;
                updated.MissedTributes++;
                _records[idx] = updated;

                if (updated.MissedTributes >= 2)
                {
                    updated.IsActive = false;
                    _records[idx] = updated;

                    if (suzerain.Leader == Hero.MainHero)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{vassal.Name} has defaulted on tribute twice. The agreement is void.",
                            Color.FromUint(0xFFCC2200)));
                }
            }
        }
    }

    private void NotifyVassalOfWar(SuzerainRecord record, Kingdom enemy)
    {
        Kingdom vassal   = Kingdom.All.FirstOrDefault(k => k.StringId == record.VassalKingdomId);
        Kingdom suzerain = Kingdom.All.FirstOrDefault(k => k.StringId == record.SuzerainKingdomId);
        if (vassal == null || suzerain == null || enemy == null) return;

        float legit = LegitimacyBehavior.Instance?.GetLegitimacy(suzerain.Leader) ?? 50f;
        float complianceChance = legit / 100f;

        if (MBRandom.RandomFloat < complianceChance)
            DeclareWarAction.Apply(vassal, enemy, DeclareWarAction.DeclareWarDetail.Default);
        else
        {
            int idx = _records.IndexOf(record);
            if (idx >= 0)
            {
                var updated = record;
                updated.MissedTributes++;
                _records[idx] = updated;
            }
        }
    }

    private void CheckVassalLoyalty()
    {
        Kingdom playerKingdom = Hero.MainHero?.Clan?.Kingdom;
        float legit = LegitimacyBehavior.Instance?.GetLegitimacy(Hero.MainHero) ?? 50f;

        for (int i = _records.Count - 1; i >= 0; i--)
        {
            var record = _records[i];
            if (!record.IsActive || record.SuzerainKingdomId != playerKingdom?.StringId) continue;

            if (legit < 25f && MBRandom.RandomFloat < 0.10f)
            {
                var updated = record;
                updated.IsActive = false;
                _records[i] = updated;

                Kingdom vassal = Kingdom.All.FirstOrDefault(k => k.StringId == record.VassalKingdomId);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{vassal?.Name} has renounced their suzerainty oath. Your weakness emboldens them.",
                    Color.FromUint(0xFFCC2200)));
            }
        }
    }

    public override void SyncData(IDataStore dataStore)
    {
        var sIds  = _records.Select(r => r.SuzerainKingdomId).ToList();
        var vIds  = _records.Select(r => r.VassalKingdomId).ToList();
        var rates = _records.Select(r => r.TributeRate).ToList();
        var days  = _records.Select(r => r.NextTributeDay).ToList();
        var acts  = _records.Select(r => r.IsActive ? 1 : 0).ToList();
        var miss  = _records.Select(r => r.MissedTributes).ToList();
        var full  = _records.Select(r => r.IsFullSuzerainty ? 1 : 0).ToList();

        dataStore.SyncData("hind_sz_sids",  ref sIds);
        dataStore.SyncData("hind_sz_vids",  ref vIds);
        dataStore.SyncData("hind_sz_rates", ref rates);
        dataStore.SyncData("hind_sz_days",  ref days);
        dataStore.SyncData("hind_sz_acts",  ref acts);
        dataStore.SyncData("hind_sz_miss",  ref miss);
        dataStore.SyncData("hind_sz_full",  ref full);

        if (!dataStore.IsSaving)
        {
            _records.Clear();
            for (int i = 0; i < sIds.Count; i++)
                _records.Add(new SuzerainRecord
                {
                    SuzerainKingdomId = sIds[i],
                    VassalKingdomId   = i < vIds.Count  ? vIds[i]  : "",
                    TributeRate       = i < rates.Count ? rates[i] : 0.15f,
                    NextTributeDay    = i < days.Count  ? days[i]  : 0,
                    IsActive          = i < acts.Count && acts[i] == 1,
                    MissedTributes    = i < miss.Count  ? miss[i]  : 0,
                    IsFullSuzerainty  = i < full.Count && full[i] == 1
                });
        }
    }
}

// SuzerainRecord struct with IsFullSuzerainty field
public struct SuzerainRecord
{
    public string SuzerainKingdomId;
    public string VassalKingdomId;
    public float  TributeRate;
    public int    AgreementDay;
    public int    NextTributeDay;
    public bool   IsActive;
    public int    MissedTributes;
    public bool   IsFullSuzerainty;  // true = full suzerainty; false = just annual tribute
}
```

---

**[← Chapter 20](20-Character-Depth-and-Intrigue.md)** | **[Home](Home.md)**
