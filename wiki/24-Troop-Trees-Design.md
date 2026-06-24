# Chapter 24 — Troop Trees Design (proposal)

> A ground-up, historically grounded troop overhaul for the six culture slots, built
> from **vanilla equipment only** — no firearms, no elephants (not yet modelled).
> **Status: design for approval.** No troop XML is written until this is signed off.

---

## Why a rewrite

Today the mod keeps the **vanilla troop trees unchanged** and only renames them through
`hindostan_string_map.xml` (so an "Imperial Legionary" becomes a Hindostani name but still
fights, equips, and upgrades exactly like a Byzantine legionary). That is the "lazy
translation" problem. This proposal replaces each culture's tree with a new one whose
**structure, roles, names, and equipment** reflect the real army it represents.

## Conventions (all cultures)

- **Two lines per culture**, as vanilla expects:
  - **Basic line** (`basic_troop`): the levied/common path — recruit → T5.
  - **Noble/elite line** (`elite_basic_troop`): the aristocratic/professional path — T1 noble → T5–T6.
- **Tiers T1–T5** (basic) / **T1→T6** (elite), branching once at **T3** into two role specialisations.
- **No firearms / no elephants.** Where a culture historically relied on the matchlock, that
  slot becomes a **bow / composite-bow** unit instead, noted per tree.
- **Equipment = vanilla items only**, chosen for an Indian silhouette. Primary item pools:
  - **Indo-Muslim look** (Mughal, Bengal, Hyderabad, Durrani, Sikh cavalry): `aserai_*` robes &
    mail + `khuzait_*` lamellar/turban-helms + scimitars/`*_sword`/spears, composite bows.
  - **Rajput / heavy native cavalry**: `vlandia_*` mail & helms + `empire_*` for lamellar,
    lances, kite/round shields.
  - **South Indian (Mysore) & light infantry**: lighter `aserai_*`/`battania_*` cloth, round
    shields, javelins, talwar-analogue one-handers.
- **Roles** map to vanilla formation classes: Infantry (shield), Archer (foot bow/javelin),
  Cavalry (lance/shock), Horse Archer/Light Cavalry (skirmish horse).

> Equipment names below are *categories* ("mail hauberk", "lamellar", "composite bow"); the
> exact `Item.id` per slot is filled in at implementation, drawn from the pools above.

---

## 1. Mughal / Hindustani — culture `empire` (Mughal, Bengal, Hyderabad)

The mansabdari host: a cavalry-aristocratic army over a mass of levied foot, foreign in
neither Persian armour nor Hindustani archery.

**Basic line — the paik (foot)**
| Tier | Name | Role | Equipment sketch |
|---|---|---|---|
| 1 | Paik | levy footman | tunic, spear, dagger |
| 2 | Sipahi | infantry | quilted coat, talwar + dhal (round shield) |
| 3a | Dhali | shield infantry | mail vest, talwar, large shield |
| 3b | Tirandaz | foot archer | cloth, composite bow, dagger *(was matchlock)* |
| 4a | Bani-Sipahi | veteran infantry | mail, talwar + shield, javelins |
| 4b | Bargandaz-i-Kaman | veteran archer | mail vest, strong composite bow, side-sword |
| 5a | Dakhili Veteran | elite infantry | lamellar over mail, fine talwar, steel shield |
| 5b | Master Tirandaz | elite archer | mail, war bow, talwar |

**Noble line — the sowar (horse)**
| Tier | Name | Role | Equipment sketch |
|---|---|---|---|
| 1 | Ahadi | gentleman trooper | mail, lance, talwar, horse |
| 2 | Sowar | cavalry | mail + helm, lance, round shield, armoured horse |
| 3a | Barq-Sowar | shock lancer | mail hauberk, heavy lance, talwar, barded horse |
| 3b | Turki Sowar | horse archer | lamellar, composite bow, mace, fast horse |
| 4 | Khasa Sowar | heavy cavalry | reinforced mail, lance + mace, fine shield |
| 5 | Walashahi | imperial guard cavalry | full mail + plate, lance, fine talwar, mace |
| 6 | Ahadi Khan | champion | best mail/plate, lance, jewelled talwar, mace, barded charger |

---

## 2. Maratha — culture `battania`

Speed over weight. Hardy Mavala hill infantry and the famous light horse (*ganimi kava*,
hit-and-run). Minimal armour, maximum mobility.

