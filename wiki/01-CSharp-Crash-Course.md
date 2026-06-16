# Chapter 1 — C# Crash Course for Programmers

> You already know how to program. This chapter covers only the C# syntax and patterns you'll encounter constantly in Bannerlord code.

**[← Home](Home.md)** | **[Next: Bannerlord Architecture →](02-Bannerlord-Architecture.md)**

---

## Contents

- [Properties](#properties)
- [Type inference with `var`](#type-inference-with-var)
- [Null safety operators](#null-safety-operators)
- [LINQ — querying collections](#linq--querying-collections)
- [Delegates and events](#delegates-and-events)
- [Attributes](#attributes)
- [ref and out parameters](#ref-and-out-parameters)
- [String interpolation](#string-interpolation)
- [Access levels](#access-levels)

---

## Properties

C# classes use *properties* instead of bare fields. A property is a field with custom getter/setter logic. Bannerlord exposes almost everything through properties.

```csharp
// Setting a property (may trigger events, UI updates, validation)
hero.Gold = 500;

// Getting a property
int g = hero.Gold;
```

Properties look like field access but run code. If setting a property has no effect, the field is probably `private set` and you need Harmony to reach it (see [Chapter 6](06-Harmony-Patching.md)).

---

## Type Inference with `var`

```csharp
var kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");
// Equivalent to:
Kingdom kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");
```

Use `var` when the type is obvious from the right-hand side. Bannerlord code uses it constantly.

---

## Null Safety Operators

Bannerlord objects can be null at any time — heroes die, kingdoms fall. These operators prevent `NullReferenceException`.

```csharp
// ?. — null-conditional: returns null instead of crashing
string name = hero?.Name?.ToString();

// ?? — null-coalescing: provides a fallback
int gold = clan?.Gold ?? 0;

// Chained null-safe assignment
kingdom?.RulingClan?.Gold += 5000;
```

**Rule:** Any time you access an object from `Campaign.Current`, `Kingdom.All`, `Hero.FindFirst`, or similar lookups, guard with `?.` until you have verified the object is non-null.

---

## LINQ — Querying Collections

Every Bannerlord collection (`Kingdom.All`, `Hero.All`, `Settlement.All`) supports LINQ operators. Add `using System.Linq;` at the top of any file that uses them.

```csharp
// Find one object — returns null if not found
var mughalKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");

// Filter to a list
var marathaLords = Hero.All
    .Where(h => h.IsAlive && h.Clan?.Kingdom?.StringId == "battania")
    .ToList();

// Check existence
bool hasMughalTown = Settlement.All.Any(s =>
    s.IsTown && s.Culture?.StringId == "empire");

// Count
int sikhClans = Clan.All.Count(c => c.Kingdom?.StringId == "khuzait");

// Aggregate
int totalGold = Clan.All
    .Where(c => c.Kingdom?.StringId == "empire_w")
    .Sum(c => c.Gold);

// Order
var richestClan = Clan.All.OrderByDescending(c => c.Gold).First();

// Transform
var kingdomNames = Kingdom.All
    .Select(k => k.Name.ToString())
    .ToList();
```

The `k => k.StringId == "empire"` syntax is a **lambda** — an anonymous function. `k` is the parameter (each element of the collection), the right side is the return value. Equivalent to `def f(k): return k.StringId == "empire"` in Python.

---

## Delegates and Events

Bannerlord's event system uses delegates. You write this exact pattern in every behavior:

```csharp
// Subscribe — tell the event to call your method when it fires
CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);

// Your handler — must match the event's expected signature
private void OnDailyTick()
{
    // runs every in-game day
}
```

`AddNonSerializedListener` means: *call my method when this event fires, but don't persist this subscription in the save file*. The subscription is re-established automatically by `RegisterEvents()` when the game loads.

---

## Attributes

Attributes are metadata decorators. Same concept as Python decorators but with `[]` syntax:

```csharp
[HarmonyPatch(typeof(SomeClass), "MethodName")]
[HarmonyPostfix]
public static void MyPatch(SomeClass __instance) { }
```

Harmony reads these to decide which method to patch and when to call your code. You never call `MyPatch` yourself — Harmony injects it automatically.

---

## `ref` and `out` Parameters

Bannerlord methods sometimes return multiple values this way.

```csharp
// 'out' — caller must receive the value; method assigns it
if (MBSaveLoad.TryLoadSaveGame(name, out SaveGameFileInfo info)) { ... }

// 'ref' — caller passes a value IN; method may modify it
void SomeMethod(ref int score) { score += 10; }
```

In Harmony patches, the patched method's `ref` parameters appear as `ref T paramName` in your patch method. The special `ref __result` lets you modify a method's return value.

---

## String Interpolation

```csharp
string msg = $"Kingdom {kingdom.Name} has {kingdom.RulingClan.Gold} gold";
// Same as: "Kingdom " + kingdom.Name + " has " + kingdom.RulingClan.Gold + " gold"
```

Use `$"..."` whenever you're inserting variables into a string.

---

## Access Levels

| Modifier | Accessible from |
|----------|----------------|
| `public` | Anywhere |
| `internal` | Same DLL only (Bannerlord's own classes use this) |
| `protected` | The class and its subclasses (you reach these through inheritance) |
| `private` | Only within the class (reach via Harmony or reflection) |

Most Bannerlord properties you want to *set* are `private set`. You either subclass the containing class or use Harmony to patch the setter directly.

---

**[← Home](Home.md)** | **[Next: Bannerlord Architecture →](02-Bannerlord-Architecture.md)**
