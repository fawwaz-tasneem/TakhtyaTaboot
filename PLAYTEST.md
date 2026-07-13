# Playtest Plan — Stability Pass + Foundations Pass + July Waves

Covers every component built across the passes. Work through in order; sections A–D
are the first pass (stability, zamindari, village fiefs, core wave), E–I the foundations
pass (farmaans, opinions, dynasties, dialogue, court menu), **J–N the July 2026 waves**
(unified empire, clan safety net, succession economy + treachery, siege parley, the two
Gauntlet screens), **O the akhbaar scouts, P bonded labour, Q coronation ceremonies, R monsoon
harvest+famine, S village-jagir rotation, T fief petitions, U darbar petition court, V zat/sawar
split, W clan-screen zamindari** — J–W are the CURRENT focus; A–I were exercised in playtest
rounds 1–3.

**Status ledger (2026-07-12, commits `6bad71f` → HEAD):** A–I built + playtested
(rounds 1–3); J–Y built, unit-tested (362 green) and statically verified against the
v1.3.11 decompile. **Playtest round 4 (2026-07-12)** exercised the akhbaar scout (works), the
darbar court (worked but felt shallow → REWORKED as dialogue, see U), fief petitions (two grants
seen in the log), the monsoon roll, and found + fixed: the works-ledger CRASH on Begin (N4, the
int/float binding — Modding-Findings ch.19) and the missing coronation on FOUNDING a kingdom
(Q1). **Round-5 request (2026-07-12): the coronation now happens IN THE HALL** — the summons →
travel to your keep → lords stand bodily in the lord's hall and swear in dialogue (section Q
fully rewritten). **Round 6 (2026-07-12, screenshots):** the hall ceremony WORKS live; feedback
shipped as section Z + the Q2 procession rework (lords address the SEATED sovereign one by one,
7 culture-keyed oath variations), the farmaan layout fix (title was at the bottom), encyclopedia
button placement + native "Last seen" updates (the "4 scouts to no avail" — reports had in fact
all delivered), the qasid messenger (Diplomacy parity), and spoken follow-me orders.
**STANDING DECISION: no Diplomacy mod** — its mechanics are integrated natively instead
(X war exhaustion, Y secession/abdication conspiracies; ROADMAP block E: messengers now SHIPPED,
alliances/NAPs + coalitions remain). Priority re-verify: **Q2 (the procession), Z1 (farmaan
layout, needs the prefab deployed), Z3 (qasid audience), X/Y (Y5 graduation still riskiest)**,
plus the still-unseen J3 breakaway, M2 siege unwind, and W (clan-screen zamindari).

## Setup

1. Build Release (`dotnet build src\TheHindostanMod\TheHindostanMod\TheHindostanMod.csproj -c Release`) — output lands in `bin\Win64_Shipping_Client`.
2. Enable the developer console for the `hindostan.*` commands.
3. Keep `tyt_log.txt` open throughout — **any `[ERR]`/`TYTLog.Error` line is a finding**, note it with what you were doing.
4. Run the whole plan twice if possible: once on a **new campaign**, once on a **pre-pass save** (save-compat matters).
5. On first launch, confirm the log line: `Harmony: N patch classes applied, 0 failed`.

For the report: for each numbered check mark PASS / FAIL / SKIPPED, and for FAILs give repro steps + the matching log lines. Screenshots for UI checks help.

---

## A. Stability (crash fixes)

- **A1.** New campaign → play to day 5 unpaused. No crash (the world-gen race class), no error spam.
- **A2.** Save → load → save → load. Encyclopedia hero pages before and after; no "SAVE ERROR ... TextObject" lines in the log.
- **A3.** Play to the first scripted accession (~2 months in). Expect: death + accession farmaans, the heir crowned, **NO** simultaneous "war of princes" crisis popup for the empire, and the Maratha war still on afterwards.
- **A4.** Accession war both ways: `hindostan.set_rank 6`, challenge the throne from the mansab menu, then `hindostan.force_accession_win`. Expect: peace concluded, rebel kingdom gone within a day, you are sovereign. Repeat on another save and *lose* (let the 45-day deadline pass while weak): rank stripped, rebel kingdom gone, no lingering war (check the diplomacy screen).
- **A5.** Win a war and impose **Make them a tributary**, then console-declare war on the tributary. Expect exactly one "The Tributary Yoke Is Cast Off" farmaan — no daily peace/war flapping.
- **A6.** Impose **total submission (Subjugate)** on a small realm. Expect: clans join you, kingdom destroyed cleanly, no exception, no ghost war.
- **A7.** Under-muster warning numbers must match the clan-wide sawar shown in the mansab menu (not just your main party).

## B. Zamindari / hierarchy

- **B1.** `hindostan.village_lords` — no vacant villages with living notables available.
- **B2.** Open the hierarchy screen (court menu) in the largest realm: opens fast, every lord appears (nobody "vanished"), and **your displayed liege is the same person** who sends your tribute demand (`hindostan.tax_now`) and your summons (`hindostan.summon`).
- **B3.** Conquer (or cheat-transfer) a town: lord-zamindars of its bound villages get reseated; local notable zamindars stay.
- **B4.** As a **mercenary**, try "claim your due": refused, and your influence is NOT spent.
- **B5.** `hindostan.grant_village` → your encyclopedia page shows "Village Zamindar" (not "Unlanded Noble") and the village appears in the feudal layer.
- **B6.** Rank 1 is displayed everywhere as **Mansabdar-e-Sad** — the word "Zamindar" only ever appears as the feudal village-lord tier.

## C. Village fiefs

