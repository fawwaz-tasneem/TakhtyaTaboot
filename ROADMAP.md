# Roadmap — Takht ya Taboot

The single source of truth for what comes next. Ordered within each block; blocks A → D.
Design details for many items exist in wiki ch.13–27; as-built ground truth is ch.28.
Update this file when an item ships (move it to "Shipped") or when priorities change.

## A. Near-term (from playtest round 1, July 2026)

1. **Succession-crisis economy rework.** Abdication difficulty scales with years reigned
   (a long-seated Aurangzeb costs millions); offers combine gold + troops + fief +
   influence in one package; pressing a secure king (≥3:1 strength against you) risks a
   treachery declaration → banishment from the realm + war; if the king wins that war he
   chooses: execute / heavy fine / imprison (clan head inactive in prison until a ransom
   in the hundreds of thousands is paid — or he dies there). *Partially shipped:* a ruler
   who concedes peacefully now abdicates with honour (no fate decree).
2. **Hierarchy screen as a tree.** Replace the flat list with a troop-tree-style branching
   layout (sovereign → lords → zamindars). Match the user's reference screenshot.
3. **Village construction UI.** A proper Gauntlet screen in the style of town construction
   (project queue, progress bars, coffer, tax estimate) replacing the text menu.
4. **Akhbaar scouts.** Pay to dispatch a scout after a named lord; on locating him an
   *akhbaar* report arrives in the farmaan-style layer: location, troop count, rough
   composition. Seed of a wider akhbarat espionage layer (wiki ch.17).
5. **Culture-keyed dialogue register + Calradia purge.** Persianate Urdu honorifics for
   Muslim courts (Zill-e-Ilahi, Jahanpanah, bismillah invocations), distinct registers for
   Rajput/Maratha/Sikh courts; sweep remaining vanilla Calradia prose through the
   LocalizationOverride pipeline.
6. **Slave labour in villages.** Capped workforce per village fed from battle captives:
   +productivity/tax, +threat. (User-requested upcoming feature.)
7. **Clan-screen fiefs visibility.** The engine cannot give a village an owner separate
   from its bound town, so zamindari villages never appear in the vanilla Fiefs tab —
   inject a "Zamindari" block into the clan screen via UIExtenderEx instead.

## B. Foundations-ready systems (the second wave the opinion/dynasty layer was built for)

1. **Coronation ceremonies.** On accession, summon each house head; attendance decided by
   `EffectiveOpinion`; absentees get `MissedCeremony` records and become targets of the
   existing grievance dialogue; ruler may demand a late oath. Player-sovereign holds court
   while the lords arrive; player-vassal travels to swear — or deliberately stays away.
   (`SworeFealty`/`MissedCeremony` opinion types, Ceremonial farmaan priority, and the
   fealty dialogue already exist.)
2. **Darbar petition court.** Unify council/darbar convening into one sitting where
   notables, merchants and zamindars bring grounded cases (village land disputes, a robbed
   caravan, zamindar vs zamindar). Judgments write `CourtRuling` opinion records to both
   parties, cost/earn gold and influence, nudge traits. The village-fief layer supplies
   endless non-random petition material. (User confirmed current convening feels
   unchanged — this is the fix.)
3. **Fief petitions replacing claim-fief.** "Claim your due" becomes a petition queue:
   gold gift + influence stake filed with the sovereign; when a fief frees up the court
   weighs gift, influence, `EffectiveOpinion` and `CanHold` rank — and refuses outright
   below a relation threshold. The weekly court-grant tick becomes the queue engine.
   (Playtest: Kanpur/Lucknow were instantly claimable — this replaces that.)

## C. Deepening what already ships

1. **Tenure rotation reach + opinions.** Jagir rotation EXISTS
   (`MansabdariTenureBehavior`: Mansabdari tenure law, rotation clocks, comply/defy
   ladder). Extend it to village jagirs (the zamindari layer is currently never rotated)
   and write `Grudge`/`Favor` opinion records on rotation and defiance instead of raw
   relation only.
