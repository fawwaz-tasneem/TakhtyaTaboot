#!/usr/bin/env python3
"""Purge vanilla Calradic PLACE/PEOPLE names from all semantic-id strings in Native module_strings.xml
(dialogue, notifications, story log, item names, character-creation prose, ...). These are keyed by
their outer id, so LocalizationOverride can set them directly. Faction/ruler TITLES are handled by
gen_faction_names.py; here we only swap proper nouns (Calradia, Vlandia, Sturgia, Nord, ...).

Output: ModuleData/Languages/hindostan_prose_overrides.xml"""
import os, re, xml.sax.saxutils as sx

NATIVE = "../../Native/ModuleData/module_strings.xml"
HERE = os.path.dirname(__file__)
SRC = os.path.normpath(os.path.join(HERE, "..", "..", "Native", "ModuleData", "module_strings.xml"))
OUT = os.path.join(HERE, "..", "ModuleData", "Languages", "hindostan_prose_overrides.xml")

# Ordered phrase replacements first, then whole-word (case-sensitive) swaps.
PHRASES = [
    ("Calradian Empire", "Mughal Empire"),
    ("the Aserai", "the Mysoreans"),
    ("an Aserai", "a Mysorean"),
]
WORDS = [
    ("Calradians", "Hindostanis"), ("Calradian", "Hindostani"), ("Calradia", "Hindostan"),
    ("calradian", "hindostani"),
    ("Vlandians", "Rajputs"), ("Vlandian", "Rajput"), ("Vlandia", "Rajputana"),
    ("Sturgians", "Afghans"), ("Sturgian", "Afghan"), ("Sturgia", "Afghanistan"),
    ("Battanians", "Marathas"), ("Battanian", "Maratha"), ("Battania", "Maharashtra"),
    ("Khuzaits", "Sikhs"), ("Khuzait", "Sikh"),
    ("Aserai", "Mysorean"),
    ("Nords", "Pathans"), ("Nord", "Pathan"),
]

# Culture-keyed faction ids are authored in gen_faction_names.py — let that file own them.
CK = re.compile(r'str_(faction_[a-z_]+|adjective_for_[a-z]+|neutral_term_for_culture|short_term_for_faction|culture_rich_name|kingdom_formal_name|culture_description)\.(vlandia|sturgia|battania|khuzait|aserai|empire|empire_w|empire_s)(_f)?$')

def replace(t):
    for a, b in PHRASES:
        t = t.replace(a, b)
    for a, b in WORDS:
        t = re.sub(r'\b' + re.escape(a) + r'\b', b, t)
    return t

s = open(SRC, encoding="utf-8").read()
pat = re.compile(r'<string\s+id="(str_[^"]+)"\s+text="([^"]*)"\s*/>')
rows = pat.findall(s)
out = []
for k, t in rows:
    if CK.search(k):
        continue
    body = re.sub(r'^\{=[^}]*\}', '', t)
    new = replace(body)
    if new != body:
        out.append((k, new))

with open(OUT, "w", encoding="utf-8") as f:
    f.write('<?xml version="1.0" encoding="utf-8"?>\n')
    f.write('<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="string">\n')
    f.write('  <strings>\n')
    for k, t in out:
        f.write(f'    <string id="{k}" text="{sx.escape(t, {chr(34): "&quot;"})}" />\n')
    f.write('  </strings>\n')
    f.write('</base>\n')

print(f"Wrote {len(out)} prose overrides to {os.path.abspath(OUT)}")
for k, t in out:
    print(" ", k, "||", t[:80])