- **C1.** At rank 1, claim your first village → "Oversee your fief" shows hearth, threat, **coffer + tax/day estimate**, zamindar.
- **C2.** Projects: build Granary; confirm **Great Granary is gated** until it finishes; queue a second work → it auto-starts on completion and charges *then* (or cancels with a notice if you're broke).
- **C3.** Collect taxes; collect again within the week → notables take it ill (relation penalty message).
- **C4.** Appoint a high-Steward notable as zamindar → the tax/day estimate visibly rises; high-Engineering zamindar → construction days tick down faster than 1/day.
- **C5.** AI development: within ~2 weeks, log lines `AI village work: ...`; spot-check an AI village later in the campaign for completed works.
- **C6.** Weekly court grants: log lines `Court grant: ... seated as zamindar ...` for unlanded AI lords.
- **C7.** Threat loop: `hindostan.set_village_threat 90` → hearth declines daily, plea farmaan arrives (and NOT again within 12 days), sending the relief detachment removes ~40 men and returns them after 6 days.
- **C8.** Old save: pre-pass villages keep threat/works; coffers start at 0; no crash.

## D. Core-wave systems

- **D1.** **Nazrana (vassal)**: lower the cycle days in MCM → the call arrives, present each tier once across saves (token/expected/lavish — relation/influence effects differ); let 3 calls lapse → mansab reduced with the rebuke farmaan.
- **D2.** **Nazrana (ruler)**: weekly trickle of payments; monthly summary — routine when clean (log/digest), full farmaan when lords withhold.
- **D3.** **AI civil war**: `hindostan.force_ai_civil_war` → a `hind_rebel` claim kingdom, the side-choice farmaan if it's your realm (try both answers on separate saves), resolution within 45 days (`hindostan.civil_war_status`), clans fold back, peace concluded, winner/loser effects applied.
- **D4.** **Tolerance**: as ruler decree each stance → in a faith-mismatched town watch loyalty over 3 days (falls under Strict, rises under Tolerant); enact the jizya → your clan income tooltip shows "The jizya", a mismatched lord's shows "The realm's faith policy".
- **D5.** **Court factions**: rule for a month → a petition farmaan from the dominant party; accept and refuse (separate saves) → relation shifts with that party's lords.
- **D6.** **Monsoon**: in summer the party-speed tooltip shows "Monsoon mud" (×0.70), in autumn "Dry roads of the marching season" (×1.10); the MCM toggle removes both.

## E. Farmaan overhaul

- **E1.** **Pause**: any farmaan freezes the campaign clock; dismissing resumes it. A *chain* of queued farmaans stays paused until the last is dismissed. If you paused manually *before* the farmaan, you stay paused after dismissing.
- **E2.** MCM "Farmaans pause the game" off → no pause.
- **E3.** **Downgrades**: on stipend day, expect a log line ("The court records: ...") and NO popup; once ≥3 routine items accumulate, the weekly **Court Circular** digest farmaan lists them.
- **E4.** **Dedup**: fire `hindostan.tax_now` twice back-to-back → exactly one tribute popup.

## F. Personal opinions

- **F1.** Swear fealty (dialogue, F below) → your liege's encyclopedia page gains "Disposition toward you: ... (an oath sworn)".
- **F2.** Refuse a court-faction petition → those lords' pages show a grudge modifier.
- **F3.** Over a few weeks, log lines "words exchanged at court between X and Y" (organic quarrels).
- **F4.** As ruler, order a vassal party (map orders) — a lord holding a grudge refuses more readily than one holding your favour.

## G. Dynasty & cadet houses

- **G1.** **Royal styles**: emperor's children show *Shahzada/Shahzadi* (hierarchy tree + encyclopedia "Royal style" row); a Maratha ruler's son shows *Yuvraj*; a Sikh ruler's son *Kanwar*. After the scripted cascade, children of the DEAD emperors keep their style (the roll of sovereigns).
- **G2.** **Charter a cadet house** (court menu; needs an adult non-heir kinsman, 25 000 dinars, 100 influence): pick the kinsman → new clan appears in the kingdom screen named "House of {founder}", encyclopedia page opens without crashing (banner, leader present). **CRITICAL — report the log line**: `(warband raised)` vs `(no warband yet — leader waits at his seat)`; if the latter, note whether the founder's party appears on the map within a few weeks anyway.
- **G3.** With the sovereign's opinion of you below 0 → charter refused by farmaan, nothing charged.
- **G4.** Over a year+, occasional "A New House Is Chartered" for AI clans; total never exceeds the MCM cap.
- **G5.** Save → load with a cadet house alive → intact (members, kingdom, dynasty link).

## H. Dialogue pack (talk to the right person for each)

- **H1.** **Fealty** — option appears ONLY with your feudal liege, disappears after swearing (returns when the oath decays).
- **H2.** **Nazrana in person** — only with your sovereign while a call is pending; presenting closes the call with a warmer reception than the popup path.
- **H3.** **Grievance** — needs a live grudge (create one via F2): "There is a shadow between us..." → mend for 500 dinars (grudge cleared, favour written) or press it (both sides' grudges deepen).
- **H4.** **Village notable** — in your village, reassure the notable → threat drops ~5, small relation gain.
- **H5.** **Prince** — greet any styled royal ("Adaab, Shahzada") → his succession answer differs for the heir, a resentful brother, and a content one.
- **H6.** **Council invite** — as a council holder, invite an unposted lord (−20 influence) → he tops the candidate list at your next appointment.

## I. Court menu consolidation

- **I1.** Town and castle root menus show exactly ONE mod entry: "Attend to matters of court and realm" (plus the village-trade/oversee options in villages). Inside it: survey the empire, councils/Darbar, convene options, capital move (towns), tolerance decree (ruler), hierarchy, summon levies, charter cadet house. Back returns to the settlement.
- **I2.** None of those options still appear at the town/castle root (no duplicates).

---

## J. Unified Empire (A.1 + A.0, ships in `UnifiedEmpireBehavior`)

- **J1.** New campaign → instant gold message "The empire stands whole…", Bengal/Hyderabad
  towns fly imperial banner AND imperial clan colours (shields/trims — the A.0 colour fix),
  encyclopedia Kingdoms page does NOT list Bengal/Hyderabad, `hindostan.unified_status`
  says `Phase: Unified`, empire ~30 clans. *(Fold itself confirmed in round 2/3; colours +
  encyclopedia hiding are new.)*
- **J2.** First daily tick → "One Throne, One Hindostan" farmaan.
- **J3.** **THE BREAKAWAY (never seen live):** play/cheat past month 2 (Aurangzeb's death).
  Expect in order: death farmaan, Bahadur Shah accession farmaan, "Bengal Stands Apart" +
  "The Deccan Raises Its Own Banner" farmaans; both realms reappear on map/encyclopedia in
  their ANCESTRAL colours; Nawab (Nasiri) and Nizam (Asaf Jahi) are the rulers;
  `unified_status` says `Sundered`; Mysore↔Hyderabad war resumes; intra-Mughal peace holds.
- **J4.** While unified, Mysore must NOT be at war with the empire (round-3 change).
- **J5.** Save during the unified window → load → breakaway still fires on schedule.
- **J6.** Old save (pre-feature): loads, nothing unifies, no farmaans, `Phase: NotArmed`.

## K. Clan safety net (`ClanSafetyNetBehavior`)

- **K1.** In the round-2 save with kingdomless ex-claim clans: within ~3 days each masterless
  house swears to a realm (gold message "The masterless house of X swears to Y") — check
  faith/nearness look sane.
- **K2.** `hindostan.force_ai_civil_war`, let the rebel side LOSE all its fiefs mid-war →
  the claim kingdom must NOT be scattered by the engine (vanilla cull vetoed); on resolution
  its clans are back in the realm — nobody kingdomless a week later.
- **K3.** No noble clan sits at Kingdom == null for more than ~4 days at any point (spot-check
  encyclopedia after big wars).

## L. Succession economy + treachery (persuade menu, `hindostan_succession`)

- **L1.** Force a crisis with the incumbent among the claimants; open "Persuade a rival
  claimant to stand down" targeting the SITTING king → offer text mentions his years reigned;
  price should be millions for Aurangzeb (49y seeded) vs ~1/6 for a fresh cascade emperor.
- **L2.** With the king ≥3:1 against you, the offer dialog shows the BEWARE treachery warning.
  Refusal → either the wrathful "The Throne Is Not For Sale" farmaan (warnings/demands) or
  the full treachery decree (banished + war). Both texts should read furious.
- **L3.** After treachery: you are out of the realm (fiefs kept), at war; get captured by that
  kingdom → one of three fates fires (execute farmaan → death; fine → gold gone, released,
  peace; the fort → monthly ransom farmaan "A Price for Your Chains", pay → free + peace).
  The fort's monthly 5% death roll: leave a save parked to confirm it can fire.
- **L4.** Old save: no treachery state, everything else as before.

## M. Siege parley (`menu_siege_strategies` → "Send an envoy to the qiladar")

- **M1.** Besiege with <2:1 odds → parley text says resolve firm, both options disabled with
  explanatory tooltips (no dice-rolling gold sink).
- **M2.** At ~2:1+ → bribe enabled with a concrete price; pay → gates open, settlement YOURS
  bloodlessly, garrison gone, siege UI unwinds cleanly back to map (**the risky engine path —
  watch for stuck encounter/menus**), no errors in tyt_log.
- **M3.** At ~3:1 + low food → terms enabled; accept → honour/defy farmaan. HONOUR: legitimacy
  +4, authority +2, old owner relation +3. DEFY: garrison lands in your prisoner roster,
  legitimacy −8, authority −3, relations/grudges written, town loyalty −10.
- **M4.** In an army you don't lead → option disabled ("Only the commander of the siege…").

## N. The two Gauntlet screens (round-3 layout fix applied — verify orientation FIRST)

- **N1.** Hierarchy board: title on TOP now; sovereign's card (with FACE) top-centre, stub
  line down, one column per direct vassal (faces on every card), horizontal + vertical
  scrollbars work, clicking cards opens the encyclopedia.
- **N2.** "Show the zamindars" toggle: off by default; on → zamindars appear on the LOWEST
  rung of each branch with connector elbows; toggle back off.
- **N3.** Castle lords hang under the NEAREST town lord column (round-3 liege change), and
  `hindostan.tax_now` / `hindostan.summon` come from that same town lord (B2 invariant).
- **N4.** Village works ledger ("Open the works ledger" in your village): header numbers
  match the old menu, progress BAR fills as days pass, Collect button works + refreshes,
  Begin/Queue per project with correct disabled reasons; ESC and the X both close it.
  **ROUND-4 CRASH FIXED (2026-07-12):** clicking Begin (the mosque case) crashed the game —
  `BarWidth` was an int bound to a float widget attribute (see Modding-Findings ch.19). Re-run
  this check, especially STARTING a work while the ledger is open and watching the bar appear.
- **N5.** Council + Farmaan screens were ALWAYS rendered upside-down (see wiki findings
  ch.18) and were left untouched — if their reversed layout bothers you now that you know,
  ask for the flip (one-line change each, needs a visual check after).

## O. Akhbaar scouts (`AkhbaarScoutBehavior`, court menu → "Dispatch an akhbaar scout after a lord")

*New this wave (2026-07-11) — built, unit-tested (23 new `AkhbaarMathTests`, 289 green), never
run live. This is the seed of the wider akhbarat espionage layer (wiki ch.17).*

- **O1.** In any town/castle court menu, the entry "Dispatch an akhbaar scout after a lord"
  appears. Opening it lists scoutable lords (heads of houses + lords in the field, not your
  own clan), **your realm's lords sorted first**, each with a fee. Cost sanity: a famous
  (high-tier) house should be cheaper than an obscure low-tier one; a foreign-realm lord costs
  ~1.5× a home lord of the same tier. Lords you can't afford / already track are greyed with a
  reason in the hint.
- **O2.** Dispatch one → gold deducted, message "Your harkara slips out after X … expect his
  akhbaar in some N days." Re-open the menu → that lord now greyed ("a scout is already on his
  trail"). The court entry's tooltip shows the count on the road.
- **O3.** **The report (the payoff):** wait N days (or `hindostan.akhbaar_arrive` to force it).
  A farmaan "Akhbaar: {lord}" arrives from your harkara. Verify the body reads as HEARSAY, not a
  data dump: a rounded count ("some 75 men"), a worded strength tier, and a composition line
  ("chiefly horse, with foot and bows") — never an exact roster. It should name what he's doing
  (marching near a holding / besieging / quartered in a town / in a battle) and where.
- **O4.** Scout a lord with **no war band** (a clan head sitting at court) → report says he keeps
  to his town / has gone to ground, no host under his banner. Scout an **imprisoned** lord →
  report says CAPTIVE and where he's held. Scout a lord, then let him **die** before the runner
  returns (or `akhbaar_arrive` after killing him) → report says he's dead, business passes to
  his heirs (no crash, no null lord).
- **O5.** **Save/load:** dispatch a scout, save mid-road, load → `hindostan.akhbaar_status`
  still lists it with the right days remaining; it still delivers on schedule. Deliver a report
  while another screen is up (not the map) → farmaan queues and shows without re-entrancy issues.
- **O6.** Console: `hindostan.akhbaar_send <name>` dispatches a free scout arriving next daily
  tick; `hindostan.akhbaar_status` lists the road; `hindostan.akhbaar_arrive` forces delivery.
- **O7.** Old save (pre-feature): loads clean, no scouts, court entry present and usable.
- **O8.** **Encyclopedia surface (round 4 — replaces hunting the list):** open any foreign lord's
  encyclopedia page → a "Dispatch a scout (N dinars)" button sits under the bookmark star; click →
  confirm inquiry → dispatched (works from the map too, not just in a settlement — the scout rides
  from your camp). While the scout is out the button reads "A scout is on his trail…", and the
  Info section gains an "Akhbaar" row ("a harkara is on his trail (akhbaar in ~N day(s))"). After
  the report arrives, the row shows his LAST REPORTED whereabouts with age ("last reported
  besieging X — 3 days ago"), persisting across save/load. The court-menu list still works.

## P. Bonded labour in villages (`SlaveLabourBehavior`, village menu → "Settle captive labourers here")

*New this wave (2026-07-11), user-requested — built, unit-tested (12 new `SlaveLabourMathTests`,
301 green), never run live. Extends the village-fief tax/threat pipeline; MCM toggle "Allow
bonded labour in villages" under Village Fiefs (on by default).*

- **P1.** Win a battle, take common prisoners, ride to a village you hold → "Oversee your fief"
  → the option "Settle captive labourers here" is enabled. With no captives, or a full gang, it's
  greyed with the right tooltip. In a village you DON'T hold, the option is absent. Turn the MCM
  toggle off → the option disappears entirely.
- **P2.** Settle → a confirmation inquiry states the count and the trade-off; confirm → that many
  common prisoners LEAVE your prison roster, the fief menu shows "Bonded labourers: X/cap (+Y%
  tax, +Z unrest/day)". Cap sanity: a bigger-hearth village allows a larger gang (≈ hearth/40,
  floored at 5, capped at 60). Captured **lords are never taken** (only non-hero prisoners).
- **P3.** **The yield:** with a gang settled, the coffer's "~/day" tax estimate rises vs. before
  (and the bound TOWN's prosperity ticks up daily). Verify the tax bonus roughly matches +0.4%
  per labourer.
- **P4.** **The cost (unrest):** watch the village's bandit threat over several days — it climbs
  faster than an identical village with no gang, and a **near-full** gang pushes threat harder
  than a sparse one. Confirm a Watchtower/Kotwali does NOT fully suppress it (unrest is added
  after the watch multiplier by design). Push `hindostan.set_village_threat 90` then hold a gang:
  expect faster attrition (see P5).
- **P5.** **Attrition & escape:** over days, "N bonded labourer(s) are lost — M fled to the bandit
  country" messages appear; the gang count drops; when men flee, the village threat bumps up. Loss
  is faster at high threat (~5%/day) than in a calm village (~1%/day). A gang can dwindle to zero
  and the line disappears.
- **P6.** **Free them:** "Free the bonded labourers" → gang cleared, a threat drop, small notable
  relation gain, confirming message. Option then disappears (nothing to free).
- **P7.** **Save/load:** settle a gang, save, load → `hindostan.labour_status` still lists it with
  the right count/cap; yields and attrition resume. Old save (pre-feature): loads clean, no gangs.
- **P8.** Console: `hindostan.settle_labour <n>` adds a free gang (no captives spent) up to cap;
  `hindostan.labour_status` lists every village's gang with its tax/unrest.

## Q. Coronation ceremonies (`CoronationBehavior`; `hindostan.coronation_test`) — REWORKED round 5: held IN THE HALL

*Round-5 rework (2026-07-12, "lords physically present inside the hall in my keep"): the player's
own coronation is no longer an instant popup. Acceding/founding SUMMONS the darbar; you travel to
a keep of your realm and hold court in the lord's hall, where the attending house heads stand
bodily before the throne and swear IN DIALOGUE. 5 new `CoronationMathTests` (362 green). The
in-hall staging (LocationCharacter materialisation + mission open) has NEVER run live — Q1/Q2
are the riskiest items in this section.*

- **Q1.** **The summons → the hall (RISKIEST).** Take/found a throne (or `hindostan.coronation_test`
  as ruler) → "The Coronation Darbar Is Summoned" farmaan (no instant verdict). Travel to any
  town/castle of YOUR realm → keep menu shows **"Hold your coronation darbar in the hall"** (on
  `town_keep` and `castle` menus, right under "Go to the lord's hall"). Click it: the lord's hall
  mission opens and the ATTENDING house heads stand in the hall (native keep-notable stand-ins —
  their map parties don't move). Verify: lords visible, hall doesn't crash on entry, leaving the
  hall works.
- **Q2.** **The procession (ROUND-6 REWORK — the sovereign is ADDRESSED, he does not go asking).**
  Take your place — stand at the throne — and after ~5 seconds the attending lords come to you
  ONE BY ONE (`CoronationProcessionLogic`): each walks to two paces before you, faces you, and
  opens the conversation himself. Each swears **in his own culture's voice — 7 variations per
  culture** (Mughal/Bengali/Hyderabadi/Afghan/Mysorean/Rajput/Maratha/Sikh, `CoronationOaths`,
  14 new tests), the same lord always speaking the same oath, coloured warm/even/cold by his
  regard. Spoken oaths land +3 relation vs +2 unspoken. You can still approach anyone by hand
  (the round-5 floor); a lord you already heard is skipped by the procession. Leaving the hall
  closes the ceremony: the verdict farmaan fires, legitimacy shifts, attendees never heard still
  count (+2, "entered from the dais"). NOTE: you STAND at the throne — the engine has no player
  throne-sitting; the procession comes to wherever you place yourself.
- **Q3.** **The empty places.** With absentees, the verdict farmaan still offers "Demand a late
  oath" → "The Late Oath" follow-up (warmer lords bend, colder defy and take a grudge → grievance
  dialogue H3). Check opinion records: attendees "an oath sworn", absentees "an empty place".
- **Q4.** **The courier fallback.** On the summons farmaan pick "Take the oaths by farmaan" (or
  hold no court for 14 days): the OLD instant resolution fires, framed as oaths taken by courier.
  A summons must never dangle: dethroned/eliminated before holding court → silently void.
- **Q5.** **Player as vassal — swear in person.** Non-ruling clan, realm's ruler changes → "A
  Summons to the Coronation". "I will travel to court and swear in person" → a message says where
  the sovereign is; find him (his throne room ideally, anywhere works) → new dialogue line
  "Jahanpanah, I answer the summons…" → +6 relation and SworeFealty. Ignore him for 14 days →
  counts as staying away (−8, MissedCeremony). "Send regrets" on the farmaan → immediate snub.
- **Q6.** **AI accessions are silent** (no farmaan for foreign realms; no darbar per beat of the
  scripted 1707 cascade). **Save/load:** pending summons + pending vassal oath persist (new
  `hind_coron_pend_*`/`hind_coron_oath_*` keys); the live in-hall ceremony state is deliberately
  NOT serialized (you can't save inside the mission). Old saves load clean.

## R. Monsoon harvest + famine (`MonsoonBehavior`; `hindostan.monsoon_status / set_monsoon`)

*New this wave (2026-07-11) — built, unit-tested (11 new `SeasonMath` harvest/famine cases,
324 green), never run live. Extends the monsoon (previously speed-only). MCM "Monsoon drives
the harvest" under Seasons (on by default).*

- **R1.** Play across a monsoon (summer) → in a notably good or bad year, a farmaan ("A
  Bountiful Monsoon" / "The Rains Have Failed") announces it; a middling year is a quiet log
  line only. `hindostan.monsoon_status` shows the year's quality and the current harvest tax
  multiplier.
- **R2.** **Harvest swing:** hold a village; compare its coffer "~/day" and accrual across
  seasons. In **autumn (post-monsoon)** after a **good** year it accrues clearly faster than
  base; after a **bad** year, clearly slower. Hot season and the monsoon itself carry no swing
  (×1.0). Force it: `hindostan.set_monsoon 1.0` vs `hindostan.set_monsoon 0.1`, watch autumn.
- **R3.** **Famine:** `hindostan.set_monsoon 0.05`, then enter a village you hold in autumn/
  winter with a thinnish hearth and some threat → within a few days a "Famine in the District"
  plea. **Open the granaries** (pay) → people fed, threat down ~10, notable relation up, small
  hearth cost. On a separate save, **let them fend** → hearth drops hard (~30), threat up ~12,
  relation down. One famine per village per year (no repeat spam).
- **R4.** Famine never fires in a fair/good year (`set_monsoon 0.6` → no pleas), and never in
  hot season / monsoon (only the autumn–winter harvest window).
- **R5.** **Save/load:** the year's quality and famine-fired record persist; no double famine
  after load. Old save (pre-feature): loads clean, quality defaults to a neutral year.
- **R6.** MCM "Monsoon drives the harvest" off → no harvest swing, no famine, no monsoon
  farmaan (the speed effect, governed by the separate "Monsoon slows parties" toggle, is
  unaffected).

## S. Village-jagir tenure rotation + opinion records (`MansabdariTenureBehavior`)

*New this wave (2026-07-11) — extends the existing town/castle rotation to village jagirs and
adds Favor/Grudge opinion records. No new pure math (reuses tested `MansabTenureMath`); 324
green. Requires a realm under Mansabdari tenure (`hindostan.tenure_mansabdari`).*

- **S1.** Put your realm under Mansabdari (`hindostan.tenure_mansabdari`), then
  `hindostan.tenure` → it now reports **two** overdue lists: town/castle fiefs AND village
  jagirs (villages with an AI **lord** zamindar, not local notables).
- **S2.** `hindostan.tenure_rotate_village` (as sovereign) → an AI lord zamindar is rotated off a
  village and a deserving lord seated in his place (fewest villages, best relation to you). The
  encyclopedia/hierarchy should show the new zamindar; the old one is unseated. Message reports
  comply/grumble/dismiss per the ladder.
- **S3.** **Opinion records:** after a rotation, check dispositions — the **new** zamindar carries
  "a favour done" toward you; a **dismissed/defiant** one carries "an old grudge" (making him a
  grievance-dialogue target, playtest H3). This now also applies to **town/castle** rotations
  (S-adjacent: run `hindostan.tenure_rotate` and check the same records).
- **S4.** **Player as village zamindar being rotated:** hold a village jagir as a vassal under a
  Mansabdari sovereign; when its term is up (or force via console as the AI crown), an "Order of
  Transfer (Zamindari)" farmaan offers comply (surrender, the crown favours you) or defy (gamble
  your local roots — hold and the crown resents it, or fail and lose the village + influence).
- **S5.** Local **notable** zamindars (non-lords) are never rotated — only lord-held village jagirs.
- **S6.** Save/load: the rotation clock persists for villages (the `_appointed` map is shared);
  no double-rotation after load. Under feudal (non-Mansabdari) law, nothing rotates.

## T. Fief petitions replacing instant claim (`FiefPetitionBehavior`, mansab menu)

*New this wave (2026-07-11) — replaces the old instant "claim your due" (playtest complaint:
Kanpur/Lucknow were instantly claimable). Built, unit-tested (6 new `FiefPetitionMathTests`,
330 green), never run live.*

- **T1.** As a vassal eligible for a fief (rank ≥1, none held at your rank height), open the
  mansab menu (town/castle → "Review your mansabdari rank") → "Petition the court for a fief
  befitting your rank". It should NO LONGER instantly grant a fief. Instead an inquiry offers
  modest/handsome/lavish stakes (gold gift + influence), each showing a weekly grant chance;
  unaffordable tiers are greyed.
- **T2.** File a petition → the gold gift and influence are deducted now; the mansab menu shows a
  "Fief petition standing" line. `hindostan.petition_status` shows tier, stakes, sovereign regard,
  whether a fief is available, and the weekly chance.
- **T3.** **The queue engine:** with a qualifying fief available, advance weeks (or
  `hindostan.petition_resolve` to force a pass). The court grants it probabilistically — a
  "Your Petition Is Granted" farmaan, the fief/zamindari seated. A **lavish** stake with good
  relations should resolve in far fewer passes than a **modest** one with poor relations.
- **T4.** **Refusal below the floor:** drop your relation with the sovereign below −10 (e.g. via
  a grudge), file/keep a petition, resolve with a fief available → "Your Petition Is Refused",
  petition closed, **stake forfeit** (gift AND influence kept by the court). The filing inquiry
  should warn you when you're below the floor.
- **T5.** **Withdraw:** open the menu with a standing petition → "Review your standing fief
  petition" → withdraw → influence refunded, the gift stays spent.
- **T6.** **No fief available:** file when no qualifying fief is currently free (all held) → the
  petition waits (no grant, no refusal) until one frees up. Becoming **sovereign** while a
  petition stands closes it and refunds the influence (a sovereign grants, not petitions).
- **T7.** Save/load: a standing petition persists (tier, stakes, filed day). Old save: no
  petition; the menu option now files rather than instant-grants.

## U. Darbar petition court AS DIALOGUE (`DarbarPetitionBehavior`; sovereign's Darbar → "Hear a petition")

*Round-4 REWORK (2026-07-12) of the inquiry-screen version the playtest called shallow. The
sitting now runs as a CHAIN OF CONVERSATIONS before the throne: the plaintiff speaks, the
defendant answers, your advisor counsels, and you speak the judgment. Same tested outcome math
(`DarbarCourtMath`, CourtRuling records). The conversation chain itself is UNVERIFIED LIVE —
this is the riskiest new engine path of round 4 (map conversations chained through a tick pump).*

- **U1.** As a sovereign in your town/castle, court menu → "Hold court and issue decrees" →
  "Hear a petition and render judgment" (`hindostan.darbar_petition` bypasses the 3-day cooldown).
  A CONVERSATION opens with the **plaintiff** (a zamindar / a raided village's notable / a market
  notable, drawn from your realm), who states his case in his own words.
- **U2.** **Act I — the plaintiff:** press him ("what proof do you bring?") → he answers once (the
  option then disappears). Options: call the accused forward (disputes), take counsel (pleas, if
  an advisor is seated), judge directly (pleas with no advisor), or adjourn.
- **U3.** **Act II — the defendant** (disputes): the accused's conversation opens on its own a
  beat after the plaintiff's closes (**verify the chain doesn't stall here** — if it does, that's
  the finding). Press him to swear; then judge directly, take counsel, or adjourn.
- **U4.** **Act III — the advisor:** if your council seats a wazir (or diwan), "take counsel"
  opens HIS conversation: political advice leaning toward whichever party HE likes better (or the
  middle way) — defy it freely. Judgment options appear here too, as spoken rulings ("The throne
  has heard…").
- **U5.** **Outcomes (unchanged math):** rule for one side → he warms ("a judgement at court"
  record, +relation), the other cools; influence up, a little legitimacy. Compromise → both warm
  slightly, influence SPENT. Dismiss → both sour, legitimacy LOST. Plea: grant (deep gratitude +
  legitimacy) / refer (mild) / turn away (sours, legitimacy drops). Check the encyclopedia
  "Disposition toward you" rows afterward (grievance-dialogue fodder, H3).
- **U6.** **Abandonment safety:** ESC-ing out mid-testimony adjourns the sitting cleanly ("without
  judgment"); the adjourn option does the same; no stuck state, no re-opened conversation. The
  cooldown still applies (the docket was spent). Save/load mid-sitting is impossible (conversations
  block saving); after any load there is no lingering sitting.
- **U7.** With no eligible parties (a tiny, vassal-less realm): "No petitioner brings a case worth
  the crown's time."

## V. Zat/sawar split ranks (`MansabdariBehavior` display + stipend)

*New this wave (2026-07-11) — mostly re-labelling numbers already tracked, plus the stipend now
follows zat. 4 new `MansabRankMathTests` (340 green). Existing mansab progression is unchanged.*

- **V1.** Mansab menu (town/castle → "Review your mansabdari rank"): the rank line now reads
  "{Title} — zat X / sawar Y", with a note that ZAT gates fiefs+stipend and SAWAR is the muster
  obligation, and a "Your muster: A of Y men required" line. The encyclopedia hero page's "Mansab"
  row shows the same "zat X / sawar Y".
- **V2.** **Stipend follows zat:** across a 30-day stipend cycle as a vassal, the "A Stipend from
  the Treasury" note now cites your zat and pays MCM "Stipend per point of zat" × your zat
  (default 0.4). A higher-zat noble draws a larger stipend than a low one regardless of how many
  troops he currently fields. MCM "Stipend per point of zat" 0 disables it.
- **V3.** Fief eligibility is unchanged (still zat-tier gated: village ≥ Mansabdar-e-Sad, castle ≥
  Qiledar, town ≥ Subahdar), and the muster/retention floor still uses the sawar target — confirm
  promotion/demotion behave exactly as before (no regression from the relabel).
- **V4.** Old save: loads clean; ranks intact; the stipend simply switches basis to zat.

## W. Clan-screen zamindari visibility (`UI/ClanFiefsZamindariMixin`) — UI-ONLY, VERIFY LIVE

*New this wave (2026-07-11). A UIExtenderEx mixin — no unit tests possible (pure UI). Fully
guarded (failure = villages just not listed, never a crash). This is the ONE feature of this
wave that could not be verified without opening the screen; please eyeball it first.*

- **W1.** **The core check (open the screen):** be a village zamindar of a village you hold ONLY
  through the feudal layer (a village beneath an AI town-lord — e.g. `hindostan.grant_village`
  while a vassal, or seat yourself via the village menu). Open the clan screen (kingdom/clan
  management) → **Fiefs** tab. Confirm it OPENS WITHOUT ERROR and your zamindari village(s) now
  appear as fief cards. (Before this, they never showed there at all.)
- **W2.** No duplicates: a village that took real engine ownership (already in the vanilla list)
  must appear ONCE, not twice.
- **W3.** Clicking a zamindari card should not crash (its select action is a deliberate no-op —
  the vanilla town/castle selection logic is bypassed for these injected villages).
- **W4.** Known cosmetic caveat to note in your report: injected villages currently sit in the
  **Castles** bucket (the header count may read one short), because there is no dedicated
  "Zamindari" section yet. If the plain listing reads oddly, say so and I'll add a labelled block
  (a prefab change that itself needs a visual pass).
- **W5.** If the Fiefs tab ever throws (check `tyt_log.txt` for `ClanFiefsZamindari...`), that is
  the finding to report — the guard should prevent it, but this path is unverified live.

## X. War exhaustion (`WarExhaustionBehavior`; no-Diplomacy mandate)

*New (2026-07-12) — 12 new `WarExhaustionMathTests` (357 green), never run live. Replaces the
old time-only "realm wearies" counter as the source of the council's peace advisory.*

- **X1.** At war, `hindostan.exhaustion_status` lists both sides' exhaustion per war with tiers
  (fresh/strained/weary/reeling/spent). It should CLIMB with battles (casualties on each side,
  scaled so small realms tire faster), jump when a fief falls (+8 to the loser) or a village is
  raided (+3 to the defender), and creep daily.
- **X2.** "Direct the war effort" (sovereign, war menu) now shows "exhaustion ours/theirs" per
  war, with tier words in the hint. The council's "Realm Wearies of War" farmaan fires when YOUR
  side's exhaustion crosses 60 (was: a time-only counter).
- **X3.** **AI peace:** `hindostan.set_exhaustion 100 <enemy>` while YOUR realm is AI-uninvolved…
  or simpler: find two AI realms at war and wait/watch — when one side hits 100 it makes peace
  ("X is spent by the war and has sued for peace" if you're in either realm). Verify in the
  diplomacy screen.
- **X4.** **Player-ruler is never forced:** with YOUR realm spent (set_exhaustion 100), no auto-
  peace; instead "The Realm Is Spent" farmaan + authority bleeding daily (~0.3/day) until you
  make peace from the war menu.
- **X5.** **Throne wars are immune:** during an accession war / AI civil war / secession war
  (hind_rebel_*), `exhaustion_status` shows NO entry for that war, and it can never peace out by
  exhaustion (the binary rules hold).
- **X6.** After peace, the pair's exhaustion DECAYS (~1.5/day, visible in status as "[at peace,
  decaying]") — an immediate re-war starts already weary. Save/load persists the ledger.

## Y. Secession & abdication conspiracies (`DisaffectionBehavior`; no-Diplomacy mandate)

*New (2026-07-12) — 6 new `DisaffectionMathTests`, never run live. Built on the opinion
ledger + the existing civil-war/claim-kingdom machinery; the riskiest new path is the
secession war's resolution and GRADUATION of a winning breakaway into a real kingdom.*

- **Y1.** `hindostan.disaffection_status` shows simmering cabals, an active secession war, and
  graduated realms. `hindostan.force_conspiracy` (as ruler) forges a pre-simmered cabal from
  your most disaffected clans — note they must genuinely hold a low opinion of you (≤ −15) to
  hold together on the next weekly tick.
- **Y2.** **The spymaster earns his keep:** with a Spymaster seated on your council, a forming
  cabal produces his warning message; without one, the first you hear is the ultimatum.
- **Y3.** **The ultimatum (player-ruler):** a Ceremonial farmaan from the confederate houses.
  With a lawful heir + your legitimacy < 55 they demand ABDICATION (yield → the heir accedes,
  authority −8, and the coronation darbar fires for the new ruler); otherwise they demand to
  DEPART (yield → a new kingdom leaves in peace, graduated immediately, founding darbar held).
- **Y4.** **Refusal:** refused abdication → the cabal's lead house raises the EXISTING
  leadership civil war (D3 rules apply: side-choice farmaan, 45-day resolution). Refused
  secession → a secession war: the confederates rise as a hind_rebel_* "Dominion of X"; within
  45 days it is crushed (clans fold home, authority +6, ringleader's grudge) or WINS FREE.
- **Y5.** **Graduation (the big new path):** a secession that wins (or is granted) becomes a
  REAL kingdom: `disaffection_status` lists it under "Graduated realms"; it can make peace and
  war normally (no longer blocked by the throne-war patch); the safety net stops vetoing its
  lifecycle; its ruler holds a founding darbar; it never gets darbar'd as a claim kingdom. Play
  on several days: no odd re-folding, no eternal war, encyclopedia sane.
- **Y6.** **AI realms run the whole arc autonomously:** watch the log for "Disaffection:" lines —
  cabals forming, ultimatums, AI rulers yielding (abdication or granted independence) or
  fighting, by legitimacy and strength odds.
- **Y7.** One secession war at a time; no new ultimatum while ANY convulsion (accession war, AI
  civil war, secession war) is raging — cabals bide their time. Save/load persists cabals, the
  war, and the graduation register.

## Z. Round-6 fixes & features (2026-07-12: farmaan layout, encyclopedia surfaces, the qasid, follow-me, the procession)

*Built on round-6 screenshots + feedback; 381 tests green. The procession (Q2, rewritten above)
and the qasid audience opening are the risky new paths here.*

- **Z1.** **Farmaan popup reads top-down at last:** decorative header, then TITLE, divider,
  sender, body, seal — the round-6 screenshot showed it reversed (seal on top, title at the
  bottom): the prefab carried the backwards `VerticalTopToBottom` (Modding-Findings ch.18).
  Requires the updated `GUI/Prefabs/HindostanFarmaan.xml` to be deployed, not just the DLL.
- **Z2.** **Encyclopedia buttons sit in the column, not on the name.** The scout button (and
  the new qasid button beside it) now flow UNDER the hero's name and kingdom-rank line. And the
  scout's report now teaches the game itself: after an akhbaar arrives, the native top-right
  line reads "Last seen around <place>" instead of "Never seen before"
  (`Hero.UpdateLastKnownClosestSettlement`) — round 6's "4 scouts to no avail" was exactly this
  (the log shows all 4 reports DELIVERED; the page just never showed it).
- **Z3.** **The qasid (messenger — Diplomacy parity, ROADMAP E).** "Send a qasid (120/180
  dinars)" on any lord's page → he rides 0.5–4 days (faster + cheaper than a scout,
  `MessengerMath`, 5 tests) → when he arrives, a conversation OPENS AS IF YOU STOOD BEFORE THE
  LORD — the full tree: fealty, grievances, invitations, war talk, everything. The audience
  waits for the map (never interrupts a battle/mission/darbar sitting); arrived-but-unheard
  audiences survive save/load (`hind_qasid_*`). Dead target → "The Qasid Returns Unheard"
  farmaan. Console: `hindostan.qasid_status`, `hindostan.qasid_arrive`.
- **Z4.** **"Ride with me" said face to face.** Talking to a lord who leads a party you may
  command (your clan; or your vassals, who may refuse — asked in person carries +10 weight):
  new dialogue line "Ride with me — keep your banner at my side" → the existing
  `PartyOrdersBehavior` Follow order (hourly re-assert, expiry, save-safe). "You may resume
  your own course" releases any standing order. The map-menu "Command your parties" is
  unchanged — same ledger, two surfaces.
- **Z5.** **Save/load with a qasid on the road and a follow order standing:** both persist;
  a follow order re-asserts hourly after load exactly as before.

## AA. Round-7 historical overhaul (biographies, relations web, quest re-theme, Calradia sweep)

*The world made historical: 66 encyclopedia biographies covering EVERY house head (heroes.xml,
cross-referencing one another), a tested relations+traits web applied once per campaign
(`HistoricalCastBehavior`, `hindostan.cast_reapply`), the main quest re-themed onto Alamgir's
Deccan folly, and ~250 string overrides killing every remaining Calradia/vanilla-faction leak.*

- **AA1.** **Biographies:** open the encyclopedia on any clan leader in any kingdom — every one
  now has a historical bio, and they reference each other (e.g. Abdullah Khan Barha ↔ Qamar ud
  Din's Turani party; the Sidi of Janjira ↔ Laxmibai Angre; Rohilla ↔ Bangash; Zamorin ↔
  Palakkad). Spot-check ~10 across kingdoms.
- **AA2.** **Relations web:** on first load (new or old save) `HistoricalCast: applied N
  relations` appears in tyt_log (~60 pairs, ~50 lords). Verify a few in-game: Bajirao vs Asaf
  Jah hostile; the Sayyid Brothers +80; Scindia/Holkar/Bajirao warm; Charat Singh vs Jai Singh
  Kanhaiya hostile. Traits: check encyclopedia trait icons (e.g. Budh Singh Hada honest+valiant,
  Murshid Quli calculating).
- **AA3.** **Main quest re-themed:** 'Investigate Neretzes' Folly' is now **'Investigate
  Alamgir's Folly'** — the veterans' testimonies retell the storming of Wagingera (1705) and the
  Deccan war (Marathas in the ghats, Berad marksmen, turncoat Afghan companies, Rajputs who
  withheld their lances, hired Sikh/Mysorean horse). The banner is the **Alam of Timur**; the
  mentors are **Zinat-un-Nissa Begum** (restore the Raj) and **Khando Ballal** (break it); the
  murdered emperor beat maps to Farrukhsiyar. Radagos→Raghu Das, Tacteos→Tek Chand.
- **AA4.** **Family + character creation:** new campaign → parents/siblings carry Indian names
  per culture (e.g. Mughal: Mirza Baqir/Taj Begum/Nadir/Parviz/Aisha); backstory and CC texts
  say Hindostan. Verify the culture-select and review screens show no Calradia.
- **AA5.** **The sweep:** encyclopedia concept pages, companion/wanderer backstories, settlement
  lore, tournament/tavern lines — grep-level goal: NO "Calradia/Calradian/Calradios" anywhere in
  normal play. Report any survivor with a screenshot (it will have a findable string key).

## BB. Round-8: the English-override FIX, court honours, scripted history, Mysore restructure

*Round 8 (2026-07-12). ROOT CAUSE of "vanilla names persist": the engine NEVER loads language
files for English (`LocalizedTextManager.LoadLanguage` skips strings when the language is
English; `MBTextManager.GetLocalizedText` returns inline text without consulting any table).
ALL of round 7's language-file overrides were dead. Fixed with a Harmony prefix on
`GetLocalizedText` (`EnglishTextOverridePatch`) that serves the mod's ~700 inner-key overrides
at render time — which also heals EXISTING SAVES (names/quest titles are stored as raw
"{=key}text" and re-resolve every frame). 401 tests green.*

- **BB1.** **THE FIX (verify first):** load your existing save → the quest log should now read
  "Investigate Alamgir's Folly"; your brother should be renamed (e.g. Nadir for a Mughal
  culture start); tyt_log shows `EnglishTextOverride: N inner-key overrides live` (~700).
  Any surviving vanilla name/Calradia → screenshot it.
- **BB2.** **Mughal dating.** Farmaan seals now read "...1709 AD (1121 AH)" — the campaign
  calendar is UNIFIED on 1707 = Aurangzeb's death (the old display said 1719 at start; your
  dates will shift back accordingly). Ruler-issued farmaans add "in the Nth year of the reign".
  Currency is now the RUPEE everywhere (one engine currency, renamed; mohur/dam appear only
  as prose denominations).
- **BB3.** **The news of the roads.** Ask any lord "What word do the roads carry these days?"
  → one of 14 historical anecdotes (Alamgir's shroud-caps, the Jagat Seth, Banda's cage, the
  English factories, Jai Singh's observatories...). Same lord keeps his tale for a week.
- **BB4.** **The khil'at.** As sovereign, talking to a vassal lord: "Approach. The court would
  bestow a khil'at upon you" → the khil'at-e-fakhira (royal silk, 500 rupees, +5 relation) or
  the char-aina breastplate (feats of arms, 900, +7); Favor in the opinion ledger; one per
  lord per 45 days.
- **BB5.** **Granted titles.** "Kneel. The court would join a title of honour to your name" →
  pick Bahadur / Jang / ud-Daula / ul-Mulk (60 influence, permanent) — thereafter the court
  writes him "Najaf Khan Bahadur" everywhere NameWithHonorific is used.
- **BB6.** **Jharokha darshan.** In any town of your realm, as sovereign: "Show yourself at the
  jharokha" — once a MONTH: +1.5 legitimacy, +2 town loyalty; the option shows its cooldown.
- **BB7.** **Scripted history** (`hindostan.history_status` / `history_fire <id>`): 1707 the
  Deccan war (empire vs Marathas), 1709 Banda's rising (empire vs misls), 1714 the Deccan
  treaty (peace), 1724 THE DALVAI'S COUP (Mysore's ruling clan → Hyder Ali's house; Wodeyar
  keeps his palace and a −60 grudge), 1737 Bajirao's dash on Delhi, 1739 NADIR SHAH'S SACK
  (authority −15, the treasury bled, the Peacock Throne gone), 1747 the Durrani proclamation.
  Stale-load guard: wars 5+ years past their year don't fire into old saves.
- **BB8.** **Mysore restructure.** Tipu now rides under Hyder Ali's OWN house (XML for new
  campaigns; the `mysore_house` event migrates existing saves). The Kalale pass to
  Nanjarajayya (renamed lord_3_20, new bio, −50 vs Hyder). **When Tipu succeeds to Mysore's
  throne, the kingdom raises the LION STANDARD — yellow and black** (banner + kingdom colours;
  `hindostan.mysore_banner [code]` to preview/iterate the exact stripes).

### Round-7 BUGS FILED (2026-07-12, not yet fixed — user report with screenshot)

- **BUG-1 (procession placement).** The ceremony lords do NOT appear before the sovereign at
  the throne: they spawn scattered — other floors/rooms of the keep — and the procession does
  not bring them to the player. EXPECTED: the attending lords spawn/assemble in the CENTRE of
  the throne hall and each walks to the seated sovereign in turn. Suspects: `sp_notable` spawn
  tags resolve to points outside the hall chamber (multi-level keep scenes), and
  `AgentNavigator.SetTargetFrame` may not path across floors — the stand-ins likely need a
  hall-centre spawn tag (e.g. `sp_common_area`/dedicated frames near `sp_throne`) plus a
  procession teleport fallback when pathing fails/timeouts.
- **BUG-2 (farmaan buttons overflow).** The primary/secondary buttons at the popup's foot
  render wider than the decree panel — text like "Take the oaths by farmaan — forgo the
  ceremony" pushes the button past the canvas/frame edge. The button row needs to fit within
  the 780-wide panel (cap button text width / allow wrap / widen panel).

---

## CC. The claim ledger & the price of the writ (`ClaimsBehavior` + `ClaimMath`) — wiki ch.30 steps 1-3

*2026-07-13. The FOUNDATION of the complete diplomacy layer (wiki ch.30). A claim is a HOUSE's
pretension to a place, in YEARS OF STANDING: it belongs to the clan (a successor inherits it),
deepens 1/yr while you govern (cap 20), and — the point of the whole thing — does NOT vanish when
you lose the fief. It freezes and fades at HALF the rate, so a grudge outlives the holding that made
it. Seeded at 1707 on a normal curve (0-10 yrs) from the actual holders, so the map has grievances to
act on from year one. First consumer: Mansabdari rotation, which was FREE and is now PRICED by the
sitting holder's claim. 428 tests green. The war layer (steps 4-5) reads this ledger next.*

- **CC1.** **The ledger seeded.** New campaign → `hindostan.claims`. Expect: your clan holds a claim on
  each fief it starts with, somewhere in 0–10 years. Then `hindostan.claims_on delhi` (or any town):
  expect the sitting house listed with its years and a description ("a firm claim", "an ancient claim"),
  marked *sits here*. Check `tyt_log.txt` for `Claims: seeded N founding claims`. **N should be roughly
  the number of towns+castles on the map** — if it is 0 or tiny, the seed ran before world-gen finished.
- **CC2.** **Claims are seeded on an OLD SAVE too** (the back-fill). Load a pre-existing save →
  `hindostan.claims` should be populated, not empty, and the log line should appear once.
- **CC3.** **Governing deepens it.** `hindostan.claims`, note a fief's years → pass a campaign year →
  `hindostan.claims` again. Expect **+1.0 year** on every fief you still hold. (Settles weekly, so it
  moves in weekly steps, not daily.)
- **CC4.** **THE GRUDGE — the core mechanic.** Lose a fief (let an enemy take it, or `hindostan.grant_claim`
  a rival's town then take it and give it back). Immediately after losing it, `hindostan.claims` must
  **still list it**, now reading *dispossessed — fading, forgotten in Nd*. It must NOT disappear. Pass a
  year: the value should drop by **0.5** (half the accrual rate). This decaying claim is what the next
  war will be declared on — if it silently vanishes, the whole layer is dead.
- **CC5.** **A retaken seat resumes, it does not restart.** Lose a fief with (say) 8 years of claim, retake
  it within a year or two, `hindostan.claims`: expect it to resume near **7.5**, NOT reset to 0.
- **CC6.** **THE PRICE OF THE WRIT (Mansabdari).** As sovereign, enact Mansabdari law. Then
  `hindostan.rotation_price <town>` on a fief held by a long-seated vassal: expect a multiplier well
  above x1.00 (up to **x3.00** at a 20-year claim). When that fief's term expires, expect the farmaan
  **"A Tenure Has Run Its Term"** quoting the exact influence cost — and a real choice: *Order the transfer
  (N influence)* or *Leave him where he sits*. Ordering it must actually **deduct the influence**.
- **CC7.** **The writ that cannot reach.** Drain the ruling clan's influence below the quoted cost (or use a
  deeply entrenched 20-yr holder). Expect the farmaan **"The Writ Stops at His Gate"** — the rotation is
  *not issued at all*, the holder keeps the fief, and `tyt_log.txt` logs `rotation UNAFFORDABLE`. This is
  the intended empire-decay mechanic: entrenched magnates pass beyond the crown's reach.
- **CC8.** **Feudal law is unaffected.** Under Feudal tenure, no rotation farmaans, no influence charged —
  claims still accrue silently in the background (`hindostan.claims` confirms), they just do nothing yet.
- **CC9.** **Save/load.** Save mid-campaign, reload, `hindostan.claims`: identical values, no doubling, no
  loss. (The ledger's index is rebuilt at session launch — a corrupted index would show wrong claims
  against the wrong settlements, so spot-check two houses.)
- **CC10.** *Not yet wired (do not report as bugs):* the wakil/companion agent that manufactures external
  claims (step 8), and the war layer actually *using* claims as casus belli (steps 4–5). `ClaimsBehavior`
  already stores and expires external claims, but nothing grants them yet.

---

## DD. The war layer rebuilt: aims, targets, progress, truces, subjugation (wiki ch.30 steps 4-10)

*2026-07-13. `WarfareBehavior` REWRITTEN. The old system's war goal was a label: a second enum
(`WarGoal`) disconnected from the tested `WarAimMath`, terms gated on SCORE rather than on the aim,
"demand a province" annexing an arbitrary `FirstOrDefault()` town, and the aim prompt firing during
CIVIL WARS. All of that is gone. `WarGoal` is deleted; `WarAim` is the single enum. Every war on the
map (AI-vs-AI included) is now a RECORD — aggressor, defender, aim, target fiefs — and the aim IS the
win condition. Score is an itemized contribution ledger, so the progress bar can explain itself.
454 tests green.*

**DD1. THE CIVIL-WAR BUG (verify first — this was the reported defect).** Start an accession or AI civil
war (`hindostan.force_ai_civil_war`, or challenge the throne yourself). Expect **NO "War Aim against
X" popup**. A war for the throne has no aim to choose — it is the throne. Previously this fired every time.

**DD2. The aim is chosen, and it is honest.** As a ruler, declare war on a realm your houses hold claims
against (`hindostan.claims_on <their town>` to confirm). Expect the aim menu to offer **"Conquest — press
our claims: <named towns>"** listing the actual fiefs and which house claims each. Declare on a realm you
hold NO claim against: the conquest option should be **absent**, and picking Tribute should warn that you
march "without a claim or a grievance" and cost you authority + legitimacy.

**DD3. THE PROGRESS BAR (the transparency requirement).** Open the kingdom screen → Diplomacy tab. Each war
should show a **gold progress bar** under the enemy's name with a line like *"Conquest — 33%, ground is
being made"*. **Hover it.** Expect the itemized breakdown: the named targets and whether each is TAKEN or
*still theirs*, plus the score ledger rolled up (*"Battles won: +30 (3 times)"*), plus exhaustion with
*what* wore you down (*"Casualties 34 · Fiefs lost 16"*). Nothing on that bar should be unexplained.
Also check `hindostan.war_status` prints the same for every war on the map, AI-vs-AI included.

**DD4. A war of conquest is won by TAKING WHAT YOU CAME FOR — not by score.** Declare a conquest war for
a named town. Win battles until your score is huge but do NOT take the town: "Direct the war effort" must
**refuse to let you annex it** (the option is absent) and tell you the aim is only N% achieved. Then take
the town: expect the farmaan **"The War Is Won"** offering to conclude on your terms.

**DD5. The prize goes to the CLAIMANT (Feudal law).** Under Feudal tenure, conclude a won conquest war.
The target fief must pass to **the house whose claim was pressed**, not to you by default. Under
**Mansabdari**, it must NOT auto-pass — you should be told it is "the crown's to grant, not the claimant's",
and it goes through the normal channel.

**DD6. THE FORCED TRUCE.** After concluding a war, `hindostan.truces` should list a **3-year** truce. Now try
to re-declare war on them: it must be **refused**, with the message "A truce stands between X and Y."
(A white peace gives 1 year instead.) Confirm the AI also cannot declare across it.

**DD7. THE GRUDGE LOOP (the payoff of the whole design).** After DD5, check `hindostan.claims_on <the taken
town>`: the **dispossessed house must still be listed**, claim now decaying. That is the casus belli for
their war of reconquest when the truce lapses. This closing of the loop is the single most important
outcome in the wave.

**DD8. Defensive wars.** Let an AI declare on you. You get **no aim prompt** (correct — the defender's aim
is to deny theirs). The bar should read **"(we defend)"** and fill as you DENY them: hold everything and it
sits near 100%; let them take a target and it drops. The hover must name *their* aim openly.

**DD9. SUBJUGATION — both roads, both visible.** Declare a war of Subjugation. Hover the bar: expect **all
three collapse conditions with live values** ("Their king: still at liberty", "Their legitimacy: 73",
"Their lords with you: 22%") **and** the other road ("— or take everything: 3 of 12 fiefs"). Meet the collapse
gate (capture/kill their king, low legitimacy, ≥30% of their lords at ≥+10) → the war completes. Or take
**every last fief** → it completes anyway.

**DD10. The swallowed realm SEETHES (intended, not a bug).** On subjugation: their kingdom is absorbed, NOT
destroyed-and-scattered. Every one of their houses should **join your realm** (check the clan list) rather
than vanish, each carrying a **−50 opinion** (`OpinionType.Subjugated`) — check the encyclopedia
"Disposition toward you" rows, it should read *"a realm swallowed whole"*. **Expect civil-war and
conspiracy risk to spike for the next ~3 years** as it decays. This is the designed price of an empire.
If it makes the game unplayable rather than dramatic, say so and I'll add a grace period.

**DD11. THE WAKIL (the riskiest engine path in this wave — a hero parked in a settlement).** Enter a foreign
town → **"Leave a companion as our wakil"** → pick a companion. He should **leave your party** and stay in
the town. `hindostan.wakils` tracks his progress (merchants won / needed). Each week he raises your relation
with the town's merchants (pace = his Charm + Intelligence). When **2/3 of merchants hit +40**, expect the
farmaan **"The Bazaar Is Ours"**, an external claim in `hindostan.claims` **with a 2-year countdown**, and the
companion returning to you. **Also verify "Recall your wakil" returns him to the party** — if either path
leaves a companion stranded (in no party and no settlement), that is a serious bug: report immediately.

**DD12. The Deccan war is no longer unconditional.** The Maratha–Mughal war still fires at campaign start.
But it should now be endable: make peace with the Marathas and it must **stay** peace (previously
`ReassertStance` silently re-declared it, which would have evaporated any truce you won).

**DD13. Save/load across the rewrite.** **The war state model changed shape** (`hind_war2_*` keys replace
`hind_war_gIds/sIds`). Load an OLD save with wars in progress: expect no crash, and every ongoing war
reconstructed with an aim inferred from the aggressor's claims (`hindostan.war_status`). Targets will be
empty on reconstructed wars — that is expected, not a bug.

**DD14. Withheld tribute is a casus belli.** Make a realm your tributary. When their treasury runs dry and
the nazrana stops, expect the farmaan **"The Nazrana Does Not Come"** granting you just cause to chastise them.

**DD15. The vassal's petition (only the sovereign declares war).** As a **vassal** (not the ruler) holding a
claim on a foreign fief — earned by the wakil (DD11) or inherited — enter any town/castle → **"Lay a claim
before the throne"**. Costs 100 influence whatever the answer. The crown weighs your standing, the depth of
the claim, and the realm's weariness. On consent, expect either a **fresh war declared for your claim**
(with that fief as its named target, and your house as the claimant who gets it), or — if the realm is
already fighting them a war of conquest — **your fief added to that war's aims**. If the realm's existing
war has a different aim, the crown should refuse to recast it mid-war. A standing truce must block the
petition outright.

> ⚠️ **Check your launcher:** `Bannerlord.Diplomacy` is present in your Modules folder. It must stay
> **disabled** — it patches the same diplomacy screen and the same war/peace actions this layer now owns,
> and the two will fight. (Standing decision: no Diplomacy mod; this is its native replacement.)

---

**When you report back**, the ideal format per finding: section-number, PASS/FAIL, repro steps, log excerpt, screenshot if UI. The single most valuable data points now are **DD1 (the civil-war aim prompt must be gone)**, **DD7 + CC4 (the grudge must survive dispossession — the whole diplomacy layer rests on it)**, **DD3 (the bar explains itself)**, **DD11 (the wakil must never strand a companion)**, **DD13 (old saves survive the war-model change)**, **CC7 (the unaffordable writ)**, and **J3 (the breakaway, never seen live)**, **M2 (siege unwind — the riskiest engine path)**, **N1/N2 (the fixed board)**, L3's fates, **O3/O4 (the first akhbaar report and its dead/captive/no-party edge cases)**, **P3–P5 (the labour trade-off: yield up, unrest up, gang thinning)**, and anything that throws in `tyt_log.txt`.