**Basic line — Mavala foot**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Gawli | levy | tunic, spear |
| 2 | Mavala | hill infantry | light cloth, talwar + small shield |
| 3a | Mavala Veteran | infantry | leather/quilt, talwar + shield, javelins |
| 3b | Kamathi | foot archer/skirmisher | cloth, bow, javelins |
| 4 | Hetkari | marksman infantry | leather, strong bow + sword |
| 5 | Mavala Sardar | elite light infantry | mail vest, fine talwar, shield, javelins |

**Noble line — the bargir/silahdar horse**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Shiledar | retainer horse | light cloth, spear, talwar, fast horse |
| 2 | Bargir | state cavalry | quilted coat, lance, talwar, light horse |
| 3a | Silahdar | light lancer | leather/mail vest, lance, talwar, shield |
| 3b | Pendhari | raider horse archer | light cloth, bow, sword, very fast horse |
| 4 | Huzurat | Peshwa's cavalry | mail vest, lance + talwar, shield |
| 5 | Sardar | noble commander | mail, lance, fine talwar, mace |
| 6 | Maratha Senapati | elite | mail + helm, lance, jewelled talwar, fast barded horse |

---

## 3. Rajput — culture `vlandia`

Heavy, honour-bound shock cavalry and stubborn warrior-infantry. The most armoured native
host: mail and the *khanda* straight-sword.

**Basic line — pyada foot**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Pyada | levy | tunic, spear |
| 2 | Sipahi | infantry | quilt + mail vest, khanda + shield |
| 3a | Khandait | swordsman | mail, khanda, large shield |
| 3b | Dhanuk | archer | cloth/leather, longbow-analogue, sword |
| 4 | Rajput Bhala | heavy infantry/pikeman | mail hauberk, long spear, shield |
| 5 | Rajput Veteran | elite infantry | mail + helm, fine khanda, steel shield |

**Noble line — the thakur horse**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Bhomia | landed retainer | mail vest, lance, khanda, horse |
| 2 | Rajput Sowar | cavalry | mail + helm, lance, khanda, shield, armoured horse |
| 3a | Bhala-bardar | heavy lancer | mail hauberk, heavy lance, barded horse |
| 3b | Sirdar Sowar | cavalry archer/shock | mail, bow + khanda, shield |
| 4 | Thakur | noble heavy cavalry | reinforced mail + plate, lance + mace, fine shield |
| 5 | Rawat | champion cavalry | mail+plate, heavy lance, jewelled khanda |
| 6 | Rajkumar | royal knight | best plate/mail, lance, mace, jewelled khanda, heavy charger |

---

## 4. Mysore — culture `aserai`

Pre-modernisation South Indian host (firearms deliberately excluded): cotton-armoured
infantry, the *jettige* wrestler-soldiers, sword-and-buckler men, light hired horse.

**Basic line — Nayaka foot (infantry-heavy)**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Bedar | levy | loincloth/tunic, spear |
| 2 | Peon | infantry | cotton coat, talwar + buckler |
| 3a | Asadar | swordsman | cotton armour, talwar + shield |
| 3b | Billidar | archer/skirmisher | cloth, bow, javelins |
| 4a | Jettige | shock infantry | cotton armour, two-hand sword/large blade |
| 4b | Nayaka Marksman | archer | leather, strong bow, sword |
| 5 | Nayaka Veteran | elite infantry | reinforced cotton/mail vest, fine talwar, shield |

**Noble line — Mysore horse (smaller, supporting)**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Silledar | hired trooper | cloth, spear, talwar, horse |
| 2 | Mysore Sowar | cavalry | quilt/mail vest, lance, talwar, shield |
| 3 | Sardar Sowar | lancer | mail vest, lance + talwar, shield, light horse |
| 4 | Mysore Lancer | heavy cavalry | mail, heavy lance, talwar, shield |
| 5 | Dalavayi's Horse | elite cavalry | mail + helm, lance, fine talwar, mace |

---

## 5. Durrani Afghan — culture `sturgia`

Hardy Pashtun hill infantry and superb cavalry; a Perso-Afghan look. The jezail-musketeer
slot becomes the foot bowman.

**Basic line — lashkar foot**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Lashkari | tribal levy | robe, knife, spear |
| 2 | Ghazi | infantry | robe + leather, sword + shield |
| 3a | Khasadar | infantry | mail vest, sword, large shield |
| 3b | Kamandar | foot archer | robe/leather, composite bow, sword *(was jezail)* |
| 4 | Ghazi Veteran | shock infantry | mail, sword + shield, javelins |
| 5 | Khan's Footguard | elite infantry | mail + helm, fine sword, steel shield |

