"""
Swap EW (empire_w/Bengal) and ES (empire_s/Hyderabad) settlement names+descriptions.

Current bug: EW towns have Deccan names (Warangal, Hyderabad, Aurangabad...)
             ES towns have Bengali names (Murshidabad, Dacca, Patna...)

Fix: Give EW towns Bengali names+descriptions, ES towns Deccan names+descriptions.
"""

import re, sys

XML_PATH = r'C:\Users\tasne\Desktop\TakhtyaTaboot\ModuleData\settlements.xml'

with open(XML_PATH, encoding='utf-8') as f:
    content = f.read()

# ─── helpers ─────────────────────────────────────────────────────────────────

def get_attr(content, settlement_id, attr):
    """Extract attribute value from settlement entry by id."""
    idx = content.find(f'id="{settlement_id}"')
    if idx < 0:
        return None
    tag_start = content.rfind('<', 0, idx)
    # find end of opening tag
    i, in_q, q = tag_start, False, None
    while i < len(content):
        c = content[i]
        if in_q:
            if c == q: in_q = False
        else:
            if c in ('"', "'"): in_q, q = True, c
            elif c == '>': break
        i += 1
    tag = content[tag_start:i+1]
    m = re.search(rf'{attr}="\{{=!\}}([^"]*)"', tag)
    return m.group(1) if m else None

def set_settlement(content, settlement_id, new_name, new_text):
    """Replace name and text attributes of a settlement."""
    idx = content.find(f'id="{settlement_id}"')
    if idx < 0:
        print(f'  WARNING: {settlement_id} not found', file=sys.stderr)
        return content
    tag_start = content.rfind('<', 0, idx)
    i, in_q, q = tag_start, False, None
    while i < len(content):
        c = content[i]
        if in_q:
            if c == q: in_q = False
        else:
            if c in ('"', "'"): in_q, q = True, c
            elif c == '>': break
        i += 1
    tag = content[tag_start:i+1]
    # escape replacement for re.sub (avoid \1 etc.)
    safe_name = new_name.replace('\\', r'\\')
    safe_text = new_text.replace('\\', r'\\')
    new_tag = re.sub(r'name="\{=!\}[^"]*"',
                     lambda m, n=safe_name: f'name="{{=!}}{n}"', tag, count=1)
    new_tag = re.sub(r'text="\{=!\}[^"]*"',
                     lambda m, t=safe_text: f'text="{{=!}}{t}"', new_tag, count=1)
    return content[:tag_start] + new_tag + content[i+1:]

# ─── 1. collect current texts ─────────────────────────────────────────────────

print("Reading current names+texts...")

# Towns
ew_town = {f'EW{i}': (get_attr(content, f'town_EW{i}', 'name'),
                       get_attr(content, f'town_EW{i}', 'text'))
           for i in range(1, 7)}
es_town = {f'ES{i}': (get_attr(content, f'town_ES{i}', 'name'),
                       get_attr(content, f'town_ES{i}', 'text'))
           for i in range(1, 8)}

# Castles
ew_castle = {f'EW{i}': (get_attr(content, f'castle_EW{i}', 'name'),
                         get_attr(content, f'castle_EW{i}', 'text'))
             for i in range(1, 9)}
es_castle = {f'ES{i}': (get_attr(content, f'castle_ES{i}', 'name'),
                         get_attr(content, f'castle_ES{i}', 'text'))
             for i in range(1, 9)}

# Villages – build {id: (name, text)}
all_village_ids = re.findall(r'id="(village_(?:EW|ES)\d+_\d+)"', content)
vdata = {}
for vid in all_village_ids:
    vdata[vid] = (get_attr(content, vid, 'name'), get_attr(content, vid, 'text'))

print("  Done reading.")

# ─── 2. new texts needed ──────────────────────────────────────────────────────

