#!/usr/bin/env python3
# Generates ModuleData/tyt_troops.xml — the mod's authentic per-culture troop trees.
#
# DESIGN (decided 2026-06-20): OVERRIDE the vanilla troop ids in place rather than create new
# ids. Rule: "only new troops, no vanilla anywhere". By reskinning each existing id (name + face
# + skills + equipment) and OMITTING level/default_group/occupation/culture/upgrade_targets, the
# engine's XML merge keeps vanilla's tier and tree wiring, so there is exactly ONE tree per
# culture and the new troops appear in every spawn path (recruitment, AI armies, garrisons,
# sieges, tournaments). Registered AFTER spnpccharacters.xml so these full overrides win.
#
# To make every culture LOOK different, each is given a distinct vanilla equipment family:
#   Mughal=aserai  Mysore=empire  Afghan=khuzait  Rajput=vlandia  Maratha=battania  Sikh=sturgia
# No guns / cannon / elephants: crossbow & gunner slots are reskinned as bowmen, artillery as
# heavy infantry. All item ids are real vanilla items (validated by tools, see the validator run).
# Run from the module root:  python tools/gen_troops.py
import os, xml.sax.saxutils as su

OUT = os.path.join(os.path.dirname(__file__), "..", "ModuleData", "tyt_troops.xml")
SLOT_ORDER = ["Item0","Item1","Item2","Item3","Item4","Head","Cape","Body","Gloves","Leg","Horse","HorseHarness"]
FACE = "BodyProperty.fighter_aserai"   # consistent South-Asian faces across all cultures

def skills_block(sk):
    return "\n".join(f'\t\t\t<skill id="{k}" value="{sk.get(k,0)}" />'
                     for k in ["OneHanded","TwoHanded","Polearm","Bow","Crossbow","Throwing","Riding","Athletics"])

def equip_block(eq):
    return "\n".join(f'\t\t\t\t<equipment slot="{s}" id="Item.{eq[s]}" />' for s in SLOT_ORDER if s in eq and eq[s])

def civ_tier(t): return 1 if t <= 2 else 2 if t <= 3 else 3

def troop_xml(t):
    civ = f'\t\t\t<EquipmentSet id="{t["civ"]}_troop_civilian_template_t{civ_tier(t["tier"])}" civilian="true" />'
    return f'''\t<NPCCharacter id="{t["id"]}" name={su.quoteattr(t["name"])}>
\t\t<face>
\t\t\t<face_key_template value="{FACE}" />
\t\t</face>
\t\t<skills>
{skills_block(t["skills"])}
\t\t</skills>
\t\t<Equipments>
\t\t\t<EquipmentRoster>
{equip_block(t["eq"])}
\t\t\t</EquipmentRoster>
{civ}
\t\t</Equipments>
\t</NPCCharacter>'''

