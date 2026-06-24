#!/usr/bin/env python3
"""Generate Hindostan overrides for EVERY culture-keyed faction/ruler/culture string, so no vanilla
Calradic name (Vlandia, Sturgia, Empire, baron, knez, ...) leaks anywhere — UI, dialogue, join
cutscenes, encyclopedia. Native module_strings.xml keys these by their OUTER id (str_<base>.<culture>),
which LocalizationOverride can set directly without the inner->outer string map.

Output: ModuleData/Languages/hindostan_faction_names.xml (loaded by LocalizationOverride)."""
import os, xml.sax.saxutils as sx

OUT = os.path.join(os.path.dirname(__file__), "..", "ModuleData", "Languages", "hindostan_faction_names.xml")

# Per-culture Hindostan scheme. Keys: ruler m/f, official m/f, adjective, demonym (plural people),
# formal (kingdom formal name), informal ("the X"), short (faction short name).
C = {
    "empire":   dict(rm="Padishah", rf="Begum",   om="a mansabdar", of="a mansabdar",
                     adj="Mughal",     dem="Mughals",     formal="Mughal Empire",      informal="the Mughals",     short="Mughal Empire"),
    "empire_w": dict(rm="Nawab",    rf="Begum",   om="a zamindar",  of="a zamindar",
                     adj="Bengali",    dem="Bengalis",    formal="Subah of Bengal",    informal="the Bengalis",    short="Bengal"),
    "empire_s": dict(rm="Nizam",    rf="Begum",   om="a jagirdar",  of="a jagirdar",
                     adj="Hyderabadi", dem="Hyderabadis", formal="Nizamate of Hyderabad", informal="the Hyderabadis", short="Hyderabad"),
    "sturgia":  dict(rm="Amir",     rf="Begum",   om="a sardar",    of="a sardar",
                     adj="Afghan",     dem="Afghans",     formal="Afghan Kingdom",     informal="the Afghans",     short="Afghans"),
    "aserai":   dict(rm="Sultan",   rf="Sultana", om="an amir",     of="an amira",
                     adj="Mysorean",   dem="Mysoreans",   formal="Mysorean Sultanate", informal="the Mysoreans",   short="Mysoreans"),
    "vlandia":  dict(rm="Maharaja", rf="Maharani",om="a thakur",    of="a thakurani",
                     adj="Rajput",     dem="Rajputs",     formal="Rajput Kingdom",     informal="the Rajputs",     short="Rajputs"),
    "battania": dict(rm="Chhatrapati", rf="Chhatrapati", om="a sardar", of="a sardar",
                     adj="Maratha",    dem="Marathas",    formal="High Kingdom of the Marathas", informal="the Marathas", short="Marathas"),
    "khuzait":  dict(rm="Maharaja", rf="Maharani",om="a sardar",    of="a sardar",
                     adj="Sikh",       dem="Sikhs",       formal="Sikh Khanate",       informal="the Sikhs",       short="Sikhs"),
}

# Character-creation culture descriptions for the three imperial cultures (the other five are already
# authored in the mod's existing override files). Replaces vanilla "Calradian Empire / Arenicos" prose.
DESC = {
    "empire": "The Mughals are the paramount power of Hindostan, heirs to Timur and Babur, ruling a vast and "
              "wealthy empire from Delhi and Agra. Their armies blend heavy cavalry, war elephants and massed "
              "artillery, and their court — a splendour of poets, painters and astronomers — sets the fashion "
              "for all the subcontinent. Yet the empire is past its zenith: ambitious governors and restless "
              "vassals test the writ of the Padishah, and the provinces strain toward independence.",
    "empire_w": "The Bengalis hold the rich, water-laced delta of the east, the wealthiest subah of the empire. "
                "Grown fat on rice, muslin and the river trade, the Nawabs of Bengal rule in the empire's name "
                "but increasingly for themselves. Their soldiers are at home in flooded paddy and on river "
                "boats, and their treasuries are the envy of every prince in Hindostan.",
    "empire_s": "The Hyderabadis command the Deccan plateau of the south, where the Nizam governs a patchwork "
                "of warlike domains in the fading shadow of imperial authority. Masters of fortress warfare and "
                "of playing rival powers against one another, they hold a hard, proud country of basalt hills, "
                "diamond mines and ancient citadels.",
}

def gendered(f, m):
    return "{?RULER.GENDER}" + f + "{?}" + m + "{\\?} {RULER.NAME}"

rows = []
def add(idv, text):
    rows.append((idv, text))

for c, d in C.items():
    # Ruler term (lowercase, used in composed phrases).
    add(f"str_faction_ruler.{c}",    d["rm"].lower())
    add(f"str_faction_ruler.{c}_f",  d["rf"].lower())
    # Official / noble functionary term.
    add(f"str_faction_official.{c}",   d["om"])
    add(f"str_faction_official.{c}_f", d["of"])
    # Ruler name with title, and the in-speech variant.
    add(f"str_faction_ruler_name_with_title.{c}", gendered(d["rf"], d["rm"]))
    add(f"str_faction_ruler_term_in_speech.{c}",  gendered(d["rf"], d["rm"]))
    # Culture adjectives / demonyms / faction names.
    add(f"str_adjective_for_culture.{c}", d["adj"])
    add(f"str_adjective_for_faction.{c}", d["adj"])
    add(f"str_neutral_term_for_culture.{c}", d["dem"])
    add(f"str_short_term_for_faction.{c}", d["short"])
    add(f"str_culture_rich_name.{c}", d["dem"])
    add(f"str_faction_formal_name_for_culture.{c}", d["formal"])
    add(f"str_faction_informal_name_for_culture.{c}", d["informal"])
    add(f"str_kingdom_formal_name.{c}", d["formal"])
    if c in DESC:
        add(f"str_culture_description.{c}", DESC[c])

with open(OUT, "w", encoding="utf-8") as f:
    f.write('<?xml version="1.0" encoding="utf-8"?>\n')
    f.write('<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="string">\n')
    f.write('  <strings>\n')
    for idv, text in rows:
        f.write(f'    <string id="{idv}" text="{sx.escape(text, {chr(34): "&quot;"})}" />\n')
    f.write('  </strings>\n')
    f.write('</base>\n')

print(f"Wrote {len(rows)} faction-name overrides to {os.path.abspath(OUT)}")