**Noble line — Durrani horse**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Suwar | retainer horse | robe + mail vest, lance, sword, horse |
| 2 | Durrani Sowar | cavalry | mail, lance, sword, shield, fast horse |
| 3a | Qizilbash | heavy lancer | mail hauberk + helm, heavy lance, sabre, barded horse |
| 3b | Durrani Horse Archer | horse archer | lamellar/mail vest, composite bow, sabre |
| 4 | Sardar's Horse | heavy cavalry | reinforced mail, lance + mace, shield |
| 5 | Durrani Sardar | elite | mail + plate, lance, fine sabre, mace |
| 6 | Abdali Champion | champion | best mail/plate, lance, jewelled sabre, barded charger |

---

## 6. Sikh Khalsa — culture `khuzait`

A cavalry confederation (the *ghorchara* horse), stiffened by the fanatic *Akali Nihang*
warriors. Khuzait's steppe armour pool gives the right medium-cavalry feel.

**Basic line — Khalsa foot**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Jatha | levy | tunic + turban, spear |
| 2 | Singh | infantry | quilt, talwar + shield |
| 3a | Khalsa Footman | infantry | mail vest, talwar, large shield |
| 3b | Tirandaz | foot archer | cloth, composite bow, talwar |
| 4 | Nihang | zealot shock infantry | blue robe + mail, talwar + chakram (throwing), shield |
| 5 | Akali Nihang | elite infantry | mail + dastar-bunga helm, fine talwar, chakram, shield |

**Noble line — the ghorchara horse**
| Tier | Name | Role | Equipment |
|---|---|---|---|
| 1 | Ghorchara | horseman | quilt + mail vest, lance, talwar, fast horse |
| 2 | Khalsa Sowar | cavalry | mail, lance, talwar, shield, horse |
| 3a | Ghorchara Lancer | shock lancer | mail hauberk, heavy lance, talwar, barded horse |
| 3b | Khalsa Horse Archer | horse archer | lamellar, composite bow, talwar, fast horse |
| 4 | Misldar's Horse | heavy cavalry | reinforced mail + helm, lance + mace, shield |
| 5 | Sardar | noble cavalry | mail + plate, lance, fine talwar, mace |
| 6 | Misldar | champion | best mail/plate, lance, jewelled talwar, mace, barded charger |

---

## Implementation plan (after approval)

1. **Define troops** as new `<NPCCharacter>` entries (a dedicated `tyt_troops.xml`, registered
   like the other XML) with `is_basic_troop="true"`, culture, `level`, formation `class`,
   skills, and `upgrade_targets` wiring the trees above.
2. **Equipment rosters** per troop via `<Equipments><EquipmentRoster>` referencing the chosen
   vanilla `Item.id`s (with 1–2 alternates per slot for variety).
   - **MANDATORY: a civilian set too.** Every soldier `<Equipments>` must end with a
     `<EquipmentSet id="<palette>_troop_civilian_template_t{1..3}" civilian="true" />`. A troop
     with only a battle roster and no civilian equipment **native-crashes at world generation**
     (the engine dresses a culture's `basic_troop` in civilian contexts — town walkers, the
     character-creation culture preview). This was the 2026-06-20 new-game crash. `gen_troops.py`
     emits it automatically (`civilian_line`), picking the tier from the troop's level.
3. **Point each culture** at its new T1 troops: set `basic_troop` / `elite_basic_troop` in
   `tyt_spcultures.xml`.
4. **Retire the troop renames** in `hindostan_string_map.xml` (the new troops carry their own
   names), keeping faction/title remaps.
5. **Balance pass**: tier costs, upgrade XP, skill curves matched to vanilla equivalents so the
   economy and battle power stay sane.
6. **Verify in-game** (recruit from each culture's villages; check upgrade paths, gear, and that
   no troop pulls a firearm/mount we didn't intend).

### Open questions for you
- **Tier depth**: keep elite lines at **T6** (one above basic) as drafted, or cap everything at T5?
- **Bengal & Hyderabad** share the `empire` tree above — happy with that, or do you want them
  visually distinct later (separate culture slots)?
- **Mysore cavalry**: historically weak — fine to keep their noble line short (T5, no champion)?