GOLCONDA_TEXT = (
    "The ancient fortress-city of Golconda rises from a granite hill above the plains "
    "of Telangana, its concentric walls enclosing the old Qutb Shahi capital that once "
    "commanded the richest diamond trade in the world. The Koh-i-Noor, the Hope, and "
    "the Regent all emerged from mines in the Krishna valley below this fortress, and "
    "the gem-cutters of Golconda became so synonymous with wealth that the city's name "
    "passed into common speech as a byword for limitless riches. Aurangzeb took Golconda "
    "after a long siege in 1687, ending a century of Qutb Shahi splendor, but the bazaars "
    "still glitter with the finest carnelian, beryl, and worked gold in the Deccan. The "
    "Hyderabad Nizam now holds these walls, and his soldiers guard a treasury built on "
    "the memory of diamonds."
)
DANAPUR_TEXT = (
    "Danapur is the great cantonment on the Ganga's south bank where the Bengal Nawab "
    "stations his river flotilla and gunboat crews who patrol the Gangetic trade lanes "
    "between Patna and Calcutta, its barracks perpetually busy with armed boatmen who "
    "double as river police for the grain merchants."
)
NARSAMPET_TEXT = (
    "Narsampet is a weaving settlement on the plateau country east of Warangal where the "
    "local cotton is worked into coarse cloth and turbans for the Deccan market, its small "
    "bazaar trading also in the iron hoes and ploughshares forged in the village charcoal "
    "furnaces."
)
GANGAPUR_TEXT = (
    "Gangapur lies on the Godavari where the river emerges from the rocky plateau country, "
    "a crossing village of importance where the road from Aurangabad to Osmanabad fords the "
    "water, its ghat perpetually busy with pilgrims heading for the temples upstream."
)
NIMAD_TEXT = (
    "Nimad is the fertile river-bottom country of the Narmada valley below Burhanpur, a "
    "string of villages growing cotton, millet, and lentils in the black soil that local "
    "farmers say can swallow a plough whole in the monsoon season."
)
IBRAHIMPATNAM_TEXT = (
    "Ibrahimpatnam is a small settlement below Golconda's walls on the road south toward "
    "the Krishna River, its inhabitants employed in the lime-burning kilns that once "
    "supplied mortar for the great fortress and now serve the masonry needs of the growing "
    "Hyderabad city downstream."
)
KOTHUR_TEXT = (
    "Kothur is a diamond-washing village in the Krishna valley below Golconda where the "
    "famous gem deposits were worked by thousands of laborers in the Qutb Shahi era. The "
    "great mines are exhausted, but washers still sift the river gravel seeking the rare "
    "uncut stones that occasionally surface."
)

# ─── 3. build assignment tables ──────────────────────────────────────────────

# Towns: (new_name, new_text)
town_assign = {
    # EW towns → Bengali names (pulling from ES descriptions)
    'town_EW1': ('Dacca',       es_town['ES2'][1]),   # Dacca desc was on ES2
    'town_EW2': ('Murshidabad', es_town['ES1'][1]),   # Murshidabad desc was on ES1
    'town_EW3': ('Hooghly',     es_town['ES5'][1]),   # Hooghly desc was on ES5
    'town_EW4': ('Patna',       es_town['ES4'][1]),   # Patna desc was on ES4
    'town_EW5': ('Chittagong',  es_town['ES7'][1]),   # Chittagong desc was on ES7
    'town_EW6': ('Cuttack',     es_town['ES3'][1]),   # Cuttack desc was on ES3
    # ES towns → Deccan names (pulling from EW descriptions)
    'town_ES1': ('Warangal',  ew_town['EW1'][1]),
    'town_ES2': ('Aurangabad',ew_town['EW3'][1]),
    'town_ES3': ('Bidar',     ew_town['EW6'][1]),
    'town_ES4': ('Hyderabad', ew_town['EW2'][1]),
    'town_ES5': ('Burhanpur', ew_town['EW5'][1]),
    'town_ES6': ('Elichpur',  ew_town['EW4'][1]),
    'town_ES7': ('Golconda',  GOLCONDA_TEXT),
}

