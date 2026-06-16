# Quick Reference

**[← Home](Home.md)**

---

## Finding Objects

```csharp
// Kingdoms
Kingdom.All.FirstOrDefault(k => k.StringId == "empire")
MBObjectManager.Instance.GetObject<Kingdom>("empire")

// Heroes
Hero.FindFirst(h => h.StringId == "lord_1_1")
Hero.MainHero  // the player

// Settlements
Settlement.Find("town_EN2")
Settlement.All.FirstOrDefault(s => s.StringId == "town_EN2")

// Clans
Clan.All.FirstOrDefault(c => c.StringId == "clan_empire_1")

// Troops / characters
MBObjectManager.Instance.GetObject<CharacterObject>("war_elephant")
MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cavalry")

// Cultures
MBObjectManager.Instance.GetObject<CultureObject>("empire")
```

---

## Actions

```csharp
// Diplomacy
DeclareWarAction.Apply(k1, k2, DeclareWarAction.DeclareWarDetail.Default)
MakePeaceAction.Apply(k1, k2)

// Gold
GiveGoldAction.ApplyBetweenCharacters(giver, receiver, amount)
clan.Gold += amount  // direct

// Heroes
KillCharacterAction.ApplyByMurder(hero, killer: null, showHPBar: false)
TakePrisonerAction.Apply(capturingParty, prisonerHero)
EndCaptivityAction.ApplyByReleasedByPlayer(hero)

// Clans / kingdoms
ChangeKingdomAction.ApplyByJoinToKingdom(clan, kingdom, showNotification: false)
ChangeKingdomAction.ApplyByLeaveKingdom(clan, showNotification: false)
ChangeRulingClanAction.Apply(kingdom, newRulingClan)

// Relations
ChangeRelationAction.ApplyPlayerRelation(hero, delta: 10)
ChangeRelationAction.ApplyRelationChangeBetweenHeroes(h1, h2, delta: -20)
```

---

## Time

```csharp
CampaignTime.Now.ToDays                                      // total days elapsed
(int)CampaignTime.Now.ToDays % 84                           // day of year (0–83)
Campaign.Current.CampaignStartTime.ElapsedYearsUntilNow     // years elapsed (float)
(int)Campaign.Current.CampaignStartTime.ElapsedYearsUntilNow // years elapsed (int)

// Seasons (84-day year, 4 seasons of 21 days each):
// CampaignTime.Now.GetSeasonOfYear() returns 0–3
// Season 0 (Spring)  : Day  0–20  — planting, moderate weather
// Season 1 (Summer)  : Day 21–41  — Monsoon; movement −30%, disease ×2, harvest income +40%
// Season 2 (Autumn)  : Day 42–62  — harvest, good travel
// Season 3 (Winter)  : Day 63–83  — Sardi; cold in the north, slower campaigns
```

---

## Displaying Things

```csharp
// Ticker message (bottom right)
InformationManager.DisplayMessage(new InformationMessage("text"))

// Colored ticker message
InformationManager.DisplayMessage(new InformationMessage("text", Color.FromUint(0xFFRRGGBB)))

// Modal popup with yes/no
InformationManager.ShowInquiry(new InquiryData(
    "Title", "Body",
    isAffirmativeOptionShown: true, isNegativeOptionShown: true,
    "Yes", "No",
    affirmativeAction: () => { }, negativeAction: () => { }
))

// Log to file only (not shown in game)
Debug.Print("[Hindostan] debug message")
```

---

## Colors (ARGB hex)

| Color | Uint |
|-------|------|
| Red — war/danger | `0xFFCC2200` |
| Orange — Maratha | `0xFFFF9933` |
| Blue — monsoon | `0xFF4488FF` |
| Gold — wealth | `0xFFD4AF37` |
| Green — positive | `0xFF44AA44` |
| White | `0xFFFFFFFF` |

---

## Culture / Kingdom IDs

| Display Name | StringId | Short Name |
|---|---|---|
| Mughal Empire (Delhi) | `empire` | Mughliya Sultanat |
| Bengal | `empire_w` | Bangaal |
| Hyderabad | `empire_s` | Hyderabad |
| Afghan/Durrani | `sturgia` | Afghans |
| Mysore | `aserai` | Mysore |
| Rajput | `vlandia` | Rajputs |
| Maratha | `battania` | Marathas |
| Sikh | `khuzait` | Sikhs |

---

## ExplainedNumber Modifiers

```csharp
result.Add(0.5f, new TextObject("{=!}Flat Bonus Label"))       // +0.5 flat
result.AddFactor(0.20f, new TextObject("{=!}+20% Label"))      // +20%
result.AddFactor(-0.30f, new TextObject("{=!}-30% Label"))     // -30%
// NEVER: result.ResultNumber *= 1.2f  — use AddFactor instead
```

---

## SyncData Cheat Sheet

```csharp
public override void SyncData(IDataStore dataStore)
{
    dataStore.SyncData("hindostan_bool",   ref _myBool);
    dataStore.SyncData("hindostan_int",    ref _myInt);
    dataStore.SyncData("hindostan_float",  ref _myFloat);
    dataStore.SyncData("hindostan_string", ref _myString);
    dataStore.SyncData("hindostan_list",   ref _myList);    // List<string>, List<int>
    dataStore.SyncData("hindostan_hero",   ref _myHero);    // Hero (by StringId internally)
    dataStore.SyncData("hindostan_kingdom",ref _myKingdom); // Kingdom
}
```

---

## Behavior Template

```csharp
public class MyBehavior : CampaignBehaviorBase
{
    private bool _flag = false;

    public override void RegisterEvents()
    {
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
        CampaignEvents.YearlyTickEvent.AddNonSerializedListener(this, OnYearly);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDaily);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("hindostan_flag", ref _flag);
    }

    private void OnNewGame(CampaignGameStarter s) { }
    private void OnYearly() { }
    private void OnDaily() { }
}
```

---

## Null-Safe Object Access

```csharp
// Always guard chains from global lookups
var k = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");
if (k == null) { Debug.Print("[Hindostan] kingdom not found"); return; }

// Guard property chains
string name = hero?.Clan?.Kingdom?.Name?.ToString() ?? "Unknown";

// Guard iteration (never modify while iterating)
foreach (var clan in kingdom.Clans.ToList())
    ChangeKingdomAction.ApplyByLeaveKingdom(clan);
```

---

**[← Home](Home.md)**
