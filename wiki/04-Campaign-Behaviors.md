# Chapter 4 — Campaign Behaviors

> `CampaignBehaviorBase` is where all campaign logic lives. This chapter covers the full anatomy, common patterns, and complete Hindostan examples.

**[← Chapter 3](03-Project-Setup.md)** | **[Home](Home.md)** | **[Next: Game Model Overrides →](05-Game-Model-Overrides.md)**

---

## Contents

- [Full behavior template](#full-behavior-template)
- [How events work](#how-events-work)
- [New game setup — wars and treasuries](#new-game-setup--wars-and-treasuries)
- [Daily tick pattern — monsoon season](#daily-tick-pattern--monsoon-season)
- [Yearly tick pattern — historical events](#yearly-tick-pattern--historical-events)
- [Responding to specific events](#responding-to-specific-events)

---

## Full Behavior Template

Every behavior follows this skeleton:

```csharp
using TaleWorlds.CampaignSystem;

namespace TheHindostanMod
{
    public class MyBehavior : CampaignBehaviorBase
    {
        // State — persisted to save file
        private bool _initialized = false;

        // Required: subscribe to events
        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        // Required: save/load state
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("my_initialized_flag", ref _initialized);
        }

        private void OnNewGameCreated(CampaignGameStarter starter) { }
        private void OnDailyTick() { }
    }
}
```

The two `override` methods are not optional — failing to implement them causes a compile error.

---

## How Events Work

`CampaignEvents` is a static class containing hundreds of event fields. Each is a `CampaignEvent<T>` typed to match the handler signature you must provide.

```csharp
// This event's signature is (Hero victim, Hero killer, detail, showNotification)
CampaignEvents.OnHeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);

private void OnHeroKilled(Hero victim, Hero killer,
    KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
{
    // your logic
}
```

**Finding the right event:** In Visual Studio, type `CampaignEvents.` and scroll the autocomplete list. The name describes when it fires. Whatever parameters the event carries, your handler method must accept the same parameters in the same order.

---

## New Game Setup — Wars and Treasuries

`OnNewGameCreatedEvent` fires once after XML loads but before the player can do anything. Use it for all one-time historical setup.

```csharp
public class HindostanStartingStateBehavior : CampaignBehaviorBase
{
    private bool _startingStateApplied = false;

    public override void RegisterEvents()
    {
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(
            this, OnNewGameCreated);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("hindostan_start_applied", ref _startingStateApplied);
    }

    private void OnNewGameCreated(CampaignGameStarter starter)
    {
        if (_startingStateApplied) return;
        _startingStateApplied = true;

        ApplyHistoricalWars();
        SetStartingTreasuries();
        SetSettlementProsperity();
    }

    private void ApplyHistoricalWars()
    {
        // 1719: Mughal vs Sikh, Mughal vs Maratha
        DeclareWarBetween("empire",   "khuzait");
        DeclareWarBetween("empire",   "battania");
        // Hyderabad fights on two fronts
        DeclareWarBetween("empire_s", "aserai");
        DeclareWarBetween("empire_s", "battania");
        // Bengal vs Maratha (Bengal expansion southward)
        DeclareWarBetween("empire_w", "battania");
        // Afghan vs Rajput (traditional rivalry)
        DeclareWarBetween("sturgia",  "vlandia");
    }

    private void DeclareWarBetween(string k1Id, string k2Id)
    {
        var k1 = Kingdom.All.FirstOrDefault(k => k.StringId == k1Id);
        var k2 = Kingdom.All.FirstOrDefault(k => k.StringId == k2Id);

        if (k1 == null || k2 == null)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"[Hindostan] WARNING: kingdom '{k1Id}' or '{k2Id}' not found"));
            return;
        }

        if (!k1.IsAtWarWith(k2))
            DeclareWarAction.Apply(k1, k2, DeclareWarAction.DeclareWarDetail.Default);
    }

    private void SetStartingTreasuries()
    {
        SetKingdomTreasury("empire",   250_000);  // Delhi — wealthy but strained
        SetKingdomTreasury("empire_w", 500_000);  // Bengal — richest in the world
        SetKingdomTreasury("empire_s", 180_000);  // Hyderabad — efficient
        SetKingdomTreasury("vlandia",  120_000);  // Rajput — land-rich
        SetKingdomTreasury("aserai",    90_000);  // Mysore — developing
        SetKingdomTreasury("battania",  40_000);  // Maratha — poor agrarian base
        SetKingdomTreasury("khuzait",   30_000);  // Sikh — egalitarian, lean
        SetKingdomTreasury("sturgia",   60_000);  // Afghan — raid economy
    }

    private void SetKingdomTreasury(string kingdomId, int gold)
    {
        var kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
        if (kingdom?.RulingClan == null) return;
        kingdom.RulingClan.Gold = gold;
    }

    private void SetSettlementProsperity()
    {
        foreach (var settlement in Settlement.All.Where(s => s.IsTown))
        {
            string cultureId = settlement.Culture?.StringId;
            float multiplier = cultureId switch
            {
                "empire"   => 1.2f,   // Mughal towns
                "empire_w" => 1.6f,   // Bengal — richest in the world
                "empire_s" => 1.0f,   // Hyderabad — average
                "battania" => 0.7f,   // Maratha — poor agrarian base
                "sturgia"  => 0.6f,   // Afghan — mountainous
                "vlandia"  => 0.9f,   // Rajput — land-rich
                "khuzait"  => 0.8f,   // Sikh — fertile Punjab
                "aserai"   => 0.85f,  // Mysore — developing
                _          => 1.0f
            };

            if (settlement.Town != null)
                settlement.Town.Prosperity *= multiplier;
        }
    }
}
```

---

## Daily Tick Pattern — Monsoon Season

```csharp
public class MonsoonSeasonBehavior : CampaignBehaviorBase
{
    public static bool IsMonsoonActive { get; private set; }
    private int _lastMonsoonYear = -1;

    public override void RegisterEvents()
    {
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        bool active = IsMonsoonActive;
        dataStore.SyncData("monsoon_active",    ref active);
        IsMonsoonActive = active;

        dataStore.SyncData("last_monsoon_year", ref _lastMonsoonYear);
    }

    private void OnDailyTick()
    {
        // Day 28–55 of the 84-day year = second season = monsoon
        int day = (int)CampaignTime.Now.ToDays % 84;
        bool shouldBeMonsoon = day >= 28 && day <= 55;

        if (shouldBeMonsoon && !IsMonsoonActive)
            StartMonsoon();
        else if (!shouldBeMonsoon && IsMonsoonActive)
            EndMonsoon();
    }

    private void StartMonsoon()
    {
        IsMonsoonActive = true;
        int year = (int)Campaign.Current.CampaignStartTime.ElapsedYearsUntilNow;

        if (year != _lastMonsoonYear)
        {
            _lastMonsoonYear = year;
            InformationManager.DisplayMessage(new InformationMessage(
                "The monsoon has arrived. Movement across Hindostan is slowed.",
                Color.FromUint(0xFF4488FF)));
        }
    }

    private void EndMonsoon()
    {
        IsMonsoonActive = false;

        foreach (var village in Village.All)
            village.Hearth = Math.Min(village.Hearth + 30f, 2000f);

        InformationManager.DisplayMessage(new InformationMessage(
            "The monsoon has passed. Harvests are plentiful."));
    }
}
```

The `public static bool IsMonsoonActive` is read by `HindostanPartySpeedModel` to apply the movement penalty. Static properties are the standard pattern for sharing state between a behavior and a model.

---

## Yearly Tick Pattern — Historical Events

```csharp
public class HistoricalEventsBehavior : CampaignBehaviorBase
{
    private bool _nadirShahFired  = false;
    private bool _sayyidFell      = false;
    private bool _bengalIndependent = false;

    public override void RegisterEvents()
    {
        CampaignEvents.YearlyTickEvent.AddNonSerializedListener(this, OnYearlyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("nadir_shah_fired",    ref _nadirShahFired);
        dataStore.SyncData("sayyid_fell",         ref _sayyidFell);
        dataStore.SyncData("bengal_independent",  ref _bengalIndependent);
    }

    private void OnYearlyTick()
    {
        int year = (int)Campaign.Current.CampaignStartTime.ElapsedYearsUntilNow;

        // Year 3: Nadir Shah's invasion — Afghan kingdom reinforced
        if (year >= 3 && !_nadirShahFired)
        {
            _nadirShahFired = true;
            TriggerNadirShahInvasion();
        }

        // Year 4: Bengal formally declares independence
        if (year >= 4 && !_bengalIndependent)
        {
            _bengalIndependent = true;
            TriggerBengalIndependence();
        }

        // Year 5: Sayyid Brothers (Delhi court faction) lose power
        if (year >= 5 && !_sayyidFell)
        {
            _sayyidFell = true;
            TriggerSayyidCollapse();
        }
    }

    private void TriggerNadirShahInvasion()
    {
        var afghans = Kingdom.All.FirstOrDefault(k => k.StringId == "sturgia");
        if (afghans?.RulingClan == null) return;

        // Simulate Persian gold flowing into Afghan coffers
        afghans.RulingClan.Gold += 150_000;

        // Reinforce their troops (add directly to ruling clan lord's party)
        var generalParty = afghans.Leader?.PartyBelongedTo;
        if (generalParty != null)
        {
            var heavyCavalry = MBObjectManager.Instance
                .GetObject<CharacterObject>("sturgian_veteran_warrior");
            if (heavyCavalry != null)
                generalParty.MemberRoster.AddToCounts(heavyCavalry, 200);
        }

        InformationManager.DisplayMessage(new InformationMessage(
            "Nadir Shah's armies pour through the Khyber Pass. " +
            "The Durrani Afghans grow stronger.",
            Color.FromUint(0xFFFF3300)));
    }

    private void TriggerBengalIndependence()
    {
        // Bengal war against Delhi if not already at war
        var bengal = Kingdom.All.FirstOrDefault(k => k.StringId == "empire_w");
        var delhi  = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");
        if (bengal != null && delhi != null && !bengal.IsAtWarWith(delhi))
            DeclareWarAction.Apply(bengal, delhi, DeclareWarAction.DeclareWarDetail.Default);

        InformationManager.DisplayMessage(new InformationMessage(
            "Bengal has renounced allegiance to Delhi. " +
            "The Nawab rules as an independent sovereign."));
    }

    private void TriggerSayyidCollapse()
    {
        // Weaken Delhi's ruling clan
        var delhi = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");
        if (delhi?.RulingClan != null)
        {
            delhi.RulingClan.Gold     -= Math.Min(80_000, delhi.RulingClan.Gold);
            delhi.RulingClan.Influence -= 200f;
        }

        InformationManager.DisplayMessage(new InformationMessage(
            "The Sayyid Brothers have been overthrown. " +
            "The imperial court is in turmoil."));
    }
}
```

---

## Responding to Specific Events

```csharp
public override void RegisterEvents()
{
    // Hero dies
    CampaignEvents.OnHeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);

    // Siege ends (town captured or defender holds)
    CampaignEvents.OnSiegeAftermathAppliedEvent.AddNonSerializedListener(
        this, OnSiegeAftermath);

    // Battle ends
    CampaignEvents.OnMapEventEndedEvent.AddNonSerializedListener(this, OnBattleEnded);
}

private void OnHeroKilled(Hero victim, Hero killer,
    KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
{
    // If a Mughal Emperor dies, the empire weakens
    if (victim?.Kingdom?.StringId == "empire" && victim == victim.Kingdom.Leader)
    {
        victim.Kingdom.RulingClan.Influence -= 300f;
        InformationManager.DisplayMessage(new InformationMessage(
            $"The Mughal throne is now disputed after the death of {victim.Name}!"));
    }
}

private void OnSiegeAftermath(MapEvent mapEvent, MapEventSide attackerSide,
    MapEventSide defenderSide, MapEventResultExplainer resultExplainer,
    Settlement settlement, MBReadOnlyList<InvolvedParty> involvedParties)
{
    // If Delhi falls, trigger succession crisis
    if (settlement?.StringId == "town_EN2"
        && settlement.OwnerClan?.Kingdom?.StringId != "empire")
    {
        InformationManager.DisplayMessage(new InformationMessage(
            "Delhi has fallen! The Mughal Empire enters its final crisis.",
            Color.FromUint(0xFFCC2200)));
    }
}

private void OnBattleEnded(MapEvent mapEvent)
{
    // Check if Sikh misl won a field battle
    var winner = mapEvent.Winner;
    if (winner?.Culture?.StringId == "khuzait")
    {
        winner.MobileParty?.RecentEventsMorale?.Add(10f);  // morale bonus for victory
    }
}
```

---

**[← Chapter 3](03-Project-Setup.md)** | **[Home](Home.md)** | **[Next: Game Model Overrides →](05-Game-Model-Overrides.md)**
