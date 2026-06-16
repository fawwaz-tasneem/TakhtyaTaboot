# Chapter 17 — Quality of Life and Depth Systems

> Ten historically-grounded mechanics that turn the campaign from a battle simulator into a living world. Each is rooted in the actual political economy of 1719–1720 India.

**[← Chapter 16](16-Civil-War-and-Imprisonment.md)** | **[Home](Home.md)**

---

## Contents

1. [Seasonal Calendar and Monsoon](#1-seasonal-calendar-and-monsoon)
2. [Nazrana — Ritual Gift Obligation](#2-nazrana--ritual-gift-obligation)
3. [Trade Route Network and Merchant Caravans](#3-trade-route-network-and-merchant-caravans)
4. [Famine and Food Security](#4-famine-and-food-security)
5. [Epidemic and Disease Spread](#5-epidemic-and-disease-spread)
6. [Religious Tolerance Policy](#6-religious-tolerance-policy)
7. [Festivals and Cultural Events](#7-festivals-and-cultural-events)
8. [Akhbarat — Intelligence Network](#8-akhbarat--intelligence-network)
9. [Cultural Patronage](#9-cultural-patronage)
10. [Caravanserai — Information Brokers](#10-caravanserai--information-brokers)

---

## 1. Seasonal Calendar and Monsoon

### Historical basis

India does not have four European seasons. It has three functional campaign seasons:

| Indian season | Months | Character |
|---------------|--------|-----------|
| Garmi (pre-monsoon) | March–May | Brutal heat, dust, poor army morale, fast marches on dry roads |
| Barsat (monsoon) | June–September | Heavy rain, rivers flood, road mud, disease spikes, crops planted |
| Sardi (post-monsoon + winter) | October–February | Pleasant, prime campaign and harvest time, heavy trade |

The Mughals timed their campaigns around this. Armies avoided campaigning in Barsat. Most major battles — Panipat, Karnal — were fought in Sardi.

### Mapping to Bannerlord seasons

Bannerlord's `CampaignTime.GetSeasonOfYear()` returns 0–3 (Spring, Summer, Autumn, Winter). We remap:

| Bannerlord season | Hindostan equivalent | Period |
|-------------------|---------------------|--------|
| Spring (0) | Pre-monsoon | Dry heat campaign window |
| Summer (1) | Monsoon (Barsat) | Slow movement, floods, crop growth |
| Autumn (2) | Post-monsoon harvest | Peak income, festivals, prime campaign |
| Winter (3) | Cold-season campaign | Mountain passes close, Deccan armies active |

### Effects per season

```csharp
public static class SeasonalEffects
{
    public static float GetMoveSpeedMultiplier(MobileParty party)
    {
        int season = (int)CampaignTime.Now.GetSeasonOfYear();
        string terrain = GetDominantTerrain(party.Position2D);

        return (season, terrain) switch
        {
            (1, "plain")    => 0.70f,  // Monsoon on plains: deep mud
            (1, "ford")     => 0.50f,  // River crossings nearly impassable
            (1, "forest")   => 0.80f,  // Forest offers some shelter
            (0, "plain")    => 1.05f,  // Pre-monsoon: dry roads
            (2, _)          => 1.10f,  // Post-monsoon: perfect roads
            (3, "mountain") => 0.60f,  // Winter: mountain passes blocked
            _               => 1.00f
        };
    }

    public static float GetVillageIncomeMultiplier(Settlement settlement)
    {
        int season = (int)CampaignTime.Now.GetSeasonOfYear();
        string cultureId = settlement.Culture?.StringId ?? "";

        return (season, cultureId) switch
        {
            (1, _)         => 1.20f,  // Monsoon: crops being planted, anticipation bonus
            (2, _)         => 1.40f,  // Harvest: peak rural income
            (3, "aserai")  => 1.15f,  // Mysore/South India: second harvest in winter
            (0, _)         => 0.85f,  // Pre-monsoon: reserves low before harvest
            _              => 1.00f
        };
    }

    public static float GetDiseaseRiskMultiplier()
    {
        return (int)CampaignTime.Now.GetSeasonOfYear() == 1 ? 2.0f : 1.0f;
    }

    private static string GetDominantTerrain(Vec2 pos)
    {
        // Simplified: use PathFaceRecord for terrain type lookup
        // Full impl requires reading PathFaceRecord from terrainData
        return "plain";
    }
}
```

### SeasonalBehavior — the main wiring

```csharp
public class SeasonalBehavior : CampaignBehaviorBase
{
    private int _lastSeason = -1;

    public override void RegisterEvents()
    {
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("hind_last_season", ref _lastSeason);
    }

    private void OnDailyTick()
    {
        int currentSeason = (int)CampaignTime.Now.GetSeasonOfYear();
        if (currentSeason == _lastSeason) return;
        _lastSeason = currentSeason;
        OnSeasonChanged(currentSeason);
    }

    private void OnSeasonChanged(int newSeason)
    {
        string msg = newSeason switch
        {
            0 => "The heat of the pre-monsoon season is upon us. Rivers run low; roads are fast but morale suffers.",
            1 => "The monsoon has arrived. Rivers swell. Movement across the plains becomes treacherous. Villages will flourish by autumn.",
            2 => "The rains have ended. The harvest is in. This is the prime season for campaign and trade.",
            3 => "Winter settles. Mountain passes in the north are closing. The Deccan remains open.",
            _ => ""
        };

        if (!string.IsNullOrEmpty(msg))
            InformationManager.DisplayMessage(new InformationMessage(
                $"[Season Change] {msg}", Color.FromUint(0xFF88CCFF)));

        // Trigger festival events if applicable
        FestivalBehavior.Instance?.CheckForSeasonalFestival(newSeason);
    }
}
```

### Model override — movement speed

Patch `DefaultPartySpeedCalculatingModel` to inject the seasonal multiplier:

```csharp
[HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), "CalculateFinalSpeed")]
public static class MonsoonSpeedPatch
{
    public static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
    {
        float mult = SeasonalEffects.GetMoveSpeedMultiplier(mobileParty);
        if (Math.Abs(mult - 1f) > 0.01f)
        {
            string label = (int)CampaignTime.Now.GetSeasonOfYear() == 1
                ? "Monsoon mud" : "Seasonal roads";
            __result.AddFactor(mult - 1f, new TextObject($"{{=!}}{label}"));
        }
    }
}
```

---

## 2. Nazrana — Ritual Gift Obligation

### Historical basis

In the Mughal court, **nazrana** (presentation gift) was a compulsory ritual whenever a lord visited court, was promoted, or sought a favour. The gift amount was tied to the lord's rank. Withholding it was a political insult. Lavish gifts above expectation signalled ambition and loyalty simultaneously.

### Obligation structure

| Rank | Base nazrana (gold) | Cycle (days) |
|------|---------------------|--------------|
| 100 | 200 | 90 |
| 500 | 600 | 90 |
| 1000 | 1,400 | 90 |
| 2000 | 3,000 | 90 |
| 3000 | 6,000 | 90 |
| 5000 | 12,000 | 90 |

Every 90 days, the player receives a notification that nazrana is due. They have 30 days to present it.

### Three tiers of gift

| Choice | Gold cost | King relation | Influence gain | Note |
|--------|-----------|---------------|----------------|------|
| Minimal (20% of base) | base × 0.2 | −5 | −5 | Insulting — signals disrespect |
| Standard | base × 1.0 | +5 | +10 | Expected |
| Lavish | base × 2.5 | +20 | +30 | Opens petition window for rank/fief |
| Withhold | 0 | −15 | −10 | Triggers poor standing counter |

The "Lavish" option additionally opens a special dialogue branch: the king is in a generous mood and will hear one immediate petition (fief assignment, rank review, troop requisition).

### NazranaBehavior

```csharp
public class NazranaBehavior : CampaignBehaviorBase
{
    private int  _nextNazranaDay   = -1;
    private int  _nazranaDeadline  = -1;
    private bool _duePending       = false;
    private int  _missedCount      = 0;

    private static readonly int[] RankValues  = { 100, 500, 1000, 2000, 3000, 5000 };
    private static readonly int[] BaseAmounts = { 200, 600, 1400, 3000, 6000, 12000 };

    public static NazranaBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(
            this, g => _nextNazranaDay = (int)CampaignTime.Now.ToDays + 90);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("hind_nazrana_next",   ref _nextNazranaDay);
        dataStore.SyncData("hind_nazrana_dead",   ref _nazranaDeadline);
        dataStore.SyncData("hind_nazrana_pend",   ref _duePending);
        dataStore.SyncData("hind_nazrana_missed", ref _missedCount);
    }

    private void OnDailyTick()
    {
        if (Hero.MainHero?.Clan?.Kingdom == null) return;
        int rank = MansabdariBehavior.Instance?.GetRank(Hero.MainHero) ?? 0;
        if (rank == 0) return;

        int today = (int)CampaignTime.Now.ToDays;

        if (!_duePending && today >= _nextNazranaDay)
        {
            _duePending      = true;
            _nazranaDeadline = today + 30;
            int amount = GetBaseAmount(rank);

            InformationManager.DisplayMessage(new InformationMessage(
                $"Nazrana is due to the king within 30 days. " +
                $"Standard gift: {amount:N0} gold.",
                Color.FromUint(0xFFD4AF37)));
        }

        if (_duePending && today >= _nazranaDeadline)
        {
            // Automatically missed
            MissNazrana();
        }
    }

    private int GetBaseAmount(int rank)
    {
        int idx = Array.IndexOf(RankValues, rank);
        return idx >= 0 ? BaseAmounts[idx] : 200;
    }

    public void PresentGift(float tierMultiplier)
    {
        if (!_duePending) return;

        int rank   = MansabdariBehavior.Instance?.GetRank(Hero.MainHero) ?? 100;
        int amount = (int)(GetBaseAmount(rank) * tierMultiplier);
        Hero king  = Hero.MainHero?.Clan?.Kingdom?.Leader;

        if (Hero.MainHero.Gold < amount)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "You do not have enough gold for this gift.", Color.FromUint(0xFFCC4400)));
            return;
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, king, amount);

        int relationChange;
        int influenceGain;
        string msg;

        if (tierMultiplier <= 0.25f)
        {
            relationChange = -5;  influenceGain = -5;
            msg = "Your meagre gift has been noticed. The king is displeased.";
        }
        else if (tierMultiplier <= 1.1f)
        {
            relationChange = 5;   influenceGain = 10;
            msg = $"You presented nazrana of {amount:N0} gold. The king is satisfied.";
        }
        else
        {
            relationChange = 20;  influenceGain = 30;
            msg = $"Your lavish gift of {amount:N0} gold has greatly pleased the king. He will hear your petition.";
            OpenPetitionDialogue();
        }

        if (king != null)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, king, relationChange);
        Hero.MainHero.Clan.Influence += influenceGain;

        _duePending       = false;
        _missedCount      = 0;
        _nextNazranaDay   = (int)CampaignTime.Now.ToDays + 90;

        InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFD4AF37)));
    }

    private void MissNazrana()
    {
        _duePending = false;
        _missedCount++;
        _nextNazranaDay = (int)CampaignTime.Now.ToDays + 90;

        Hero king = Hero.MainHero?.Clan?.Kingdom?.Leader;
        if (king != null)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, king, -15);
        Hero.MainHero.Clan.Influence -= 10;

        ValourBehavior.Instance?.RemoveValour(5f, "Nazrana withheld");

        string warning = _missedCount >= 2
            ? " The king is watching your loyalty closely."
            : "";
        InformationManager.DisplayMessage(new InformationMessage(
            $"You failed to present nazrana. The king takes notice.{warning}",
            Color.FromUint(0xFFCC2200)));
    }

    private void OpenPetitionDialogue()
    {
        // Trigger a special inquiry asking the player what they want to petition for
        InformationManager.ShowInquiry(new InquiryData(
            "The King Hears You",
            "Your generous gift has opened the king's ear. What do you petition for?",
            true, false,
            "A new fief",
            "Cancel",
            () => FiefHolderBehavior.Instance?.GrantFiefPetition(Hero.MainHero),
            () => { }
        ));
    }
}
```

### Game menu integration

In the king's capital settlement menu:

```csharp
starter.AddGameMenuOption("town", "nazrana_present",
    "{=!}Present nazrana to the king",
    args =>
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Mission;
        return Hero.MainHero?.Clan?.Kingdom?.Leader != null
            && NazranaBehavior.Instance?.IsDue == true;
    },
    args => OpenNazranaMenu()
);
```

---

## 3. Trade Route Network and Merchant Caravans

### Historical basis

Mughal India had the most extensive overland trade network in Asia outside China. The Grand Trunk Road ran from Bengal through Delhi to Peshawar. The Deccan trade routes linked the cotton-producing Marathas with Mughal markets. Surat was the empire's primary ocean port. These were not abstract — they were physical roads with sarais (rest houses), toll gates, and constant caravan traffic.

The economy of a town lord was inseparable from the caravan traffic passing through. Disrupting a rival's trade routes was as effective as besieging their towns.

### Trade graph

Trade routes are edges connecting settlements. The graph is computed once at game start from proximity:

```csharp
public class TradeRouteBehavior : CampaignBehaviorBase
{
    // Route = pair of settlement StringIds
    private Dictionary<string, float>       _routeHealth     = new Dictionary<string, float>();
    // 0–100; low = bandits, disrupted trade
    private Dictionary<string, int>         _activeCaravans  = new Dictionary<string, int>();
    // route key → count of active caravans
    private List<(string from, string to)>  _routeGraph      = new List<(string, string)>();

    private const float MAX_ROUTE_DIST = 80f;  // map units; ~3 hour march

    public static TradeRouteBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, BuildRouteGraph);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        var keys  = _routeHealth.Keys.ToList();
        var vals  = _routeHealth.Values.ToList();
        dataStore.SyncData("hind_trade_keys", ref keys);
        dataStore.SyncData("hind_trade_vals", ref vals);
        if (!dataStore.IsSaving)
        {
            _routeHealth.Clear();
            for (int i = 0; i < keys.Count; i++)
                _routeHealth[keys[i]] = i < vals.Count ? vals[i] : 100f;
        }
    }

    private void BuildRouteGraph(CampaignGameStarter _)
    {
        var towns = Settlement.All.Where(s => s.IsTown || s.IsCastle).ToList();
        _routeGraph.Clear();
        _routeHealth.Clear();

        foreach (var a in towns)
        {
            var nearby = towns
                .Where(b => b != a && a.Position2D.DistanceSquared(b.Position2D) < MAX_ROUTE_DIST * MAX_ROUTE_DIST)
                .OrderBy(b => a.Position2D.DistanceSquared(b.Position2D))
                .Take(3);  // each settlement connects to its 3 nearest neighbours

            foreach (var b in nearby)
            {
                string key = RouteKey(a.StringId, b.StringId);
                if (!_routeGraph.Any(r => RouteKey(r.from, r.to) == key))
                {
                    _routeGraph.Add((a.StringId, b.StringId));
                    _routeHealth[key] = 100f;
                }
            }
        }
    }

    private void OnDailyTick()
    {
        // Decay route health based on bandit presence
        foreach (var route in _routeGraph)
        {
            string key   = RouteKey(route.from, route.to);
            float health = _routeHealth.GetValueOrDefault(key, 100f);

            // Count nearby bandit parties
            Settlement from = Settlement.Find(route.from);
            if (from == null) continue;
            int nearbyBandits = MobileParty.All
                .Count(p => p.IsBandit && p.IsActive
                         && p.Position2D.DistanceSquared(from.Position2D) < 20 * 20);

            float decay = nearbyBandits * 2f + 1f;        // baseline -1/day plus bandit bonus
            float recovery = DoesPlayerPatrol(route) ? 8f : 0f;

            _routeHealth[key] = Math.Clamp(health - decay + recovery, 0f, 100f);
        }
    }

    private void OnWeeklyTick()
    {
        // Spawn caravans on healthy routes; award income to route lords
        foreach (var route in _routeGraph)
        {
            string key    = RouteKey(route.from, route.to);
            float health  = _routeHealth.GetValueOrDefault(key, 100f);
            if (health < 20f) continue;  // route is too dangerous for merchants

            // Passive tariff income for lords who own the endpoints
            int tariffGold = (int)(health * 0.5f);  // max 50 gold/week per route
            AwardTariff(route.from, tariffGold / 2);
            AwardTariff(route.to, tariffGold / 2);
        }
    }

    private void AwardTariff(string settlementId, int gold)
    {
        Settlement s   = Settlement.Find(settlementId);
        Hero lord      = FiefHolderBehavior.Instance?.GetHolder(s);
        if (lord != null && gold > 0)
            GiveGoldAction.ApplyBetweenCharacters(null, lord, gold);
    }

    private bool DoesPlayerPatrol(in (string from, string to) route)
    {
        if (!MobileParty.MainParty.IsActive) return false;
        Settlement from = Settlement.Find(route.from);
        return from != null &&
               MobileParty.MainParty.Position2D.DistanceSquared(from.Position2D) < 15 * 15;
    }

    public float GetRouteHealth(Settlement a, Settlement b)
        => _routeHealth.GetValueOrDefault(RouteKey(a.StringId, b.StringId), 100f);

    public string GetRouteHealthLabel(float health) => health switch
    {
        >= 80 => "Prosperous",
        >= 50 => "Active",
        >= 20 => "Dangerous",
        _     => "Severed"
    };

    private static string RouteKey(string a, string b)
        => string.Compare(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";
}
```

### Town menu — route overview

Add to the town menu a status display: which routes pass through this town, their health, and estimated weekly tariff.

### Player patrol reward

When the player's party is near a dangerously-rated route for 3+ consecutive days:

```csharp
private int _patrolDays = 0;

// In DailyTick:
if (DoesPlayerPatrolDangerousRoute())
{
    _patrolDays++;
    if (_patrolDays >= 3)
    {
        _patrolDays = 0;
        int reward = 150;
        GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, reward);
        Hero.MainHero.Clan.Influence += 5;
        InformationManager.DisplayMessage(new InformationMessage(
            $"Merchants along the route reward your escort. +{reward} gold, +5 influence.",
            Color.FromUint(0xFFD4AF37)));
    }
}
```

---

## 4. Famine and Food Security

### Historical basis

Three major famines struck the Mughal heartland between 1700 and 1750. The Deccan famines under Aurangzeb's endless wars depopulated entire regions. The link between military campaigns and food insecurity was direct: armies consumed grain, disrupted irrigation, and diverted peasant labor to military service. A village whose men were conscripted could not plant its fields.

### Food security index

Every settlement has a `FoodSecurity` float (0–100). At 100, the village is self-sufficient and trading surplus. At 0, people are starving.

```csharp
// Part of FoodSecurityBehavior

private Dictionary<string, float> _foodSecurity = new Dictionary<string, float>();

private void UpdateFoodSecurity(Settlement settlement)
{
    string id   = settlement.StringId;
    float food  = _foodSecurity.GetValueOrDefault(id, 80f);

    // Base daily change
    float change = 0f;

    // Harvest season bonus
    float seasonMult = SeasonalEffects.GetVillageIncomeMultiplier(settlement);
    change += (seasonMult - 1f) * 5f;  // +3/day at harvest, -0.75/day in pre-monsoon

    // Irrigation canal: +1.5/day
    if (HasProject(settlement, VillageProject.IrrigationCanal)) change += 1.5f;

    // Active war nearby: −2/day
    bool nearWar = MobileParty.All.Any(p => p.IsEnemy(Hero.MainHero?.MapFaction)
                                         && p.Position2D.DistanceSquared(settlement.Position2D) < 25 * 25);
    if (nearWar) change -= 2f;

    // Army camping nearby: −1.5/day per 100 troops
    int nearbyTroops = MobileParty.All
        .Where(p => p.IsActive && !p.IsBandit
                 && p.Position2D.DistanceSquared(settlement.Position2D) < 10 * 10)
        .Sum(p => p.Party?.TotalStrength ?? 0);
    change -= nearbyTroops / 100f * 1.5f;

    // Bandit raids: −3/day when threat is high
    float banditThreat = FiefHolderBehavior.Instance?.GetBanditThreat(settlement) ?? 0f;
    if (banditThreat > 60f) change -= 3f;

    _foodSecurity[id] = Math.Clamp(food + change, 0f, 100f);
    EvaluateFamineState(settlement, _foodSecurity[id]);
}

private void EvaluateFamineState(Settlement settlement, float food)
{
    bool wasFamine = IsFamine(settlement);
    string status;

    if (food < 10f)
    {
        // Active famine
        settlement.Village?.ChangeVillageState(Village.VillageStates.Looted);  // NOT ideal but visible
        // hearth loss
        if (settlement.IsVillage)
            settlement.Village.Hearth = Math.Max(10f, settlement.Village.Hearth - 5f);

        if (!wasFamine)
            InformationManager.DisplayMessage(new InformationMessage(
                $"Famine has struck {settlement.Name}! People are starving.",
                Color.FromUint(0xFFCC2200)));
    }
    else if (food < 25f)
    {
        if (!wasFamine)
            InformationManager.DisplayMessage(new InformationMessage(
                $"Food is dangerously scarce in {settlement.Name}.",
                Color.FromUint(0xFFFF6600)));
    }
}
```

### Player intervention

```csharp
// Game menu option at any settlement with food < 30
starter.AddGameMenuOption("village", "donate_grain",
    "{=!}Donate grain reserves to the village (costs gold)",
    args => GetFoodSecurity(Settlement.CurrentSettlement) < 30f,
    args =>
    {
        int cost = 300;
        if (Hero.MainHero.Gold < cost)
        {
            InformationManager.DisplayMessage(new InformationMessage("Not enough gold."));
            return;
        }
        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost);
        SetFoodSecurity(Settlement.CurrentSettlement, GetFoodSecurity(Settlement.CurrentSettlement) + 35f);
        Hero.MainHero.Clan.Influence += 8;
        InformationManager.DisplayMessage(new InformationMessage(
            "Your donation of grain has been received with gratitude. +8 influence.",
            Color.FromUint(0xFF44AA44)));
    }
);
```

---

## 5. Epidemic and Disease Spread

### Historical basis

Cholera and smallpox were endemic in 18th-century India, spiking dramatically during the monsoon. The Mughal army's worst enemy was not Maratha cavalry — it was the water it drank after July. The term *waba* (epidemic) appears throughout Mughal chronicles as a cause of campaign failures.

### Disease level

Every settlement and every large army has a `DiseaseLevel` float (0–100).

| DiseaseLevel | Effect |
|--------------|--------|
| 0–20 | None |
| 21–40 | Merchants avoid the settlement; trade income −10% |
| 41–60 | Army loses 0.5% men/week; hearth −1/week |
| 61–80 | Army loses 1.5% men/week; hearth −3/week; notable morale −15 |
| 81–100 | Army loses 3% men/week; settlement locked (no recruitment); risk of notable death |

### Spread model

```csharp
public class EpidemicBehavior : CampaignBehaviorBase
{
    private Dictionary<string, float> _diseaseLevel = new Dictionary<string, float>();
    private float _armyDiseaseLevel = 0f;

    public static EpidemicBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnWeeklyTick()
    {
        float monsoonMult = SeasonalEffects.GetDiseaseRiskMultiplier();

        // Settlements
        foreach (var settlement in Settlement.All.Where(s => s.IsVillage || s.IsTown || s.IsCastle))
        {
            string id    = settlement.StringId;
            float level  = _diseaseLevel.GetValueOrDefault(id, 0f);

            // Natural decay
            float change = -2f;

            // Spread from nearby infected settlements
            foreach (var nearby in Settlement.All.Where(
                n => n != settlement &&
                     n.Position2D.DistanceSquared(settlement.Position2D) < 30 * 30))
            {
                float nearbyLevel = _diseaseLevel.GetValueOrDefault(nearby.StringId, 0f);
                if (nearbyLevel > 40f) change += nearbyLevel * 0.02f;  // bleed from neighbours
            }

            // Overcrowding: high hearth + monsoon season
            float hearth = settlement.Village?.Hearth ?? 0f;
            if (hearth > 1500 && monsoonMult > 1f) change += 3f;

            // Army camping nearby is a disease vector
            bool armyCamping = MobileParty.All.Any(p =>
                p.Party?.TotalStrength > 300 &&
                p.Position2D.DistanceSquared(settlement.Position2D) < 8 * 8);
            if (armyCamping) change += 5f * monsoonMult;

            level = Math.Clamp(level + change, 0f, 100f);
            _diseaseLevel[id] = level;

            ApplySettlementDiseaseEffects(settlement, level);
        }

        // Player's army
        UpdateArmyDisease(monsoonMult);
    }

    private void UpdateArmyDisease(float monsoonMult)
    {
        // Pick up disease if camped near infected settlement
        Settlement nearest = Settlement.All
            .OrderBy(s => MobileParty.MainParty.Position2D.DistanceSquared(s.Position2D))
            .FirstOrDefault();

        float nearestDisease = nearest != null
            ? _diseaseLevel.GetValueOrDefault(nearest.StringId, 0f) : 0f;

        float change = -1f;  // natural recovery when moving
        if (!MobileParty.MainParty.IsMoving) change += nearestDisease * 0.03f * monsoonMult;
        if (MobileParty.MainParty.IsBesieging) change += 5f * monsoonMult;  // sieges are petri dishes

        _armyDiseaseLevel = Math.Clamp(_armyDiseaseLevel + change, 0f, 100f);

        if (_armyDiseaseLevel > 40f)
        {
            float casualtyRate = _armyDiseaseLevel switch
            {
                > 80 => 0.03f,
                > 60 => 0.015f,
                _    => 0.005f
            };

            int casualties = (int)(MobileParty.MainParty.MemberRoster.TotalRegulars * casualtyRate);
            if (casualties > 0)
            {
                RemoveRandomTroops(MobileParty.MainParty, casualties);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Disease claims {casualties} of your men this week.",
                    Color.FromUint(0xFFCC4400)));
            }
        }
    }

    private void ApplySettlementDiseaseEffects(Settlement settlement, float level)
    {
        if (level <= 40f || settlement.Village == null) return;

        float hearthLoss = level switch
        {
            > 80 => -3f,
            > 60 => -1f,
            _    => 0f
        };

        if (hearthLoss < 0)
            settlement.Village.Hearth = Math.Max(10f, settlement.Village.Hearth + hearthLoss);
    }

    public float GetDiseaseLevel(Settlement s)
        => _diseaseLevel.GetValueOrDefault(s.StringId, 0f);

    private void RemoveRandomTroops(MobileParty party, int count)
    {
        foreach (var element in party.MemberRoster.GetTroopRoster().OrderBy(_ => MBRandom.RandomFloat))
        {
            if (element.Character?.IsHero == true || count <= 0) continue;
            int remove = Math.Min(element.Number, count);
            party.MemberRoster.AddToCounts(element.Character, -remove);
            count -= remove;
        }
    }

    public override void SyncData(IDataStore dataStore)
    {
        var keys = _diseaseLevel.Keys.ToList();
        var vals = _diseaseLevel.Values.ToList();
        dataStore.SyncData("hind_disease_keys",  ref keys);
        dataStore.SyncData("hind_disease_vals",  ref vals);
        dataStore.SyncData("hind_army_disease",  ref _armyDiseaseLevel);
        if (!dataStore.IsSaving)
        {
            _diseaseLevel.Clear();
            for (int i = 0; i < keys.Count; i++)
                _diseaseLevel[keys[i]] = i < vals.Count ? vals[i] : 0f;
        }
    }
}
```

### Quarantine option

```csharp
starter.AddGameMenuOption("town", "quarantine_settlement",
    "{=!}Order a quarantine (close the gates — halt trade but contain disease)",
    args => EpidemicBehavior.Instance?.GetDiseaseLevel(Settlement.CurrentSettlement) > 40f,
    args =>
    {
        // Reduce disease spread rate from this settlement by 70% for 30 days
        // Trade income drops 50%
        QuarantineBehavior.Instance?.ApplyQuarantine(Settlement.CurrentSettlement, 30);
        InformationManager.DisplayMessage(new InformationMessage(
            "The gates are closed. Disease will be contained but trade will suffer.",
            Color.FromUint(0xFFFF6600)));
    }
);
```

---

## 6. Religious Tolerance Policy

### Historical basis

The most consequential political variable in the Mughal empire's decline was the swing between Akbar's broad-tent policy and Aurangzeb's strict Sunni governance (including reimposition of the jizya tax on non-Muslims in 1679). Aurangzeb's approach alienated the Rajputs, accelerated the Maratha rebellion, and fragmented the empire. The tolerant mid-period policy (under Jahangir and Shah Jahan) had kept those alliances intact.

Every kingdom in the mod should have this dial. It is both a roleplay choice and a mechanical lever.

### The three stances

| Stance | Name | Key effect |
|--------|------|-----------|
| Strict | Mulk-e-Sharia | +20% income from Muslim settlements; −15 loyalty in non-Muslim settlements; Rajput/Maratha/Sikh clans −15 relation with king |
| Moderate | Sulh-e-Kul (Akbar's policy: "peace with all") | Baseline — no modifiers |
| Tolerant | Din-i-Ilahi | −10% income from devout Muslim settlements; +10 loyalty everywhere; non-Muslim notable relations +10; higher probability of Rajput/Maratha clans joining the kingdom |

### Implementation

```csharp
public enum ToleranceStance { Strict = 0, Moderate = 1, Tolerant = 2 }

public class ReligiousToleranceBehavior : CampaignBehaviorBase
{
    private Dictionary<string, ToleranceStance> _kingdomStances
        = new Dictionary<string, ToleranceStance>();

    public static ReligiousToleranceBehavior Instance { get; private set; }

    public ToleranceStance GetStance(Kingdom kingdom)
        => _kingdomStances.GetValueOrDefault(kingdom?.StringId, ToleranceStance.Moderate);

    public void SetStance(Kingdom kingdom, ToleranceStance stance)
    {
        if (kingdom == null) return;
        _kingdomStances[kingdom.StringId] = stance;
        ApplyStanceEffects(kingdom, stance);
    }

    private void ApplyStanceEffects(Kingdom kingdom, ToleranceStance stance)
    {
        foreach (Clan clan in kingdom.Clans.Where(c => c.Leader != null))
        {
            bool isNonMuslim = IsNonMuslimCulture(clan.Culture?.StringId);

            if (stance == ToleranceStance.Strict && isNonMuslim)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                    kingdom.Leader, clan.Leader, -15);

            if (stance == ToleranceStance.Tolerant && isNonMuslim)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                    kingdom.Leader, clan.Leader, +10);
        }
    }

    private bool IsNonMuslimCulture(string cultureId)
    {
        // vlandia=Rajput, battania=Maratha, khuzait=Sikh are the non-Muslim factions
        return cultureId is "vlandia" or "battania" or "khuzait";
    }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnNewGame(CampaignGameStarter _)
    {
        // Mughal empire starts Moderate
        foreach (Kingdom k in Kingdom.All)
            _kingdomStances[k.StringId] = ToleranceStance.Moderate;
    }

    private void OnWeeklyTick()
    {
        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
        {
            ToleranceStance stance = GetStance(kingdom);

            // Devout AI rulers drift toward Strict over time (simulating Aurangzeb tendency)
            // Tolerant AI drift toward Moderate
            // (simplified — full AI would weigh prestige and faction interests)
        }
    }

    public override void SyncData(IDataStore dataStore)
    {
        var ids    = _kingdomStances.Keys.ToList();
        var stances = _kingdomStances.Values.Select(s => (int)s).ToList();
        dataStore.SyncData("hind_tolerance_ids",    ref ids);
        dataStore.SyncData("hind_tolerance_stances", ref stances);
        if (!dataStore.IsSaving)
        {
            _kingdomStances.Clear();
            for (int i = 0; i < ids.Count; i++)
                _kingdomStances[ids[i]] = i < stances.Count
                    ? (ToleranceStance)stances[i] : ToleranceStance.Moderate;
        }
    }
}
```

### Income model override

Patch `DefaultSettlementTaxModel` to apply tolerance modifiers:

```csharp
[HarmonyPatch(typeof(DefaultSettlementTaxModel), "CalculateTownTax")]
public static class ToleranceTaxPatch
{
    public static void Postfix(Settlement settlement, ref ExplainedNumber __result)
    {
        Kingdom kingdom = settlement.OwnerClan?.Kingdom;
        if (kingdom == null) return;

        ToleranceStance stance = ReligiousToleranceBehavior.Instance?.GetStance(kingdom)
                              ?? ToleranceStance.Moderate;

        bool isMuslimSettlement  = !IsNonMuslimCulture(settlement.Culture?.StringId);
        bool isNonMuslimSettlement = IsNonMuslimCulture(settlement.Culture?.StringId);

        if (stance == ToleranceStance.Strict && isMuslimSettlement)
            __result.AddFactor(0.20f, new TextObject("{=!}Sharia governance"));

        if (stance == ToleranceStance.Strict && isNonMuslimSettlement)
            __result.AddFactor(-0.10f, new TextObject("{=!}Jizya resistance"));

        if (stance == ToleranceStance.Tolerant && !isMuslimSettlement)
            __result.AddFactor(-0.10f, new TextObject("{=!}Din-i-Ilahi exemptions"));
    }

    private static bool IsNonMuslimCulture(string id)
        => id is "vlandia" or "battania" or "khuzait";
}
```

### Kingdom policy option

Add to the King's council menu (when the player is the ruler):

```
[Set religious policy → Strict / Moderate / Tolerant]
```

---

## 7. Festivals and Cultural Events

### Historical basis

The calendar of 1719 India was rich: Eid-ul-Fitr (ending Ramadan), Diwali (Hindu festival of lights, celebrated even at the Mughal court), Holi (spring colour festival), Gurpurab (Sikh holy days), and local harvest festivals. These were not private — they were political events. Attendance, gifts, and hospitality during festivals built the patronage networks that determined who had power.

### Festival schedule

| Festival | Season | Culture(s) | Duration |
|----------|--------|-----------|----------|
| Vasant Panchami | Spring | Rajput, Maratha | 3 days |
| Holi | Spring | Rajput, Maratha | 1 day |
| Eid-ul-Fitr | Spring (varies) | Mughal, Afghan, Mysore | 3 days |
| Guru Nanak Gurpurab | Summer | Sikh | 3 days |
| Eid-ul-Adha | Summer | Mughal, Afghan, Mysore | 3 days |
| Diwali | Autumn | Rajput, Maratha (all celebrate) | 5 days |
| Dussehra | Autumn | Rajput, Maratha | 1 day |
| Lohri | Winter | Sikh, Rajput (Punjab) | 1 day |

```csharp
public class FestivalBehavior : CampaignBehaviorBase
{
    private struct Festival
    {
        public string Name;
        public int    Season;       // 0-3
        public int    StartDayOfSeason; // day within the season (0-83 per season)
        public int    Duration;
        public string[] CultureIds; // which cultures celebrate primarily
    }

    private static readonly Festival[] Festivals = new[]
    {
        new Festival { Name="Holi",           Season=0, StartDayOfSeason=15, Duration=1,
                        CultureIds=new[]{"vlandia","battania"} },
        new Festival { Name="Eid-ul-Fitr",    Season=0, StartDayOfSeason=45, Duration=3,
                        CultureIds=new[]{"empire","empire_w","empire_s","sturgia","aserai"} },
        new Festival { Name="Guru Nanak Gurpurab", Season=1, StartDayOfSeason=20, Duration=3,
                        CultureIds=new[]{"khuzait"} },
        new Festival { Name="Eid-ul-Adha",    Season=1, StartDayOfSeason=60, Duration=3,
                        CultureIds=new[]{"empire","empire_w","empire_s","sturgia","aserai"} },
        new Festival { Name="Dussehra",       Season=2, StartDayOfSeason=10, Duration=1,
                        CultureIds=new[]{"vlandia","battania"} },
        new Festival { Name="Diwali",         Season=2, StartDayOfSeason=25, Duration=5,
                        CultureIds=new[]{"vlandia","battania","empire"} }, // Mughals also celebrated Diwali
        new Festival { Name="Lohri",          Season=3, StartDayOfSeason=5,  Duration=1,
                        CultureIds=new[]{"khuzait","vlandia"} },
    };

    private HashSet<string> _runFestivals = new HashSet<string>(); // key = "FestivalName_Year"

    public static FestivalBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    private void OnDailyTick()
    {
        int season    = (int)CampaignTime.Now.GetSeasonOfYear();
        int dayOfYear = (int)(CampaignTime.Now.ToDays % CampaignTime.DayOfYear);
        int year      = (int)(CampaignTime.Now.ToDays / CampaignTime.DayOfYear);

        foreach (var festival in Festivals)
        {
            string key = $"{festival.Name}_{year}";
            if (_runFestivals.Contains(key)) continue;

            int festivalDayOfYear = festival.Season * (CampaignTime.DayOfYear / 4)
                                  + festival.StartDayOfSeason;

            if (dayOfYear >= festivalDayOfYear && dayOfYear < festivalDayOfYear + festival.Duration)
            {
                _runFestivals.Add(key);
                TriggerFestival(festival);
            }
        }
    }

    private void TriggerFestival(Festival festival)
    {
        InformationManager.DisplayMessage(new InformationMessage(
            $"[Festival] {festival.Name} has begun. Settlements across the region celebrate.",
            Color.FromUint(0xFFD4AF37)));

        // Apply settlement bonuses
        foreach (var settlement in Settlement.All.Where(s => s.IsTown || s.IsVillage))
        {
            if (!festival.CultureIds.Contains(settlement.Culture?.StringId)) continue;
            // Crime reduction and trade boost last for festival.Duration days
            FestivalEffectBehavior.Instance?.ApplyBonus(settlement.StringId,
                festival.Duration, crimeReduction: -15f, tradeBoost: 0.20f);
        }

        // If player is in a festival-celebrating settlement, offer attendance
        if (Settlement.CurrentSettlement != null
            && festival.CultureIds.Contains(Settlement.CurrentSettlement.Culture?.StringId))
        {
            OfferFestivalAttendance(festival);
        }
    }

    private void OfferFestivalAttendance(Festival festival)
    {
        InformationManager.ShowInquiry(new InquiryData(
            $"{festival.Name}",
            $"The settlement is celebrating {festival.Name}. You can join the festivities, " +
            $"make an offering, or pass on.",
            true, true,
            "Join the celebration (spend 1 day, gain relations with local notables)",
            "Pass",
            () => AttendFestival(festival),
            () => { }
        ));
    }

    private void AttendFestival(Festival festival)
    {
        // Boost relations with all notables in the current settlement
        foreach (var notable in Settlement.CurrentSettlement.Notables)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, notable, +8);

        // Boost relations with all lords of the same culture
        foreach (var clan in Clan.All.Where(c => festival.CultureIds.Contains(c.Culture?.StringId)))
        {
            if (clan.Leader != null)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                    Hero.MainHero, clan.Leader, +3);
        }

        Hero.MainHero.Clan.Influence += 5;
        CampaignTime.Now.ToDays += 1;  // NOTE: use proper campaign time skip method

        InformationManager.DisplayMessage(new InformationMessage(
            $"You attended {festival.Name}. Your presence has been noted with warmth.",
            Color.FromUint(0xFFD4AF37)));
    }

    public void CheckForSeasonalFestival(int newSeason)
    {
        // Called by SeasonalBehavior on season change
    }

    public override void SyncData(IDataStore dataStore)
    {
        var list = _runFestivals.ToList();
        dataStore.SyncData("hind_festivals_run", ref list);
        if (!dataStore.IsSaving)
            _runFestivals = new HashSet<string>(list);
    }
}
```

---

## 8. Akhbarat — Intelligence Network

### Historical basis

The Mughals maintained a service of **waqai-nawis** (news writers) stationed in every major city, reporting to the court. The Maratha intelligence network was even better — their *harkara* messengers were legendary for speed. **Information was power** in 18th-century India. A lord who knew where armies were, which nobles were disloyal, and which towns were weakened by famine could campaign with devastating precision.

In vanilla Bannerlord, you instantly know everything about every army everywhere. That's ahistorical and removes strategic depth.

### Information delay system

Each settlement has an information staleness counter. The player only sees accurate data when they have a news agent stationed there or when they were recently present.

```csharp
public class AkhbaratBehavior : CampaignBehaviorBase
{
    private Dictionary<string, int>    _agentPresent  = new Dictionary<string, int>();
    // value = days remaining on agent contract
    private Dictionary<string, float>  _reportStaleness = new Dictionary<string, float>();
    // 0 = current; 14+ = 2-week-old data

    private const int AGENT_COST_PER_MONTH = 50;  // gold per month per station

    public static AkhbaratBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    private void OnDailyTick()
    {
        foreach (var s in Settlement.All.Where(s => s.IsTown || s.IsCastle))
        {
            string id = s.StringId;

            // Staleness grows when no agent is present AND player is far away
            bool playerPresent = Settlement.CurrentSettlement == s ||
                MobileParty.MainParty.Position2D.DistanceSquared(s.Position2D) < 5 * 5;

            bool agentPresent = _agentPresent.ContainsKey(id) && _agentPresent[id] > 0;

            if (playerPresent)
                _reportStaleness[id] = 0f;
            else if (agentPresent)
                _reportStaleness[id] = Math.Max(0f,
                    _reportStaleness.GetValueOrDefault(id, 0f) - 0.5f);  // agents keep info fresh
            else
                _reportStaleness[id] = Math.Min(28f,
                    _reportStaleness.GetValueOrDefault(id, 0f) + 1f);  // ages 1 day per day

            // Decrement agent contracts
            if (_agentPresent.ContainsKey(id))
            {
                _agentPresent[id]--;
                if (_agentPresent[id] <= 0)
                {
                    _agentPresent.Remove(id);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Your news agent in {s.Name} has left service.",
                        Color.FromUint(0xFFFF6600)));
                }
            }

            // Daily agent fee
            if (agentPresent && (int)CampaignTime.Now.ToDays % 30 == 0)
            {
                int cost = AGENT_COST_PER_MONTH;
                if (Hero.MainHero.Gold >= cost)
                    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost);
                else
                    _agentPresent.Remove(id);  // can't pay, agent leaves
            }
        }
    }

    public float GetStaleness(Settlement s)
        => _reportStaleness.GetValueOrDefault(s.StringId, 0f);

    public string GetStalenessLabel(Settlement s)
    {
        float days = GetStaleness(s);
        return days switch
        {
            0    => "Current",
            < 7  => $"{days:F0} days old",
            < 14 => "Roughly a week old",
            < 21 => "Two weeks old",
            _    => "Stale — you have no reports from this region"
        };
    }

    public bool HasAgent(Settlement s)
        => _agentPresent.ContainsKey(s.StringId) && _agentPresent[s.StringId] > 0;

    public void HireAgent(Settlement s, int months)
    {
        _agentPresent[s.StringId] = months * 30;
        _reportStaleness[s.StringId] = 0f;
        InformationManager.DisplayMessage(new InformationMessage(
            $"News agent hired in {s.Name} for {months} month(s). Cost: {months * AGENT_COST_PER_MONTH} gold/month.",
            Color.FromUint(0xFFD4AF37)));
    }

    public override void SyncData(IDataStore dataStore)
    {
        var agentIds  = _agentPresent.Keys.ToList();
        var agentDays = _agentPresent.Values.ToList();
        var staleIds  = _reportStaleness.Keys.ToList();
        var staleVals = _reportStaleness.Values.ToList();
        dataStore.SyncData("hind_akhbarat_agent_ids",  ref agentIds);
        dataStore.SyncData("hind_akhbarat_agent_days", ref agentDays);
        dataStore.SyncData("hind_akhbarat_stale_ids",  ref staleIds);
        dataStore.SyncData("hind_akhbarat_stale_vals", ref staleVals);
        if (!dataStore.IsSaving)
        {
            _agentPresent.Clear();
            _reportStaleness.Clear();
            for (int i = 0; i < agentIds.Count; i++)
                _agentPresent[agentIds[i]] = i < agentDays.Count ? agentDays[i] : 0;
            for (int i = 0; i < staleIds.Count; i++)
                _reportStaleness[staleIds[i]] = i < staleVals.Count ? staleVals[i] : 28f;
        }
    }
}
```

### How staleness manifests

Staleness does NOT hide map icons — that would be frustrating to implement and bad UX. Instead, the staleness label appears in the settlement tooltip and the encyclopedia entry, indicating how reliable the data is. When you click on a settlement with 21+ days staleness:

> *"Your last reliable report from Agra is 21 days old. Troop counts and lord positions may have changed."*

The Spymaster council position (Chapter 13) halves the staleness growth rate across the entire kingdom.

### Hiring agents

```csharp
starter.AddGameMenuOption("town", "hire_news_agent",
    "{=!}Hire a news agent here (50 gold/month)",
    args => !AkhbaratBehavior.Instance.HasAgent(Settlement.CurrentSettlement),
    args =>
    {
        InformationManager.ShowInquiry(new InquiryData(
            "Hire News Agent",
            "For how many months do you wish to retain this agent?",
            true, true,
            "3 months (150 gold)",
            "1 month (50 gold)",
            () => AkhbaratBehavior.Instance.HireAgent(Settlement.CurrentSettlement, 3),
            () => AkhbaratBehavior.Instance.HireAgent(Settlement.CurrentSettlement, 1)
        ));
    }
);
```

---

## 9. Cultural Patronage

### Historical basis

The Mughal courts at Agra, Delhi, and later Hyderabad were among the most significant centres of artistic patronage in the world. Mughal miniature painting, Urdu poetry, garden architecture, and the building of mosques and temples were not just cultural expression — they were political acts. A lord who built a mosque or endowed a madrassa gained the loyalty of the ulema; one who commissioned poetry gained literary immortality and court prestige; one who built a caravanserai became beloved along the trade route.

### Patronage projects

| Project | Gold cost | Build time | Permanent effect |
|---------|-----------|-----------|-----------------|
| Mosque / Temple | 2,000 | 60 days | +10 notable relations in settlement; religion tolerance improved |
| Madrassa (school) | 3,500 | 90 days | Trader notable income +15%; Akhbarat staleness growth −30% in this settlement |
| Mughal Garden | 2,500 | 45 days | Settlement prosperity +50; notable morale +10 |
| Caravanserai | 4,000 | 75 days | Trade route health recovery +3/day; see separate chapter |
| Poetry commission | 500 | 14 days | Player clan influence +20; king relation +5 |
| Court musician | 300 | 7 days | Hiring bonus: notables 10% more likely to volunteer troops |

```csharp
public class PatronageBehavior : CampaignBehaviorBase
{
    private struct PatronageProject
    {
        public string SettlementId;
        public string Type;
        public int    CompletionDay;
    }

    private List<PatronageProject> _ongoing  = new List<PatronageProject>();
    private HashSet<string>        _completed = new HashSet<string>(); // "settlementId:type"

    public static PatronageBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public void CommissionProject(Settlement s, string type, int goldCost, int buildDays)
    {
        if (Hero.MainHero.Gold < goldCost)
        {
            InformationManager.DisplayMessage(new InformationMessage("Not enough gold."));
            return;
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, goldCost);
        _ongoing.Add(new PatronageProject
        {
            SettlementId  = s.StringId,
            Type          = type,
            CompletionDay = (int)CampaignTime.Now.ToDays + buildDays
        });

        InformationManager.DisplayMessage(new InformationMessage(
            $"{type} commissioned in {s.Name}. Will be complete in {buildDays} days.",
            Color.FromUint(0xFFD4AF37)));
    }

    private void OnDailyTick()
    {
        int today = (int)CampaignTime.Now.ToDays;
        var completed = _ongoing.Where(p => p.CompletionDay <= today).ToList();

        foreach (var proj in completed)
        {
            _ongoing.Remove(proj);
            string key = $"{proj.SettlementId}:{proj.Type}";
            if (_completed.Add(key))
                ApplyCompletionEffects(proj);
        }
    }

    private void ApplyCompletionEffects(PatronageProject proj)
    {
        Settlement s = Settlement.Find(proj.SettlementId);
        string msg   = proj.Type;

        switch (proj.Type)
        {
            case "Mosque/Temple":
                foreach (var notable in s?.Notables ?? new MBList<Hero>())
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        Hero.MainHero, notable, +10);
                break;

            case "Mughal Garden":
                if (s?.Town != null)
                    s.Town.Prosperity += 50f;
                foreach (var notable in s?.Notables ?? new MBList<Hero>())
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        Hero.MainHero, notable, +5);
                break;

            case "Poetry Commission":
                Hero.MainHero.Clan.Influence += 20f;
                Hero king = Hero.MainHero?.Clan?.Kingdom?.Leader;
                if (king != null)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        Hero.MainHero, king, +5);
                break;
        }

        InformationManager.DisplayMessage(new InformationMessage(
            $"Your patronage of {msg} in {s?.Name} is complete. The people remember your generosity.",
            Color.FromUint(0xFFD4AF37)));
    }

    public bool HasProject(Settlement s, string type)
        => _completed.Contains($"{s.StringId}:{type}");

    public override void SyncData(IDataStore dataStore)
    {
        var sids  = _ongoing.Select(p => p.SettlementId).ToList();
        var types = _ongoing.Select(p => p.Type).ToList();
        var days  = _ongoing.Select(p => p.CompletionDay).ToList();
        var done  = _completed.ToList();

        dataStore.SyncData("hind_patron_sids",  ref sids);
        dataStore.SyncData("hind_patron_types", ref types);
        dataStore.SyncData("hind_patron_days",  ref days);
        dataStore.SyncData("hind_patron_done",  ref done);

        if (!dataStore.IsSaving)
        {
            _ongoing.Clear();
            for (int i = 0; i < sids.Count; i++)
                _ongoing.Add(new PatronageProject
                {
                    SettlementId  = sids[i],
                    Type          = i < types.Count ? types[i] : "",
                    CompletionDay = i < days.Count  ? days[i]  : 0
                });
            _completed = new HashSet<string>(done);
        }
    }
}
```

---

## 10. Caravanserai — Information Brokers

### Historical basis

The **sarai** (Persian: پادشاه‌سرای, "royal rest-house") was the backbone of Mughal logistics. Sarais were built every 12–15 miles along major roads, funded by nobles and the crown. They offered free lodging to merchants, pilgrims, and state messengers. Each sarai was a node of information: travelers exchanged news, merchants disclosed prices, and intelligence agents (disguised as pilgrims) collected reports.

### The sarai as a mechanic

The sarai is a buildable structure in villages along trade routes. It extends the Akhbarat system and the trade route health system simultaneously.

**Build requirements:** The village must be on or adjacent to an active trade route. Cost: 1,500 gold, 45 days.

**Effects:**
- Trade route health recovery +5/day for connected routes
- Akhbarat staleness growth: −50% in this village and adjacent settlements within 20 map units
- Once per 30 days, the player can visit the sarai to hear "broker rumors" — structured gossip about what is happening in the kingdom

```csharp
public class CaravanseraiBehavior : CampaignBehaviorBase
{
    private HashSet<string> _sarais = new HashSet<string>(); // village StringIds with a sarai

    public static CaravanseraiBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
    }

    public bool HasSarai(Settlement s) => _sarais.Contains(s.StringId);

    public void BuildSarai(Settlement village)
    {
        if (!village.IsVillage) return;
        _sarais.Add(village.StringId);

        InformationManager.DisplayMessage(new InformationMessage(
            $"Caravanserai established at {village.Name}. Merchants and travelers will now use this route.",
            Color.FromUint(0xFFD4AF37)));
    }

    public void PresentBrokerRumors(Settlement sarai)
    {
        // Generate 3-5 contextual rumors based on current world state
        var rumors = GenerateRumors();
        string text = string.Join("\n\n", rumors.Select((r, i) => $"{i + 1}. {r}"));

        InformationManager.ShowInquiry(new InquiryData(
            "What the Travelers Say",
            text,
            true, false,
            "Thank you",
            "",
            () => { },
            () => { }
        ));
    }

    private List<string> GenerateRumors()
    {
        var rumors = new List<string>();

        // Rumor type 1: army movement
        MobileParty largeArmy = MobileParty.All
            .Where(p => p.IsLordParty && p.Party?.TotalStrength > 100)
            .OrderByDescending(p => p.Party.TotalStrength)
            .FirstOrDefault();
        if (largeArmy != null)
            rumors.Add($"A great army led by {largeArmy.LeaderHero?.Name} was seen " +
                       $"moving {GetDirection(largeArmy)} some days ago.");

        // Rumor type 2: famine or disease
        Settlement sick = Settlement.All
            .FirstOrDefault(s => EpidemicBehavior.Instance?.GetDiseaseLevel(s) > 50f);
        if (sick != null)
            rumors.Add($"They say there is sickness in {sick.Name}. Merchants are taking longer routes.");

        // Rumor type 3: a lord's mood
        Hero disgruntled = Kingdom.All
            .SelectMany(k => k.Clans)
            .Where(c => c.Leader != null && c.Leader != Hero.MainHero)
            .Select(c => c.Leader)
            .FirstOrDefault(h =>
                h?.Clan?.Kingdom?.Leader != null &&
                CharacterRelationManager.GetHeroRelation(h, h.Clan.Kingdom.Leader) < -20);
        if (disgruntled != null)
            rumors.Add($"It is whispered that {disgruntled.Name} is unhappy with the king. " +
                       $"He drinks more than he campaigns.");

        // Rumor type 4: trade
        Settlement prosperous = Settlement.All
            .Where(s => s.IsTown)
            .OrderByDescending(s => s.Town?.Prosperity ?? 0)
            .FirstOrDefault();
        if (prosperous != null)
            rumors.Add($"The markets at {prosperous.Name} are thriving this season. " +
                       $"Silk and spice move freely.");

        if (rumors.Count < 3)
            rumors.Add("The roads are quiet. No remarkable news from distant regions.");

        return rumors;
    }

    private string GetDirection(MobileParty party)
    {
        Vec2 pos = party.Position2D;
        float lat = pos.y;
        float lon = pos.x;
        if (lat > 400) return "north";
        if (lat < 200) return "south";
        if (lon > 400) return "east";
        return "west";
    }

    public override void SyncData(IDataStore dataStore)
    {
        var list = _sarais.ToList();
        dataStore.SyncData("hind_sarais", ref list);
        if (!dataStore.IsSaving)
            _sarais = new HashSet<string>(list);
    }
}
```

### Village menu integration

```csharp
starter.AddGameMenuOption("village", "build_sarai",
    "{=!}Establish a caravanserai (1,500 gold, 45 days)",
    args => !CaravanseraiBehavior.Instance.HasSarai(Settlement.CurrentSettlement)
         && TradeRouteBehavior.Instance.HasNearbyRoute(Settlement.CurrentSettlement),
    args => PatronageBehavior.Instance.CommissionProject(
        Settlement.CurrentSettlement, "Caravanserai", 1500, 45)
);

starter.AddGameMenuOption("village", "visit_broker",
    "{=!}Seek out the information broker",
    args => CaravanseraiBehavior.Instance.HasSarai(Settlement.CurrentSettlement),
    args => CaravanseraiBehavior.Instance.PresentBrokerRumors(Settlement.CurrentSettlement)
);
```

---

## Behavior Registration Summary

Add all new behaviors to your `SubModuleBase.OnSubModuleLoad()` registration list:

```csharp
protected override void OnBeforeInitialModuleScreenSetUp()
{
    // (existing behaviors)
    CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
}

private void OnSessionLaunched(CampaignGameStarter starter)
{
    starter.AddBehavior(new SeasonalBehavior());
    starter.AddBehavior(new NazranaBehavior());
    starter.AddBehavior(new TradeRouteBehavior());
    starter.AddBehavior(new FoodSecurityBehavior());
    starter.AddBehavior(new EpidemicBehavior());
    starter.AddBehavior(new ReligiousToleranceBehavior());
    starter.AddBehavior(new FestivalBehavior());
    starter.AddBehavior(new AkhbaratBehavior());
    starter.AddBehavior(new PatronageBehavior());
    starter.AddBehavior(new CaravanseraiBehavior());
}
```

---

## Cross-System Interactions

The systems above are designed to interlock. Here is the dependency graph:

```
Monsoon Season
  ├─► Village income ×1.4 (post-monsoon)
  ├─► Disease spread rate ×2 (monsoon)
  ├─► Movement speed −30% on plains (monsoon)
  └─► Festival calendar timing

Famine
  ├─► Triggered by: monsoon failure, army camping, bandit raids
  └─► Worsened by: disease (hearth loss from both)

Trade Routes
  ├─► Health degraded by: bandits, disease in endpoint settlements
  ├─► Tariff income flows to lords (fuels nazrana payments)
  └─► Caravanserai improves route health

Akhbarat / Agents
  ├─► Funded by: trade tariff income
  ├─► Enhanced by: Spymaster council position (Chapter 13)
  └─► Sarai reduces staleness growth

Religious Tolerance
  ├─► Affects: town tax income, notable relations
  └─► Affects: how likely non-Muslim clans join your kingdom (civil war side selection)

Festivals
  ├─► Seasonal trigger (SeasonalBehavior)
  └─► +relations with notables → civil war legitimacy (Chapter 16)

Nazrana
  └─► Funded by: trade tariffs + fief income
```

A player who lets bandit threat grow loses trade route health, which reduces tariff income, which makes nazrana payments harder, which damages king relations, which — if they are already failing military obligations — can trigger demotion.

A player who builds irrigation, commissions a mosque, and posts news agents is more resilient to famine, earns more notable relations (feeding civil war legitimacy), and has better information for campaign planning.

These are not separate minigames. They are the same system viewed from different angles.

---

**[← Chapter 16](16-Civil-War-and-Imprisonment.md)** | **[Home](Home.md)**