# ─────────────────────────── per-family equipment palettes ───────────────────────────
# Lists are indexed by tier 1..5 (index tier-1). 'cape'/'glove' may hold None at low tiers.
# Weapons: sword[tier], lance[tier], spear[tier]; bow_lo/bow_hi; arrow; horse_lo/hi/noble; harness.
P = {
 "aserai": dict(  # (palette kept for reference / Mysore civ; Mughal itself is authored explicitly below)
   head=["aserai_civil_e_hscarf","pointed_skullcap_over_cloth_headwrap","pointed_skullcap_with_mail","closed_desert_helmet_with_mail","brass_aserai_helmet_b_open"],
   body=["aserai_civil_e","studded_leather_coat","leather_strips_over_padded_robe","desert_lamellar","aserai_scale_armor_on_chain"],
   leg=["eastern_leather_boots"]*3+["plated_strip_boots","strapped_mail_chausses"],
   glove=[None,"buttoned_leather_bracers","eastern_plated_leather_vambraces","reinforced_mail_mitten","reinforced_mail_mitten"],
   cape=[None,None,None,"a_aserai_scale_b_shoulder_c","a_aserai_scale_b_shoulder_d"],
   shield_lo="desert_oval_shield", shield_hi="studded_adarga",
   sword=["aserai_sword_2_t2","aserai_sword_3_t3","aserai_sword_3_t3","aserai_sword_4_t4","aserai_sword_5_t4"],
   lance=[None,None,"eastern_spear_1_t2","southern_spear_3_t4","aserai_lance_1_t5"],
   spear=[None,None,"eastern_spear_4_t4","eastern_spear_4_t4","eastern_spear_4_t4"],
   bow_lo="composite_bow", bow_hi="steppe_heavy_bow", arrow="barbed_arrows", arrow2="piercing_arrows",
   horse_lo="aserai_horse", horse_hi="aserai_horse", horse_noble="aserai_horse", harness="half_mail_and_plate_barding"),

 "empire": dict(   # MYSORE — imperial padded & lamellar
   head=["heavy_nasalhelm_over_laced_cloth","heavy_nasalhelm_over_laced_cloth","heavy_nasalhelm_over_imperial_padding","heavy_nasalhelm_over_imperial_mail","feathered_spangenhelm_over_imperial_coif"],
   body=["empire_warrior_padded_armor_a","empire_warrior_padded_armor_c","basic_imperial_leather_armor","empire_legion_a","empire_plate_vest_armor"],
   leg=["strapped_leather_boots","folded_town_boots","empire_horseman_boots","plated_strip_boots","lamellar_plate_boots"],
   glove=[None,"woven_leather_bracers","padded_mitten","reinforced_padded_mitten","plated_strip_gauntlets"],
   cape=[None,None,"imperial_studded_strip_shoulders","imperial_lamellar_shoulders","empire_plate_armor_shoulder_a"],
   shield_lo="oval_shield", shield_hi="steel_round_shield",
   sword=["empire_sword_1_t2","empire_sword_2_t3","empire_sword_3_t3","empire_sword_4_t4","empire_sword_5_t4"],
   lance=[None,None,"empire_lance_1_t3","empire_lance_2_t4","empire_lance_3_t5"],
   spear=[None,None,"imperial_spear_t2","eastern_spear_4_t4","eastern_spear_4_t4"],
   bow_lo="composite_bow", bow_hi="steppe_heavy_bow", arrow="barbed_arrows", arrow2="piercing_arrows",
   horse_lo="t2_empire_horse", horse_hi="t3_empire_horse", horse_noble="noble_horse_imperial", harness="half_mail_and_plate_barding"),

 "khuzait": dict(  # AFGHAN — central-asian lamellar, fur, recurve bows
   head=["nomad_cap","fur_hat","eastern_cap","nomad_helmet","plumed_lamellar_helmet"],
   body=["crude_leather_armor","eastern_stitched_leather_coat","eastern_plated_leather","eastern_lamellar_armor","brass_lamellar_over_mail"],
   leg=["leather_boots","steppe_leather_boots","eastern_leather_boots","khuzait_curved_boots","reinforced_suede_boots"],
   glove=[None,"steppe_leather_vambraces","eastern_plated_leather_vambraces","studded_vambraces","khuzait_heavy_armor_bracer"],
   cape=[None,None,"leather_lamellar_shoulders","lamellar_shoulders","brass_lamellar_shoulder"],
   shield_lo="eastern_wicker_shield", shield_hi="tribal_steppe_shield",
   sword=["khuzait_sword_1_t2","khuzait_sword_2_t3","khuzait_sword_3_t3","khuzait_sword_4_t4","khuzait_sword_5_t4"],
   lance=[None,None,"khuzait_lance_1_t3","khuzait_lance_2_t4","khuzait_lance_3_t5"],
   spear=[None,None,"eastern_spear_2_t3","eastern_spear_4_t4","eastern_spear_5_t5"],
   bow_lo="steppe_bow", bow_hi="steppe_war_bow", arrow="steppe_arrows", arrow2="heavy_steppe_arrows",
   horse_lo="t2_khuzait_horse", horse_hi="t3_khuzait_horse", horse_noble="noble_horse_eastern", harness="half_mail_and_plate_barding"),

 "vlandia": dict(  # RAJPUT — heavy mail & plate, kite/heater shields, lances
   head=["arming_cap","cervelliere_over_cloth_headwrap","cervelliere_over_laced_coif","full_helm_over_mail_coif","full_helm_over_arming_coif"],
   body=["padded_leather_shirt","leather_coat_over_cloth","mail_shirt","hauberk","coat_of_plates_over_mail"],
   leg=["leather_cavalier_boots","leather_cavalier_boots","mail_chausses","mail_chausses","strapped_mail_chausses"],
   glove=[None,"reinforced_leather_vambraces","mail_mitten","mail_mitten","reinforced_mail_mitten"],
   cape=[None,None,"chainmail_shoulder_armor","scale_shoulder_armor","pauldron_over_scale_armor"],
   shield_lo="vlandia_infantry_shield_a", shield_hi="heavy_heater_shield", shield_kite="western_riders_kite_shield",
   sword=["vlandia_sword_1_t2","vlandia_sword_2_t3","vlandia_sword_3_t4","vlandia_sword_4_t4","vlandia_sword_5_t5"],
   lance=[None,None,"vlandia_lance_1_t3","vlandia_lance_2_t4","vlandia_lance_3_t5"],
   spear=[None,None,"eastern_spear_3_t3","eastern_spear_4_t4","eastern_spear_5_t5"],
   bow_lo="lowland_longbow", bow_hi="lowland_yew_bow", arrow="barbed_arrows", arrow2="piercing_arrows",
   horse_lo="t2_vlandia_horse", horse_hi="t3_vlandia_horse", horse_noble="noble_horse_western", harness="half_mail_and_plate_barding"),

 "battania": dict(  # MARATHA — light leather, targes, woodland bows, throwing axes
   head=["battania_civil_hood","battania_fur_helmet","battania_earmuff_helmet_a","battania_earmuff_helmet_c","battanian_noble_helmet_with_feather"],
   body=["battania_light_armor_a","battania_light_armor_b","battania_light_armor_c","battania_light_armor_d","battania_brass_plate_armor"],
   leg=["battania_leather_boots","battania_woodland_boots","highland_boots","battania_fur_boots","battania_warlord_boots"],
   glove=[None,"armwraps","buttoned_leather_bracers","guarded_armwraps","battania_noble_bracers"],
   cape=[None,"battania_cloak","battania_woodland_cloak","battania_shoulder_furr","battania_warlord_pauldrons"],
   shield_lo="battania_shield_targe_a", shield_hi="battania_large_shield_a",
   sword=["battania_sword_1_t2","battania_sword_2_t3","battania_sword_3_t3","battania_sword_4_t4","battania_sword_5_t5"],
   lance=[None,None,"eastern_spear_3_t3","eastern_spear_4_t4","eastern_spear_5_t5"],
   spear=[None,None,"eastern_spear_3_t3","eastern_spear_4_t4","eastern_spear_5_t5"],
   throw="woodland_throwing_axe_1_t1",
   bow_lo="hunting_bow", bow_hi="highland_ranger_bow", arrow="barbed_arrows", arrow2="piercing_arrows",
   horse_lo="t2_battania_horse", horse_hi="t3_battania_horse", horse_noble="noble_horse_battania", harness="half_mail_and_plate_barding"),

 "sturgia": dict(  # SIKH — nordic mail/lamellar, round shields, axes & spears
   head=["nordic_leather_cap","nordic_fur_cap","nasal_helmet","nasal_helmet_with_mail","nasalhelm_over_mail"],
   body=["light_tunic","layered_leather_tunic","leather_and_iron_plate_armor","nordic_hauberk","nordic_lamellar_armor"],
   leg=["sturgia_boots_a","sturgia_boots_b","sturgia_boots_c","sturgia_boots_d","northern_plated_boots"],
   glove=[None,None,"northern_brass_bracers","northern_brass_bracers","northern_plated_gloves"],
   cape=[None,None,"stitched_leather_shoulders","mail_shoulders","brass_scale_shoulders"],
   shield_lo="leather_round_shield", shield_hi="heavy_round_shield", shield_kite="leather_bound_kite_shield",
   sword=["sturgia_sword_1_t2","sturgia_sword_2_t3","sturgia_sword_3_t3","sturgia_sword_4_t4","sturgia_sword_5_t4"],
   lance=[None,None,"eastern_spear_3_t3","eastern_spear_4_t4","eastern_spear_5_t5"],
   spear=[None,None,"eastern_spear_3_t3","eastern_spear_4_t4","eastern_spear_5_t5"],
   axe="sturgia_axe_3_t3", axe_hi="sturgia_2haxe_1_t4",
   bow_lo="nordic_shortbow", bow_hi="nordic_shortbow", arrow="barbed_arrows", arrow2="piercing_arrows",
   horse_lo="t2_sturgia_horse", horse_hi="t3_sturgia_horse", horse_noble="noble_horse_northern", harness="half_mail_and_plate_barding"),
}

