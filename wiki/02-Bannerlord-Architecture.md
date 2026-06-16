# Chapter 2 — Bannerlord Architecture

> Before writing any code, you need to understand how the game is organized.

**[← Chapter 1](01-CSharp-Crash-Course.md)** | **[Home](Home.md)** | **[Next: Project Setup →](03-Project-Setup.md)**

---

## Contents

- [Module load order](#module-load-order)
- [The object hierarchy](#the-object-hierarchy)
- [Finding objects by StringId](#finding-objects-by-stringid)
- [The two main extension points](#the-two-main-extension-points)
- [Events vs tick handlers](#events-vs-tick-handlers)
- [TextObject — the localized string type](#textobject--the-localized-string-type)

---

## Module Load Order

When Bannerlord starts, it processes modules in this sequence:

```
1. Read all SubModule.xml files (in dependency order)
2. Call OnSubModuleLoad() on each module
       ↓ no game objects exist yet
3. [Main menu — player navigates]
4. Player clicks New Game or Load
5. Call OnGameStart() on each module
       ↓ register behaviors and models here
6. XML data is loaded into the object database
7. OnNewGameCreatedEvent fires (new game only)
8. Campaign begins
```

**Critical rule:** Do NOT access `Campaign.Current`, `Kingdom.All`, `Hero.All`, or any game objects in `OnSubModuleLoad()`. Nothing exists yet. Only use `OnSubModuleLoad` for Harmony patches and one-time static initialization.

---

## The Object Hierarchy

```
Game
└── Campaign  (accessed via Campaign.Current)
    ├── Kingdoms  (Kingdom.All)
    │   └── Clans  (kingdom.Clans, or Clan.All globally)
    │       └── Heroes  (clan.Heroes)
    │           └── Hero.PartyBelongedTo → MobileParty
    ├── Settlements  (Settlement.All)
    │   ├── Town       (settlement.Town    — only if IsTown)
    │   ├── Castle     (settlement.Castle  — only if IsCastle)
    │   └── Village    (settlement.Village — only if IsVillage)
    ├── MobileParties  (MobileParty.All)
    └── MapEvents      (active battles)
```

All global collections are `IEnumerable<T>` — they support LINQ operators directly.

---

## Finding Objects by StringId

Every game object has a `StringId` — the `id=` value from its XML definition. This is the permanent, save-game-safe identifier. Names can change; StringIds cannot.

```csharp
// Generic pattern — works for any type
Kingdom mughals   = MBObjectManager.Instance.GetObject<Kingdom>("empire");
Hero muhammadShah = MBObjectManager.Instance.GetObject<Hero>("lord_1_1");
Settlement delhi  = MBObjectManager.Instance.GetObject<Settlement>("town_EN2");
CharacterObject elephant = MBObjectManager.Instance.GetObject<CharacterObject>("war_elephant");

// Shorthand for common types
Kingdom mughals2 = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");
Hero mainHero    = Hero.MainHero;   // the player character
Settlement found = Settlement.Find("town_EN2");
```

All of these can return null if the object doesn't exist or was eliminated. Always null-check before use.

---

## The Two Main Extension Points

Everything you write will be one of two things:

### A. Campaign Behavior

Code that runs during a campaign, responding to events:

```csharp
public class MyBehavior : CampaignBehaviorBase
{
    public override void RegisterEvents()
    {
        // Subscribe to events here
    }

    public override void SyncData(IDataStore dataStore)
    {
        // Save/load your state here
    }
}
```

Registered in `OnGameStart`:

```csharp
((CampaignGameStarter)gameStarter).AddBehavior(new MyBehavior());
```

### B. Game Model

Override calculation methods to change game math:

```csharp
public class MyModel : DefaultPartySpeedCalculatingModel
{
    public override ExplainedNumber CalculateBaseSpeed(MobileParty party, ...)
    {
        ExplainedNumber result = base.CalculateBaseSpeed(party, ...);
        result.AddFactor(-0.30f, new TextObject("{=!}Monsoon Rains"));
        return result;
    }
}
```

Registered in `OnGameStart`:

```csharp
((CampaignGameStarter)gameStarter).AddModel(new MyModel());
```

Bannerlord keeps **one model per model type**. Your model replaces the default entirely. If two mods both add a speed model, only the last one registered applies.

---

## Events vs Tick Handlers

### Specific Events

Fire when something particular happens:

```csharp
CampaignEvents.OnSiegeAftermathAppliedEvent  // after a siege resolves
CampaignEvents.OnHeroKilledEvent             // any hero dies
CampaignEvents.KingdomDecisionConcluded      // parliament vote ends
CampaignEvents.OnNewGameCreatedEvent         // new campaign initialized
CampaignEvents.OnGameLoadedEvent             // save file loaded
```

### Tick Events

Fire on a schedule:

```csharp
CampaignEvents.DailyTickEvent    // every in-game day   (most common)
CampaignEvents.WeeklyTickEvent   // every in-game week
CampaignEvents.HourlyTickEvent   // every in-game hour  (expensive — use sparingly)
CampaignEvents.YearlyTickEvent   // every in-game year  (used for Chauth, historical events)
```

**Time facts:**
- 1 year = 84 in-game days (3 seasons × 28 days)
- `(int)CampaignTime.Now.ToDays % 84` gives the current day of year (0–83)
- Day 0–27 = Spring, Day 28–55 = Summer (monsoon season), Day 56–83 = Autumn

---

## TextObject — the Localized String Type

Bannerlord wraps all displayed strings in `TextObject` to support localization.

```csharp
// Create from a literal — {=!} bypasses the localization lookup
TextObject label = new TextObject("{=!}Monsoon Rains");

// Convert to plain string for display
string s = label.ToString();

// In ExplainedNumber (stat tooltips), always use TextObject with {=!}
result.AddFactor(0.20f, new TextObject("{=!}Ganimi Kava"));

// Displaying a message
InformationManager.DisplayMessage(new InformationMessage(label.ToString()));
```

**The `{=!}` prefix** tells the engine: *this string is already final, do not look it up in any language file*. Without it, the engine tries to look up the text as a string ID and may display garbage. Always use `{=!}` for your custom strings.

---

**[← Chapter 1](01-CSharp-Crash-Course.md)** | **[Home](Home.md)** | **[Next: Project Setup →](03-Project-Setup.md)**
