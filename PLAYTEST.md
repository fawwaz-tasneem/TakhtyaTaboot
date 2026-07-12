# Playtest Plan — Stability Pass + Foundations Pass + July Waves

Covers every component built across the passes. Work through in order; sections A–D
are the first pass (stability, zamindari, village fiefs, core wave), E–I the foundations
pass (farmaans, opinions, dynasties, dialogue, court menu), **J–N the July 2026 waves**
(unified empire, clan safety net, succession economy + treachery, siege parley, the two
Gauntlet screens), **O the akhbaar scouts, P bonded labour, Q coronation ceremonies, R monsoon
harvest+famine, S village-jagir rotation, T fief petitions, U darbar petition court, V zat/sawar
split, W clan-screen zamindari** — J–W are the CURRENT focus; A–I were exercised in playtest
rounds 1–3.

**Status ledger (2026-07-11, commits `6bad71f` → HEAD):** A–I built + playtested
(rounds 1–3); J–W built, unit-tested (340 green) and statically verified against the
v1.3.11 decompile, but **not yet proven in a live campaign end-to-end** except: J1 fold
confirmed by log + player, hierarchy board seen once (was upside down — fixed, needs a
second look). O–W (akhbaar scouts, bonded labour, coronations, monsoon harvest, village-jagir
rotation, fief petitions, darbar court, zat/sawar, clan-screen zamindari) are the newest and
wholly unverified live — W (clan-screen) is UI-only and the highest-priority live check.

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

## Q. Coronation ceremonies (`CoronationBehavior`, fires on accession; `hindostan.coronation_test`)

*New this wave (2026-07-11) — built, unit-tested (8 new `CoronationMathTests`, 314 green), never
run live. Reuses the existing opinion records, Ceremonial farmaan, and grievance dialogue.*

- **Q1.** **Player accedes as sovereign** (take a throne, or `hindostan.coronation_test` while you
  lead a realm): a Ceremonial "Your Coronation Darbar" farmaan lists the verdict of the hall, who
  bent the knee, and who left an empty place. Sanity: high-relation house heads should mostly
  attend, resented ones mostly stay away. Legitimacy shifts with the balance of attend vs. absent.
- **Q2.** With absentees, the farmaan offers "Demand a late oath" → a follow-up "The Late Oath"
  farmaan reports who bent (warmer lords) and who defied (colder lords). Defiant lords get a
  **grudge** you can then pursue in the grievance dialogue (playtest H3). "Let their absence stand"
  instead → a message that the court remembers.
- **Q3.** Check the opinion records afterward (encyclopedia "Disposition toward you" rows / grudge
  dialogue availability): attendees carry "an oath sworn", absentees "an empty place at the ceremony".
- **Q4.** **Player as vassal:** be a member (non-ruling) clan of a realm, then cause its ruler to
  die/change → a "A Summons to the Coronation" farmaan from the new sovereign. "Travel and swear" →
  relation up, the sovereign marks your loyalty. On a separate save, "Stay away" → relation down,
  the sovereign holds the empty place against you.
- **Q5.** **AI accessions are silent:** when a foreign realm's ruler changes (not yours), NO farmaan
  fires — no coronation spam. During the scripted 1707 cascade specifically, no darbar per beat.
- **Q6.** **Save/load:** the ruler snapshot persists (no phantom coronation on load). Old save
  (pre-feature): loads clean; the next real accession in your realm stages the darbar.

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

## U. Darbar petition court (`DarbarPetitionBehavior`; sovereign's Darbar → "Hear a petition")

*New this wave (2026-07-11) — the first user of the `CourtRuling` opinion record (defined long
ago, never written until now). Built, unit-tested (6 new `DarbarCourtMathTests`, 336 green),
never run live. Requires you to be a sovereign.*

- **U1.** As a sovereign, court menu → "Hold court and issue decrees" → "Hear a petition and
  render judgment". A grounded case appears drawn from YOUR realm: a boundary dispute between two
  village zamindars, OR a raided village's plea (needs a high-threat village — force via
  `hindostan.set_village_threat 60`), OR a quarrel between two notables. `hindostan.darbar_petition`
  forces a sitting (bypasses the 3-day cooldown).
- **U2.** **Dispute case:** four rulings — for plaintiff / for defendant / compromise / dismiss.
  Rule for one side → he warms to you (a "judgement at court" record, +relation), the other cools
  (negative record, −relation); you gain influence, a little legitimacy. Compromise → both warm
  slightly but you SPEND influence. Dismiss → both cool and you LOSE legitimacy.
- **U3.** **Plea case:** grant relief (deep gratitude + legitimacy), refer to the local lord
  (mild), or turn away (the petitioner sours, legitimacy drops). Verify the plaintiff's disposition
  toward you moves accordingly.
- **U4.** **The CourtRuling record:** after a ruling, check the parties' encyclopedia "Disposition
  toward you" — a favoured party shows "a judgement at court" (positive); a party ruled against
  shows it negative, making him a grievance-dialogue target (playtest H3).
- **U5.** **Cooldown:** immediately after a sitting the option is greyed ("the docket is thin…")
  for 3 days. Save/load preserves the cooldown day. With no eligible parties (a tiny, vassal-less
  realm), "No petitioner brings a case worth the crown's time."

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

---

**When you report back**, the ideal format per finding: section-number, PASS/FAIL, repro steps, log excerpt, screenshot if UI. The single most valuable data points now are **J3 (the breakaway, never seen live)**, **M2 (siege unwind — the riskiest engine path)**, **N1/N2 (the fixed board)**, L3's fates, **O3/O4 (the first akhbaar report and its dead/captive/no-party edge cases)**, **P3–P5 (the labour trade-off: yield up, unrest up, gang thinning)**, and anything that throws in `tyt_log.txt`.