def gear(fam, role, tier):
    p = P[fam]; i = tier-1; eq = {}
    def put(slot, v):
        if v: eq[slot] = v
    head = p["head"][i]; body = p["body"][i]; leg = p["leg"][i]
    glove = p["glove"][i]; cape = p["cape"][i]
    if role == "inf":
        put("Item0", p["sword"][i]); put("Item1", p["shield_hi"] if tier>=3 else p["shield_lo"])
    elif role == "spear":
        put("Item0", p["spear"][i] or p["sword"][i]); put("Item1", p["shield_hi"] if tier>=3 else p["shield_lo"])
        put("Item2", p["sword"][i])
    elif role == "2h":
        put("Item0", p.get("axe_hi") or p["sword"][i]); put("Item1", p["sword"][i])
    elif role == "arch":
        put("Item0", p["bow_hi"] if tier>=4 else p["bow_lo"]); put("Item1", p["arrow"]); put("Item2", p["arrow2"] if tier>=3 else p["arrow"])
        put("Item3", p["sword"][i])
    elif role == "skirm":   # javelin / throwing skirmisher
        put("Item0", p.get("throw") or p["spear"][i] or p["sword"][i]); put("Item1", p["sword"][i]); put("Item2", p["shield_lo"])
    elif role == "cav":
        put("Item0", p["lance"][i] or p["sword"][i]); put("Item1", p["sword"][i])
        put("Item2", p.get("shield_kite") or p["shield_hi"] if tier>=3 else p["shield_lo"])
        put("Horse", p["horse_noble"] if tier>=5 else (p["horse_hi"] if tier>=4 else p["horse_lo"]))
        if tier>=4: put("HorseHarness", p["harness"])
    elif role == "ha":      # horse archer
        put("Item0", p["bow_hi"] if tier>=4 else p["bow_lo"]); put("Item1", p["arrow"]); put("Item2", p["sword"][i])
        put("Horse", p["horse_noble"] if tier>=5 else (p["horse_hi"] if tier>=4 else p["horse_lo"]))
        if tier>=4: put("HorseHarness", p["harness"])
    put("Head", head); put("Cape", cape); put("Body", body); put("Gloves", glove); put("Leg", leg)
    return eq

