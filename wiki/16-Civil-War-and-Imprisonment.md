# Chapter 16 — Civil War, Leadership Challenges, and Noble Imprisonment

> Corrections to the sawar system, the demotion mechanic, how an Amir-ul-Umara challenges the king, and how the player can arrest any noble including the king himself.

**[← Chapter 15](15-Lord-Progression-and-Mansabdari.md)** | **[Home](Home.md)**

---

## Contents

- [Sawar correction — total troops, not cavalry](#sawar-correction--total-troops-not-cavalry)
- [Demotion system](#demotion-system)
- [Leadership challenge — who, when, and why](#leadership-challenge--who-when-and-why)
- [Civil war setup](#civil-war-setup)
- [Clan side-selection algorithm](#clan-side-selection-algorithm)
- [Noble imprisonment system](#noble-imprisonment-system)
- [Troop desertion when challenging upward](#troop-desertion-when-challenging-upward)
- [Diplomacy mod integration notes](#diplomacy-mod-integration-notes)
- [Implementation: MansabdariBehavior (corrected)](#implementation-mansabdaribehavior-corrected)
- [Implementation: CivilWarBehavior](#implementation-civilwarbehavior)
- [Implementation: ArrestBehavior](#implementation-arrestbehavior)

---

## Sawar Correction — Total Troops, Not Cavalry

Chapter 15 was wrong. Sawar is now **total regular troops across all parties led by heroes in the clan**, not cavalry only. This is also more historically accurate — the sawar obligation covered all arms, not just horse.

### How to count clan troops

```csharp
public static int GetClanTotalTroops(Clan clan)
{
    return MobileParty.All
        .Where(p => p.LeaderHero?.Clan == clan
                 && p.IsActive
                 && !p.IsGarrison)   // exclude garrison — those are static defence, not field strength
        .Sum(p => p.MemberRoster.TotalRegulars);  // TotalRegulars excludes hero lords themselves
}
```

### Revised rank table (max 600 troops)

| Rank | Title (Mughal) | Field troops required | Fief tier | Tax rate paid upward |
|------|----------------|-----------------------|-----------|---------------------|
| 100 | Zamindar | 25 | 1 village | 15% |
| 500 | Mansabdar-e-Panjsad | 100 | 1 castle | 12% |
| 1000 | Qiledar | 200 | 1 major castle | 10% |
| 2000 | Faujdar | 350 | 1 town | 8% |
| 3000 | Subahdar | 500 | multi-settlement | 6% |
| 5000 | Amir-ul-Umara | 600 | council seat + largest fief | 5% |

### Revised promotion table

| Current → Next | Required Renown | Required Valour | Required Troops |
|----------------|----------------|-----------------|-----------------|
| 100 → 500 | 200 | 100 | 100 |
| 500 → 1000 | 600 | 300 | 200 |
| 1000 → 2000 | 1200 | 700 | 350 |
| 2000 → 3000 | 2500 | 1500 | 500 |
| 3000 → 5000 | 4000 | 2500 | 600 |

---

## Demotion System

Ranks can be lost. If a mansabdar's field troop count falls below their current rank's **minimum** and stays there, they are demoted to the rank that their current troops actually justify.

### Demotion thresholds

The minimum troops to *retain* a rank is 75% of the threshold required to *gain* it:

| Rank held | Troop minimum to retain it |
|-----------|---------------------------|
| 5000 | 450 (75% of 600) |
| 3000 | 375 (75% of 500) |
| 2000 | 263 (75% of 350) |
| 1000 | 150 (75% of 200) |
| 500 | 75 (75% of 100) |
| 100 | 19 (75% of 25) |

### Demotion timeline

```
Day 0:   Troops drop below retention threshold
Day 14:  Warning issued to player (Notification message)
Day 30:  Demotion triggers — rank reduced by one step
         Fief is downgraded (town → castle, castle → village, etc.)
```

When demoted:
1. `MansabdariRank` drops by one tier
2. Current fief is revoked and replaced with an appropriate lower-tier fief
3. `_playerValour` penalty: −20
4. King relation penalty: −10 (failure to maintain obligations)

The player CAN recover: rebuild troops above the promotion threshold and re-petition.

### Demotion of AI lords

AI lords are also demoted weekly if their troop count drops. This creates a natural churn — lords who get their armies destroyed in bad battles lose rank, and their fiefs become available for re-assignment.

---

## Leadership Challenge — Who, When, and Why

### Who can challenge

- **Rank 5000 (Amir-ul-Umara)**: Can challenge the king directly for rulership.
- **Any rank (player-initiated)**: The player can challenge at any rank if the civil war trigger conditions are met — they are betting everything on a rebellion.

For AI, only Rank 5000 holders initiate challenges.

### Automatic triggers (for AI and as conditions for player)

The system evaluates trigger conditions weekly. Any ONE being true opens the challenge window:

| Trigger | Condition |
|---------|-----------|
| Military failure | Kingdom has lost 3+ battles in the last 84 days (1 in-game year) |
| Territory loss | Kingdom has lost 3+ settlements (towns/castles) in the last year |
| Unpopular king | King's average relation with all clan leaders < −20 |
| Humiliation | Kingdom is currently losing 2+ simultaneous wars |
| Usurper opportunity | Challenger's valour is 300+ higher than the king's effective score |
| Player-initiated | Player has renown 1500+ AND is Rank 2000+ (no trigger requirement) |

### The challenge option

When conditions are met, a menu option appears in the player's throne city:

```
[Declare a leadership challenge against the king]
```

For AI, the Rank 5000 holder performs a probability check:
```csharp
float challengeProbability = triggerCount * 0.15f;  // 15% per active trigger
// At 3 triggers: 45% chance per year of an AI challenge occurring
```

---

## Civil War Setup

### Step 1 — The challenger leaves the kingdom

```csharp
private void InitiateCivilWar(Hero challenger, Kingdom targetKingdom)
{
    // 1. Challenger's clan leaves the kingdom
    ChangeKingdomAction.ApplyByLeaveKingdom(challenger.Clan, showNotification: false);

    // 2. Create (or use) the rebel faction
    //    If challenger's clan already had a prior kingdom claim, reuse it.
    //    Otherwise create a new rebel kingdom.
    Kingdom rebelKingdom = GetOrCreateRebelKingdom(challenger, targetKingdom);

    // 3. Declare war between rebel and loyalist kingdoms
    DeclareWarAction.Apply(rebelKingdom, targetKingdom,
        DeclareWarAction.DeclareWarDetail.Default);

    // 4. Give other clans 7 days to choose sides
    _pendingCivilWar = new CivilWarRecord
    {
        Challenger      = challenger,
        Loyalist        = targetKingdom,
        Rebel           = rebelKingdom,
        ChoiceDeadline  = (int)CampaignTime.Now.ToDays + 7,
        TriggerReasons  = GetActiveTriggers(targetKingdom)
    };

    string msg = challenger == Hero.MainHero
        ? $"You have declared a challenge for the throne of {targetKingdom.Name}!"
        : $"{challenger.Name} has declared a challenge for the throne of {targetKingdom.Name}!";

    InformationManager.DisplayMessage(new InformationMessage(
        msg, Color.FromUint(0xFFCC2200)));
}
```

### Step 2 — Create the rebel kingdom

```csharp
private Kingdom GetOrCreateRebelKingdom(Hero challenger, Kingdom parent)
{
    // Use Bannerlord's kingdom creation system
    // NOTE: The exact API depends on the game version.
    // In v1.4.6, clan leaders can create kingdoms if they hold a settlement.
    // We replicate that flow here.

    Settlement capitalFief = FiefHierarchy.GetFiefOf(challenger) as Settlement
                          ?? challenger.HomeSettlement;

    Kingdom rebel = Kingdom.CreateKingdom(
        new TextObject($"{{=!}}Rebel {challenger.Clan.Name}"),
        new TextObject($"{{=!}}Rebel"),
        parent.Culture,
        challenger,
        capitalFief,
        challenger.Clan.Banner);

    ChangeKingdomAction.ApplyByJoinToKingdom(challenger.Clan, rebel);
    return rebel;
}
```

> **API note:** `Kingdom.CreateKingdom` may have a different signature in v1.4.6. If it does not exist, use the equivalent: check `TaleWorlds.CampaignSystem.Actions.CreateKingdomAction` or the player's existing "create kingdom" flow and replicate it via Harmony or the action class.

### Step 3 — Victory and defeat conditions

The civil war ends when:

| Condition | Outcome |
|-----------|---------|
| Challenger captures the king's home settlement | Challenger wins; king is captured or exiled |
| Challenger defeats king in battle and captures him | Challenger wins; can execute, imprison, or exile |
| King defeats challenger in battle and captures them | Challenge fails; challenger is imprisoned or executed |
| Challenger's faction is eliminated (no settlements) | Challenge fails |
| Peace decision passes (via Bannerlord's `MakePeaceAction`) | Civil war ends without a winner |

On challenger victory:
```csharp
private void OnChallengerVictory(CivilWarRecord war)
{
    // The challenger becomes the new ruler
    ChangeRulingClanAction.Apply(war.Loyalist, war.Challenger.Clan);

    // Merge the rebel faction back into the original kingdom
    // (or the rebel faction absorbs the loyalist kingdom)
    foreach (Clan clan in war.Rebel.Clans.ToList())
    {
        if (clan != war.Challenger.Clan)
            ChangeKingdomAction.ApplyByJoinToKingdom(clan, war.Loyalist);
    }
    ChangeKingdomAction.ApplyByJoinToKingdom(war.Challenger.Clan, war.Loyalist);

    InformationManager.DisplayMessage(new InformationMessage(
        $"{war.Challenger.Name} has taken the throne of {war.Loyalist.Name}!",
        Color.FromUint(0xFFD4AF37)));
}
```

---

## Clan Side-Selection Algorithm

Seven days after the challenge is declared, all clans in the kingdom choose sides. Their decision is based on:

```csharp
private void ResolveClanSides(CivilWarRecord war)
{
    Kingdom loyalistKingdom = war.Loyalist;
    Hero    challenger      = war.Challenger;
    Hero    king            = loyalistKingdom.Leader;

    foreach (Clan clan in loyalistKingdom.Clans.ToList())
    {
        if (clan == king.Clan)          continue; // king's own clan stays loyalist
        if (clan == challenger.Clan)    continue; // already rebel

        // Score: positive = favour challenger, negative = favour king
        int score = 0;

        // Relation with challenger vs king
        if (clan.Leader != null)
        {
            int relChallenger = CharacterRelationManager
                .GetHeroRelation(challenger, clan.Leader);
            int relKing = CharacterRelationManager
                .GetHeroRelation(king, clan.Leader);
            score += (relChallenger - relKing);
        }

        // Has the king given them a good fief? Loyal clans stay.
        bool hasGoodFief = clan.Settlements.Any(s => s.IsTown);
        if (hasGoodFief) score -= 20;

        // Active trigger bonus — clans unhappy with the war situation side with challenger
        score += war.TriggerReasons.Count * 5;

        // Random factor (court intrigue is unpredictable)
        score += MBRandom.RandomInt(-15, 15);

        if (score > 10)
        {
            // Defect to challenger
            ChangeKingdomAction.ApplyByJoinToKingdom(clan, war.Rebel,
                showNotification: false);

            if (war.Challenger == Hero.MainHero)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{clan.Name} has declared for your cause!",
                    Color.FromUint(0xFF44AA44)));
        }
        // else: clan stays loyalist (default — do nothing)
    }
}
```

### Player civil war — notable support

When the **player** triggers a civil war (rather than an AI Amir-ul-Umara), the player's legitimacy comes not from rank but from their **relations with notables**. If the player has:

- 75+ relation with **at least N notables** (N = total kingdom settlements ÷ 4, minimum 5)

...then the civil war is broadly supported and more clans defect. If not, the challenge fails immediately — the player lacks popular backing and their troops don't follow.

```csharp
private bool PlayerHasCivilWarLegitimacy()
{
    Kingdom kingdom = Hero.MainHero?.Clan?.Kingdom;
    if (kingdom == null) return false;

    int totalSettlements = kingdom.Settlements.Count(s => s.IsTown || s.IsCastle);
    int notableThreshold = Math.Max(5, totalSettlements / 4);

    int supportingNotables = Settlement.All
        .Where(s => s.OwnerClan?.Kingdom == kingdom)
        .SelectMany(s => s.Notables)
        .Count(n => CharacterRelationManager.GetHeroRelation(Hero.MainHero, n) >= 75);

    return supportingNotables >= notableThreshold;
}
```

---

## Noble Imprisonment System

### The dialogue option

Every hero the player can talk to (any lord, including allies and the king) gets a new dialogue line: **"I am placing you under arrest."**

This option appears ALWAYS — but consequences vary dramatically based on who you're arresting and your standing.

```csharp
// Wire into campaign dialogue via CampaignGameStarter:
starter.AddDialogue(new ConversationCharacter(/* any lord */), 
    "place_under_arrest_option",
    "{=!}I am placing you under arrest.",
    () => IsValidArrestTarget(Hero.OneToOneConversationHero),
    OpenArrestDialogue
);

private bool IsValidArrestTarget(Hero target)
{
    if (target == null || !target.IsAlive || !target.IsLord) return false;
    if (target == Hero.MainHero) return false;
    return true;  // can attempt to arrest anyone
}
```

### The arrest declaration

When the player declares arrest on a target:

1. **The target is set as pending arrest** in `_pendingArrestTargets`
2. **Troop desertion fires immediately** (see below) — some troops leave camp on the spot
3. **The target's party becomes hostile:**
   - If target is in an enemy kingdom: already hostile, normal battle
   - If target is a neutral or ally: their clan temporarily becomes hostile to the player's clan
4. **Player must now defeat the target's party in open battle** — there is no instant capture

> The system does NOT auto-start a battle. The player must ride to the target and engage them manually. This makes the imprisonment a physical act, not a menu click.

```csharp
public void DeclareArrest(Hero target)
{
    _pendingArrestTargets.Add(target.StringId);

    // Apply desertion (immediate)
    float desertRate = CalculateDesertionRate(Hero.MainHero, target);
    ApplyDesertion(MobileParty.MainParty, desertRate);

    // Make target's clan hostile to player's clan
    if (target.Clan?.Kingdom == Hero.MainHero?.Clan?.Kingdom)
    {
        // Ally arrest — their clan becomes temporarily hostile
        _tempHostileClans.Add(target.Clan.StringId);
        // In the daily tick, we check if any party led by a hostile-clan hero
        // is near the player and treat encounters as battles
    }

    string tierName = GetRankTitle(target);
    InformationManager.DisplayMessage(new InformationMessage(
        $"You have declared the arrest of {target.Name} ({tierName}). " +
        $"Defeat them in battle to complete the capture.",
        Color.FromUint(0xFFCC4400)));
}
```

### After defeating the target

In `OnMapEventEnded`, check if the captured hero was in `_pendingArrestTargets`:

```csharp
private void OnBattleEnded(MapEvent mapEvent)
{
    if (mapEvent.Winner?.LeaderParty != MobileParty.MainParty.Party) return;

    foreach (var party in mapEvent.PartiesOnSide(BattleSideEnum.Defender))
    {
        Hero capturedHero = party.Party?.LeaderHero;
        if (capturedHero == null) continue;
        if (!_pendingArrestTargets.Contains(capturedHero.StringId)) continue;

        _pendingArrestTargets.Remove(capturedHero.StringId);
        _tempHostileClans.Remove(capturedHero.Clan?.StringId);

        // Imprison them
        TakePrisonerAction.Apply(MobileParty.MainParty.Party, capturedHero);

        OnArrestCompleted(capturedHero);
    }
}

private void OnArrestCompleted(Hero imprisoned)
{
    bool wasAlly = imprisoned.Clan?.Kingdom == Hero.MainHero?.Clan?.Kingdom;

    if (wasAlly)
    {
        Kingdom kingdom = Hero.MainHero?.Clan?.Kingdom;
        Hero    king    = kingdom?.Leader;

        // Player's clan is now at war with the kingdom
        if (kingdom != null)
        {
            ChangeKingdomAction.ApplyByLeaveKingdom(
                Hero.MainHero.Clan, showNotification: false);
            if (kingdom.RulingClan != null)
                DeclareWarAction.Apply(Hero.MainHero.Clan.Kingdom ?? CreateRebelKingdom(),
                    kingdom, DeclareWarAction.DeclareWarDetail.Default);
        }

        // Check if civil war triggers
        if (PlayerHasCivilWarLegitimacy())
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Kingdom Is Divided",
                $"You have imprisoned {imprisoned.Name}. Lords and notables across the kingdom " +
                $"are rallying to your cause. Will you press your claim for the throne?",
                true, true,
                "Press my claim — civil war",
                "This was personal, not political",
                () => InitiateCivilWar(Hero.MainHero, kingdom),
                () =>
                {
                    // Just at war with the kingdom as a rebel clan, no full civil war
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Your clan is now at war with the kingdom."));
                }
            ));
        }
        else
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"You have imprisoned {imprisoned.Name}. " +
                $"Your clan is now at war with {kingdom?.Name}.",
                Color.FromUint(0xFFCC2200)));
        }
    }
    else
    {
        InformationManager.DisplayMessage(new InformationMessage(
            $"{imprisoned.Name} has been captured and imprisoned."));
    }
}
```

---

## Troop Desertion When Challenging Upward

When the player declares arrest on someone, or formally launches a civil war, some troops desert immediately. The amount depends on the **rank gap** between the challenger and the target.

The logic: lower-ranked soldiers serving under the player are uncertain about fighting against someone with imperial authority over them. The wider the rank gap, the more uncertainty.

### Desertion rate table

| Challenger rank vs Target rank | Desertion rate |
|-------------------------------|----------------|
| Challenger outranks target | 5% (minor morale impact) |
| Same rank | 8% |
| Target 1 tier above | 15% |
| Target 2 tiers above | 25% |
| Target 3 tiers above | 35% |
| Target is the King | 40% + 5% per active trigger condition (capped at 65%) |

```csharp
public float CalculateDesertionRate(Hero challenger, Hero challenged)
{
    int challengerTier = GetRankTier(challenger);
    int challengedTier = GetRankTier(challenged);

    // Special case: challenging the king
    bool targetIsKing = challenged == challenged.Clan?.Kingdom?.Leader;

    if (targetIsKing)
    {
        int triggers = GetActiveTriggers(challenged.Clan.Kingdom).Count;
        return Math.Min(0.65f, 0.40f + triggers * 0.05f);
    }

    int diff = challengedTier - challengerTier;
    return diff switch
    {
        <= 0 => 0.05f + (0.03f * Math.Abs(diff)),  // 5% base, less if outranking
        1    => 0.15f,
        2    => 0.25f,
        3    => 0.35f,
        _    => 0.50f
    };
}

private int GetRankTier(Hero hero)
{
    // Check if they are the king
    if (hero == hero.Clan?.Kingdom?.Leader) return 7;

    int rank = MansabdariBehavior.Instance?.GetRank(hero) ?? 100;
    return rank switch
    {
        >= 5000 => 6,
        >= 3000 => 5,
        >= 2000 => 4,
        >= 1000 => 3,
        >= 500  => 2,
        _       => 1
    };
}

public void ApplyDesertion(MobileParty party, float rate)
{
    if (rate <= 0f || party?.MemberRoster == null) return;

    var troopsToRemove = new List<(CharacterObject troop, int count)>();

    foreach (var element in party.MemberRoster.GetTroopRoster())
    {
        if (element.Character?.IsHero == true) continue;  // heroes don't desert
        int toRemove = (int)(element.Number * rate);
        if (toRemove > 0)
            troopsToRemove.Add((element.Character, toRemove));
    }

    foreach (var (troop, count) in troopsToRemove)
        party.MemberRoster.AddToCounts(troop, -count);

    int totalRemoved = troopsToRemove.Sum(t => t.count);
    if (totalRemoved > 0 && party == MobileParty.MainParty)
    {
        InformationManager.DisplayMessage(new InformationMessage(
            $"{totalRemoved} troops have deserted your camp — uncertain about " +
            $"fighting against a superior lord.",
            Color.FromUint(0xFFFF6600)));
    }
}
```

---

## Diplomacy Mod Integration Notes

The user wants this to build on the **Diplomacy mod** if present. The Diplomacy mod (by various authors) adds:
- Non-aggression pacts
- Alliance support systems
- Enhanced peace negotiation
- Kingdom destruction prevention settings

### Integration approach

Build the civil war system as a **standalone** that uses vanilla APIs. Then add conditional hooks that call Diplomacy mod methods IF the mod is loaded:

```csharp
private static bool _diplomacyModPresent = false;

public static void CheckForDiplomacyMod()
{
    // Check if Diplomacy mod assembly is loaded
    _diplomacyModPresent = AppDomain.CurrentDomain.GetAssemblies()
        .Any(a => a.GetName().Name.Contains("Diplomacy"));
    Debug.Print($"[Hindostan] Diplomacy mod present: {_diplomacyModPresent}");
}
```

### Where Diplomacy mod hooks matter

| Our system | Diplomacy mod hook | Notes |
|------------|-------------------|-------|
| Civil war declaration | `DiplomacyEvents.OnWarDeclared` (if it exists) | Diplomacy mod may apply war justification scoring |
| Alliance side-picking | Diplomacy alliance system | Kingdoms with formal alliances side with their ally |
| Peace resolution | Diplomacy peace score system | Civil war may resolve via diplomacy if both sides want out |
| Kingdom destruction | Diplomacy may prevent kingdom elimination | Override their "don't destroy kingdoms" setting for the rebel faction |

The key rule: **never depend on Diplomacy mod being present.** All critical paths must work without it. Diplomacy integrations are additive bonuses.

---

## Implementation: MansabdariBehavior (corrected)

Replaces the version in Chapter 15 with corrected sawar and demotion logic.

```csharp
public class MansabdariBehavior : CampaignBehaviorBase
{
    // Mansab records: heroStringId → current rank value (100, 500, 1000, 2000, 3000, 5000)
    private Dictionary<string, int> _ranks             = new Dictionary<string, int>();
    private Dictionary<string, int> _daysBelowThreshold = new Dictionary<string, int>();
    // days counter tracking how long a hero has been below their retention threshold

    public static MansabdariBehavior Instance { get; private set; }

    private static readonly int[] RankValues    = { 100, 500, 1000, 2000, 3000, 5000 };
    private static readonly int[] PromoteTroops = { 100, 200, 350, 500, 600, 9999 };  // troops to gain next rank
    private static readonly int[] RetainTroops  = {  19,  75, 150, 263, 375, 450 };   // troops to keep current rank

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        var heroIds   = _ranks.Keys.ToList();
        var rankVals  = _ranks.Values.ToList();
        var badDays   = _daysBelowThreshold.Values.ToList();

        dataStore.SyncData("hind_mansab_ids",  ref heroIds);
        dataStore.SyncData("hind_mansab_vals", ref rankVals);
        dataStore.SyncData("hind_mansab_bad",  ref badDays);

        if (!dataStore.IsSaving)
        {
            _ranks.Clear();
            _daysBelowThreshold.Clear();
            for (int i = 0; i < heroIds.Count; i++)
            {
                if (i < rankVals.Count) _ranks[heroIds[i]]              = rankVals[i];
                if (i < badDays.Count)  _daysBelowThreshold[heroIds[i]] = badDays[i];
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public int GetRank(Hero hero)
    {
        _ranks.TryGetValue(hero.StringId, out int rank);
        return rank;  // 0 = unranked (not in the system)
    }

    public string GetRankTitle(Hero hero)
    {
        string cultureId = hero.Culture?.StringId ?? "";
        int rank = GetRank(hero);

        return (cultureId, rank) switch
        {
            ("empire", 100)    => "Zamindar",
            ("empire", 500)    => "Mansabdar-e-Panjsad",
            ("empire", 1000)   => "Qiledar",
            ("empire", 2000)   => "Faujdar",
            ("empire", 3000)   => "Subahdar",
            ("empire", 5000)   => "Amir-ul-Umara",
            ("battania", 100)  => "Village Sardar",
            ("battania", 500)  => "Killedar",
            ("battania", 1000) => "Senior Killedar",
            ("battania", 2000) => "Sardar",
            ("battania", 3000) => "Pradhan",
            ("battania", 5000) => "Peshwa's Right Hand",
            ("vlandia", 100)   => "Patel",
            ("vlandia", 500)   => "Thakur",
            ("vlandia", 1000)  => "Rao",
            ("vlandia", 2000)  => "Raja",
            ("vlandia", 3000)  => "Maharaja",
            ("vlandia", 5000)  => "Maharajadhiraj",
            _                  => $"Rank {rank}"
        };
    }

    public bool CanPromote(Hero hero)
    {
        int currentRank = GetRank(hero);
        int tierIndex   = Array.IndexOf(RankValues, currentRank);
        if (tierIndex < 0 || tierIndex >= RankValues.Length - 1) return false;

        int troops  = GetClanTotalTroops(hero.Clan);
        int renown  = (int)(hero.Clan?.Renown ?? 0);
        float valour = hero == Hero.MainHero
            ? ValourBehavior.Instance?.Valour ?? 0f : 0f;

        int nextTier = tierIndex + 1;
        return troops  >= PromoteTroops[tierIndex]
            && renown  >= GetPromoteRenown(tierIndex)
            && valour  >= GetPromoteValour(tierIndex);
    }

    public void Promote(Hero hero)
    {
        int currentRank = GetRank(hero);
        int tierIndex   = Array.IndexOf(RankValues, currentRank);
        if (tierIndex < 0 || tierIndex >= RankValues.Length - 1) return;

        _ranks[hero.StringId] = RankValues[tierIndex + 1];
        _daysBelowThreshold.Remove(hero.StringId);

        if (hero == Hero.MainHero)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"Promoted to {GetRankTitle(hero)}! Your new obligations apply immediately.",
                Color.FromUint(0xFFD4AF37)));
        }
    }

    // ── Weekly evaluation ─────────────────────────────────────────────────────
    private void OnWeeklyTick()
    {
        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
        {
            foreach (Clan clan in kingdom.Clans)
            {
                foreach (Hero hero in clan.Heroes.Where(h => h.IsAlive && h.IsLord))
                {
                    EvaluateRetention(hero);
                }
            }
        }
    }

    private void EvaluateRetention(Hero hero)
    {
        int rank = GetRank(hero);
        if (rank == 0) return;

        int tierIndex = Array.IndexOf(RankValues, rank);
        if (tierIndex < 0) return;

        int troops        = GetClanTotalTroops(hero.Clan);
        int retainMinimum = RetainTroops[tierIndex];

        if (troops < retainMinimum)
        {
            _daysBelowThreshold.TryGetValue(hero.StringId, out int days);
            days += 7;
            _daysBelowThreshold[hero.StringId] = days;

            if (days == 14 && hero == Hero.MainHero)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"WARNING: Your field strength ({troops}) is below your rank obligation " +
                    $"of {retainMinimum}. Recruit more troops within 16 days or face demotion.",
                    Color.FromUint(0xFFFF6600)));
            }
            else if (days >= 30)
            {
                Demote(hero);
            }
        }
        else
        {
            // Recovering — reset counter
            _daysBelowThreshold.Remove(hero.StringId);
        }
    }

    private void Demote(Hero hero)
    {
        int currentRank = GetRank(hero);
        int tierIndex   = Array.IndexOf(RankValues, currentRank);
        if (tierIndex <= 0) return;  // already at minimum rank, can't go lower

        int newRank = RankValues[tierIndex - 1];
        _ranks[hero.StringId] = newRank;
        _daysBelowThreshold.Remove(hero.StringId);

        if (hero == Hero.MainHero)
        {
            ValourBehavior.Instance?.RemoveValour(20f, "Demoted");
            Hero king = hero.Clan?.Kingdom?.Leader;
            if (king != null)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, king, -10);

            InformationManager.DisplayMessage(new InformationMessage(
                $"You have been demoted to {GetRankTitle(hero)} for failing to maintain " +
                $"your troop obligation. Your fief will be reassigned.",
                Color.FromUint(0xFFCC2200)));
        }

        // Downgrade fief
        FiefHolderBehavior.Instance?.DowngradeFiefForHero(hero);
    }

    private void OnNewGameCreated(CampaignGameStarter starter) => AssignStartingRanks();
    private void OnGameLoaded(CampaignGameStarter starter)
    {
        Instance = this;
        if (_ranks.Count == 0) AssignStartingRanks();
    }

    private void AssignStartingRanks()
    {
        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
        {
            foreach (Clan clan in kingdom.Clans)
            {
                foreach (Hero hero in clan.Heroes.Where(h => h.IsAlive && h.IsLord))
                {
                    int troops = GetClanTotalTroops(clan);
                    int rank = troops switch
                    {
                        >= 600 => 5000,
                        >= 500 => 3000,
                        >= 350 => 2000,
                        >= 200 => 1000,
                        >= 100 => 500,
                        _      => 100
                    };
                    _ranks[hero.StringId] = rank;
                }
            }
        }
    }

    private int GetPromoteRenown(int tierIndex) => new[] { 200, 600, 1200, 2500, 4000, 9999 }[tierIndex];
    private int GetPromoteValour(int tierIndex) => new[] { 100, 300,  700, 1500, 2500, 9999 }[tierIndex];

    public static int GetClanTotalTroops(Clan clan)
    {
        if (clan == null) return 0;
        return MobileParty.All
            .Where(p => p.LeaderHero?.Clan == clan && p.IsActive && !p.IsGarrison)
            .Sum(p => p.MemberRoster.TotalRegulars);
    }
}
```

---

## Implementation: CivilWarBehavior

```csharp
public class CivilWarBehavior : CampaignBehaviorBase
{
    private class CivilWarRecord
    {
        public Hero     Challenger;
        public Kingdom  Loyalist;
        public Kingdom  Rebel;
        public int      ChoiceDeadline;   // in-game absolute day
        public List<string> TriggerReasons = new List<string>();
    }

    private CivilWarRecord _activeCivilWar = null;
    private HashSet<string> _activeWars    = new HashSet<string>();

    public static CivilWarBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
        CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(
            this, OnSettlementChanged);
    }

    public override void SyncData(IDataStore dataStore)
    {
        // Persist active war state if needed
        // (simplified — full implementation needs to serialize CivilWarRecord)
    }

    private void OnWeeklyTick()
    {
        CheckForNewWars();
        if (_activeCivilWar != null) EvaluateCivilWarProgress();
        EvaluateAIChallenges();

        // Side-selection deadline
        if (_activeCivilWar != null &&
            (int)CampaignTime.Now.ToDays >= _activeCivilWar.ChoiceDeadline)
            ResolveClanSides(_activeCivilWar);
    }

    // Detect new wars and notify the player of call to arms
    private void CheckForNewWars()
    {
        Kingdom playerKingdom = Hero.MainHero?.Clan?.Kingdom;
        if (playerKingdom == null) return;

        foreach (Kingdom other in Kingdom.All.Where(k => k != playerKingdom))
        {
            string key = GetWarKey(playerKingdom, other);
            bool atWar  = playerKingdom.IsAtWarWith(other);

            if (atWar && !_activeWars.Contains(key))
            {
                _activeWars.Add(key);
                FiefHolderBehavior.Instance?.TriggerCallToArms(playerKingdom, other);
            }
            else if (!atWar)
            {
                _activeWars.Remove(key);
            }
        }
    }

    private string GetWarKey(Kingdom a, Kingdom b)
        => string.Compare(a.StringId, b.StringId) < 0
            ? $"{a.StringId}|{b.StringId}"
            : $"{b.StringId}|{a.StringId}";

    private List<string> GetActiveTriggers(Kingdom kingdom)
    {
        var triggers = new List<string>();
        // (Implementation references the trigger conditions described above)
        // simplified version for readability:
        int warCount = Kingdom.All.Count(k => kingdom.IsAtWarWith(k));
        if (warCount >= 2) triggers.Add("losing_two_wars");
        if (kingdom.Leader != null)
        {
            int avgRelation = kingdom.Clans
                .Where(c => c != kingdom.RulingClan && c.Leader != null)
                .Select(c => CharacterRelationManager.GetHeroRelation(kingdom.Leader, c.Leader))
                .DefaultIfEmpty(0).Average() is double avg ? (int)avg : 0;
            if (avgRelation < -20) triggers.Add("unpopular_king");
        }
        return triggers;
    }

    private void EvaluateAIChallenges()
    {
        if (_activeCivilWar != null) return;  // one at a time

        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
        {
            var triggers = GetActiveTriggers(kingdom);
            if (triggers.Count == 0) continue;

            float probability = triggers.Count * 0.15f / 52f; // weekly probability
            if (MBRandom.RandomFloat > probability) continue;

            // Find the Amir-ul-Umara
            Hero challenger = kingdom.Clans
                .SelectMany(c => c.Heroes)
                .Where(h => h.IsAlive && h.IsLord && h != kingdom.Leader
                         && MansabdariBehavior.Instance?.GetRank(h) >= 5000)
                .OrderByDescending(h => h.Clan?.Renown ?? 0)
                .FirstOrDefault();

            if (challenger != null)
                InitiateCivilWar(challenger, kingdom);
        }
    }

    private void EvaluateCivilWarProgress()
    {
        // Check victory conditions
        var war = _activeCivilWar;
        if (war == null) return;

        if (war.Rebel.IsEliminated || war.Rebel.Settlements.Count == 0)
        {
            EndCivilWar(war, challengerWins: false);
            return;
        }

        bool kinglyCaptured = war.Loyalist.Leader != null &&
            war.Loyalist.Leader.IsPrisoner &&
            war.Loyalist.Leader.PartyBelongedToAsPrisoner?.LeaderHero?.Clan == war.Challenger.Clan;

        if (kinglyCaptured)
        {
            EndCivilWar(war, challengerWins: true);
        }
    }

    private void OnBattleEnded(MapEvent mapEvent)
    {
        // If the civil war challenger is defeated in battle and captured
        if (_activeCivilWar == null) return;
        // (Victory condition checking delegated to EvaluateCivilWarProgress)
    }

    private void OnSettlementChanged(Settlement s, bool openToClaim, Hero newOwner,
        Hero oldOwner, Hero capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        if (_activeCivilWar == null) return;
        // Notify progress when capital changes hands
        if (s.StringId == _activeCivilWar.Loyalist.InitialHomeFortSettlement?.StringId
            && s.OwnerClan?.Kingdom == _activeCivilWar.Rebel)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"The capital has fallen to the rebel faction! The king's position is untenable.",
                Color.FromUint(0xFFD4AF37)));
        }
    }

    private void EndCivilWar(CivilWarRecord war, bool challengerWins)
    {
        if (challengerWins)
            OnChallengerVictory(war);
        else
            OnChallengerDefeated(war);
        _activeCivilWar = null;
    }

    private void OnChallengerVictory(CivilWarRecord war)
    {
        ChangeRulingClanAction.Apply(war.Loyalist, war.Challenger.Clan);

        foreach (Clan c in war.Rebel.Clans.ToList())
            ChangeKingdomAction.ApplyByJoinToKingdom(c, war.Loyalist, showNotification: false);

        string msg = war.Challenger == Hero.MainHero
            ? $"You have seized the throne of {war.Loyalist.Name}!"
            : $"{war.Challenger.Name} has seized the throne of {war.Loyalist.Name}!";

        InformationManager.DisplayMessage(new InformationMessage(msg,
            Color.FromUint(0xFFD4AF37)));
    }

    private void OnChallengerDefeated(CivilWarRecord war)
    {
        // Rebel clans return (or are expelled)
        foreach (Clan c in war.Rebel.Clans.ToList())
        {
            if (c == war.Challenger.Clan)
                ChangeKingdomAction.ApplyByLeaveKingdom(c, showNotification: false);
            else
                ChangeKingdomAction.ApplyByJoinToKingdom(c, war.Loyalist, showNotification: false);
        }

        string msg = war.Challenger == Hero.MainHero
            ? "Your rebellion has been crushed."
            : $"The rebellion of {war.Challenger.Name} has been defeated.";

        InformationManager.DisplayMessage(new InformationMessage(msg));
    }
}
```

---

## Implementation: ArrestBehavior

```csharp
public class ArrestBehavior : CampaignBehaviorBase
{
    private HashSet<string> _pendingArrestTargets = new HashSet<string>();
    private HashSet<string> _tempHostileClans     = new HashSet<string>();

    public static ArrestBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
    }

    public override void SyncData(IDataStore dataStore)
    {
        var pendingList = _pendingArrestTargets.ToList();
        var hostileList = _tempHostileClans.ToList();
        dataStore.SyncData("hind_arrest_pending", ref pendingList);
        dataStore.SyncData("hind_arrest_hostile",  ref hostileList);
        if (!dataStore.IsSaving)
        {
            _pendingArrestTargets = new HashSet<string>(pendingList);
            _tempHostileClans     = new HashSet<string>(hostileList);
        }
    }

    public void DeclareArrest(Hero target)
    {
        _pendingArrestTargets.Add(target.StringId);

        float desertRate = CivilWarBehavior.Instance?.CalculateDesertionRate(Hero.MainHero, target) ?? 0.05f;
        // desertRate calculation is in CivilWarBehavior but can be static utility
        ApplyDesertion(MobileParty.MainParty, desertRate);

        bool isAlly = target.Clan?.Kingdom == Hero.MainHero?.Clan?.Kingdom;
        if (isAlly)
            _tempHostileClans.Add(target.Clan.StringId);

        InformationManager.DisplayMessage(new InformationMessage(
            $"Arrest declared against {target.Name}. Track them down and defeat their party.",
            Color.FromUint(0xFFCC4400)));
    }

    private void OnBattleEnded(MapEvent mapEvent)
    {
        if (!mapEvent.IsPlayerMapEvent) return;
        if (mapEvent.BattleState != BattleState.AttackerVictory) return;

        foreach (var involvedParty in mapEvent.PartiesOnSide(BattleSideEnum.Defender))
        {
            Hero capturedHero = involvedParty.Party?.LeaderHero;
            if (capturedHero == null) continue;
            if (!_pendingArrestTargets.Contains(capturedHero.StringId)) continue;
            if (!capturedHero.IsPrisoner) continue; // ensure they were captured, not just defeated

            _pendingArrestTargets.Remove(capturedHero.StringId);
            _tempHostileClans.Remove(capturedHero.Clan?.StringId);

            ProcessSuccessfulArrest(capturedHero);
        }
    }

    private void ProcessSuccessfulArrest(Hero imprisoned)
    {
        bool wasAlly = imprisoned.Clan?.Kingdom == Hero.MainHero?.Clan?.Kingdom;
        if (!wasAlly) return;

        Kingdom kingdom = Hero.MainHero?.Clan?.Kingdom;
        ChangeKingdomAction.ApplyByLeaveKingdom(Hero.MainHero.Clan, showNotification: false);

        Kingdom rebelKingdom = CivilWarBehavior.Instance?.GetOrCreateRebelKingdomForPlayer()
                            ?? Hero.MainHero.Clan.Kingdom;
        if (kingdom != null)
            DeclareWarAction.Apply(rebelKingdom, kingdom,
                DeclareWarAction.DeclareWarDetail.Default);

        bool hasLegitimacy = CivilWarBehavior.Instance?.PlayerHasCivilWarLegitimacy() ?? false;
        if (hasLegitimacy)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Kingdom Is Divided",
                $"You have imprisoned {imprisoned.Name}. Lords and notables are watching. " +
                $"Do you press your claim for the throne, or was this personal?",
                true, true,
                "Press my claim",
                "This was personal",
                () => CivilWarBehavior.Instance?.InitiateCivilWar(Hero.MainHero, kingdom),
                () => InformationManager.DisplayMessage(new InformationMessage(
                    $"Your clan is now at war with {kingdom?.Name}."))
            ));
        }
        else
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"You have imprisoned {imprisoned.Name}. " +
                $"Your clan is now at war with {kingdom?.Name}.",
                Color.FromUint(0xFFCC2200)));
        }
    }

    private void ApplyDesertion(MobileParty party, float rate)
    {
        if (rate <= 0f || party?.MemberRoster == null) return;
        foreach (var element in party.MemberRoster.GetTroopRoster().ToList())
        {
            if (element.Character?.IsHero == true) continue;
            int remove = (int)(element.Number * rate);
            if (remove > 0)
                party.MemberRoster.AddToCounts(element.Character, -remove);
        }
    }
}
```

---

**[← Chapter 15](15-Lord-Progression-and-Mansabdari.md)** | **[Home](Home.md)**