# Castles: (new_name, new_text)
castle_assign = {
    # EW castles → Bengali fort names (descriptions from ES castles)
    'castle_EW1': ('Gaur Fort',       es_castle['ES2'][1]),
    'castle_EW2': ('Rajmahal Fort',   es_castle['ES3'][1]),
    'castle_EW3': ('Burdwan Fort',    es_castle['ES4'][1]),
    'castle_EW4': ('Rohtasgarh',      es_castle['ES1'][1]),
    'castle_EW5': ('Midnapore Fort',  es_castle['ES6'][1]),
    'castle_EW6': ('Sylhet Fort',     es_castle['ES5'][1]),
    'castle_EW7': ('Purnea Fort',     es_castle['ES8'][1]),
    'castle_EW8': ('Shah Sarai Fort', es_castle['ES7'][1]),
    # ES castles → Deccan fort names (descriptions from EW castles)
    'castle_ES1': ('Golconda Fort',  ew_castle['EW1'][1]),
    'castle_ES2': ('Daulatabad',     ew_castle['EW2'][1]),
    'castle_ES3': ('Naldurg Fort',   ew_castle['EW3'][1]),
    'castle_ES4': ('Udgir Fort',     ew_castle['EW4'][1]),
    'castle_ES5': ('Mahur Fort',     ew_castle['EW5'][1]),
    'castle_ES6': ('Parenda Fort',   ew_castle['EW6'][1]),
    'castle_ES7': ('Kaulas Fort',    ew_castle['EW7'][1]),
    'castle_ES8': ('Medak Fort',     ew_castle['EW8'][1]),
}