def sk(role, tier):
    base = 10 + tier*22
    if role in ("inf","spear","2h"):
        return {"OneHanded":base,"Polearm":base if role!="2h" else 0,"TwoHanded":base if role=="2h" else 0,"Athletics":base-10}
    if role in ("arch","skirm"):
        return {"Bow":base+10 if role=="arch" else 0,"Throwing":base if role=="skirm" else 0,"OneHanded":base-15,"Athletics":base-10}
    if role == "cav":
        return {"Polearm":base,"OneHanded":base-10,"Riding":base+5,"Athletics":base-20}
    if role == "ha":
        return {"Bow":base,"OneHanded":base-15,"Riding":base+5,"Athletics":base-20}
    return {"OneHanded":base}

def build(fam, civ, rows):
    # rows: (vanilla_id, name, role, tier)
    return [dict(id=i, name=n, civ=civ, tier=t, skills=sk(r,t), eq=gear(fam,r,t)) for (i,n,r,t) in rows]

# ───────────────────────── MUGHAL (overrides Culture.empire) — authored explicitly ─────────────────────────
def mg(vanilla_id, name, role, tier, eq):
    return dict(id=vanilla_id, name=name, civ="aserai", tier=tier, skills=sk(role,tier), eq=eq)
mughal = [
    mg("imperial_recruit","Paik","inf",1,{"Item0":"aserai_sword_2_t2","Head":"aserai_civil_e_hscarf","Body":"aserai_civil_e","Leg":"eastern_leather_boots"}),
    mg("imperial_infantryman","Sipahi","inf",2,{"Item0":"aserai_sword_3_t3","Item1":"desert_oval_shield","Head":"pointed_skullcap_over_cloth_headwrap","Body":"studded_leather_coat","Gloves":"buttoned_leather_bracers","Leg":"eastern_leather_boots"}),
    mg("imperial_trained_infantryman","Dhali","inf",3,{"Item0":"aserai_sword_3_t3","Item1":"southern_oval_shield","Head":"pointed_skullcap_with_mail","Body":"leather_strips_over_padded_robe","Gloves":"eastern_plated_leather_vambraces","Leg":"eastern_leather_boots"}),
    mg("imperial_veteran_infantryman","Najib","inf",4,{"Item0":"aserai_sword_4_t4","Item1":"southern_oval_shield","Head":"closed_desert_helmet_with_mail","Cape":"a_aserai_scale_b_shoulder_c","Body":"desert_lamellar","Gloves":"reinforced_mail_mitten","Leg":"plated_strip_boots"}),
    mg("imperial_legionary","Dakhili","inf",5,{"Item0":"aserai_sword_5_t4","Item1":"studded_adarga","Head":"brass_aserai_helmet_b_open","Cape":"a_aserai_scale_b_shoulder_d","Body":"aserai_scale_armor_on_chain","Gloves":"reinforced_mail_mitten","Leg":"strapped_mail_chausses"}),
    mg("imperial_menavliaton","Barchhaiyat","spear",4,{"Item0":"eastern_spear_4_t4","Item1":"southern_oval_shield","Item2":"aserai_sword_4_t4","Head":"pointed_skullcap_with_mail","Body":"desert_lamellar","Gloves":"eastern_plated_leather_vambraces","Leg":"plated_strip_boots"}),
    mg("imperial_elite_menavliaton","Neza Bardar","spear",5,{"Item0":"eastern_spear_4_t4","Item1":"studded_adarga","Item2":"aserai_sword_5_t4","Head":"brass_aserai_helmet_b_open","Body":"aserai_scale_armor_on_chain","Gloves":"reinforced_mail_mitten","Leg":"strapped_mail_chausses"}),
    mg("imperial_archer","Tirandaz","arch",2,{"Item0":"composite_bow","Item1":"barbed_arrows","Item2":"aserai_sword_2_t2","Head":"aserai_civil_e_hscarf","Body":"aserai_civil_e","Gloves":"rough_tied_bracers","Leg":"eastern_leather_boots"}),
    mg("imperial_trained_archer","Khadang Andaz","arch",3,{"Item0":"composite_bow","Item1":"barbed_arrows","Item2":"piercing_arrows","Item3":"aserai_sword_2_t2","Head":"pointed_skullcap_over_cloth_headwrap","Cape":"wrapped_scarf","Body":"aserai_chain_plate_armor_d","Gloves":"rough_tied_bracers","Leg":"eastern_leather_boots"}),
    mg("imperial_veteran_archer","Kamandar","arch",4,{"Item0":"composite_steppe_bow","Item1":"piercing_arrows","Item2":"piercing_arrows","Item3":"aserai_sword_3_t3","Head":"pointed_skullcap_over_mail","Body":"aserai_archer_armor","Gloves":"reinforced_leather_vambraces","Leg":"plated_strip_boots"}),
    mg("imperial_palatine_guard","Khasa Tirandaz","arch",5,{"Item0":"steppe_heavy_bow","Item1":"piercing_arrows","Item2":"piercing_arrows","Item3":"aserai_sword_5_t4","Head":"pointed_skullcap_over_mail","Body":"aserai_chain_plate_armor_b","Gloves":"reinforced_leather_vambraces","Leg":"strapped_mail_chausses"}),
    mg("imperial_crossbowman","Sakht Kaman","arch",4,{"Item0":"composite_steppe_bow","Item1":"piercing_arrows","Item2":"aserai_sword_3_t3","Head":"pointed_skullcap_over_mail","Body":"aserai_chain_plate_armor_d","Gloves":"reinforced_leather_vambraces","Leg":"plated_strip_boots"}),
    mg("imperial_sergeant_crossbowman","Mir Tirandaz","arch",5,{"Item0":"steppe_heavy_bow","Item1":"piercing_arrows","Item2":"piercing_arrows","Item3":"aserai_sword_5_t4","Head":"pointed_skullcap_over_mail","Body":"aserai_chain_plate_armor_b","Gloves":"reinforced_leather_vambraces","Leg":"strapped_mail_chausses"}),
    mg("imperial_vigla_recruit","Ahadi","cav",2,{"Item0":"aserai_sword_3_t3","Item1":"oval_shield","Item2":"eastern_spear_1_t2","Head":"open_desert_helmet","Body":"studded_leather_coat","Gloves":"buttoned_leather_bracers","Leg":"eastern_leather_boots","Horse":"aserai_horse"}),
    mg("imperial_equite","Sowar","cav",3,{"Item0":"aserai_sword_4_t4","Item1":"desert_round_shield","Item2":"southern_spear_3_t4","Head":"brass_aserai_helmet_b_leather","Body":"aserai_scale_armor_on_cloth","Gloves":"buttoned_leather_bracers","Leg":"eastern_leather_boots","Horse":"aserai_horse"}),
    mg("imperial_heavy_horseman","Bargir","cav",4,{"Item0":"aserai_lance_1_t5","Item1":"aserai_sword_4_t4","Item2":"desert_round_shield","Head":"closed_desert_helmet_with_mail","Body":"aserai_scale_armor_on_chain","Gloves":"rough_tied_bracers","Leg":"strapped_mail_chausses","Horse":"aserai_horse","HorseHarness":"half_mail_and_plate_barding"}),
    mg("imperial_cataphract","Silahdar","cav",5,{"Item0":"aserai_lance_1_t5","Item1":"aserai_sword_5_t4","Item2":"studded_adarga","Head":"brass_aserai_helmet_b_open","Cape":"a_aserai_scale_b_shoulder_d","Body":"aserai_full_scale_armor_on_chain","Gloves":"reinforced_mail_mitten","Leg":"strapped_mail_chausses","Horse":"aserai_horse","HorseHarness":"half_mail_and_plate_barding"}),
    mg("imperial_elite_cataphract","Walashahi","cav",5,{"Item0":"aserai_lance_1_t5","Item1":"aserai_sword_5_t4","Item2":"studded_adarga","Head":"aserai_lord_helmet_a","Cape":"a_aserai_scale_b_shoulder_d","Body":"aserai_brass_plate_a","Gloves":"reinforced_mail_mitten","Leg":"strapped_mail_chausses","Horse":"aserai_horse","HorseHarness":"half_mail_and_plate_barding"}),
    mg("bucellarii","Turki Sowar","ha",5,{"Item0":"composite_steppe_bow","Item1":"piercing_arrows","Item2":"aserai_sword_5_t4","Item3":"desert_round_shield","Head":"desert_helmet_with_mail","Body":"aserai_chain_plate_armor_b","Gloves":"reinforced_leather_vambraces","Leg":"strapped_mail_chausses","Horse":"aserai_horse","HorseHarness":"half_mail_and_plate_barding"}),
]

