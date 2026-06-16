# Chapter 19 — Imperial Depth: Political Systems

> Macro-scale systems borrowed from CK3, EU4, and Total War: Three Kingdoms, adapted to the specific historical reality of 1719–1720 Mughal India. These model how empires fracture, not just how lords fight.

**[← Chapter 18](18-News-Reporter-and-Biographer.md)** | **[Home](Home.md)** | **[Chapter 20 →](20-Character-Depth-and-Intrigue.md)**

---

## Contents

1. [Mughal Succession Crisis](#1-mughal-succession-crisis)
2. [Imperial Authority — Padshahi Hukm](#2-imperial-authority--padshahi-hukm)
3. [The Three Estates](#3-the-three-estates)
4. [Internal Kingdom Factions](#4-internal-kingdom-factions)
5. [Legitimacy](#5-legitimacy)
6. [Revolt Cascade](#6-revolt-cascade)
7. [How these systems interlock](#7-how-these-systems-interlock)

---

## 1. Mughal Succession Crisis

### Historical basis

This is the defining mechanic of the 1719–1720 period. The Mughal empire had no fixed succession rule. The Timurid tradition was effectively: all princes fight, the winner rules. Aurangzeb himself killed his brothers and imprisoned his father to take the throne. The years 1719–1720 saw *four emperors* — Rafi ud-Darajat, Rafi ud-Daulah, Nikusiyar, and Muhammad Shah — each toppled or killed within months.

No succession crisis, no Hindostan mod.

### Three succession modes (per kingdom culture)

| Culture | Mode | Mechanic |
|---------|------|----------|
| Mughal (`empire`) | War of Princes | All male heirs declare claims; civil war; winner takes all |
| Afghan (`sturgia`) | Agnatic seniority | Eldest male relative inherits; lower conflict but more distant claimants |
| Rajput (`vlandia`) | Primogeniture | Eldest son inherits; succession is stable but produces weak heirs |
| Maratha (`battania`) | Council election | Council of senior sardars elects the next Peshwa |
| Sikh (`khuzait`) | Misl collective | No single succession; the Misl assembly chooses a leader |
| Bengali, Hyderabadi, Mysore (`empire_w/s`, `aserai`) | Gubernatorial | Subahdar appointed by the Mughal emperor (succession to the emperor affects them) |

### On emperor death — War of Princes trigger

```csharp
public class SuccessionBehavior : CampaignBehaviorBase
{
    // Track all legitimate claimants per kingdom
    private Dictionary<string, List<string>> _claimants = new Dictionary<string, List<string>>();
    // kingdomId → list of claimant hero StringIds

    public static SuccessionBehavior Instance { get; private set; }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.OnHeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
    }

    private void OnHeroKilled(Hero victim, Hero killer,
        KillCharacterAction.KillCharacterActionDetail detail, bool showNotif)
    {
        // Is the ruler of a kingdom dead?
        Kingdom kingdom = Kingdom.All.FirstOrDefault(k => k.Leader == victim);
        if (kingdom == null) return;

        string cultureId = kingdom.Culture?.StringId ?? "";
        switch (cultureId)
        {
            case "empire":
                TriggerWarOfPrinces(kingdom, victim);
                break;
            case "battania":
                TriggerCouncilElection(kingdom);
                break;
            default:
                TriggerStandardSuccession(kingdom, victim);
                break;
        }
    }

    // ── War of Princes (Mughal) ─────────────────────────────────────────────
    private void TriggerWarOfPrinces(Kingdom kingdom, Hero deadEmperor)
    {
        // Find all male adult relatives of the dead emperor
        var princes = FindMaleHeirs(deadEmperor, kingdom);

        if (princes.Count == 0)
        {
            // No heirs → use oldest senior mansabdar (Amir-ul-Umara)
            TriggerSuccessionByAcclamation(kingdom);
            return;
        }

        // Eldest/strongest prince becomes interim heir with low legitimacy
        Hero designatedHeir = princes.OrderByDescending(p => p.Age).First();
        ChangeRulingClanAction.Apply(kingdom, designatedHeir.Clan);
        LegitimacyBehavior.Instance?.SetLegitimacy(designatedHeir, 30f);
        // Low legitimacy: this ruler will be challenged

        // Each OTHER prince becomes a pretender — spawns a rebel faction
        foreach (Hero prince in princes.Where(p => p != designatedHeir))
        {
            SpawnPretender(prince, kingdom);
        }

        string msg = $"Emperor {deadEmperor.Name} is dead. The War of Princes has begun. " +
                     $"{princes.Count} claimants fight for the throne of {kingdom.Name}.";
        InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFCC2200)));

        EventCaptureBehavior.Instance?.RecordPlayerMilestone(
            GameEventType.Kingdom_War_Declared,
            msg,
            deadEmperor.HomeSettlement?.Name.ToString() ?? "the capital");
    }

    private void SpawnPretender(Hero prince, Kingdom legitimateKingdom)
    {
        // Prince's clan leaves + forms rebel claim-kingdom
        if (prince.Clan?.Kingdom != legitimateKingdom)
            return; // already independent

        ChangeKingdomAction.ApplyByLeaveKingdom(prince.Clan, showNotification: false);

        // Create a pretender kingdom using the same culture
        Kingdom pretenderKingdom = Kingdom.CreateKingdom(
            new TextObject($"{{=!}}Claim of {prince.Name}"),
            new TextObject($"{{=!}}Pretenders"),
            legitimateKingdom.Culture,
            prince,
            prince.HomeSettlement ?? prince.Clan.HomeSettlement,
            prince.Clan.Banner);

        ChangeKingdomAction.ApplyByJoinToKingdom(prince.Clan, pretenderKingdom);
        DeclareWarAction.Apply(pretenderKingdom, legitimateKingdom,
            DeclareWarAction.DeclareWarDetail.Default);

        LegitimacyBehavior.Instance?.SetLegitimacy(prince, 40f);
        // Pretenders start slightly higher than the disputed heir

        // Clans with high relation to the prince may join them
        foreach (Clan clan in legitimateKingdom.Clans.ToList())
        {
            if (clan == prince.Clan || clan.Leader == null) continue;
            int rel = CharacterRelationManager.GetHeroRelation(prince, clan.Leader);
            if (rel > 30 && MBRandom.RandomFloat < 0.35f)
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, pretenderKingdom, false);
        }
    }

    // ── Council election (Maratha) ──────────────────────────────────────────
    private void TriggerCouncilElection(Kingdom kingdom)
    {
        // Each clan leader votes; winner has legitimacy proportional to vote share
        var candidates = kingdom.Clans
            .Where(c => c.Leader != null && c.Leader.IsAlive)
            .Select(c => c.Leader)
            .ToList();

        Dictionary<Hero, int> votes = new Dictionary<Hero, int>();
        foreach (var candidate in candidates)
        {
            int totalVotes = kingdom.Clans
                .Where(c => c.Leader != null)
                .Sum(c => CharacterRelationManager.GetHeroRelation(c.Leader, candidate) > 20 ? 1 : 0);
            votes[candidate] = totalVotes;
        }

        Hero winner = votes.OrderByDescending(kv => kv.Value).First().Key;
        int winVotes = votes[winner];
        float legitimacy = Math.Min(100f, 40f + (winVotes / (float)candidates.Count) * 60f);

        ChangeRulingClanAction.Apply(kingdom, winner.Clan);
        LegitimacyBehavior.Instance?.SetLegitimacy(winner, legitimacy);

        InformationManager.DisplayMessage(new InformationMessage(
            $"The Maratha council has elected {winner.Name} as Peshwa. " +
            $"Vote: {winVotes}/{candidates.Count} sardars.",
            Color.FromUint(0xFFD4AF37)));
    }

    // ── Standard succession ────────────────────────────────────────────────
    private void TriggerStandardSuccession(Kingdom kingdom, Hero deadRuler)
    {
        Hero heir = FindPrimaryHeir(deadRuler);
        if (heir == null)
        {
            TriggerSuccessionByAcclamation(kingdom);
            return;
        }

        ChangeRulingClanAction.Apply(kingdom, heir.Clan);
        float legitimacy = heir.Clan == deadRuler.Clan ? 70f : 45f;
        LegitimacyBehavior.Instance?.SetLegitimacy(heir, legitimacy);
    }

    private void TriggerSuccessionByAcclamation(Kingdom kingdom)
    {
        // The senior Amir-ul-Umara (or highest Mansabdar rank) takes power
        Hero strongestLord = kingdom.Clans
            .SelectMany(c => c.Heroes)
            .Where(h => h.IsAlive && h.IsLord)
            .OrderByDescending(h => h.Clan?.Renown ?? 0)
            .FirstOrDefault();

        if (strongestLord != null)
        {
            ChangeRulingClanAction.Apply(kingdom, strongestLord.Clan);
            LegitimacyBehavior.Instance?.SetLegitimacy(strongestLord, 20f);
            // Acclamation by the strongest lord is weakly legitimate

            InformationManager.DisplayMessage(new InformationMessage(
                $"With no clear heir, {strongestLord.Name} has seized the throne of {kingdom.Name} " +
                $"by acclamation. Their claim is disputed.",
                Color.FromUint(0xFFCC4400)));
        }
    }

    private List<Hero> FindMaleHeirs(Hero ruler, Kingdom kingdom)
    {
        // In Bannerlord, family relations are limited; use clan members as proxies for princes
        return ruler.Clan?.Heroes
            .Where(h => h != ruler && h.IsAlive && h.IsLord && h.IsMale && h.Age >= 18)
            .ToList() ?? new List<Hero>();
    }

    private Hero FindPrimaryHeir(Hero ruler)
    {
        // Eldest son, or eldest male clan member
        return ruler.Clan?.Heroes
            .Where(h => h != ruler && h.IsAlive && h.IsMale && h.Age >= 18)
            .OrderByDescending(h => h.Age)
            .FirstOrDefault();
    }

    public override void SyncData(IDataStore dataStore)
    {
        var kingdomIds  = _claimants.Keys.ToList();
        var claimLists  = _claimants.Values.Select(l => string.Join(",", l)).ToList();
        dataStore.SyncData("hind_succ_kingdoms", ref kingdomIds);
        dataStore.SyncData("hind_succ_claims",   ref claimLists);
        if (!dataStore.IsSaving)
        {
            _claimants.Clear();
            for (int i = 0; i < kingdomIds.Count; i++)
                _claimants[kingdomIds[i]] = i < claimLists.Count
                    ? claimLists[i].Split(',').ToList() : new List<string>();
        }
    }
}
```

### Player involvement

If the player is a member of the kingdom where the emperor dies:
- They receive a notification with the legitimacy of each pretender
- If the player's clan has higher renown than all pretenders, they become a candidate themselves
- Choosing a side: the player can swear loyalty to any pretender — that pretender's clan becomes the player's liege

---

## 2. Imperial Authority — Padshahi Hukm

### Historical basis

EU4's Imperial Authority modeled how the Holy Roman Emperor's grip over vassals weakened over time. The Mughal equivalent is **Padshahi Hukm** (Imperial Decree — literally "the emperor's command"). In 1707 under Aurangzeb it was near absolute. By 1719 it was a fiction. Subadars kept their tax revenue. Governors passed titles to sons without imperial approval. The empire existed on paper; in practice, regional power had taken over.

### The authority meter

```csharp
public class ImperialAuthorityBehavior : CampaignBehaviorBase
{
    // Per kingdom: 0–100 authority score
    private Dictionary<string, float> _authority = new Dictionary<string, float>();

    public static ImperialAuthorityBehavior Instance { get; private set; }

    // ── Gain sources ──────────────────────────────────────────────────────────
    // +8   : Victory in major battle (> 300 enemy casualties)
    // +5   : Successful siege of enemy town
    // +3   : Nazrana fully paid by all lords (weekly check)
    // +4   : Festival held at the capital
    // +6   : Rival pretender defeated and captured
    // +10  : Peace treaty on favourable terms

    // ── Loss sources ──────────────────────────────────────────────────────────
    // -10  : Major battle defeat
    // -5   : Settlement captured by enemy
    // -3   : Lord publicly refuses call to arms
    // -7   : Civil war triggered within the kingdom
    // -2/week : Active war with 2+ enemies simultaneously
    // -15  : Emperor captured in battle
    // -8   : Lord executes a fellow lord without emperor's sanction

    public float GetAuthority(Kingdom kingdom)
        => _authority.GetValueOrDefault(kingdom?.StringId, 75f);

    public void ModifyAuthority(Kingdom kingdom, float delta, string reason)
    {
        if (kingdom == null) return;
        float current = GetAuthority(kingdom);
        float newVal  = Math.Clamp(current + delta, 0f, 100f);
        _authority[kingdom.StringId] = newVal;

        // Threshold crossing notifications
        float[] thresholds = { 75f, 50f, 25f, 10f };
        foreach (float t in thresholds)
        {
            if (current > t && newVal <= t && kingdom == Hero.MainHero?.Clan?.Kingdom)
            {
                NotifyAuthorityThreshold(kingdom, t, reason);
            }
        }
    }

    private void NotifyAuthorityThreshold(Kingdom kingdom, float threshold, string cause)
    {
        string msg = threshold switch
        {
            75f => $"Imperial authority in {kingdom.Name} is weakening. Lords are testing the emperor's resolve.",
            50f => $"The emperor's writ no longer runs freely. Governors are withholding tax revenues. Cause: {cause}",
            25f => $"Imperial authority has collapsed in the provinces. Lords act as independent rulers. Central command is failing.",
            10f => $"The empire exists in name only. Fragmentation is imminent.",
            _   => ""
        };
        if (!string.IsNullOrEmpty(msg))
            InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFCC2200)));
    }

    // ── Effects by threshold ──────────────────────────────────────────────────
    public float GetTaxCollectionRate(Kingdom kingdom)
    {
        float auth = GetAuthority(kingdom);
        return auth switch
        {
            >= 75 => 1.00f,   // Full tax
            >= 50 => 0.80f,   // Governors keep 20%
            >= 25 => 0.55f,   // Governors keep nearly half
            >= 10 => 0.30f,   // Only direct demesne pays
            _     => 0.10f    // Near-total collapse
        };
    }

    public float GetCallToArmsCompliance(Kingdom kingdom)
    {
        float auth = GetAuthority(kingdom);
        return auth switch
        {
            >= 75 => 0.90f,   // 90% of lords respond
            >= 50 => 0.65f,
            >= 25 => 0.35f,
            >= 10 => 0.15f,
            _     => 0.05f
        };
    }

    public bool CanIssueImperialDecree(Kingdom kingdom)
        => GetAuthority(kingdom) >= 40f;
    // Below 40, even basic decrees are ignored

    // ── Weekly decay ─────────────────────────────────────────────────────────
    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnWeeklyTick()
    {
        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
        {
            // Passive decay when at war
            int warCount = Kingdom.All.Count(k => kingdom.IsAtWarWith(k));
            if (warCount >= 2)
                ModifyAuthority(kingdom, -2f, "simultaneous wars");

            // Lords refusing obligations
            int rebelliousLords = kingdom.Clans.Count(c =>
                c.Leader != null &&
                CharacterRelationManager.GetHeroRelation(kingdom.Leader, c.Leader) < -30);
            if (rebelliousLords > 0)
                ModifyAuthority(kingdom, -rebelliousLords * 0.5f, "disloyal lords");

            // Recovery if at peace and high legitimacy
            float legitimacy = LegitimacyBehavior.Instance?.GetLegitimacy(kingdom.Leader) ?? 50f;
            if (warCount == 0 && legitimacy > 60f)
                ModifyAuthority(kingdom, +1f, "peace and stable rule");
        }
    }

    public override void SyncData(IDataStore dataStore)
    {
        var ids  = _authority.Keys.ToList();
        var vals = _authority.Values.ToList();
        dataStore.SyncData("hind_authority_ids",  ref ids);
        dataStore.SyncData("hind_authority_vals", ref vals);
        if (!dataStore.IsSaving)
        {
            _authority.Clear();
            for (int i = 0; i < ids.Count; i++)
                _authority[ids[i]] = i < vals.Count ? vals[i] : 75f;
        }
    }
}
```

### Model override — tax income

```csharp
[HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange")]
public static class ImperialAuthorityTaxPatch
{
    public static void Postfix(Clan clan, ref ExplainedNumber goldChange, bool applyWithdrawal)
    {
        Kingdom kingdom = clan.Kingdom;
        if (kingdom == null) return;

        float authRate = ImperialAuthorityBehavior.Instance?.GetTaxCollectionRate(kingdom) ?? 1f;
        if (Math.Abs(authRate - 1f) > 0.01f)
        {
            float penalty = authRate - 1f; // negative number
            goldChange.AddFactor(penalty,
                new TextObject("{=!}Imperial authority weakened"));
        }
    }
}
```

---

## 3. The Three Estates

### Historical basis

EU4's estates system modeled the three great power blocs of early modern states: the Church, the Nobility, and the Burghers. Mughal India had exact equivalents:

| Estate | Mughal equivalent | Power base |
|--------|------------------|------------|
| Nobility | Mansabdar nobles | Military force, land revenue |
| Clergy | Ulema (Islamic scholars) | Religious legitimacy, popular loyalty |
| Burghers | Mahajans/Seths (merchant guilds) | Tax revenue, credit, trade |

Non-Muslim kingdoms (Maratha, Rajput, Sikh) have different clergy estates: Brahmin advisors (Maratha), Hindu temple networks (Rajput), Akali Nihangs (Sikh warrior-priests).

### Estate mechanics

Each estate has two metrics:
- **Influence** (0–100): how much political power they have accumulated
- **Loyalty** (0–100): whether they support the current ruler

```csharp
public enum Estate { Nobility = 0, Clergy = 1, Merchants = 2 }

public class EstatesBehavior : CampaignBehaviorBase
{
    // Per kingdom, per estate
    private Dictionary<string, float[]> _influence = new Dictionary<string, float[]>();
    private Dictionary<string, float[]> _loyalty   = new Dictionary<string, float[]>();

    public static EstatesBehavior Instance { get; private set; }

    // Privileges the emperor can grant or seize
    public static readonly string[] NobilityPrivileges = {
        "Hereditary Fiefs",     // lords pass fiefs to sons; emperor loses control of assignments
        "Tax Exemption",        // nobles pay no tax; imperial income falls 15%
        "Military Autonomy",    // lords raise their own armies without imperial sanction
    };

    public static readonly string[] ClergyPrivileges = {
        "Waqf Rights",          // religious endowments are tax-free; treasury income -10%
        "Judicial Authority",   // ulema adjudicate disputes; player loses some dialogue options
        "Fatwa Power",          // ulema can issue legitimacy-affecting decrees
    };

    public static readonly string[] MerchantPrivileges = {
        "Trade Monopoly",       // merchants control specific goods; +15% trade income
        "Credit Line",          // merchants offer emergency loans; -15% interest on loans
        "Market Self-Rule",     // merchants set their own tariffs; player loses tariff control
    };

    public float GetInfluence(Kingdom kingdom, Estate estate)
    {
        if (!_influence.TryGetValue(kingdom.StringId, out float[] vals))
            return 33f; // default: all estates equal
        return vals[(int)estate];
    }

    public float GetLoyalty(Kingdom kingdom, Estate estate)
    {
        if (!_loyalty.TryGetValue(kingdom.StringId, out float[] vals))
            return 50f;
        return vals[(int)estate];
    }

    // ── Grant a privilege ──────────────────────────────────────────────────────
    public void GrantPrivilege(Kingdom kingdom, Estate estate, string privilege)
    {
        EnsureInitialized(kingdom);
        _influence[kingdom.StringId][(int)estate] += 15f;  // they gain power
        _loyalty[kingdom.StringId][(int)estate]   += 20f;  // they become loyal
        ImperialAuthorityBehavior.Instance?.ModifyAuthority(kingdom, -8f,
            $"Granted privilege to {estate}");

        // Apply the privilege effect
        ApplyPrivilegeEffect(kingdom, estate, privilege, granted: true);

        InformationManager.DisplayMessage(new InformationMessage(
            $"You granted '{privilege}' to the {estate}. " +
            $"Their loyalty increases; your imperial authority weakens.",
            Color.FromUint(0xFFD4AF37)));
    }

    // ── Seize a privilege ──────────────────────────────────────────────────────
    public void SeizePrivilege(Kingdom kingdom, Estate estate, string privilege)
    {
        EnsureInitialized(kingdom);
        _influence[kingdom.StringId][(int)estate] -= 20f;
        _loyalty[kingdom.StringId][(int)estate]   -= 30f;
        ImperialAuthorityBehavior.Instance?.ModifyAuthority(kingdom, +6f,
            $"Seized privilege from {estate}");

        ApplyPrivilegeEffect(kingdom, estate, privilege, granted: false);

        InformationManager.DisplayMessage(new InformationMessage(
            $"You seized '{privilege}' from the {estate}. " +
            $"Imperial authority grows; their loyalty falls.",
            Color.FromUint(0xFFCC4400)));
    }

    private void ApplyPrivilegeEffect(Kingdom kingdom, Estate estate, string privilege, bool granted)
    {
        // Effects are applied/removed via flags read by other behaviors
        // e.g. FiefHolderBehavior checks "HeredinaryFiefs" flag before reassigning
        // TradeRouteBehavior checks "TradMonopoly" flag for income calculation
        string key = $"{kingdom.StringId}:{estate}:{privilege}";
        if (granted) _activePrivileges.Add(key);
        else         _activePrivileges.Remove(key);
    }

    private HashSet<string> _activePrivileges = new HashSet<string>();

    public bool HasPrivilege(Kingdom kingdom, Estate estate, string privilege)
        => _activePrivileges.Contains($"{kingdom.StringId}:{estate}:{privilege}");

    // ── Weekly effects ─────────────────────────────────────────────────────────
    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnWeeklyTick()
    {
        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
        {
            EnsureInitialized(kingdom);
            ApplyEstateEffects(kingdom);
            DecayEstateInfluence(kingdom);
        }
    }

    private void ApplyEstateEffects(Kingdom kingdom)
    {
        float nobLoyalty  = GetLoyalty(kingdom, Estate.Nobility);
        float clerLoyalty = GetLoyalty(kingdom, Estate.Clergy);
        float merLoyalty  = GetLoyalty(kingdom, Estate.Merchants);

        // Low loyalty from Nobility → revolt risk
        if (nobLoyalty < 25f)
            RevoltCascadeBehavior.Instance?.AddRevoltPressure(kingdom.StringId, "Noble revolt", 5f);

        // Low loyalty from Clergy → legitimacy drain
        if (clerLoyalty < 25f)
            LegitimacyBehavior.Instance?.ModifyLegitimacy(kingdom.Leader, -1f, "Ulema opposition");

        // Low loyalty from Merchants → trade income penalty already applied via HasPrivilege
        // High Merchant loyalty → small income bonus
        if (merLoyalty > 70f)
        {
            // Trade bonus: handled in TradeRouteBehavior
        }
    }

    private void DecayEstateInfluence(Kingdom kingdom)
    {
        // Influence naturally normalizes toward 33 over time (estates balance each other)
        var inf = _influence[kingdom.StringId];
        for (int i = 0; i < 3; i++)
            inf[i] += (33f - inf[i]) * 0.02f; // slow pull toward 33
    }

    private void EnsureInitialized(Kingdom kingdom)
    {
        if (!_influence.ContainsKey(kingdom.StringId))
            _influence[kingdom.StringId] = new float[] { 33f, 33f, 33f };
        if (!_loyalty.ContainsKey(kingdom.StringId))
            _loyalty[kingdom.StringId]   = new float[] { 50f, 50f, 50f };
    }

    public override void SyncData(IDataStore dataStore)
    {
        var kIds     = _influence.Keys.ToList();
        var infVals  = _influence.Values.Select(a => string.Join(",", a.Select(f => f.ToString("F1")))).ToList();
        var loyVals  = _loyalty.Values.Select(a => string.Join(",", a.Select(f => f.ToString("F1")))).ToList();
        var privList = _activePrivileges.ToList();

        dataStore.SyncData("hind_estate_kids",  ref kIds);
        dataStore.SyncData("hind_estate_inf",   ref infVals);
        dataStore.SyncData("hind_estate_loy",   ref loyVals);
        dataStore.SyncData("hind_estate_priv",  ref privList);

        if (!dataStore.IsSaving)
        {
            _influence.Clear(); _loyalty.Clear();
            for (int i = 0; i < kIds.Count; i++)
            {
                float[] Parse(List<string> src, int idx) =>
                    idx < src.Count
                    ? src[idx].Split(',').Select(float.Parse).ToArray()
                    : new[] { 33f, 33f, 33f };
                _influence[kIds[i]] = Parse(infVals, i);
                _loyalty[kIds[i]]   = Parse(loyVals, i);
            }
            _activePrivileges = new HashSet<string>(privList);
        }
    }
}
```

### Estates game menu

Add an "Estates" panel to the throne-city menu when the player is the ruler:

```
[Manage the Estates]
  ├─ [Nobility — Influence: 45 | Loyalty: 60]
  │    ├─ Grant "Hereditary Fiefs" (−8 authority, +15 nobility loyalty)
  │    └─ Seize "Tax Exemption" (+6 authority, −25 nobility loyalty)
  ├─ [Ulema — Influence: 38 | Loyalty: 72]
  │    └─ Grant "Fatwa Power" (−8 authority, ulema can bless/curse you)
  └─ [Merchants — Influence: 20 | Loyalty: 45]
       └─ Grant "Trade Monopoly" (+15% trade, −8 authority)
```

---

## 4. Internal Kingdom Factions

### Historical basis

TW3K's court had ministerial factions with competing agendas. Mughal courts had the same. The major internal currents in the 1719 Mughal court:

| Faction | Platform | Historical analog |
|---------|----------|------------------|
| War Party (Hawkish) | Expand territory, reconquer lost provinces | Mir Jumla / Zulfiqar Khan type |
| Peace Party (Diplomatic) | Negotiate, reduce military expenditure | Court moderates |
| Reform Party | Modernise administration, reduce corruption | Abul Fazl / Todar Mal type |
| Orthodox Party | Enforce Sharia, expel non-Muslims from positions | Aurangzeb's courtiers |

```csharp
public enum CourtFaction { WarParty, PeaceParty, ReformParty, OrthodoxParty }

public class CourtFactionsBehavior : CampaignBehaviorBase
{
    private Dictionary<string, int> _factionStrength = new Dictionary<string, int>();
    // key = "kingdomId:FactionIndex", value = strength 0–100

    private Dictionary<string, CourtFaction> _dominantFaction = new Dictionary<string, CourtFaction>();

    public static CourtFactionsBehavior Instance { get; private set; }

    public CourtFaction GetDominant(Kingdom kingdom)
        => _dominantFaction.GetValueOrDefault(kingdom?.StringId, CourtFaction.PeaceParty);

    public int GetStrength(Kingdom kingdom, CourtFaction faction)
        => _factionStrength.GetValueOrDefault($"{kingdom.StringId}:{(int)faction}", 25);

    // ── Lords are assigned faction affinity at game start ─────────────────────
    public CourtFaction GetLordFaction(Hero lord)
    {
        // Derive from personality: Martial lords → War; Scholarly → Reform;
        // Religious → Orthodox; Pragmatic → Peace
        int seed = lord.StringId.GetHashCode();
        return (CourtFaction)(Math.Abs(seed) % 4);
    }

    // ── Weekly: dominant faction pushes for a decision ────────────────────────
    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnWeeklyTick()
    {
        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
        {
            RecalculateDominantFaction(kingdom);
            ApplyDominantFactionPressure(kingdom);
        }
    }

    private void RecalculateDominantFaction(Kingdom kingdom)
    {
        var strengths = new Dictionary<CourtFaction, int>();
        foreach (var faction in Enum.GetValues(typeof(CourtFaction)).Cast<CourtFaction>())
            strengths[faction] = 0;

        foreach (Clan clan in kingdom.Clans.Where(c => c.Leader != null))
        {
            CourtFaction f = GetLordFaction(clan.Leader);
            // Each clan contributes strength proportional to their renown
            strengths[f] += (int)(clan.Renown / 100);
        }

        _dominantFaction[kingdom.StringId] = strengths.OrderByDescending(kv => kv.Value).First().Key;
    }

    private void ApplyDominantFactionPressure(Kingdom kingdom)
    {
        if (kingdom.Leader != Hero.MainHero) return; // only bother the player when they rule

        CourtFaction dominant = GetDominant(kingdom);
        bool pressureAlreadyActive = _weeksSincePressure.GetValueOrDefault(kingdom.StringId, 0) < 4;
        if (pressureAlreadyActive) return;

        _weeksSincePressure[kingdom.StringId] = 0;

        switch (dominant)
        {
            case CourtFaction.WarParty:
                // Push: demand declaration of war on weakest neighbour
                Kingdom target = Kingdom.All
                    .Where(k => !k.IsEliminated && k != kingdom && !kingdom.IsAtWarWith(k))
                    .OrderBy(k => k.TotalStrength)
                    .FirstOrDefault();
                if (target != null)
                    PresentFactionDemand(kingdom, dominant,
                        $"The war faction demands we move against {target.Name}.",
                        () => DeclareWarAction.Apply(kingdom, target,
                            DeclareWarAction.DeclareWarDetail.Default));
                break;

            case CourtFaction.OrthodoxParty:
                PresentFactionDemand(kingdom, dominant,
                    "The ulema demand enforcement of Islamic law and removal of non-Muslim governors.",
                    () => ReligiousToleranceBehavior.Instance?.SetStance(kingdom, ToleranceStance.Strict));
                break;

            case CourtFaction.ReformParty:
                PresentFactionDemand(kingdom, dominant,
                    "The reform party urges an audit of all mansabdars for ghost soldiers and embezzlement.",
                    () => TriggerMansabdariAudit(kingdom));
                break;

            case CourtFaction.PeaceParty:
                PresentFactionDemand(kingdom, dominant,
                    "Your counselors urge you to seek peace and reduce military expenditure.",
                    () => { /* Player makes peace themselves */ });
                break;
        }
    }

    private void PresentFactionDemand(Kingdom kingdom, CourtFaction faction,
        string demand, Action onAccept)
    {
        string factionName = faction switch
        {
            CourtFaction.WarParty      => "War Faction",
            CourtFaction.PeaceParty    => "Peace Faction",
            CourtFaction.ReformParty   => "Reform Faction",
            CourtFaction.OrthodoxParty => "Orthodox Faction",
            _                          => "Courtiers"
        };

        InformationManager.ShowInquiry(new InquiryData(
            $"Pressure from the {factionName}",
            demand + "\n\nYou may comply, delay, or refuse. Refusing damages your relations with " +
            "that faction's members.",
            true, true,
            "Comply",
            "Refuse",
            () => { onAccept(); AcceptFactionDemand(kingdom, faction); },
            () => RefuseFactionDemand(kingdom, faction)
        ));
    }

    private void AcceptFactionDemand(Kingdom kingdom, CourtFaction faction)
    {
        // Reward: relations +10 with all lords of this faction
        foreach (Clan clan in kingdom.Clans.Where(c => GetLordFaction(c.Leader) == faction))
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                Hero.MainHero, clan.Leader, +10);
        ImperialAuthorityBehavior.Instance?.ModifyAuthority(kingdom, +3f, "Faction satisfied");
    }

    private void RefuseFactionDemand(Kingdom kingdom, CourtFaction faction)
    {
        foreach (Clan clan in kingdom.Clans.Where(c => GetLordFaction(c.Leader) == faction))
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                Hero.MainHero, clan.Leader, -15);
    }

    private void TriggerMansabdariAudit(Kingdom kingdom)
    {
        // Audit: every lord who has been in poor standing gets a relation penalty
        // but a small gold bonus flows to the treasury from "recovered" embezzlement
        InformationManager.DisplayMessage(new InformationMessage(
            "The mansabdari audit reveals significant embezzlement. Some lords are fined.",
            Color.FromUint(0xFFD4AF37)));
    }

    private Dictionary<string, int> _weeksSincePressure = new Dictionary<string, int>();

    public override void SyncData(IDataStore dataStore)
    {
        // Strength and dominant faction re-derive from clan composition — no save needed
    }
}
```

---

## 5. Legitimacy

### What it is

A single float (0–100) attached to the ruler of each kingdom. It models how widely their right to rule is accepted. High legitimacy means lords obey, taxes arrive, and the army follows. Low legitimacy means challenges, revolts, and defection.

This is separate from the ruler's personal popularity (relations). A ruler can be personally beloved but have low legitimacy if they seized power through a coup.

```csharp
public class LegitimacyBehavior : CampaignBehaviorBase
{
    private Dictionary<string, float> _legitimacy = new Dictionary<string, float>();

    public static LegitimacyBehavior Instance { get; private set; }

    public float GetLegitimacy(Hero ruler) =>
        ruler == null ? 0f : _legitimacy.GetValueOrDefault(ruler.StringId, 60f);

    public void SetLegitimacy(Hero ruler, float value)
    {
        if (ruler != null) _legitimacy[ruler.StringId] = Math.Clamp(value, 0f, 100f);
    }

    public void ModifyLegitimacy(Hero ruler, float delta, string reason)
    {
        if (ruler == null) return;
        float current = GetLegitimacy(ruler);
        SetLegitimacy(ruler, current + delta);
        if (ruler == Hero.MainHero && Math.Abs(delta) >= 5f)
            InformationManager.DisplayMessage(new InformationMessage(
                $"Legitimacy {(delta > 0 ? "+" : "")}{delta:F0}: {reason}",
                delta > 0 ? Color.FromUint(0xFFD4AF37) : Color.FromUint(0xFFCC4400)));
    }

    // ── Gain/loss events ────────────────────────────────────────────────────────
    // Wired to other behaviors via their existing event hooks:

    // +10: Coronation ceremony at game start
    // +8:  Won a war (peace on own terms)
    // +5:  Nazrana fully paid for 3 consecutive cycles
    // +5:  Pilgrimage completed (Chapter 20)
    // +6:  Ulema fatwa in your favour
    // −12: Lost a war (peace on enemy terms)
    // −15: Seized power through coup / civil war win
    // −8:  Imprisoned a noble without trial
    // −10: Ulema fatwa against you
    // −1/week: Passive decay when authority < 40

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.MakePeaceEvent.AddNonSerializedListener(this, OnPeaceMade);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnPeaceMade(IFaction f1, IFaction f2)
    {
        // Determine which side benefited — simplified: the one with more settlements wins
        if (f1 is Kingdom k1 && f2 is Kingdom k2)
        {
            int s1 = k1.Settlements.Count(s => s.IsTown || s.IsCastle);
            int s2 = k2.Settlements.Count(s => s.IsTown || s.IsCastle);
            if (s1 >= s2) ModifyLegitimacy(k1.Leader, +8f, "Won a war");
            else          ModifyLegitimacy(k2.Leader, +8f, "Won a war");
        }
    }

    private void OnWeeklyTick()
    {
        foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
        {
            float authority = ImperialAuthorityBehavior.Instance?.GetAuthority(kingdom) ?? 75f;
            if (authority < 40f)
                ModifyLegitimacy(kingdom.Leader, -1f, "Authority collapse weakens the throne");

            // Legitimacy naturally decays toward 50 over time (rulers always face erosion)
            float current = GetLegitimacy(kingdom.Leader);
            if (current > 50f) ModifyLegitimacy(kingdom.Leader, -0.5f, "Natural erosion");
        }
    }

    // ── Effects ─────────────────────────────────────────────────────────────────
    public float GetCallToArmsModifier(Hero ruler)
        => GetLegitimacy(ruler) switch
        {
            >= 80 => 1.20f,   // High legitimacy: extra lords respond
            >= 60 => 1.00f,
            >= 40 => 0.75f,
            >= 20 => 0.45f,
            _     => 0.20f    // Barely anyone follows a ruler with no legitimacy
        };

    public override void SyncData(IDataStore dataStore)
    {
        var ids  = _legitimacy.Keys.ToList();
        var vals = _legitimacy.Values.ToList();
        dataStore.SyncData("hind_legit_ids",  ref ids);
        dataStore.SyncData("hind_legit_vals", ref vals);
        if (!dataStore.IsSaving)
        {
            _legitimacy.Clear();
            for (int i = 0; i < ids.Count; i++)
                _legitimacy[ids[i]] = i < vals.Count ? vals[i] : 60f;
        }
    }
}
```

---

## 6. Revolt Cascade

### Historical basis

In EU4, unrest in a province spawns rebel armies if left unaddressed. This directly models what happened in Mughal India: the Jat revolts near Delhi (1669–1707), the Satnami uprising (1672), the Sikh revolts in Punjab (1710s), and the Maratha campaigns all began as regional resistance that the emperor could not suppress while fighting on multiple fronts simultaneously.

### Revolt pressure and spawn

```csharp
public class RevoltCascadeBehavior : CampaignBehaviorBase
{
    private Dictionary<string, float> _revoltPressure = new Dictionary<string, float>();
    // settlementId → pressure (0–100)
    private Dictionary<string, string> _revoltType = new Dictionary<string, string>();
    // settlementId → "Jat", "Religious", "Noble", "Peasant"

    public static RevoltCascadeBehavior Instance { get; private set; }

    // Called by other systems when they raise pressure
    public void AddRevoltPressure(string settlementId, string type, float amount)
    {
        float current = _revoltPressure.GetValueOrDefault(settlementId, 0f);
        _revoltPressure[settlementId] = Math.Min(100f, current + amount);
        _revoltType[settlementId]     = type;
    }

    public override void RegisterEvents()
    {
        Instance = this;
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    private void OnWeeklyTick()
    {
        foreach (Settlement settlement in Settlement.All.Where(s => s.IsTown || s.IsVillage))
        {
            UpdateRevoltPressure(settlement);
        }
    }

    private void UpdateRevoltPressure(Settlement settlement)
    {
        string id      = settlement.StringId;
        float pressure = _revoltPressure.GetValueOrDefault(id, 0f);

        // Passive pressure gains
        float gain = 0f;

        // Famine: +5/week
        float food = FoodSecurityBehavior.Instance?.GetFoodSecurity(settlement) ?? 80f;
        if (food < 25f) gain += 5f;

        // Disease: +3/week when high
        float disease = EpidemicBehavior.Instance?.GetDiseaseLevel(settlement) ?? 0f;
        if (disease > 60f) gain += 3f;

        // Bandit threat: +2/week when high
        float bandits = FiefHolderBehavior.Instance?.GetBanditThreat(settlement) ?? 0f;
        if (bandits > 70f) gain += 2f;

        // Religious mismatch: +3/week if Strict enforcement in non-Muslim settlement
        Kingdom kingdom = settlement.OwnerClan?.Kingdom;
        if (kingdom != null)
        {
            ToleranceStance stance = ReligiousToleranceBehavior.Instance?.GetStance(kingdom)
                                  ?? ToleranceStance.Moderate;
            bool mismatch = stance == ToleranceStance.Strict
                         && IsNonMuslimSettlement(settlement);
            if (mismatch) gain += 3f;
        }

        // Military presence reduces pressure
        bool garrison = settlement.Town?.GarrisonParty?.TotalStrength > 50;
        if (garrison) gain -= 4f;

        // Player nearby also reduces pressure
        bool playerNearby = MobileParty.MainParty.Position2D
            .DistanceSquared(settlement.Position2D) < 20 * 20;
        if (playerNearby) gain -= 3f;

        pressure = Math.Clamp(pressure + gain, 0f, 100f);
        _revoltPressure[id] = pressure;

        // Spawn revolt at 80+
        if (pressure >= 80f && MBRandom.RandomFloat < 0.08f) // 8% chance per week above 80
            SpawnRevolt(settlement);
    }

    private void SpawnRevolt(Settlement settlement)
    {
        string revoltType = _revoltType.GetValueOrDefault(settlement.StringId, "Peasant");
        _revoltPressure[settlement.StringId] = 0f; // reset after spawning

        // Create a rebel bandit-type party
        string rebelName = revoltType switch
        {
            "Jat"       => "Jat Revolt",
            "Religious" => "Religious Uprising",
            "Noble"     => "Noble Rebellion",
            _           => "Peasant Revolt"
        };

        InformationManager.DisplayMessage(new InformationMessage(
            $"A {rebelName} has erupted near {settlement.Name}! " +
            $"A rebel force has taken to the field.",
            Color.FromUint(0xFFCC2200)));

        // EventCapture records this
        EventCaptureBehavior.Instance?.RecordPlayerMilestone(
            GameEventType.Kingdom_War_Declared,
            $"{rebelName} near {settlement.Name}",
            settlement.Name.ToString());

        // In full implementation: spawn a MobileParty with rebel troops
        // using the settlement's dominant culture troops
        SpawnRebelParty(settlement, revoltType);
    }

    private void SpawnRebelParty(Settlement settlement, string revoltType)
    {
        // Spawn a hostile party using vanilla Bannerlord's bandit party spawn,
        // but with stronger and more culturally appropriate troops
        // Full implementation requires CampaignSideEffectModel or direct party creation
        // via MobileParty.CreateParty and populating their roster

        // Note: spawning parties requires careful use of MobileParty.CreateParty
        // followed by TroopRoster population and setting TargetParty to null
        // (so they behave as a field army, not a raider)
    }

    private bool IsNonMuslimSettlement(Settlement s)
        => s.Culture?.StringId is "vlandia" or "battania" or "khuzait";

    public override void SyncData(IDataStore dataStore)
    {
        var pIds  = _revoltPressure.Keys.ToList();
        var pVals = _revoltPressure.Values.ToList();
        var tIds  = _revoltType.Keys.ToList();
        var tVals = _revoltType.Values.ToList();
        dataStore.SyncData("hind_revolt_pids",  ref pIds);
        dataStore.SyncData("hind_revolt_pvals", ref pVals);
        dataStore.SyncData("hind_revolt_tids",  ref tIds);
        dataStore.SyncData("hind_revolt_tvals", ref tVals);
        if (!dataStore.IsSaving)
        {
            _revoltPressure.Clear(); _revoltType.Clear();
            for (int i = 0; i < pIds.Count; i++)
                _revoltPressure[pIds[i]] = i < pVals.Count ? pVals[i] : 0f;
            for (int i = 0; i < tIds.Count; i++)
                _revoltType[tIds[i]] = i < tVals.Count ? tVals[i] : "Peasant";
        }
    }
}
```

---

## 7. How These Systems Interlock

These six systems are not independent. They form a single model of imperial health:

```
IMPERIAL AUTHORITY (Padshahi Hukm)
        │
        ├─► Tax collection rate → Affects clan gold → Affects nazrana payment
        │                                                         ↓
        │                                            Nazrana missed → Legit −5
        │
        ├─► Call-to-arms compliance → Affects civil war/leadership challenge outcomes
        │
        └─► Falls below 25 → Revolt pressure +5/week across all settlements
                                          ↓
                                 Revolt pressure > 80 → Rebel party spawns
                                          ↓
                               Rebel captures settlement → Authority −5

LEGITIMACY
        ├─► Low legitimacy → Succession crisis more likely when ruler dies
        ├─► Low legitimacy → Court factions push demands more frequently
        └─► Low legitimacy → Estates loyalty decays faster

THREE ESTATES
        ├─► Noble loyalty < 25 → Revolt pressure +5 on noble settlements
        ├─► Clergy loyalty < 25 → Legitimacy −1/week
        └─► Merchant loyalty > 70 → Trade route health recovery +2/week

SUCCESSION
        └─► War of Princes → Authority −20 immediately
                           → All pretender kingdoms start at Legitimacy 30–40
```

A player who manages the empire carefully keeps authority high by winning wars, legitimacy high by paying nazrana and holding festivals, and estate loyalty balanced by neither over-privileging nor oppressing any bloc. A player who mismanages triggers a cascading collapse: low authority → missed taxes → missed nazrana → legitimacy falls → factions push destabilizing demands → estates turn disloyal → revolts spawn → settlements lost → authority falls further.

This is 1719. This is what actually happened.

---

**[← Chapter 18](18-News-Reporter-and-Biographer.md)** | **[Home](Home.md)** | **[Chapter 20 →](20-Character-Depth-and-Intrigue.md)**
