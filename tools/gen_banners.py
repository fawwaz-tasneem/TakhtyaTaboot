#!/usr/bin/env python3
# Recolours every faction's banner to its historic field/accent colours and REMOVES the vanilla
# Calradic sigils (the double-headed eagle etc.). A banner_key is groups of 10 ints; the FIRST
# group is the background field (large size), the rest are sigils. The eagle is a sigil group, so
# keeping only the first group (recoloured) yields a clean historic-coloured field with no eagle —
# structurally identical to the many vanilla clans that already ship a field-only (10-int) key.
#
# Sigils are unnamed sprite indices and can't be previewed here, so we do the reliably-correct part
# (colours) and leave symbols to the in-game banner editor. Colour ids are the nearest palette
# entries to each faction's historic hex (see tools mapping below). Run from module root.
import os, re

MOD = os.path.join(os.path.dirname(__file__), "..", "ModuleData")

# kingdom id -> (field colour id, accent colour id) — nearest banner-palette ids to the historic hex
REALM = {
    "empire":   (126, 7),    # Mughal — green field, gold
    "empire_w": (12, 7),     # Bengal — deep blue, gold
    "empire_s": (229, 172),  # Hyderabad — yellow, white disc
    "sturgia":  (116, 126),  # Durrani/Afghan — black, green
    "aserai":   (178, 131),  # Mysore — maroon, gold
    "vlandia":  (232, 7),    # Rajput — crimson, gold
    "battania": (223, 231),  # Maratha — saffron (bhagwa), maroon
    "khuzait":  (205, 245),  # Sikh — blue, saffron
}

# A centred emblem per realm, in the accent colour. Sprites are unnamed numbered icons, so the
# exact symbol is a BEST GUESS (Animal group ~ beasts/lion/tiger; Sign group ~ suns/stars; Flora ~
# plants). Swap any id in-game via the banner editor and paste the code back for an exact match.
# (icon_id, sigil_colour_id):
SIGIL = {
    "empire":   (102, 7),    # Mughal — Animal (aiming for a lion) in gold
    "aserai":   (108, 131),  # Mysore — Animal (aiming for a tiger, Tipu) in gold
    "vlandia":  (401, 7),    # Rajput — Sign (a sun, for Mewar) in gold
    "battania": (415, 231),  # Maratha — Sign in maroon
    "khuzait":  (455, 245),  # Sikh — Sign in saffron
    "sturgia":  (120, 126),  # Afghan — Animal in green
    "empire_w": (205, 7),    # Bengal — Flora (a lotus) in gold
    "empire_s": (430, 116),  # Hyderabad — Sign (a disc/star) in dark
}

def field_only(key, field, accent, realm=None):
    g = key.split(".")
    if len(g) < 10:
        return key
    g = g[:10]            # keep just the background block -> drops every sigil (incl. the eagle)
    g[1] = str(field)     # background primary colour
    g[2] = str(accent)    # background secondary colour
    base = ".".join(g)
    sig = SIGIL.get(realm)
    if sig:               # append one centred emblem (icon.colour.0.size.size.posX.posY.0.0.0)
        base += f".{sig[0]}.{sig[1]}.0.500.500.764.764.0.0.0"
    return base

def realm_of(attrs):
    m = re.search(r'super_faction="Kingdom\.([^"]+)"', attrs)
    return m.group(1) if m else None

def do_clans(path):
    txt = open(path, encoding="utf-8").read()
    n = [0]
    def repl(m):
        attrs = m.group(0)
        realm = realm_of(attrs)
        if realm not in REALM:
            return attrs
        f, a = REALM[realm]
        def keyrepl(km):
            n[0] += 1
            return 'banner_key="%s"' % field_only(km.group(1), f, a, realm)
        return re.sub(r'banner_key="([0-9.]+)"', keyrepl, attrs)
    txt = re.sub(r'<Faction\b[^>]*?/>', repl, txt)
    open(path, "w", encoding="utf-8").write(txt)
    print(f"clans recoloured: {n[0]}")

def do_kingdoms(path):
    txt = open(path, encoding="utf-8").read()
    n = [0]
    def repl(m):
        block = m.group(0)
        idm = re.search(r'id="([^"]+)"', block)
        if not idm or idm.group(1) not in REALM:
            return block
        realm = idm.group(1)
        f, a = REALM[realm]
        def keyrepl(km):
            n[0] += 1
            return 'banner_key="%s"' % field_only(km.group(1), f, a, realm)
        return re.sub(r'banner_key="([0-9.]+)"', keyrepl, block)
    txt = re.sub(r'<Kingdom\b[^>]*>', repl, txt)   # opening tag only (banner_key lives there)
    open(path, "w", encoding="utf-8").write(txt)
    print(f"kingdoms recoloured: {n[0]}")

if __name__ == "__main__":
    do_kingdoms(os.path.join(MOD, "spkingdoms.xml"))
    do_clans(os.path.join(MOD, "tyt_spclans.xml"))
    print("Done. Banners are now historic-coloured field-only (no Calradic sigils).")
