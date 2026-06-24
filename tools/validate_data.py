#!/usr/bin/env python3
# Takht ya Taboot — data validator.
#
# Catches the class of bug that has crashed the game before: a mod XML referencing an id the
# engine can't resolve (a troop pointing at a missing item, a culture pointing at a missing
# troop, a hero in a non-existent clan, a kingdom ruled by a clan that isn't defined). Native
# crashes from these never reach a managed catch, so we catch them HERE, before launch.
#
# Usage (from module root):   python tools/validate_data.py
# Exit code 0 = clean, 1 = problems found (so it can gate a build / be a test step).
import os, re, glob, sys

HERE = os.path.dirname(os.path.abspath(__file__))
MODULE = os.path.normpath(os.path.join(HERE, ".."))
GAMEROOT = os.path.normpath(os.path.join(MODULE, "..", ".."))   # ...\Mount & Blade II Bannerlord
MODULES = os.path.join(GAMEROOT, "Modules")
MOD_DATA = os.path.join(MODULE, "ModuleData")

errors, warnings = [], []
def err(msg): errors.append(msg)
def warn(msg): warnings.append(msg)

def read(path):
    try:
        with open(path, encoding="utf-8", errors="ignore") as f: return f.read()
    except OSError: return ""

def vanilla_data_files(*names):
    """All ModuleData files matching any of names across every installed module (vanilla + libs)."""
    out = []
    for n in names:
        out += glob.glob(os.path.join(MODULES, "*", "ModuleData", n))
        out += glob.glob(os.path.join(MODULES, "*", "ModuleData", "**", n), recursive=True)
    return sorted(set(out))

# ── id universes ────────────────────────────────────────────────────────────────────────────
def collect(pattern, files):
    s = set()
    for f in files:
        s |= set(re.findall(pattern, read(f)))
    return s

ITEM_IDS = collect(r'<(?:CraftedItem|Item)\b[^>]*?\bid="([^"]+)"',
                   vanilla_data_files("*.xml") if False else
                   glob.glob(os.path.join(MODULES, "SandBoxCore", "ModuleData", "items", "*.xml")) +
                   glob.glob(os.path.join(MODULES, "Native", "ModuleData", "items", "*.xml")) +
                   [os.path.join(MOD_DATA, "items.xml")])

# Every NPCCharacter id the engine will know. These live across SEVERAL vanilla files — not just
# spnpccharacters.xml but also the templates / special / generic character files (e.g. the "guard"
# troop that neutral_culture uses is defined in spnpccharactertemplates.xml). Scan them all so a
# valid culture/troop pointer isn't flagged as dangling.
NPC_FILES = vanilla_data_files("*.xml") + [os.path.join(MOD_DATA, "spnpccharacters.xml"),
                                           os.path.join(MOD_DATA, "tyt_troops.xml"),
                                           os.path.join(MOD_DATA, "tyt_emperors.xml")]
NPC_IDS = collect(r'<NPCCharacter\s+id="([^"]+)"', NPC_FILES)
VANILLA_NPC_IDS = collect(r'<NPCCharacter\s+id="([^"]+)"',
                          [os.path.join(MODULES, "SandBoxCore", "ModuleData", "spnpccharacters.xml")])

# Clan/faction ids: vanilla spclans across modules + the mod's tyt_spclans.
CLAN_FILES = vanilla_data_files("spclans.xml", "tyt_spclans.xml")
CLAN_IDS = collect(r'<Faction\b[^>]*?\bid="([^"]+)"', CLAN_FILES) | collect(r'<Clan\b[^>]*?\bid="([^"]+)"', CLAN_FILES)

CULTURE_FILES = vanilla_data_files("spcultures.xml", "tyt_spcultures.xml")
CULTURE_IDS = collect(r'<Culture\b[^>]*?\bid="([^"]+)"', CULTURE_FILES)

CIV_TEMPLATE_TXT = "".join(read(f) for f in vanilla_data_files("*.xml"))