# ───────────────────────── MYSORE (overrides Culture.aserai) — Tipu's establishment ─────────────────────────
mysore = build("empire","empire",[
    ("aserai_recruit","Piyada","inf",1),
    ("aserai_tribesman","Jaishi","inf",2),
    ("aserai_footman","Sipahdar","inf",3),
    ("aserai_infantry","Nizami","inf",4),
    ("aserai_veteran_infantry","Usud-i-Ilahi","inf",5),
    ("aserai_skirmisher","Tirgar","arch",3),
    ("aserai_archer","Kamani","arch",4),
    ("aserai_master_archer","Sar-Kamani","arch",5),
    ("aserai_mameluke_soldier","Yuzuk","inf",2),
    ("aserai_mameluke_regular","Risaldar","cav",3),
    ("aserai_mameluke_cavalry","Sowar-i-Khas","ha",4),
    ("aserai_mameluke_heavy_cavalry","Mokul Sowar","ha",5),
    ("aserai_mameluke_axeman","Tabardar","inf",3),
    ("aserai_mameluke_guard","Jysh","spear",4),
    ("mamluke_palace_guard","Asad-Ilahi","spear",5),
    ("aserai_youth","Sowar","cav",2),
    ("aserai_tribal_horseman","Bargir-i-Mysore","cav",3),
    ("aserai_faris","Silahposh","cav",4),
    ("aserai_veteran_faris","Khasa Sowar","cav",5),
    ("aserai_vanguard_faris","Sardar-i-Risala","cav",5),
    ("aserai_militia_archer","Shahri Kamani","arch",2),
    ("aserai_militia_veteran_archer","Shahri Tirgar","arch",3),
    ("aserai_militia_spearman","Shahri Neza","spear",2),
    ("aserai_militia_veteran_spearman","Shahri Barchha","spear",3),
])

