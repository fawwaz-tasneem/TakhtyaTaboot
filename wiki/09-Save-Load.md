# Chapter 9 — Save and Load

> Every piece of state your mod creates must be persisted via `SyncData` or it disappears when the player loads a save.

**[← Chapter 8](08-Game-Objects.md)** | **[Home](Home.md)** | **[Next: Debugging →](10-Debugging.md)**

---

## Contents

- [How SyncData works](#how-syncdata-works)
- [Types you can persist](#types-you-can-persist)
- [What you need to save](#what-you-need-to-save)
- [What you do NOT save](#what-you-do-not-save)
- [Null-reference trap with hero/clan references](#null-reference-trap-with-heroclan-references)
- [Save key naming conventions](#save-key-naming-conventions)
- [Full example](#full-example)

---

## How SyncData Works

`SyncData` is called **twice** per save/load cycle:

- When **saving**: writes your variables into the save file
- When **loading**: reads your variables back from the save file

The `IDataStore` knows which direction it's going. You just call `SyncData(key, ref variable)` for every variable you care about. The same code handles both directions.

```csharp
public override void SyncData(IDataStore dataStore)
{
    dataStore.SyncData("hindostan_start_applied", ref _startingStateApplied);
    dataStore.SyncData("last_chauth_year",        ref _lastChauthorYear);
    dataStore.SyncData("paid_kingdoms",           ref _paidKingdomIds);
}
```

---

## Types You Can Persist

```csharp
// Primitives
dataStore.SyncData("my_bool",   ref _myBool);    // bool
dataStore.SyncData("my_int",    ref _myInt);     // int
dataStore.SyncData("my_float",  ref _myFloat);   // float
dataStore.SyncData("my_string", ref _myString);  // string

// Collections of primitives
dataStore.SyncData("my_int_list",    ref _myIntList);    // List<int>
dataStore.SyncData("my_string_list", ref _myStringList); // List<string>

// Game objects (serialized by StringId internally)
dataStore.SyncData("tracked_hero",       ref _trackedHero);      // Hero
dataStore.SyncData("tracked_kingdom",    ref _trackedKingdom);   // Kingdom
dataStore.SyncData("tracked_settlement", ref _trackedSettlement); // Settlement

// Lists of game objects
dataStore.SyncData("affected_settlements", ref _affectedSettlements); // List<Settlement>
dataStore.SyncData("paid_kingdoms",        ref _paidKingdoms);        // List<Kingdom>
```

---

## What You Need to Save

Save these or they reset every time the player loads:

- All `bool` flags guarding one-time events (`_nadirShahFired`, `_startingStateApplied`)
- All year/day trackers (`_lastChauthorYear`, `_lastMonsoonYear`)
- Lists of heroes or settlements affected by ongoing effects
- Custom gold/tribute amounts you've tracked
- Player choices from dialogs that have lasting consequences

---

## What You Do NOT Save

Do NOT use `SyncData` for:

- Transient UI state (currently open dialog, selection)
- Values that are re-derived on load anyway (e.g., "is it currently monsoon?" is recomputed from `CampaignTime.Now` on the first `DailyTickEvent`)
- Game objects you can look up by StringId from `Kingdom.All`, `Hero.All`, etc. (save the StringId string instead)

---

## Null-Reference Trap With Hero/Clan References

If a Hero dies between saves, the engine may deserialize the reference as `null`. Always guard:

```csharp
private Hero _imprisonedRuler;

public override void SyncData(IDataStore dataStore)
{
    dataStore.SyncData("imprisoned_ruler", ref _imprisonedRuler);
    // If the hero died, _imprisonedRuler becomes null on load
}

// Anywhere you use the saved reference:
private void CheckPrisoner()
{
    if (_imprisonedRuler == null || !_imprisonedRuler.IsAlive)
    {
        _imprisonedRuler = null;
        return;
    }
    // safe to use _imprisonedRuler here
}
```

The same applies to `Kingdom` references — a kingdom can be eliminated.

---

## Save Key Naming Conventions

Save keys are global across all behaviors. Two mods using the same key will silently corrupt each other's data.

**Convention:** prefix all keys with your mod's abbreviated ID.

```
"hindostan_start_applied"    ← good
"start_applied"              ← bad (too generic, risk of collision)
"hindostan_nadir_fired"
"hindostan_last_chauth_yr"
"hindostan_paid_kingdoms"
```

Key length limit: reasonable (avoid excessively long keys). Underscores are fine. No spaces.

---

## Full Example

```csharp
public class MarathaChauthorBehavior : CampaignBehaviorBase
{
    // One-time flags
    private bool _firstChauthorFired = false;

    // Yearly tracking
    private int _lastChauthorYear = -1;

    // List of kingdom StringIds that paid this year
    // (We save string IDs, not Kingdom references — safer across saves)
    private List<string> _paidKingdomIds = new List<string>();

    // A reference to a specific hero (null-guarded on load)
    private Hero _chauthorEnvoy;

    public override void RegisterEvents()
    {
        CampaignEvents.YearlyTickEvent.AddNonSerializedListener(this, OnYearlyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("hindostan_chauth_first_fired", ref _firstChauthorFired);
        dataStore.SyncData("hindostan_chauth_last_year",   ref _lastChauthorYear);
        dataStore.SyncData("hindostan_chauth_paid_ids",    ref _paidKingdomIds);
        dataStore.SyncData("hindostan_chauth_envoy",       ref _chauthorEnvoy);

        // Guard the hero reference on load
        if (_chauthorEnvoy != null && !_chauthorEnvoy.IsAlive)
            _chauthorEnvoy = null;
    }

    private void OnYearlyTick()
    {
        int year = (int)Campaign.Current.CampaignStartTime.ElapsedYearsUntilNow;
        if (year == _lastChauthorYear) return;
        _lastChauthorYear = year;
        _paidKingdomIds.Clear();

        // ... chauth logic ...
    }
}
```

---

**[← Chapter 8](08-Game-Objects.md)** | **[Home](Home.md)** | **[Next: Debugging →](10-Debugging.md)**