# Every EquipmentSet id and BodyProperty face-template id the engine knows (for emperor templates).
EQUIP_SET_IDS = collect(r'<EquipmentSet\b[^>]*?\bid="([^"]+)"', vanilla_data_files("*.xml"))
BODYPROP_IDS  = collect(r'<BodyProperty\b[^>]*?\bid="([^"]+)"',
                        [os.path.join(MODULES, "SandBoxCore", "ModuleData", "sandboxcore_bodyproperties.xml"),
                         os.path.join(MODULES, "SandBox", "ModuleData", "sandbox_bodyproperties.xml")])
# Body-property templates meant for crowds/commoners — using one as a HERO face native-crashes
# facegen at world-gen (this bit us with townswoman_empire). Heroes need a baked key or a fighter_*.
BAD_HERO_FACE = ("townsman", "townswoman", "beggar", "looter", "villager")

# ── checks ──────────────────────────────────────────────────────────────────────────────────
def check_emperor_templates():
    path = os.path.join(MOD_DATA, "tyt_emperors.xml")
    txt = read(path)
    if not txt: return
    n = 0
    for b in re.split(r'(?=<NPCCharacter\b)', txt)[1:]:
        m = re.search(r'id="([^"]+)"', b)
        if not m: continue
        cid = m.group(1); n += 1
        for es in re.findall(r'<EquipmentSet\s+id="([^"]+)"', b):
            if es not in EQUIP_SET_IDS:
                err(f"emperor[{cid}]: EquipmentSet '{es}' is not a defined equipment set")
        if 'civilian="true"' not in b:
            err(f"emperor[{cid}]: no civilian EquipmentSet (world-gen crash risk)")
        ft = re.search(r'face_key_template value="BodyProperty\.([^"]+)"', b)
        if ft:
            if ft.group(1) not in BODYPROP_IDS:
                err(f"emperor[{cid}]: face_key_template 'BodyProperty.{ft.group(1)}' is not defined")
            if any(bad in ft.group(1) for bad in BAD_HERO_FACE):
                err(f"emperor[{cid}]: face template '{ft.group(1)}' is a commoner/crowd body property — "
                    f"unsafe as a hero face (native facegen crash); use a baked key or fighter_*")
        if '<BodyProperties' in b and 'face_mesh_cache="true"' not in b:
            warn(f"emperor[{cid}]: has a baked face key but no face_mesh_cache=\"true\" (face may not apply)")
    print(f"  emperor templates: {n} checked")

def check_troops():
    path = os.path.join(MOD_DATA, "tyt_troops.xml")
    txt = read(path)
    if not txt: warn("tyt_troops.xml missing"); return
    blocks = re.split(r'(?=<NPCCharacter\b)', txt)
    seen = set()
    for b in blocks:
        m = re.search(r'<NPCCharacter\s+id="([^"]+)"', b)
        if not m: continue
        tid = m.group(1)
        if tid in seen: err(f"troops: duplicate override id '{tid}'")
        seen.add(tid)
        if tid not in VANILLA_NPC_IDS:
            err(f"troops: override id '{tid}' does not exist in vanilla (won't reskin anything)")
        for it in re.findall(r'id="Item\.([^"]+)"', b):
            if it not in ITEM_IDS: err(f"troops[{tid}]: item '{it}' is not a defined Item id")
        for cv in re.findall(r'<EquipmentSet id="([^"]+)" civilian', b):
            if f'id="{cv}"' not in CIV_TEMPLATE_TXT: err(f"troops[{tid}]: civilian template '{cv}' is not defined")
        slots = set(re.findall(r'slot="([^"]+)"', b))
        if "Item0" not in slots: err(f"troops[{tid}]: has no Item0 (weapon) -> unarmed")
        if "Body"  not in slots: warn(f"troops[{tid}]: has no Body armour")
        if not re.search(r'civilian="true"', b): err(f"troops[{tid}]: has no civilian EquipmentSet (world-gen crash risk)")
    print(f"  troops: {len(seen)} overrides checked")

def check_culture_pointers():
    txt = read(os.path.join(MOD_DATA, "tyt_spcultures.xml"))
    n = 0
    for m in re.finditer(r'(basic_troop|elite_basic_troop)="NPCCharacter\.([^"]+)"', txt):
        n += 1
        if m.group(2) not in NPC_IDS:
            err(f"culture: {m.group(1)} points at NPCCharacter.{m.group(2)} which is not defined")
    print(f"  culture troop pointers: {n} checked")

