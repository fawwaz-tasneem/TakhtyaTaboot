# Chapter 5 — Game Model Overrides

> Models are calculation classes. You subclass the default, call `base.Method()` for the vanilla result, then modify it.

**[← Chapter 4](04-Campaign-Behaviors.md)** | **[Home](Home.md)** | **[Next: Harmony Patching →](06-Harmony-Patching.md)**

---

## Contents

- [How model registration works](#how-model-registration-works)
- [ExplainedNumber — the stat modifier type](#explainednumber--the-stat-modifier-type)
- [Party speed model — monsoon and terrain](#party-speed-model--monsoon-and-terrain)
- [Combat simulation model — culture damage bonuses](#combat-simulation-model--culture-damage-bonuses)
- [Party size model — Mansabdari system](#party-size-model--mansabdari-system)
- [Workshop production model](#workshop-production-model)
- [Available model base classes](#available-model-base-classes)

---

## How Model Registration Works

```csharp
// In HindostanSubModule.OnGameStart:
var starter = (CampaignGameStarter)gameStarter;
starter.AddModel(new HindostanPartySpeedModel());
```

Bannerlord maintains **one instance per model type**. When you add `HindostanPartySpeedModel` (which extends `DefaultPartySpeedCalculatingModel`), it replaces the default entirely for the lifetime of the campaign.

**Mod conflict warning:** If two mods both add a model of the same base type, only the last one registered applies. The first one is silently discarded. This is why Harmony patching is sometimes safer for widely-used models.

---

## ExplainedNumber — the Stat Modifier Type

Bannerlord uses `ExplainedNumber` instead of a plain float for any stat that shows a tooltip. It tracks all modifiers separately so players can see *why* their speed is what it is.

```csharp
ExplainedNumber result = base.CalculateBaseSpeed(...);
// result.ResultNumber is the current computed value

// Add a flat bonus/penalty
result.Add(0.5f, new TextObject("{=!}Mughal Road Network"));

// Add a percentage multiplier (factor)
result.AddFactor(-0.30f, new TextObject("{=!}Monsoon Rains"));   // -30%
result.AddFactor( 0.20f, new TextObject("{=!}Ganimi Kava"));     // +20%

// Never multiply result.ResultNumber directly — use AddFactor instead
// WRONG:  result.ResultNumber *= 0.7f;   // bypasses tooltip tracking
// RIGHT:  result.AddFactor(-0.30f, new TextObject("{=!}Monsoon Rains"));

return result;
```

The `TextObject("{=!}Label")` is the label shown in the tooltip breakdown.

---

## Party Speed Model — Monsoon and Terrain

```csharp
using TaleWorlds.CampaignSystem.GameComponents;

namespace TheHindostanMod
{
    public class HindostanPartySpeedModel : DefaultPartySpeedCalculatingModel
    {
        public override ExplainedNumber CalculateBaseSpeed(
            MobileParty party,
            bool includeDescriptions = false,
            int additionalTroopOnFootCount = 0,
            int additionalTroopOnHorseCount = 0)
        {
            ExplainedNumber result = base.CalculateBaseSpeed(
                party, includeDescriptions,
                additionalTroopOnFootCount, additionalTroopOnHorseCount);

            if (party == null) return result;

            string cultureId = party.Party?.Culture?.StringId ?? "";

            // Monsoon penalty — applies to all parties
            if (MonsoonSeasonBehavior.IsMonsoonActive)
                result.AddFactor(-0.30f, new TextObject("{=!}Monsoon Rains"));

            // Culture terrain bonuses
            TerrainType terrain = GetTerrainUnderParty(party);

            if (cultureId == "battania" &&
                (terrain == TerrainType.Forest || terrain == TerrainType.Hill))
                result.AddFactor(0.20f, new TextObject("{=!}Ganimi Kava"));

            if (cultureId == "sturgia" && terrain == TerrainType.Mountain)
                result.AddFactor(0.15f, new TextObject("{=!}Mountain Warfare"));

            if (cultureId == "khuzait" && terrain == TerrainType.Plain)
                result.AddFactor(0.10f, new TextObject("{=!}Punjab Plains"));

            return result;
        }

        private TerrainType GetTerrainUnderParty(MobileParty party)
        {
            if (party.Position2D == default) return TerrainType.Plain;
            Campaign.Current.MapSceneWrapper.GetTerrainTypeAndHeightAtPosition(
                party.Position2D, out TerrainType terrain, out _);
            return terrain;
        }
    }
}
```

`MonsoonSeasonBehavior.IsMonsoonActive` is a `public static` property on the behavior — the standard pattern for sharing state from a behavior into a model.

---

## Combat Simulation Model — Culture Damage Bonuses

```csharp
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;

namespace TheHindostanMod
{
    public class HindostanCombatSimulationModel : DefaultCombatSimulationModel
    {
        public override int SimulateHit(
            CharacterObject strikerTroop,
            CharacterObject struckTroop,
            PartyBase strikerParty,
            PartyBase struckParty,
            float strikerAdvantage,
            PartyBase reinforcementParty)
        {
            int baseHit = base.SimulateHit(
                strikerTroop, struckTroop,
                strikerParty, struckParty,
                strikerAdvantage, reinforcementParty);

            string attackerCulture = strikerParty?.Culture?.StringId ?? "";

            // Rajput heavy cavalry charge — +25% damage
            if (attackerCulture == "vlandia" &&
                strikerTroop?.DefaultFormationClass == FormationClass.HeavyCavalry)
                baseHit = (int)(baseHit * 1.25f);

            // Mughal siege artillery — +20% when attacking a defended settlement
            if (attackerCulture == "empire" &&
                struckParty?.Settlement?.IsUnderSiege == true)
                baseHit = (int)(baseHit * 1.20f);

            // Maratha skirmishers — +15% for ranged/skirmish troops
            if (attackerCulture == "battania" &&
                (strikerTroop?.DefaultFormationClass == FormationClass.Skirmisher ||
                 strikerTroop?.DefaultFormationClass == FormationClass.Ranged))
                baseHit = (int)(baseHit * 1.15f);

            return baseHit;
        }
    }
}
```

---

## Party Size Model — Mansabdari System

Historically, Mughal nobles held *mansabs* — ranks that entitled them to command a fixed number of troops proportional to their renown and service. This model implements that as a party size bonus.

```csharp
using TaleWorlds.CampaignSystem.GameComponents;

namespace TheHindostanMod
{
    public class MansabdariPartySizeModel : DefaultPartySizeCalculatingModel
    {
        public override ExplainedNumber GetPartyMemberSizeLimit(
            PartyBase party, bool includeDescriptions = false)
        {
            ExplainedNumber result = base.GetPartyMemberSizeLimit(party, includeDescriptions);

            if (party?.LeaderHero == null) return result;

            Hero leader = party.LeaderHero;
            string cultureId = leader.Culture?.StringId ?? "";

            if (cultureId == "empire" || cultureId == "empire_w" || cultureId == "empire_s")
            {
                // Mansab rank tiers based on renown
                float bonus = leader.Clan?.Renown switch
                {
                    >= 3000 => 0.40f,  // Amir-ul-Umara (top commanders)
                    >= 2000 => 0.30f,  // Amir (senior nobles)
                    >= 1000 => 0.20f,  // Mansabdar 1000 zat
                    >= 500  => 0.10f,  // Mansabdar 500 zat
                    _       => 0.0f
                };

                if (bonus > 0)
                    result.AddFactor(bonus, new TextObject("{=!}Mansabdari Rank"));
            }

            return result;
        }
    }
}
```

---

## Workshop Production Model

Bengal's textile mills and Mysore's spice markets should produce more than average workshops.

```csharp
using TaleWorlds.CampaignSystem.GameComponents;

namespace TheHindostanMod
{
    public class HindostanWorkshopModel : DefaultWorkshopModel
    {
        public override int GetMaxWorkshopCountForTier(int tier)
        {
            return base.GetMaxWorkshopCountForTier(tier);
        }

        public override float GetProductionEfficiencyFactor(Workshop workshop)
        {
            float baseEfficiency = base.GetProductionEfficiencyFactor(workshop);

            string settlementCulture = workshop.Settlement?.Culture?.StringId ?? "";

            // Bengal — world-leading textile production
            if (settlementCulture == "empire_w" &&
                workshop.WorkshopType?.StringId == "loom")
                return baseEfficiency * 1.5f;

            // Mysore — spice and silk trade hub
            if (settlementCulture == "aserai" &&
                workshop.WorkshopType?.StringId == "velvet_weavery")
                return baseEfficiency * 1.35f;

            return baseEfficiency;
        }
    }
}
```

---

## Available Model Base Classes

| Base class | What it controls |
|------------|-----------------|
| `DefaultPartySpeedCalculatingModel` | Map movement speed |
| `DefaultCombatSimulationModel` | Battle simulation damage |
| `DefaultPartySizeCalculatingModel` | Max party size |
| `DefaultWorkshopModel` | Workshop production efficiency |
| `DefaultClanFinanceModel` | Daily clan income/expenses |
| `DefaultTournamentModel` | Tournament prize distribution |
| `DefaultPrisonerRecruitmentCalculationModel` | Prisoner conversion rate |
| `DefaultSettlementFoodModel` | Settlement food production |
| `DefaultSettlementProsperityModel` | Town prosperity growth |
| `DefaultMilitiaModel` | Militia size and growth |

Always call `base.MethodName(...)` first to get the vanilla result, then modify it.

---

**[← Chapter 4](04-Campaign-Behaviors.md)** | **[Home](Home.md)** | **[Next: Harmony Patching →](06-Harmony-Patching.md)**