2. **Zat/sawar split ranks.** Mansab as dual rank: zat (personal rank — gates fiefs and
   stipend) and sawar (cavalry obligation — sets the muster target). Mostly re-labelling
   numbers already tracked.
3. **Festivals** (Eid, Diwali, Nauroz, Baisakhi — culture/faith-keyed like royal styles):
   seasonal Ceremonial farmaan, a court gathering that batches nazrana presentation and
   opinion gains; natural stage for petitions and betrothals. Builds on SeasonMath + the
   court menu.
4. **Monsoon beyond speed.** Harvest modifier on village tax accrual (good rains = fat
   autumn collection); famine event chain when threat + failed rains coincide — reuses the
   plea-farmaan and relief-detachment machinery.
5. **Women's court influence.** Official darbar posts are men-only (period rule, shipped);
   model the Nur Jahan pattern properly: influential women act through the intrigue layer —
   whispers that shift the holder's decisions, opinion records, faction ties.

## D. The big new arcs

1. **Marriage alliances with negotiation.** Dowry/mehr terms, inter-faith matches
   interacting with the tolerance system, `KinBond` records binding houses, cadet houses
   giving younger sons something to be married into.
2. **Princely intrigue.** Make the princes' succession answers real: resentful princes
   (low `EffectiveOpinion` of the heir) quietly gather supporters; charter them into cadet
   houses to defuse them — or watch them fester into the challenger when the throne
   passes. Turns the accession war into the payoff of a visible, influenceable buildup.
3. **Firangi factor.** European trading companies as late-game texture for 1719: envoys
   seeking trade concessions at ports, artillery mercenaries for hire, and a slowly
   overstaying welcome.

## Longer-term designs on the shelf (wiki ch.13–27)

Estates, trade-route network, epidemics, cultural patronage/caravanserai, traits earned
from actions, ulema fatwa network, pilgrimage (Hajj/Kumbh/Amritsar), Great Works,
assassination/blackmail/spy schemes, main quest (Restoration vs Domination),
waqai-nawis LLM news layer (ch.18).

## Shipped (for orientation)

- Playtest round 2 fixes & features — July 2026. `ClanSafetyNetBehavior`: no noble house
  stands masterless (claim-kingdom scatter vetoed, scattered houses fold home, orphans
  re-home by faith/relations/nearness — `ClanRehomeMath`). Sovereign levers: grand darbar
  (+authority) and charitable endowments (+legitimacy) in the empire-survey menu, with a
  "how to raise it" guide. `SiegeParleyBehavior`: the attacker's envoy at the walls —
  bribe the qiladar or offer terms, then honour or defy them (`SiegeParleyMath`).
  White claim-kingdom banner fixed (shell clans now carry real colors).
- Unified Empire until Aurangzeb dies (was A.1) — July 2026. `UnifiedEmpireBehavior` folds
  Bengal (`empire_w`) and Hyderabad (`empire_s`) into the empire on a fresh campaign
  (dormant kingdom shells keep their ids); the accession cascade's first death sunders the
  fold — clans return, the Nawab/Nizam are re-seated, recorded wars resume, farmaans
  announce both beats. Pure logic in `UnifiedEmpireMath` (tested); ground truth in ch.28.
- Stability pass, zamindari unification, village fiefs + projects, core wave (nazrana,
  civil war, tolerance, court factions, monsoon) — July 2026.
- Foundations pass: farmaan director (pause/dedupe/digest), personal opinion ledger,
  dynasties/royal styles, cadet houses, dialogue pack, court-menu consolidation.
- Mansabdari tenure law with jagir rotation for towns/castles (pre-dates the passes).
- Playtest round 1 fixes: exile-house crash, honourable abdication, village menu dedupe,
  men-only councils, tournament/outnumbered valour, zamindar on village encyclopedia.