def check_clans():
    txt = read(os.path.join(MOD_DATA, "tyt_spclans.xml"))
    n = 0
    for m in re.finditer(r'<Faction\b[^>]*?\bculture="Culture\.([^"]+)"', txt):
        n += 1
        if m.group(1) not in CULTURE_IDS:
            err(f"clan: culture 'Culture.{m.group(1)}' is not defined")
    print(f"  clan cultures: {n} checked")

def check_kingdoms():
    txt = read(os.path.join(MOD_DATA, "spkingdoms.xml"))
    n = 0
    for m in re.finditer(r'<Kingdom\b([^>]*)>', txt):
        attrs = m.group(1); n += 1
        ow = re.search(r'owner="Faction\.([^"]+)"', attrs) or re.search(r'ruling_clan="Faction\.([^"]+)"', attrs)
        cu = re.search(r'culture="Culture\.([^"]+)"', attrs)
        if ow and ow.group(1) not in CLAN_IDS: err(f"kingdom: owner/ruling clan 'Faction.{ow.group(1)}' not defined")
        if cu and cu.group(1) not in CULTURE_IDS: err(f"kingdom: culture 'Culture.{cu.group(1)}' not defined")
    print(f"  kingdoms: {n} checked")

def check_heroes():
    txt = read(os.path.join(MOD_DATA, "heroes.xml"))
    id_list = re.findall(r'<Hero\s+id="([^"]+)"', txt)
    hero_ids = set(id_list)
    for hid in hero_ids:
        if id_list.count(hid) > 1:
            err(f"hero[{hid}]: duplicate id in heroes.xml — the engine rejects this as a duplicate "
                f"key ('Hero_unique_attribute') and crashes; merge the entries into one")
    n = 0
    for m in re.finditer(r'<Hero\b([^>]*?)/?>', txt) :
        a = m.group(1); n += 1
        hid = re.search(r'\bid="([^"]+)"', a)
        hid = hid.group(1) if hid else "?"
        # The Heroes node is OVERRIDE-only: culture/skill_template here are rejected by the XSD and
        # the hero is never created ("Null object reference" -> crash before character creation).
        # Those belong in an NPCCharacters-node template (tyt_emperors.xml / lords.xml).
        for bad in ("culture", "skill_template", "occupation", "default_group", "is_hero",
                    "is_female", "age", "voice"):
            if re.search(r'\b' + bad + r'=', a):
                err(f"hero[{hid}]: attribute '{bad}' is illegal in heroes.xml (Heroes schema) — "
                    f"move it to the NPCCharacter template; the hero will fail to instantiate")
        # Every hero id must have a backing NPCCharacter template, or the engine logs
        # "Null object reference found with ID: {id}" and crashes during world-gen.
        if hid != "?" and hid not in NPC_IDS:
            err(f"hero[{hid}]: no NPCCharacter template exists for this id "
                f"(add one under the NPCCharacters node, e.g. tyt_emperors.xml)")
        fac = re.search(r'faction="Faction\.([^"]+)"', a)
        if fac and fac.group(1) not in CLAN_IDS:
            err(f"hero: faction 'Faction.{fac.group(1)}' is not a defined clan")
        for rel in ("father", "mother", "spouse"):
            r = re.search(rel + r'="Hero\.([^"]+)"', a)
            if r and r.group(1) not in hero_ids and not re.match(r'lord_[0-9A-Za-z_]+$', r.group(1)):
                warn(f"hero: {rel} 'Hero.{r.group(1)}' not found in heroes.xml and not a lord_* id")
    print(f"  heroes: {n} checked")

def main():
    print("Validating Takht ya Taboot data...")
    print(f"  universe: {len(ITEM_IDS)} items, {len(NPC_IDS)} NPCs, {len(CLAN_IDS)} clans, {len(CULTURE_IDS)} cultures")
    check_troops(); check_culture_pointers(); check_clans(); check_kingdoms(); check_heroes(); check_emperor_templates()
    print()
    for w in warnings: print("WARN :", w)
    for e in errors:   print("ERROR:", e)
    print(f"\n{len(errors)} error(s), {len(warnings)} warning(s).")
    sys.exit(1 if errors else 0)

if __name__ == "__main__":
    main()