# ───────────────────────── AFGHAN (overrides Culture.sturgia) — Durrani / hill ─────────────────────────
afghan = build("khuzait","khuzait",[
    ("sturgian_recruit","Naukar","inf",1),
    ("sturgian_warrior","Lashkari","inf",2),
    ("sturgian_soldier","Sipah","inf",3),
    ("sturgian_veteran_warrior","Ghazi","inf",5),
    ("sturgian_shock_troop","Khasadar","inf",5),
    ("sturgian_warrior_son","Ghulam","inf",2),
    ("varyag","Nizam-i-Sipah","inf",3),
    ("varyag_veteran","Durrani Sowar","cav",4),
    ("druzhinnik","Atishin Sowar","cav",5),
    ("druzhinnik_champion","Sardar-i-Durrani","cav",5),
    ("sturgian_woodsman","Yaghi","skirm",2),
    ("sturgian_hunter","Kamangar","arch",3),
    ("sturgian_archer","Tir-i-Kohi","arch",4),
    ("sturgian_veteran_bowman","Mir Kaman","arch",5),
    ("sturgian_brigand","Khaibari","skirm",3),
    ("sturgian_hardened_brigand","Khaibari Sowar","cav",4),
    ("sturgian_horse_raider","Yaghi Sowar","ha",5),
    ("sturgian_berzerker","Ghazi-yi-Tegh","inf",4),
    ("sturgian_spearman","Nezawar","spear",4),
    ("sturgian_ulfhednar","Malang","inf",5),
    ("sturgian_militia_archer","Shahri Kamangar","arch",2),
    ("sturgian_militia_veteran_archer","Shahri Tir","arch",3),
    ("sturgian_militia_spearman","Shahri Neza","spear",2),
    ("sturgian_militia_veteran_spearman","Shahri Nezawar","spear",3),
])

