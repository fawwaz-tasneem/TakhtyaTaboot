# Chapter 8 — Working With Game Objects

> Heroes, clans, kingdoms, settlements, and mobile parties — all the properties and actions you'll use.

**[← Chapter 7](07-Game-Menus-and-Dialogues.md)** | **[Home](Home.md)** | **[Next: Save and Load →](09-Save-Load.md)**

---

## Contents

- [Heroes](#heroes)
- [Clans](#clans)
- [Kingdoms](#kingdoms)
- [Settlements](#settlements)
- [Mobile parties](#mobile-parties)
- [Common actions reference](#common-actions-reference)

---

## Heroes

```csharp
// Find a hero
Hero hero = Hero.FindFirst(h => h.StringId == "lord_1_1");  // Muhammad Shah
Hero player = Hero.MainHero;

// ── State ──────────────────────────────────────────────────────────────────
bool alive       = hero.IsAlive;
bool isPlayer    = hero == Hero.MainHero;
int  age         = (int)hero.Age;
int  gold        = hero.Gold;
float renown     = hero.Clan?.Renown ?? 0f;

// ── Allegiance ─────────────────────────────────────────────────────────────
Clan    clan    = hero.Clan;
Kingdom kingdom = hero.Clan?.Kingdom;
bool isMughal   = kingdom?.StringId == "empire";

// ── Location ───────────────────────────────────────────────────────────────
Settlement home        = hero.HomeSettlement;
MobileParty party      = hero.PartyBelongedTo;
Settlement currentTown = hero.CurrentSettlement;  // null if on the map
bool inTown            = hero.CurrentSettlement != null;

// ── Skills ─────────────────────────────────────────────────────────────────
int tactics     = hero.GetSkillValue(DefaultSkills.Tactics);
int leadership  = hero.GetSkillValue(DefaultSkills.Leadership);
int engineering = hero.GetSkillValue(DefaultSkills.Engineering);

// ── Modifying hero state ────────────────────────────────────────────────────
hero.Gold += 10_000;
hero.SetName(new TextObject("{=!}Nizam ul Mulk"), new TextObject("{=!}Nizam ul Mulk"));

// ── Actions ────────────────────────────────────────────────────────────────
KillCharacterAction.ApplyByMurder(hero, killer: null, showHPBar: false);
TakePrisonerAction.Apply(capturingParty, prisonerHero: hero);
EndCaptivityAction.ApplyByReleasedByPlayer(hero);
```

---

## Clans

```csharp
// Find a clan
Clan clan = Clan.FindFirst(c => c.StringId == "clan_empire_1");

// ── Composition ─────────────────────────────────────────────────────────────
IReadOnlyList<Hero> heroes = clan.Heroes;
Hero leader               = clan.Leader;
Hero heir                 = clan.Heirs.FirstOrDefault();

// ── Wealth ──────────────────────────────────────────────────────────────────
int gold     = clan.Gold;
clan.Gold    = 100_000;
clan.Gold   += 50_000;
clan.Gold   -= Math.Min(20_000, clan.Gold);  // never go below 0

// ── Status ──────────────────────────────────────────────────────────────────
Kingdom kingdom  = clan.Kingdom;
bool hasKingdom  = !clan.IsMinorFaction && clan.Kingdom != null;
float influence  = clan.Influence;
clan.Influence  += 50f;
float renown     = clan.Renown;
int tier         = clan.Tier;  // 1–6

// ── Actions ─────────────────────────────────────────────────────────────────
ChangeKingdomAction.ApplyByJoinToKingdom(clan, targetKingdom, showNotification: false);
ChangeKingdomAction.ApplyByLeaveKingdom(clan, showNotification: false);
```

---

## Kingdoms

```csharp
// Find a kingdom
Kingdom mughals = Kingdom.All.FirstOrDefault(k => k.StringId == "empire");
// or
Kingdom mughals2 = MBObjectManager.Instance.GetObject<Kingdom>("empire");

// ── Composition ─────────────────────────────────────────────────────────────
IReadOnlyList<Clan>       clans       = mughals.Clans;
Clan                       rulingClan  = mughals.RulingClan;
Hero                       ruler       = mughals.Leader;
IReadOnlyList<Settlement>  settlements = mughals.Settlements;

// ── Military ────────────────────────────────────────────────────────────────
float strength  = mughals.TotalStrength;  // sum of all party strengths
int armyCount   = mughals.Armies.Count;

// ── Status ──────────────────────────────────────────────────────────────────
bool isEliminated = mughals.IsEliminated;  // no settlements left

// ── Diplomacy ───────────────────────────────────────────────────────────────
bool atWar         = mughals.IsAtWarWith(otherKingdom);
var enemies        = Kingdom.All.Where(k => mughals.IsAtWarWith(k)).ToList();

// ── Actions ─────────────────────────────────────────────────────────────────
DeclareWarAction.Apply(mughals, enemy, DeclareWarAction.DeclareWarDetail.Default);
MakePeaceAction.Apply(mughals, enemy);
ChangeRulingClanAction.Apply(mughals, newRulingClan);
```

---

## Settlements

```csharp
// Find a settlement
Settlement delhi = Settlement.Find("town_EN2");
Settlement town  = Settlement.All.FirstOrDefault(s => s.StringId == "town_EN2");

// ── Type checking ────────────────────────────────────────────────────────────
bool isTown    = delhi.IsTown;
bool isCastle  = delhi.IsCastle;
bool isVillage = delhi.IsVillage;

// ── Town/castle properties ───────────────────────────────────────────────────
Town townComp    = delhi.Town;       // null if not a town or castle
float prosperity = townComp.Prosperity;
float security   = townComp.Security;      // 0–100
int garrison     = townComp.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;

// Modify
townComp.Prosperity += 200f;
townComp.Prosperity  = MathF.Clamp(townComp.Prosperity, 0f, 5000f);
townComp.Security    = Math.Clamp(townComp.Security, 0f, 100f);

// ── Village properties ───────────────────────────────────────────────────────
Village village = someVillageSettlement.Village;
float hearth    = village.Hearth;  // population proxy
village.Hearth  = Math.Min(village.Hearth + 30f, 2000f);

// ── Culture (STAYS with the settlement even if the owner changes) ─────────────
CultureObject culture = delhi.Culture;
string cultureId      = culture.StringId;  // "empire", "battania", etc.

// ── Owner ────────────────────────────────────────────────────────────────────
Hero   owner      = delhi.OwnerClan?.Leader;
Clan   ownerClan  = delhi.OwnerClan;
Kingdom ownerKing = delhi.OwnerClan?.Kingdom;

// ── Villages attached to a town ───────────────────────────────────────────────
IReadOnlyList<Village> villages = delhi.Town?.Villages;
```

---

## Mobile Parties

```csharp
MobileParty playerParty = MobileParty.MainParty;
MobileParty heroParty   = someHero.PartyBelongedTo;

// ── Troop roster ─────────────────────────────────────────────────────────────
TroopRoster roster   = playerParty.MemberRoster;
int totalTroops      = roster.TotalManCount;
int totalRegulars    = roster.TotalRegulars;

// Add troops
CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cavalry");
roster.AddToCounts(troop, count: 50);

// ── Morale ───────────────────────────────────────────────────────────────────
float morale = playerParty.Morale;
playerParty.RecentEventsMorale += 10f;

// ── Position ─────────────────────────────────────────────────────────────────
Vec2 pos = playerParty.Position2D;

// ── Speed (read from the active model) ───────────────────────────────────────
float speed = Campaign.Current.Models.PartySpeedCalculatingModel
    .CalculateBaseSpeed(playerParty).ResultNumber;

// ── Leader ────────────────────────────────────────────────────────────────────
Hero leader = playerParty.LeaderHero;
```

---

## Common Actions Reference

```csharp
// War and peace
DeclareWarAction.Apply(k1, k2, DeclareWarAction.DeclareWarDetail.Default)
MakePeaceAction.Apply(k1, k2)

// Gold
GiveGoldAction.ApplyBetweenCharacters(giverHero, receiverHero, amount, disableNotification: false)
// Or directly:
clan.Gold += amount;

// Heroes
KillCharacterAction.ApplyByMurder(hero, killer: null, showHPBar: false)
TakePrisonerAction.Apply(capturingParty, prisonerHero)
EndCaptivityAction.ApplyByReleasedByPlayer(hero)

// Clans/kingdoms
ChangeKingdomAction.ApplyByJoinToKingdom(clan, kingdom, showNotification: false)
ChangeKingdomAction.ApplyByLeaveKingdom(clan, showNotification: false)
ChangeRulingClanAction.Apply(kingdom, newRulingClan)

// Relations (affect NPC AI decisions)
ChangeRelationAction.ApplyPlayerRelation(hero, relationChange: 10)
ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero1, hero2, relationChange: -20)
```

---

**[← Chapter 7](07-Game-Menus-and-Dialogues.md)** | **[Home](Home.md)** | **[Next: Save and Load →](09-Save-Load.md)**
