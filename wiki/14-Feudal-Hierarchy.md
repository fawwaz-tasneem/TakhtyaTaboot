# Chapter 14 — Feudal Hierarchy System

> **Superseded by [Chapter 15](15-Lord-Progression-and-Mansabdari.md).** This chapter documents the first design, which used `settlement.Governor` as the political lord and village Notables as tier-4 nobles. Chapter 15 identifies two errors in this model and replaces it with `FiefRecord.Holder`. All implementation in Chapters 15 onward uses the Chapter 15 model. This chapter is kept as architectural context and to explain why the correction was needed.

**[← Home](Home.md)**

---

## Contents

- [Design constraints and decisions](#design-constraints-and-decisions)
- [The four tiers](#the-four-tiers)
- [What Bannerlord already gives us](#what-bannerlord-already-gives-us)
- [The castle → town gap](#the-castle--town-gap)
- [Data model](#data-model)
- [FiefHierarchyBehavior](#fiefhierarchybehavior)
- [Tax flow up the chain](#tax-flow-up-the-chain)
- [Liege loyalty and defection risk](#liege-loyalty-and-defection-risk)
- [Player interactions](#player-interactions)
- [Hindostan flavour — Jagirdari vs European feudalism](#hindostan-flavour--jagirdari-vs-european-feudalism)
- [What this does NOT attempt](#what-this-does-not-attempt)

---

## Design Constraints and Decisions

### Constraint 1 — Clan ownership is immovable

Bannerlord's engine hard-codes `settlement.OwnerClan`. Changing this requires fighting deep engine assumptions (conquest detection, garrison payment, income routing). **We do not touch `OwnerClan`.**

Instead, the *lord of a fief* is the settlement's **Governor** (`settlement.Governor`) — a Hero who is already a first-class citizen in the engine, with their own stats, relationships, and events.

**Clan owns the fief legally. The Governor is the political lord.**

### Constraint 2 — No new heroes for villages

Creating 300+ hero instances for village nobles would bloat save files and AI processing. Instead, village-tier leadership uses the existing **Notable Headman** (`settlement.Notables` filtered to `IsHeadman`) — these are already in every village.

### Constraint 3 — We don't replace settlement capture

When a settlement is captured, Bannerlord reassigns `OwnerClan`. Our hierarchy simply responds to the `OnSettlementOwnerChanged` event and updates governor assignments accordingly.

### Key architectural decision

The hierarchy is **derived from settlement relationships**, not stored directly as a Hero→Hero table. To find anyone's liege:

```
Village headman's liege  =  Governor of Village.Bound (the bound castle or town)
Castle lord's liege      =  Governor of CastleToTown[castle] (our lookup table)
Town lord's liege        =  Kingdom.Leader (the emperor/king)
Emperor                  =  no liege
```

This means we only need to:
1. Keep `settlement.Governor` populated
2. Maintain a `Dictionary<string, string>` mapping each castle StringId to its overlord town StringId

---

## The Four Tiers

```
TIER 1 ── Emperor / King
              │  (direct liege of all town lords)
              │
TIER 2 ── Town Lords  (settlement.Governor of each town)
              │  (liege of castle lords whose CastleToTown maps to this town)
              │
TIER 3 ── Castle Lords  (settlement.Governor of each castle)
              │  (liege of village headmen whose Village.Bound == this castle)
              │
TIER 4 ── Village Headmen  (settlement.Notables headman of each village)
```

In the Hindostan Mod, translated to period-appropriate titles:

| Tier | Generic | Mughal | Maratha | Rajput | Afghan |
|------|---------|--------|---------|--------|--------|
| 1 | Emperor | Shahenshah / Nawaab Nazim | Chhatrapati / Peshwa | Maharaja | Amir e Amiraan |
| 2 | Town Lord | Faujdar (city commander) | Sardar | Raja | Khan |
| 3 | Castle Lord | Qiledar (fort commander) | Nayak | Thakur | Malik |
| 4 | Village Headman | Muqaddam | Patil | Patel | Arbab |

---

## What Bannerlord Already Gives Us

### Village.Bound — the bottom link is free

Every village in `settlements.xml` has `bound="Settlement.X"` pointing to its parent castle or town. In C#:

```csharp
Settlement parent = village.Village.Bound;
// parent is the castle or town that governs this village
```

This means: if we know the Governor of `parent`, we know the village headman's liege. **No extra data needed for villages.**

### Settlement.Governor — hero-level ownership already exists

```csharp
Hero townLord    = town.Governor;      // the lord of this town
Hero castleLord  = castle.Governor;   // the lord of this castle

// Setting a governor
settlement.Town.Governor = someHero;   // for towns and castles
```

The engine tracks this, saves it, and fires `OnGovernorChanged` when it changes. **Governors are our fief lords with no extra infrastructure.**

### Notables — village headmen already exist

```csharp
Hero headman = settlement.Notables
    .FirstOrDefault(n => n.CharacterObject?.Occupation == Occupation.Headman);
```

Every village spawns a Headman notable. They are hero objects with relationships, quests, and loyalty tracking. **We use them as Tier 4, no new heroes needed.**

---

## The Castle → Town Gap

Bannerlord has no castle→town link. A castle only knows its owning clan, not which town it is "under". We must build and persist this.

### Strategy A — Pre-built lookup table

Define the mapping manually at game start based on game knowledge. Most reliable. The map is small enough to hardcode.

### Strategy B — Spatial proximity

At `OnNewGameCreated`, for each castle, find the nearest town by map position:

```csharp
private string FindNearestTownForCastle(Settlement castle)
{
    return Settlement.All
        .Where(s => s.IsTown)
        .OrderBy(t => t.Position2D.DistanceSquared(castle.Position2D))
        .FirstOrDefault()?.StringId;
}
```

**Strategy B is preferred** — it automatically handles any map changes and doesn't require maintaining a handcrafted table. Run once at new game, save the result.

---

## Data Model

```csharp
namespace TheHindostanMod
{
    public static class FiefHierarchy
    {
        // The single piece of data we must persist: castle → its overlord town
        // Key = castle StringId, Value = town StringId
        public static Dictionary<string, string> CastleToTown
            = new Dictionary<string, string>();

        // ── Liege lookup ──────────────────────────────────────────────────────
        // Returns the liege lord of any hero who holds a fief,
        // or null if they are the emperor or hold no fief.
        public static Hero GetLiege(Hero lord)
        {
            if (lord == null) return null;

            Kingdom kingdom = lord.Clan?.Kingdom;
            if (kingdom == null) return null;

            // Emperor has no liege
            if (lord == kingdom.Leader) return null;

            // Find which settlement this hero governs
            Settlement fief = GetFiefOf(lord);
            if (fief == null) return GetLiegeByKingdomOnly(lord, kingdom);

            return GetLiegeOfSettlement(fief, kingdom);
        }

        // Returns the liege of whoever holds the given settlement
        public static Hero GetLiegeOfSettlement(Settlement settlement, Kingdom kingdom = null)
        {
            kingdom ??= settlement.OwnerClan?.Kingdom;
            if (kingdom == null) return null;

            if (settlement.IsVillage)
            {
                // Village → bound settlement's Governor
                Settlement parent = settlement.Village?.Bound;
                return parent?.Governor ?? kingdom.Leader;
            }

            if (settlement.IsCastle)
            {
                // Castle → its overlord town's Governor
                if (CastleToTown.TryGetValue(settlement.StringId, out string townId))
                {
                    Settlement town = Settlement.Find(townId);
                    return town?.Governor ?? kingdom.Leader;
                }
                return kingdom.Leader;
            }

            if (settlement.IsTown)
            {
                // Town → Emperor/King directly
                return kingdom.Leader;
            }

            return null;
        }

        // Which settlement does this hero govern?
        public static Settlement GetFiefOf(Hero hero)
        {
            return Settlement.All.FirstOrDefault(s =>
                s.Governor == hero ||
                (s.IsVillage && s.Notables.Any(n => n == hero)));
        }

        // All vassals of a given lord (heroes who govern settlements
        // whose liege is this hero)
        public static IEnumerable<Hero> GetVassals(Hero liege)
        {
            return Settlement.All
                .Where(s => GetLiegeOfSettlement(s) == liege)
                .Select(s => s.Governor ?? s.Notables
                    .FirstOrDefault(n => n.CharacterObject?.Occupation == Occupation.Headman))
                .Where(h => h != null && h != liege)
                .Distinct();
        }

        // Is heroA the liege of heroB (directly or at any tier above)?
        public static bool IsLiegeOf(Hero potentialLiege, Hero vassal)
        {
            Hero current = GetLiege(vassal);
            int depth = 0;
            while (current != null && depth < 5)
            {
                if (current == potentialLiege) return true;
                current = GetLiege(current);
                depth++;
            }
            return false;
        }

        // Helper: for lords who hold no specific settlement
        private static Hero GetLiegeByKingdomOnly(Hero lord, Kingdom kingdom)
            => kingdom.Leader != lord ? kingdom.Leader : null;
    }
}
```

---

## FiefHierarchyBehavior

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TheHindostanMod
{
    public class FiefHierarchyBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(
                this, OnNewGameCreated);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(
                this, OnGameLoaded);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(
                this, OnSettlementOwnerChanged);
            CampaignEvents.OnGovernorChangedEvent.AddNonSerializedListener(
                this, OnGovernorChanged);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(
                this, OnWeeklyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // The only data we persist is the castle→town mapping
            // (governor assignments are already saved by the engine itself)
            var keys   = FiefHierarchy.CastleToTown.Keys.ToList();
            var values = FiefHierarchy.CastleToTown.Values.ToList();

            dataStore.SyncData("hind_fief_castle_keys",   ref keys);
            dataStore.SyncData("hind_fief_castle_values", ref values);

            if (!dataStore.IsSaving)
            {
                FiefHierarchy.CastleToTown.Clear();
                for (int i = 0; i < Math.Min(keys.Count, values.Count); i++)
                    FiefHierarchy.CastleToTown[keys[i]] = values[i];
            }
        }

        // ── Setup ─────────────────────────────────────────────────────────────
        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            BuildCastleToTownMap();
            AssignInitialGovernors();
            Debug.Print($"[Hindostan] Fief hierarchy built: " +
                        $"{FiefHierarchy.CastleToTown.Count} castle→town links, " +
                        $"{CountGovernedSettlements()} governed settlements");
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            // Map was loaded via SyncData — just verify it's populated
            if (FiefHierarchy.CastleToTown.Count == 0)
                BuildCastleToTownMap();
        }

        // ── Castle → Town map ─────────────────────────────────────────────────
        private void BuildCastleToTownMap()
        {
            FiefHierarchy.CastleToTown.Clear();
            var towns = Settlement.All.Where(s => s.IsTown).ToList();

            foreach (Settlement castle in Settlement.All.Where(s => s.IsCastle))
            {
                // Find the nearest town by Euclidean distance on the campaign map
                Settlement nearest = towns
                    .OrderBy(t => t.Position2D.DistanceSquared(castle.Position2D))
                    .FirstOrDefault();

                if (nearest != null)
                {
                    FiefHierarchy.CastleToTown[castle.StringId] = nearest.StringId;
                    Debug.Print($"[Hindostan] {castle.Name} → {nearest.Name}");
                }
            }
        }

        // ── Initial governor assignment ───────────────────────────────────────
        private void AssignInitialGovernors()
        {
            // Towns: assign the ruling clan's best Steward/Leadership hero
            foreach (Settlement town in Settlement.All.Where(s => s.IsTown))
            {
                if (town.Governor != null) continue; // already assigned (won't overwrite)
                Hero best = FindBestGovernorCandidate(town.OwnerClan, town);
                if (best != null) town.Town.Governor = best;
            }

            // Castles: assign a different hero from the same or a vassal clan
            foreach (Settlement castle in Settlement.All.Where(s => s.IsCastle))
            {
                if (castle.Governor != null) continue;
                Hero best = FindBestGovernorCandidate(castle.OwnerClan, castle,
                    excludeCurrentGovernors: true);
                if (best != null) castle.Town.Governor = best;
            }
        }

        private Hero FindBestGovernorCandidate(Clan clan, Settlement settlement,
            bool excludeCurrentGovernors = false)
        {
            if (clan == null) return null;

            return clan.Heroes
                .Where(h => h.IsAlive
                         && h.IsLord
                         && h != clan.Kingdom?.Leader       // ruler doesn't govern
                         && h.PartyBelongedTo == null        // not leading a party
                         && h.CurrentSettlement == null      // not already in a settlement
                         && (!excludeCurrentGovernors || h != settlement.Governor))
                .OrderByDescending(h =>
                    h.GetSkillValue(DefaultSkills.Steward) +
                    h.GetSkillValue(DefaultSkills.Leadership))
                .FirstOrDefault();
        }

        // ── Reacting to ownership changes ─────────────────────────────────────
        private void OnSettlementOwnerChanged(Settlement settlement,
            bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            // When a settlement changes hands, replace the governor with someone
            // from the new owning clan
            if (settlement.IsTown || settlement.IsCastle)
            {
                Hero newGovernor = FindBestGovernorCandidate(
                    settlement.OwnerClan, settlement);

                if (newGovernor != null)
                {
                    settlement.Town.Governor = newGovernor;
                    Debug.Print($"[Hindostan] {settlement.Name} new governor: {newGovernor.Name}");
                }
                else
                {
                    settlement.Town.Governor = null; // temporarily vacant
                }
            }
        }

        private void OnGovernorChanged(Settlement settlement, Hero oldGovernor, Hero newGovernor)
        {
            // If the player manually changes a governor, log the hierarchy change
            if (settlement.OwnerClan?.Kingdom == Hero.MainHero?.Clan?.Kingdom)
            {
                Hero liege = FiefHierarchy.GetLiegeOfSettlement(settlement);
                if (newGovernor != null && liege != null)
                    Debug.Print($"[Hindostan] {newGovernor.Name} is now lord of " +
                                $"{settlement.Name}, vassal of {liege.Name}");
            }
        }

        // ── Weekly maintenance ────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            // Ensure no towns or castles are left ungoverned indefinitely
            foreach (Settlement s in Settlement.All
                .Where(s => (s.IsTown || s.IsCastle) && s.Governor == null))
            {
                Hero candidate = FindBestGovernorCandidate(s.OwnerClan, s);
                if (candidate != null)
                    s.Town.Governor = candidate;
            }
        }

        private int CountGovernedSettlements()
            => Settlement.All.Count(s => (s.IsTown || s.IsCastle) && s.Governor != null);
    }
}
```

---

## Tax Flow Up the Chain

Each tier skims a small percentage of tax revenue and passes the rest upward. The net effect: the emperor receives a trickle from every village in his realm; lords earn more from settlements they directly control.

```csharp
public class FeudalTaxBehavior : CampaignBehaviorBase
{
    // Percentage that flows UP each tier boundary
    private const float VILLAGE_TO_CASTLE_TAX = 0.10f;  // headman sends 10% up to castle lord
    private const float CASTLE_TO_TOWN_TAX    = 0.08f;  // castle lord sends 8% up to town lord
    private const float TOWN_TO_KING_TAX      = 0.05f;  // town lord sends 5% up to king

    public override void RegisterEvents()
    {
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public override void SyncData(IDataStore dataStore) { }

    private void OnDailyTick()
    {
        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
            ProcessKingdomTaxFlow(kingdom);
    }

    private void ProcessKingdomTaxFlow(Kingdom kingdom)
    {
        // Town lords pay a daily tithe to the king
        foreach (Settlement town in kingdom.Settlements.Where(s => s.IsTown))
        {
            Hero townLord = town.Governor;
            if (townLord?.Clan == null) continue;
            if (townLord == kingdom.Leader) continue; // ruler owns the town directly

            // Tithe is based on town prosperity
            int tithe = (int)(town.Town.Prosperity * TOWN_TO_KING_TAX * 0.1f);
            if (tithe <= 0) continue;

            int available = townLord.Clan.Gold;
            int actual    = Math.Min(tithe, available);
            if (actual <= 0) continue;

            townLord.Clan.Gold       -= actual;
            kingdom.RulingClan.Gold  += actual;
        }

        // Castle lords pay a daily tithe to their town lord
        foreach (Settlement castle in kingdom.Settlements.Where(s => s.IsCastle))
        {
            Hero castleLord = castle.Governor;
            if (castleLord?.Clan == null) continue;

            Hero townLord = FiefHierarchy.GetLiegeOfSettlement(castle) ;
            if (townLord == null || townLord.Clan == null) continue;
            if (castleLord.Clan == townLord.Clan) continue; // same clan, no inter-clan tax

            int tithe  = (int)(castle.Town.Prosperity * CASTLE_TO_TOWN_TAX * 0.05f);
            int actual = Math.Min(tithe, castleLord.Clan.Gold);
            if (actual <= 0) continue;

            castleLord.Clan.Gold -= actual;
            townLord.Clan.Gold   += actual;
        }
    }
}
```

**Why these numbers are small:** Bannerlord's daily tick runs 84 times per year. The percentages above are designed so annual effective tax rates are:
- Village → Castle: ~3% of village income annually
- Castle → Town: ~2.5% of castle income annually
- Town → King: ~1.5% of town income annually

These are intentionally modest — enough to create a gold flow and a political signal, not enough to bankrupt lords.

---

## Liege Loyalty and Defection Risk

A lord's relationship with their liege affects their likelihood to stay in the kingdom and follow orders. This hooks into `CampaignEvents.OnClanChangedKingdom`.

```csharp
public class LiegeLoyaltyBehavior : CampaignBehaviorBase
{
    private const int DEFECTION_RELATION_THRESHOLD = -30;

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.OnClanChangedKingdom.AddNonSerializedListener(
            this, OnClanChangedKingdom);
    }

    public override void SyncData(IDataStore dataStore) { }

    private void OnWeeklyTick()
    {
        // For the player's kingdom: warn when a vassal's relation with their
        // direct liege drops dangerously low
        Kingdom playerKingdom = Hero.MainHero?.Clan?.Kingdom;
        if (playerKingdom == null) return;

        foreach (Settlement s in playerKingdom.Settlements.Where(
            s => s.IsTown || s.IsCastle))
        {
            Hero lord  = s.Governor;
            Hero liege = FiefHierarchy.GetLiegeOfSettlement(s);

            if (lord == null || liege == null || lord.Clan == liege.Clan) continue;

            int relation = CharacterRelationManager.GetHeroRelation(lord, liege);
            if (relation < DEFECTION_RELATION_THRESHOLD)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{lord.Name} ({s.Name}) is dangerously disaffected with " +
                    $"their liege {liege.Name}. Relation: {relation}.",
                    Color.FromUint(0xFFCC4400)));
            }
        }
    }

    private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom,
        Kingdom newKingdom,
        ChangeKingdomAction.ChangeKingdomActionDetail detail,
        bool showNotification)
    {
        if (oldKingdom == null) return;

        // When a clan leaves, remove all their heroes from governor positions
        // in the old kingdom's settlements
        foreach (Hero hero in clan.Heroes)
        {
            foreach (Settlement s in oldKingdom.Settlements
                .Where(s => s.Governor == hero))
            {
                s.Town.Governor = null;
                Debug.Print($"[Hindostan] {hero.Name} removed as governor of " +
                            $"{s.Name} after clan defection");
            }
        }
    }
}
```

### Relationship drivers

Use `ChangeRelationAction` to reflect the political relationship naturally:
- Lord receives a fief from their liege → `+10` relation
- Liege demands excessive tax → `-5` relation (applied in FeudalTaxBehavior if lord can't pay)
- Lord's castle is captured (liege failed to protect) → `-8` relation
- Lord wins a battle defending their liege's territory → `+5` relation

---

## Player Interactions

### Viewing your vassals

```csharp
// In GameMenu, at your capital:
private void ShowVassalReport()
{
    Kingdom kingdom = Hero.MainHero?.Clan?.Kingdom;
    if (kingdom == null) return;

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("Direct Vassals:\n");

    var directVassals = FiefHierarchy.GetVassals(kingdom.Leader).ToList();
    if (directVassals.Count == 0)
    {
        sb.AppendLine("  No vassals holding fiefs.");
    }
    else
    {
        foreach (Hero vassal in directVassals)
        {
            Settlement fief = FiefHierarchy.GetFiefOf(vassal);
            int relation = CharacterRelationManager
                .GetHeroRelation(kingdom.Leader, vassal);
            sb.AppendLine($"  {vassal.Name} — {fief?.Name ?? "no fief"} " +
                          $"(Relation: {relation})");
        }
    }

    InformationManager.ShowInquiry(new InquiryData(
        "Your Vassals", sb.ToString(),
        false, true, "", "Close", null, () => { }));
}
```

### Granting a fief to a lord

When you capture a settlement and want to grant it to a specific hero:

```csharp
private void GrantFiefToLord(Settlement settlement, Hero newLord)
{
    if (settlement.Town == null) return;

    Hero oldLord = settlement.Governor;
    settlement.Town.Governor = newLord;

    // Positive relation with the recipient
    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
        Hero.MainHero, newLord, 10);

    // If displacing someone, negative relation with them
    if (oldLord != null && oldLord != newLord)
        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
            Hero.MainHero, oldLord, -8);

    InformationManager.DisplayMessage(new InformationMessage(
        $"{settlement.Name} granted to {newLord.Name}."));
}
```

### Demanding homage (loyalty check)

Once per year, the player can call all vassals to swear homage — lords with low relation pay influence, lords who refuse risk expulsion:

```csharp
private void DemandHomage()
{
    Kingdom kingdom = Hero.MainHero?.Clan?.Kingdom;
    if (kingdom == null) return;

    int influenceGained = 0;
    var disaffected = new List<Hero>();

    foreach (Hero vassal in FiefHierarchy.GetVassals(kingdom.Leader))
    {
        int relation = CharacterRelationManager
            .GetHeroRelation(kingdom.Leader, vassal);

        if (relation >= 0)
        {
            // Loyal vassal: contributes a small influence token
            influenceGained += 5 + (relation / 10);
        }
        else
        {
            disaffected.Add(vassal);
        }
    }

    kingdom.RulingClan.Influence += influenceGained;

    string msg = $"Homage received. Gained {influenceGained} influence.";
    if (disaffected.Count > 0)
        msg += $"\n{disaffected.Count} vassal(s) refuse to appear.";

    InformationManager.DisplayMessage(new InformationMessage(msg));
}
```

---

## Hindostan Flavour — Jagirdari vs European Feudalism

The four kingdoms in this mod have fundamentally different attitudes toward the hierarchy. This affects how tightly the system enforces loyalty and tax.

### Mughal — Jagirdari (assignment, not hereditary)

The Mughal system was NOT hereditary feudalism. The Emperor assigned *jagirs* (revenue rights over territory) to *mansabdars*, who held them at imperial pleasure — they could be transferred or revoked at any time.

**Implementation:** Mughal governors can be replaced by the AI king without relation penalty. Add a `JagirdariKingdomPolicy` flag for kingdoms using this system:

```csharp
// In OnSettlementOwnerChanged or a yearly tick:
bool isJagirdari = kingdom.HasPolicy(/* jagirdari policy */);
if (isJagirdari)
{
    // Emperor freely reassigns governors — no relation penalty to displaced lord
    // (He was holding a jagir, not a hereditary fief)
}
else
{
    // European/Rajput feudalism: displacing a lord is politically costly
    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(king, displacedLord, -15);
}
```

**Tie-in to Mansabdari model:** The mansab rank (from [Chapter 5](05-Game-Model-Overrides.md)) determines which tier of fief a Mughal lord can hold:

| Mansab (Renown) | Maximum fief tier |
|---|---|
| < 500 | Village headman only |
| 500–999 | Castle lord |
| 1000–1999 | Town lord |
| 2000+ | Province (multiple towns) |

### Rajput — Hereditary clan territories

Rajput clans claim their home territories by right of lineage. Displacing a Rajput from their ancestral fort causes very large relation penalties — not just with that lord, but with all Rajput-culture lords who see it as an attack on the sacred order.

```csharp
// In GrantFiefToLord:
if (oldLord?.Culture?.StringId == "vlandia" &&
    oldLord.HomeSettlement?.StringId == settlement.StringId)
{
    // Displacing a Rajput from his ancestral home
    foreach (Hero otherRajput in Kingdom.All
        .SelectMany(k => k.Clans)
        .SelectMany(c => c.Heroes)
        .Where(h => h.Culture?.StringId == "vlandia" && h.IsLord))
    {
        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
            Hero.MainHero, otherRajput, -5);
    }
}
```

### Maratha — Confederacy (loose hierarchy, strong horizontal ties)

Maratha *sardars* nominally serve the Peshwa but have strong horizontal loyalties to each other. Tax flow up the chain is irregular. Lords are more likely to coordinate *across* tiers than through them.

**Implementation:** Lower TOWN_TO_KING_TAX for battania-culture kingdoms, but apply a bonus to Maratha clans' horizontal relations — they help each other in battles more readily.

### Sikh — Misl system (no fixed territory, collective ownership)

Sikh Misls don't really hold fixed territories — they roam and control zones. The hierarchy model fits least well here.

**Implementation:** Sikh-culture settlements have no individual governor. Instead, the Misl leader (`clan.Leader`) is the de facto authority over ALL of that Misl's settlements — no castle-to-town sub-hierarchy within a Misl.

```csharp
// Override GetLiegeOfSettlement for Sikh-culture settlements:
if (settlement.Culture?.StringId == "khuzait")
{
    // All settlements in a Misl report directly to the Misl leader
    return settlement.OwnerClan?.Leader ?? kingdom.Leader;
}
```

---

## What This Does NOT Attempt

| Feature | Complexity | Notes |
|---|---|---|
| New Hero creation for village nobles | High | Vanilla doesn't spawn hero lords for villages; Notables substitute |
| Inheritance of fiefs on lord death | High | Needs `OnHeroKilledEvent` + clan heir lookup + player notice |
| Contested fiefs when a lord dies without heirs | Very High | Essentially a new decision type |
| Sub-infeudation (a castle lord granting villages to sub-vassals) | High | Third tier down from town is already Tier 3; villages would be Tier 4 — still manageable |
| Custom settlement transfer UI | Very High | Requires Gauntlet screen work |
| Attainder (stripping fiefs for treason) | Medium | Doable with `settlement.Town.Governor = null` + war declaration |

---

**[← Home](Home.md)**