# Villages: (new_name, new_text)
# EW villages → Bengali village names (from matching ES source)
# ES villages → Deccan village names (from matching EW source)
village_assign = {
    # EW1 (→Dacca) gets ES2 (Dacca) villages – EW1 has 2 slots
    'village_EW1_1': (vdata['village_ES2_2'][0], vdata['village_ES2_2'][1]),  # Narayanganj
    'village_EW1_2': (vdata['village_ES2_3'][0], vdata['village_ES2_3'][1]),  # Demra

    # EW2 (→Murshidabad) gets ES1 (Murshidabad) villages – both have 3
    'village_EW2_2': (vdata['village_ES1_2'][0], vdata['village_ES1_2'][1]),  # Jiaganj
    'village_EW2_3': (vdata['village_ES1_3'][0], vdata['village_ES1_3'][1]),  # Lalbagh
    'village_EW2_4': (vdata['village_ES1_4'][0], vdata['village_ES1_4'][1]),  # Baharampur

    # EW3 (→Hooghly) gets ES5 (Hooghly) villages – EW3 has 2 slots
    'village_EW3_2': (vdata['village_ES5_1'][0], vdata['village_ES5_1'][1]),  # Chandernagore
    'village_EW3_3': (vdata['village_ES5_2'][0], vdata['village_ES5_2'][1]),  # Serampore

    # EW4 (→Patna) gets ES4 (Patna) villages – EW4 has 3, ES4 has 2 → add Danapur
    'village_EW4_1': (vdata['village_ES4_1'][0], vdata['village_ES4_1'][1]),  # Hajipur
    'village_EW4_3': (vdata['village_ES4_3'][0], vdata['village_ES4_3'][1]),  # Fatuha
    'village_EW4_4': ('Danapur', DANAPUR_TEXT),

    # EW5 (→Chittagong) gets ES7 (Chittagong) villages – both have 2
    'village_EW5_1': (vdata['village_ES7_1'][0], vdata['village_ES7_1'][1]),  # Comilla
    'village_EW5_2': (vdata['village_ES7_2'][0], vdata['village_ES7_2'][1]),  # Noakhali

    # EW6 (→Cuttack) gets ES3 (Cuttack) villages – both have 3
    'village_EW6_1': (vdata['village_ES3_1'][0], vdata['village_ES3_1'][1]),  # Tangi
    'village_EW6_3': (vdata['village_ES3_2'][0], vdata['village_ES3_2'][1]),  # Athgarh
    'village_EW6_4': (vdata['village_ES3_3'][0], vdata['village_ES3_3'][1]),  # Banki

    # ES1 (→Warangal) gets EW1 (Warangal) villages – ES1 has 3, EW1 has 2 → add Narsampet
    'village_ES1_2': (vdata['village_EW1_1'][0], vdata['village_EW1_1'][1]),  # Hanamkonda
    'village_ES1_3': (vdata['village_EW1_2'][0], vdata['village_EW1_2'][1]),  # Kazipet
    'village_ES1_4': ('Narsampet', NARSAMPET_TEXT),

    # ES2 (→Aurangabad) gets EW3 (Aurangabad) villages – ES2 has 3, EW3 has 2 → add Gangapur
    'village_ES2_2': (vdata['village_EW3_2'][0], vdata['village_EW3_2'][1]),  # Paithan
    'village_ES2_3': (vdata['village_EW3_3'][0], vdata['village_EW3_3'][1]),  # Khuldabad
    'village_ES2_4': ('Gangapur City', GANGAPUR_TEXT),

    # ES3 (→Bidar) gets EW6 (Bidar) villages – both have 3
    'village_ES3_1': (vdata['village_EW6_1'][0], vdata['village_EW6_1'][1]),  # Gulbarga
    'village_ES3_2': (vdata['village_EW6_3'][0], vdata['village_EW6_3'][1]),  # Chincholi
    'village_ES3_3': (vdata['village_EW6_4'][0], vdata['village_EW6_4'][1]),  # Shahabad

    # ES4 (→Hyderabad) gets EW2 (Hyderabad) villages – ES4 has 2, EW2 has 3 → use 2
    'village_ES4_1': (vdata['village_EW2_2'][0], vdata['village_EW2_2'][1]),  # Golconda
    'village_ES4_3': (vdata['village_EW2_3'][0], vdata['village_EW2_3'][1]),  # Qutbullapur

    # ES5 (→Burhanpur) gets EW5 (Burhanpur) villages – ES5 has 3, EW5 has 2 → add Nimad
    'village_ES5_1': (vdata['village_EW5_1'][0], vdata['village_EW5_1'][1]),  # Khargaon
    'village_ES5_2': (vdata['village_EW5_2'][0], vdata['village_EW5_2'][1]),  # Sendhwa
    'village_ES5_3': ('Nimad', NIMAD_TEXT),

    # ES6 (→Elichpur) gets EW4 (Elichpur) villages – ES6 has 2, EW4 has 3 → use 2
    'village_ES6_1': (vdata['village_EW4_1'][0], vdata['village_EW4_1'][1]),  # Bhadravati
    'village_ES6_2': (vdata['village_EW4_3'][0], vdata['village_EW4_3'][1]),  # Yeotmal

    # ES7 (→Golconda) – new village names
    'village_ES7_1': ('Ibrahimpatnam', IBRAHIMPATNAM_TEXT),
    'village_ES7_2': ('Kothur', KOTHUR_TEXT),
}

# ─── 4. apply all updates ────────────────────────────────────────────────────

print("Applying town updates...")
for sid, (name, text) in town_assign.items():
    print(f"  {sid} -> {name}")
    content = set_settlement(content, sid, name, text)

print("Applying castle updates...")
for sid, (name, text) in castle_assign.items():
    print(f"  {sid} -> {name}")
    content = set_settlement(content, sid, name, text)

print("Applying village updates...")
for sid, (name, text) in village_assign.items():
    if name is None:
        print(f"  SKIP {sid} (source missing)")
        continue
    print(f"  {sid} -> {name}")
    content = set_settlement(content, sid, name, text)

# ─── 5. write back ──────────────────────────────────────────────────────────

with open(XML_PATH, 'w', encoding='utf-8') as f:
    f.write(content)

print("\nDone. settlements.xml updated.")