# ───────────────────────── RAJPUT (overrides Culture.vlandia) — mail & plate, lance ─────────────────────────
rajput = build("vlandia","vlandia",[
    ("vlandian_recruit","Paidal","inf",1),
    ("vlandian_footman","Sipahi Rajput","inf",2),
    ("vlandian_spearman","Bhala Rajput","spear",3),
    ("vlandian_billman","Barchhi Rajput","spear",4),
    ("vlandian_voulgier","Naga Rajput","spear",5),
    ("vlandian_pikeman","Bhala-bardar","spear",5),
    ("vlandian_infantry","Khanda-bardar","inf",3),
    ("vlandian_swordsman","Asi Rajput","inf",4),
    ("vlandian_sergeant","Thakur","inf",5),
    ("vlandian_light_cavalry","Sawar","cav",3),
    ("vlandian_cavalry","Bhomia","cav",4),
    ("vlandian_vanguard","Rawat","cav",5),
    ("vlandian_levy_crossbowman","Dhanush","arch",2),
    ("vlandian_crossbowman","Tirandaz Rajput","arch",3),
    ("vlandian_hardened_crossbowman","Kamandar Rajput","arch",4),
    ("vlandian_sharpshooter","Mahatir","arch",5),
    ("vlandian_squire","Kumar","cav",2),
    ("vlandian_gallant","Rajkumar","cav",3),
    ("vlandian_knight","Rajput Sawar","cav",4),
    ("vlandian_champion","Ranbanka","cav",5),
    ("vlandian_banner_knight","Maharathi","cav",5),
    ("vlandian_militia_archer","Garhi Dhanush","arch",2),
    ("vlandian_militia_veteran_archer","Garhi Tirandaz","arch",3),
    ("vlandian_militia_spearman","Garhi Bhala","spear",2),
    ("vlandian_militia_veteran_spearman","Garhi Barchhi","spear",3),
])

