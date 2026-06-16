# Chapter 13 — CK3-Style Council System

> Research-backed design for a royal council with named positions, political effects, and influence mechanics — built on top of Bannerlord's existing decision and influence infrastructure.

**[← Home](Home.md)** | **[Quick Reference →](Quick-Reference.md)**

---

## Contents

- [What Bannerlord already provides](#what-bannerlord-already-provides)
- [What is completely absent](#what-is-completely-absent)
- [Council design for the Hindostan Mod](#council-design-for-the-hindostan-mod)
- [Data model](#data-model)
- [CouncilPosition enum](#councilposition-enum)
- [KingdomCouncil class](#kingdomcouncil-class)
- [CouncilBehavior — the main behavior](#councilbehavior--the-main-behavior)
- [Position effects — how each role changes the game](#position-effects--how-each-role-changes-the-game)
- [Player interaction — appointing council members](#player-interaction--appointing-council-members)
- [Council votes on Kingdom Decisions](#council-votes-on-kingdom-decisions)
- [AI council appointments](#ai-council-appointments)
- [Wiring it all together](#wiring-it-all-together)

---

## What Bannerlord Already Provides

These systems exist in v1.4.6 and the council system can plug directly into them:

### Decision and voting system — fully built

```
KingdomDecision (abstract base)
├── DeclareWarDecision
├── MakePeaceKingdomDecision
├── KingdomPolicyDecision
├── KingSelectionKingdomDecision
├── ExpelClanFromKingdomDecision
├── StartAllianceDecision
└── SettlementClaimantDecision
```

Each decision has `DetermineSupporters()` which returns a `Supporter` list of clans with their stance. Every clan's vote is weighted by `Clan.Influence`. **The council system hooks here** — council members contribute extra influence to specific decision types.

### Influence system — fully built

- `Clan.Influence` — float, earned by raiding/battles/policies, spent on proposals
- `OnClanInfluenceChanged` event fires on every change
- `GetInfluenceCostOfPolicyProposalAndDisavowal()` — policy voting costs
- `DenarsToInfluence()` — gold to influence conversion
- Council members earn influence bonuses from their position and can spend it to swing votes

### Policy system — 30+ policies exist

Relevant existing policies that affect council design:
- `policy_lords_privy_council` — reduces ruler influence, gives lords more voting weight
- `policy_council_of_the_commons` — enables commoner representation
- `policy_senate` — republican voting
- `policy_marshals` — military organization bonus
- `policy_magistrates` — regional administration

These are good policy gates: some council positions could require certain policies to be active.

### Campaign events for politics — fully built

```csharp
CampaignEvents.OnKingdomDecisionAdded       // hook to inject council input
CampaignEvents.OnKingdomDecisionConcluded   // react after votes resolve
CampaignEvents.OnClanInfluenceChanged       // track influence flow
CampaignEvents.OnClanChangedKingdom         // adjust council when lord defects
CampaignEvents.OnGovernorChanged            // governor assignments change
```

### Governor / party role system — partially useful

Heroes can be assigned as `Governor` of a settlement, or given `PartyRole` (Scout, Quartermaster, Engineer, Commander). The council positions we build are a separate, kingdom-level layer on top of this — a hero can be both Governor of Delhi AND the Diwan-i-Ala (Grand Vizier) simultaneously.

---

## What Is Completely Absent

Build these from scratch:

| Missing piece | What to build |
|---|---|
| Named council positions | `CouncilPosition` enum |
| Council membership | `KingdomCouncil` class with `Dictionary<CouncilPosition, Hero>` |
| Position-specific effects | Hooks in model overrides and behavior tick handlers |
| Council voting on decisions | Hook `OnKingdomDecisionAdded`, inject influence weight |
| Player council UI | Custom GameMenu at throne room |
| AI appointing council | Yearly behavior that auto-fills empty positions |
| Save/load | SyncData on council membership dictionaries |

---

## Council Design for the Hindostan Mod

Every kingdom gets a council of five positions. The positions are named to match Hindostan's 1719 context rather than European titles.

| Position | Hindostan name | Equivalent | Primary effect |
|---|---|---|---|
| `Vizier` | Diwan-i-Ala / Peshwa | Grand Vizier / Chancellor | +Influence income for ruling clan, boosts policy proposal success |
| `Marshal` | Mir Bakshi / Senapati | Military Commander | Party size bonus for kingdom armies, faster siege speed |
| `Treasurer` | Diwan-i-Kul / Khasnadar | Treasurer / Steward | +Tax income for all kingdom settlements, cheaper workshop construction |
| `Spymaster` | Akhbarat Nawis / Huzur Navis | Spymaster | Reveals enemy strength on map, detection of conspiracies |
| `Jurist` | Qazi ul Quzat / Dharmadhikari | Chancellor / Lord Chief Justice | Cheaper clan pacification, bonus to settlement loyalty |

Each position has a **Competence Score** derived from the holder's relevant skill, and **effects scale with competence**.

---

## Data Model

### Competence calculation per position

```csharp
private static int GetCompetenceForPosition(Hero hero, CouncilPosition position)
{
    return position switch
    {
        CouncilPosition.Vizier     => hero.GetSkillValue(DefaultSkills.Charm)
                                    + hero.GetSkillValue(DefaultSkills.Leadership),
        CouncilPosition.Marshal    => hero.GetSkillValue(DefaultSkills.Tactics)
                                    + hero.GetSkillValue(DefaultSkills.Leadership),
        CouncilPosition.Treasurer  => hero.GetSkillValue(DefaultSkills.Trade)
                                    + hero.GetSkillValue(DefaultSkills.Steward),
        CouncilPosition.Spymaster  => hero.GetSkillValue(DefaultSkills.Roguery)
                                    + hero.GetSkillValue(DefaultSkills.Scouting),
        CouncilPosition.Jurist     => hero.GetSkillValue(DefaultSkills.Charm)
                                    + hero.GetSkillValue(DefaultSkills.Steward),
        _                          => 0
    };
}
```

### Competence tiers

| Score | Tier | Effect multiplier |
|---|---|---|
| 0–99 | Incompetent | 0× (position filled but no benefit) |
| 100–199 | Capable | 0.5× base effect |
| 200–299 | Skilled | 1.0× base effect |
| 300–399 | Expert | 1.5× base effect |
| 400+ | Master | 2.0× base effect |

---

## CouncilPosition Enum

```csharp
namespace TheHindostanMod
{
    public enum CouncilPosition
    {
        None      = 0,
        Vizier    = 1,   // Diwan-i-Ala / Peshwa
        Marshal   = 2,   // Mir Bakshi / Senapati
        Treasurer = 3,   // Diwan-i-Kul / Khasnadar
        Spymaster = 4,   // Akhbarat Nawis
        Jurist    = 5,   // Qazi ul Quzat
    }
}
```

---

## KingdomCouncil Class

This is a **data container** — it holds the membership state for one kingdom's council. It is NOT a behavior (it has no RegisterEvents). The behavior manages instances of this class.

```csharp
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace TheHindostanMod
{
    public class KingdomCouncil
    {
        // The kingdom this council belongs to
        public Kingdom Kingdom { get; }

        // Position → Hero assignment (null = position is vacant)
        private Dictionary<CouncilPosition, Hero> _members
            = new Dictionary<CouncilPosition, Hero>();

        public KingdomCouncil(Kingdom kingdom)
        {
            Kingdom = kingdom;
        }

        // ── Membership ────────────────────────────────────────────────────────
        public Hero GetMember(CouncilPosition position)
        {
            _members.TryGetValue(position, out Hero hero);
            // Guard: hero might have died or left the kingdom
            if (hero != null && (!hero.IsAlive || hero.Clan?.Kingdom != Kingdom))
            {
                _members.Remove(position);
                return null;
            }
            return hero;
        }

        public void SetMember(CouncilPosition position, Hero hero)
        {
            // Fire old member from position if they held it elsewhere
            RemoveMemberFromAllPositions(hero);
            _members[position] = hero;
        }

        public void RemoveMember(CouncilPosition position)
        {
            _members.Remove(position);
        }

        public void RemoveMemberFromAllPositions(Hero hero)
        {
            var keys = _members.Where(kv => kv.Value == hero)
                               .Select(kv => kv.Key)
                               .ToList();
            foreach (var k in keys) _members.Remove(k);
        }

        public bool IsOnCouncil(Hero hero)
            => _members.Values.Any(h => h == hero);

        public CouncilPosition GetPositionOf(Hero hero)
            => _members.FirstOrDefault(kv => kv.Value == hero).Key;

        public bool IsVacant(CouncilPosition position)
            => GetMember(position) == null;

        // All current members (filters out dead/departed members automatically)
        public IEnumerable<(CouncilPosition position, Hero hero)> AllMembers
            => Enum.GetValues(typeof(CouncilPosition))
                   .Cast<CouncilPosition>()
                   .Where(p => p != CouncilPosition.None)
                   .Select(p => (p, GetMember(p)))
                   .Where(t => t.Item2 != null);

        // ── Competence ────────────────────────────────────────────────────────
        public int GetCompetence(CouncilPosition position)
        {
            Hero hero = GetMember(position);
            return hero == null ? 0 : GetCompetenceForPosition(hero, position);
        }

        // Competence as a 0.0–2.0 multiplier
        public float GetEffectMultiplier(CouncilPosition position)
        {
            int comp = GetCompetence(position);
            return comp switch
            {
                >= 400 => 2.0f,
                >= 300 => 1.5f,
                >= 200 => 1.0f,
                >= 100 => 0.5f,
                _      => 0.0f
            };
        }

        // ── Save / load support ───────────────────────────────────────────────
        // Returns data in a form SyncData can handle (lists of parallel arrays)
        public List<int>  GetPositionKeys()  => _members.Keys.Select(k => (int)k).ToList();
        public List<Hero> GetMemberValues()  => _members.Values.ToList();

        public void LoadFromLists(List<int> keys, List<Hero> heroes)
        {
            _members.Clear();
            for (int i = 0; i < Math.Min(keys.Count, heroes.Count); i++)
            {
                var pos = (CouncilPosition)keys[i];
                if (heroes[i] != null && heroes[i].IsAlive)
                    _members[pos] = heroes[i];
            }
        }

        private static int GetCompetenceForPosition(Hero hero, CouncilPosition position)
        {
            return position switch
            {
                CouncilPosition.Vizier    => hero.GetSkillValue(DefaultSkills.Charm)
                                          + hero.GetSkillValue(DefaultSkills.Leadership),
                CouncilPosition.Marshal   => hero.GetSkillValue(DefaultSkills.Tactics)
                                          + hero.GetSkillValue(DefaultSkills.Leadership),
                CouncilPosition.Treasurer => hero.GetSkillValue(DefaultSkills.Trade)
                                          + hero.GetSkillValue(DefaultSkills.Steward),
                CouncilPosition.Spymaster => hero.GetSkillValue(DefaultSkills.Roguery)
                                          + hero.GetSkillValue(DefaultSkills.Scouting),
                CouncilPosition.Jurist    => hero.GetSkillValue(DefaultSkills.Charm)
                                          + hero.GetSkillValue(DefaultSkills.Steward),
                _                         => 0
            };
        }
    }
}
```

---

## CouncilBehavior — the Main Behavior

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
    public class CouncilBehavior : CampaignBehaviorBase
    {
        // One council per kingdom
        private Dictionary<string, KingdomCouncil> _councils
            = new Dictionary<string, KingdomCouncil>();

        // Save/load parallel arrays (SyncData can't serialize Dictionary<string, KingdomCouncil>)
        private List<string> _councilKingdomIds = new List<string>();
        private List<List<int>>  _councilPositionKeys = new List<List<int>>();
        private List<List<Hero>> _councilHeroValues   = new List<List<Hero>>();

        // ── Public accessor ───────────────────────────────────────────────────
        public KingdomCouncil GetCouncil(Kingdom kingdom)
        {
            if (!_councils.TryGetValue(kingdom.StringId, out var council))
            {
                council = new KingdomCouncil(kingdom);
                _councils[kingdom.StringId] = council;
            }
            return council;
        }

        // Static singleton accessor (for use from models and other behaviors)
        public static CouncilBehavior Instance { get; private set; }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        public override void RegisterEvents()
        {
            Instance = this;

            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(
                this, OnNewGameCreated);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(
                this, OnGameLoaded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(
                this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(
                this, OnWeeklyTick);
            CampaignEvents.OnClanChangedKingdom.AddNonSerializedListener(
                this, OnClanChangedKingdom);
            CampaignEvents.OnHeroKilledEvent.AddNonSerializedListener(
                this, OnHeroKilled);
            CampaignEvents.OnKingdomDecisionAdded.AddNonSerializedListener(
                this, OnKingdomDecisionAdded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Pack councils into parallel lists before saving
            if (dataStore.IsSaving)
                PackCouncilsForSave();

            dataStore.SyncData("hind_council_kingdom_ids",  ref _councilKingdomIds);
            dataStore.SyncData("hind_council_pos_keys",     ref _councilPositionKeys);
            dataStore.SyncData("hind_council_hero_vals",    ref _councilHeroValues);

            // Unpack after loading
            if (!dataStore.IsSaving)
                UnpackCouncilsAfterLoad();
        }

        private void PackCouncilsForSave()
        {
            _councilKingdomIds.Clear();
            _councilPositionKeys.Clear();
            _councilHeroValues.Clear();

            foreach (var (id, council) in _councils)
            {
                _councilKingdomIds.Add(id);
                _councilPositionKeys.Add(council.GetPositionKeys());
                _councilHeroValues.Add(council.GetMemberValues());
            }
        }

        private void UnpackCouncilsAfterLoad()
        {
            _councils.Clear();
            for (int i = 0; i < _councilKingdomIds.Count; i++)
            {
                string id      = _councilKingdomIds[i];
                Kingdom kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == id);
                if (kingdom == null) continue;

                var council = new KingdomCouncil(kingdom);
                if (i < _councilPositionKeys.Count && i < _councilHeroValues.Count)
                    council.LoadFromLists(_councilPositionKeys[i], _councilHeroValues[i]);

                _councils[id] = council;
            }
        }

        // ── Setup ─────────────────────────────────────────────────────────────
        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            // Initialize councils for all kingdoms and auto-assign starting members
            foreach (Kingdom kingdom in Kingdom.All)
                AutoFillCouncil(GetCouncil(kingdom));
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            Instance = this;
            // Ensure all active kingdoms have councils (handles kingdoms created mid-game)
            foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
                if (!_councils.ContainsKey(kingdom.StringId))
                    AutoFillCouncil(GetCouncil(kingdom));
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom,
            Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail,
            bool showNotification)
        {
            // Remove all departed heroes from their old kingdom's council
            if (oldKingdom != null && _councils.TryGetValue(oldKingdom.StringId, out var oldCouncil))
            {
                foreach (Hero hero in clan.Heroes)
                    oldCouncil.RemoveMemberFromAllPositions(hero);
            }
        }

        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            // Remove killed heroes from all councils
            foreach (var council in _councils.Values)
                council.RemoveMemberFromAllPositions(victim);
        }

        private void OnDailyTick()
        {
            ApplyCouncilEffects();
        }

        private void OnWeeklyTick()
        {
            // Every week: AI kingdoms check for vacant positions and fill them
            foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
            {
                if (kingdom == Hero.MainHero?.MapFaction) continue; // player manages manually
                var council = GetCouncil(kingdom);
                foreach (CouncilPosition pos in Enum.GetValues(typeof(CouncilPosition))
                    .Cast<CouncilPosition>().Where(p => p != CouncilPosition.None))
                {
                    if (council.IsVacant(pos))
                        AutoFillPosition(council, pos);
                }
            }
        }

        // Hook into existing decision system
        private void OnKingdomDecisionAdded(KingdomDecision decision, bool isPlayerInvolved)
        {
            if (decision?.Kingdom == null) return;
            BoostCouncilMemberInfluenceForDecision(decision);
        }

        // ── Council auto-fill ─────────────────────────────────────────────────
        private void AutoFillCouncil(KingdomCouncil council)
        {
            foreach (CouncilPosition pos in Enum.GetValues(typeof(CouncilPosition))
                .Cast<CouncilPosition>().Where(p => p != CouncilPosition.None))
            {
                AutoFillPosition(council, pos);
            }
        }

        private void AutoFillPosition(KingdomCouncil council, CouncilPosition position)
        {
            Kingdom kingdom = council.Kingdom;

            // Find the best available hero in this kingdom for this position
            Hero best = kingdom.Clans
                .SelectMany(c => c.Heroes)
                .Where(h => h.IsAlive
                         && h.IsLord
                         && !council.IsOnCouncil(h)
                         && h != kingdom.Leader)   // ruler doesn't sit on their own council
                .OrderByDescending(h => GetCompetenceScore(h, position))
                .FirstOrDefault();

            if (best != null)
                council.SetMember(position, best);
        }

        private static int GetCompetenceScore(Hero hero, CouncilPosition position)
        {
            return position switch
            {
                CouncilPosition.Vizier    => hero.GetSkillValue(DefaultSkills.Charm)
                                          + hero.GetSkillValue(DefaultSkills.Leadership),
                CouncilPosition.Marshal   => hero.GetSkillValue(DefaultSkills.Tactics)
                                          + hero.GetSkillValue(DefaultSkills.Leadership),
                CouncilPosition.Treasurer => hero.GetSkillValue(DefaultSkills.Trade)
                                          + hero.GetSkillValue(DefaultSkills.Steward),
                CouncilPosition.Spymaster => hero.GetSkillValue(DefaultSkills.Roguery)
                                          + hero.GetSkillValue(DefaultSkills.Scouting),
                CouncilPosition.Jurist    => hero.GetSkillValue(DefaultSkills.Charm)
                                          + hero.GetSkillValue(DefaultSkills.Steward),
                _                         => 0
            };
        }

        // ── Decision influence boost ──────────────────────────────────────────
        // When a kingdom decision is filed, relevant council members add influence
        // weight to the vote — giving the ruler more or less control depending on
        // council composition.
        private void BoostCouncilMemberInfluenceForDecision(KingdomDecision decision)
        {
            var council = GetCouncil(decision.Kingdom);

            // Marshal has extra voice in war/peace decisions
            if (decision is DeclareWarDecision || decision is MakePeaceKingdomDecision)
            {
                Hero marshal = council.GetMember(CouncilPosition.Marshal);
                float multiplier = council.GetEffectMultiplier(CouncilPosition.Marshal);
                if (marshal?.Clan != null && multiplier > 0f)
                    marshal.Clan.Influence += 20f * multiplier;
            }

            // Vizier has extra voice in policy decisions
            if (decision is KingdomPolicyDecision)
            {
                Hero vizier = council.GetMember(CouncilPosition.Vizier);
                float multiplier = council.GetEffectMultiplier(CouncilPosition.Vizier);
                if (vizier?.Clan != null && multiplier > 0f)
                    vizier.Clan.Influence += 15f * multiplier;
            }

            // Treasurer has extra voice in trade agreements
            if (decision.GetType().Name.Contains("Trade"))
            {
                Hero treasurer = council.GetMember(CouncilPosition.Treasurer);
                float multiplier = council.GetEffectMultiplier(CouncilPosition.Treasurer);
                if (treasurer?.Clan != null && multiplier > 0f)
                    treasurer.Clan.Influence += 10f * multiplier;
            }
        }

        // ── Daily council effects ─────────────────────────────────────────────
        private void ApplyCouncilEffects()
        {
            foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
            {
                var council = GetCouncil(kingdom);
                ApplyVizierEffect(council);
                // Marshal and Treasurer effects are applied via model overrides
                // (see HindostanClanFinanceModel and HindostanPartySizeModel)
                // Spymaster and Jurist effects are applied in weekly ticks
            }
        }

        private void ApplyVizierEffect(KingdomCouncil council)
        {
            float mult = council.GetEffectMultiplier(CouncilPosition.Vizier);
            if (mult <= 0f) return;

            // Vizier generates influence for the ruling clan daily
            var rulingClan = council.Kingdom.RulingClan;
            if (rulingClan != null)
                rulingClan.Influence += 0.5f * mult;
        }
    }
}
```

---

## Position Effects — How Each Role Changes the Game

### Vizier (Diwan-i-Ala / Peshwa)
**Competence skills:** Charm + Leadership

Applied in `CouncilBehavior.ApplyVizierEffect()` (daily tick):
- Generates `+0.5 × multiplier` influence per day for the ruling clan
- Boosts policy decision influence weight (in `OnKingdomDecisionAdded`)

### Marshal (Mir Bakshi / Senapati)
**Competence skills:** Tactics + Leadership

Applied via `HindostanPartySizeModel` override:
```csharp
// In GetPartyMemberSizeLimit:
var council = CouncilBehavior.Instance?.GetCouncil(party.LeaderHero?.Clan?.Kingdom);
if (council != null)
{
    float marshalMult = council.GetEffectMultiplier(CouncilPosition.Marshal);
    if (marshalMult > 0f)
        result.AddFactor(0.15f * marshalMult, new TextObject("{=!}Marshal's Organisation"));
}
```

### Treasurer (Diwan-i-Kul / Khasnadar)
**Competence skills:** Trade + Steward

Applied via `HindostanClanFinanceModel` override:
```csharp
// In CalculateClanGoldChange:
var council = CouncilBehavior.Instance?.GetCouncil(queriedClan.Kingdom);
if (council != null)
{
    float treasurerMult = council.GetEffectMultiplier(CouncilPosition.Treasurer);
    if (treasurerMult > 0f)
        result.AddFactor(0.10f * treasurerMult, new TextObject("{=!}Treasurer's Management"));
}
```

### Spymaster (Akhbarat Nawis)
**Competence skills:** Roguery + Scouting

Applied in weekly behavior tick:
- Reveal the troop strength of enemy parties within kingdom territory
- Detect when a clan's relation to the king drops below -20 (conspiracy risk notification)

```csharp
private void ApplySpymasterEffect(KingdomCouncil council)
{
    float mult = council.GetEffectMultiplier(CouncilPosition.Spymaster);
    if (mult <= 0f || council.Kingdom != Hero.MainHero?.MapFaction) return;

    // Notify player of clans at risk of leaving
    foreach (Clan clan in council.Kingdom.Clans)
    {
        if (clan == council.Kingdom.RulingClan) continue;
        int relation = clan.Leader != null
            ? CharacterRelationManager.GetHeroRelation(clan.Leader, council.Kingdom.Leader)
            : 0;

        if (relation < -20 * mult)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"[Spymaster] {clan.Name} is disaffected. Relation: {relation}.",
                Color.FromUint(0xFF888888)));
        }
    }
}
```

### Jurist (Qazi ul Quzat / Dharmadhikari)
**Competence skills:** Charm + Steward

Applied via `HindostanSettlementProsperityModel` override:
```csharp
// In CalculateHearthChange for villages / loyalty for towns:
var owner = settlement.OwnerClan?.Kingdom;
if (owner != null)
{
    var council = CouncilBehavior.Instance?.GetCouncil(owner);
    float juristMult = council?.GetEffectMultiplier(CouncilPosition.Jurist) ?? 0f;
    if (juristMult > 0f)
        result.AddFactor(0.08f * juristMult, new TextObject("{=!}Jurist's Administration"));
}
```

---

## Player Interaction — Appointing Council Members

The player manages their council via a menu in their throne settlement (initial home settlement).

```csharp
// Add to HindostanSubModule.AddCustomMenuOptions():
starter.AddGameMenuOption(
    "town", "view_royal_council", "{=!}Convene the Royal Council",
    condition: args =>
    {
        bool isCapital = Settlement.CurrentSettlement?.StringId ==
                         Hero.MainHero?.Clan?.Kingdom?.InitialHomeFortSettlement?.StringId;
        args.IsEnabled = isCapital;
        args.Tooltip = isCapital
            ? new TextObject("{=!}Appoint council ministers")
            : new TextObject("{=!}Travel to your capital to convene the council");
        return isCapital;
    },
    consequence: args => OpenCouncilManagementDialog(),
    isLeave: false,
    index: 4
);

private void OpenCouncilManagementDialog()
{
    Kingdom kingdom = Hero.MainHero?.Clan?.Kingdom;
    if (kingdom == null) return;

    var council = CouncilBehavior.Instance.GetCouncil(kingdom);

    // Build a description of current council state
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("Current Council of Ministers:\n");

    foreach (CouncilPosition pos in Enum.GetValues(typeof(CouncilPosition))
        .Cast<CouncilPosition>().Where(p => p != CouncilPosition.None))
    {
        Hero member = council.GetMember(pos);
        string posName = pos switch
        {
            CouncilPosition.Vizier    => "Diwan-i-Ala",
            CouncilPosition.Marshal   => "Mir Bakshi",
            CouncilPosition.Treasurer => "Diwan-i-Kul",
            CouncilPosition.Spymaster => "Akhbarat Nawis",
            CouncilPosition.Jurist    => "Qazi ul Quzat",
            _                         => pos.ToString()
        };

        string memberName = member == null ? "[VACANT]" : member.Name.ToString();
        int competence    = member == null ? 0 : council.GetCompetence(pos);
        sb.AppendLine($"  {posName}: {memberName} (Competence: {competence})");
    }

    sb.AppendLine("\nAppointments can be changed via diplomacy with clan leaders.");

    InformationManager.ShowInquiry(new InquiryData(
        "Royal Council", sb.ToString(),
        isAffirmativeOptionShown: false,
        isNegativeOptionShown:    true,
        "", "Close",
        null, () => { }
    ));
}
```

**For a full appointment UI** (choosing a specific hero for a specific position), you would need to implement a multi-step inquiry chain, or a full Gauntlet UI screen. The multi-step inquiry approach:

1. Player clicks "Appoint minister"
2. Dialog: "Which position?" — 5 options (one per council role)
3. Player selects a position
4. Dialog: "Who to appoint?" — list of eligible lords with their competence score
5. Player confirms — `council.SetMember(position, chosenHero)`

---

## Council Votes on Kingdom Decisions

By hooking into `OnKingdomDecisionAdded`, council members' influence is pre-boosted before the vote resolves. This makes the Marshal's clan more powerful in war votes, the Vizier's clan more powerful in policy votes, etc. — without replacing Bannerlord's own decision resolution system.

This is the cleanest integration point because:
- It uses existing influence mechanics
- It doesn't need to override the decision resolution logic
- The player still sees normal voting UI
- The council's fingerprints are visible in the influence numbers

---

## AI Council Appointments

The weekly tick (`OnWeeklyTick`) auto-fills vacant positions for AI kingdoms using best-competence selection. For a more politically interesting system, the selection can incorporate relationship factors:

```csharp
private Hero SelectBestCandidateWithPolitics(KingdomCouncil council,
    CouncilPosition position)
{
    Kingdom kingdom = council.Kingdom;
    Hero ruler      = kingdom.Leader;

    return kingdom.Clans
        .SelectMany(c => c.Heroes)
        .Where(h => h.IsAlive && h.IsLord && !council.IsOnCouncil(h) && h != ruler)
        .OrderByDescending(h =>
        {
            int competence = GetCompetenceScore(h, position);

            // AI rulers prefer lords they like
            int relation = ruler != null
                ? CharacterRelationManager.GetHeroRelation(ruler, h)
                : 0;

            // Combine competence and relationship: rulers weight loyalty over skill
            return competence + (relation * 2);
        })
        .FirstOrDefault();
}
```

---

## Wiring It All Together

### In HindostanSubModule.cs

```csharp
protected override void OnGameStart(Game game, IGameStarter gameStarter)
{
    base.OnGameStart(game, gameStarter);
    if (!(game.GameType is Campaign)) return;

    var starter = (CampaignGameStarter)gameStarter;

    // Existing behaviors
    starter.AddBehavior(new HindostanStartingStateBehavior());
    starter.AddBehavior(new MonsoonSeasonBehavior());
    starter.AddBehavior(new MarathaChauthorBehavior());
    starter.AddBehavior(new HistoricalEventsBehavior());

    // Council system
    starter.AddBehavior(new CouncilBehavior());

    // Models that read council state
    starter.AddModel(new HindostanPartySizeModel());    // reads Marshal
    // starter.AddModel(new HindostanClanFinanceModel());  // reads Treasurer
    // starter.AddModel(new HindostanSettlementProsperityModel()); // reads Jurist
}
```

### Accessing the council from any model

```csharp
// Pattern: CouncilBehavior is a singleton set in RegisterEvents()
var council = CouncilBehavior.Instance?.GetCouncil(kingdom);
if (council == null) return; // CouncilBehavior not yet loaded (custom battle, etc.)
float mult = council.GetEffectMultiplier(CouncilPosition.Marshal);
```

---

## What This System Does NOT Attempt

These would be Phase 2 features, each requiring significant additional work:

| Feature | Complexity | Notes |
|---|---|---|
| Council member ambitions / plots | High | Needs a separate relationship tracking system |
| Council members demanding gifts/titles | Medium | Yearly tick with relation thresholds |
| Hereditary positions | Medium | Save which positions are hereditary per clan |
| Council veto on ruler decisions | High | Requires modifying KingdomDecision resolution via Harmony |
| Full custom council UI screen | Very High | Requires Gauntlet XAML + ViewModel work |
| Council members leading armies as dedicated commanders | Medium | Extend the Army system to prefer the Marshal as commander |

---

**[← Home](Home.md)** | **[Quick Reference →](Quick-Reference.md)**
