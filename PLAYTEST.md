# Playtest Plan — Stability Pass + Foundations Pass

Covers every component built across the two passes. Work through in order; sections A–D
are the first pass (stability, zamindari, village fiefs, core wave), E–I the foundations
pass (farmaans, opinions, dynasties, dialogue, court menu).

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

**When you report back**, the ideal format per finding: section-number, PASS/FAIL, repro steps, log excerpt, screenshot if UI. The single most valuable data points are G2 (does the warband spawn?), E1 (pause edge cases), and anything that throws in `tyt_log.txt`.