# ───────────────────────── MARATHA (overrides Culture.battania) — light, skirmish, cavalry ─────────────────────────
maratha = build("battania","battania",[
    ("battanian_volunteer","Paik","inf",1),
    ("battanian_clanwarrior","Mavala","inf",2),
    ("battanian_trained_warrior","Dhalait","inf",3),
    ("battanian_picked_warrior","Hujarat","inf",4),
    ("battanian_oathsworn","Huzurat Sardar","inf",5),
    ("battanian_scout","Pendhari","cav",4),
    ("battanian_horseman","Bargir","cav",5),
    ("battanian_mounted_skirmisher","Shiledar","cav",5),
    ("battanian_woodrunner","Ramoshi","skirm",2),
    ("battanian_raider","Gardi","inf",3),
    ("battanian_skirmisher","Bhalait","skirm",3),
    ("battanian_falxman","Patta-bardar","2h",4),
    ("battanian_veteran_falxman","Vir Maratha","2h",5),
    ("battanian_veteran_skirmisher","Pendhari Naik","skirm",4),
    ("battanian_wildling","Koli","inf",5),
    ("battanian_highborn_youth","Tirkar","arch",2),
    ("battanian_highborn_warrior","Kamthi","arch",3),
    ("battanian_hero","Dhanurdhar","arch",4),
    ("battanian_fian","Vedh","arch",5),
    ("battanian_fian_champion","Maha-Dhanurdhar","arch",5),
    ("battanian_militia_archer","Gaothan Tirkar","arch",2),
    ("battanian_militia_veteran_archer","Gaothan Kamthi","arch",3),
    ("battanian_militia_spearman","Gaothan Bhala","spear",2),
    ("battanian_militia_veteran_spearman","Gaothan Bhalait","spear",3),
])

# ───────────────────────── SIKH (overrides Culture.khuzait) — Khalsa foot & horse ─────────────────────────
sikh = build("sturgia","sturgia",[
    ("khuzait_nomad","Jawan","inf",1),
    ("khuzait_footman","Sipahi Singh","inf",2),
    ("khuzait_tribal_warrior","Sowar Singh","ha",2),
    ("khuzait_noble_son","Sahibzada","ha",2),
    ("khuzait_hunter","Tirandaz Singh","arch",3),
    ("khuzait_spearman","Nezawala","spear",3),
    ("khuzait_raider","Lutera Sowar","ha",3),
    ("khuzait_horseman","Risaldar Singh","cav",3),
    ("khuzait_qanqli","Akali Sowar","ha",3),
    ("khuzait_archer","Kamangar Singh","arch",4),
    ("khuzait_spear_infantry","Neza-bardar Singh","spear",4),
    ("khuzait_horse_archer","Misldar","ha",4),
    ("khuzait_lancer","Bhala Sowar","cav",4),
    ("khuzait_torguud","Nihang Sowar","ha",4),
    ("khuzait_marksman","Mir Kamangar","arch",5),
    ("khuzait_darkhan","Khalsa Jodha","spear",5),
    ("khuzait_heavy_horse_archer","Jathedar","ha",5),
    ("khuzait_heavy_lancer","Khalsa Sowar","cav",5),
    ("khuzait_kheshig","Akali Nihang","ha",5),
    ("khuzait_khans_guard","Sardar Khalsa","ha",5),
    ("khuzait_militia_archer","Pind Tirandaz","arch",2),
    ("khuzait_militia_veteran_archer","Pind Kamangar","arch",3),
    ("khuzait_militia_spearman","Pind Neza","spear",2),
    ("khuzait_militia_veteran_spearman","Pind Nezawala","spear",3),
])

ALL = mughal + mysore + afghan + rajput + maratha + sikh

def main():
    body = "\n".join(troop_xml(t) for t in ALL)
    doc = ('<?xml version="1.0" encoding="utf-8"?>\n'
           '<!-- Takht ya Taboot authentic troop trees. GENERATED by tools/gen_troops.py — do not hand-edit.\n'
           '     OVERRIDES vanilla troop ids in place (name/face/skills/equipment); vanilla keeps the tier\n'
           '     and upgrade wiring. One tree per culture, new troops everywhere, each culture a distinct\n'
           '     vanilla equipment family. -->\n'
           '<NPCCharacters>\n' + body + '\n</NPCCharacters>\n')
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(doc)
    print(f"Wrote {len(ALL)} troop overrides across 6 cultures to {os.path.normpath(OUT)}")

if __name__ == "__main__":
    main()
