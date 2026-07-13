using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.Config;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    public enum TenureLaw { Feudal, Mansabdari }

    // Tenure policy (design doc §A) — a realm-level edict choosing how fiefs are held:
    //
    //   Feudal (default)  — fiefs are hereditary; the heir keeps them on death (vanilla).
    //   Mansabdari        — fiefs are crown grants tied to a mansab, not the bloodline:
    //                       NON-HEREDITARY (a holder's fiefs revert to the crown on his death and
    //                       are re-granted by rank) and ROTATIONAL (the crown periodically transfers
    //                       a mansabdar on; an entrenched holder may DEFY, risking the full ladder
    //                       reprimand -> dismissal -> rebellion). Rotation reaches BOTH the
    //                       town/castle fiefs (engine ownership) AND the village jagirs (the
    //                       zamindari feudal layer). Rotation/defiance writes Favor/Grudge opinion
    //                       records (the grievance-dialogue ledger), not just raw relation.
    //
    // Imposing Mansabdari is gated by legitimacy and priced by opposition (every entrenched magnate
    // must be bought off). All cost/gate/ladder formulas are the proven, engine-free MansabTenureMath;
    // this class gathers live inputs and applies engine actions, fully guarded and save-safe.
    public class MansabdariTenureBehavior : CampaignBehaviorBase
    {
        public static MansabdariTenureBehavior Instance { get; private set; }

        // Kingdoms currently under Mansabdari law (absence => Feudal).
        private HashSet<string> _mansabdari = new HashSet<string>();

        // Rotation clock: the day each fief's CURRENT holder was appointed (settlementId -> day).
        private Dictionary<string, double> _appointed = new Dictionary<string, double>();

        // Non-serialized snapshot of each clan's leader, so a HeroKilled can be matched to the death
        // of a *clan leader* (whose heir would otherwise inherit the fiefs).
        private readonly Dictionary<string, string> _leaderOf = new Dictionary<string, string>();

        // False until the campaign is fully live. Hero/settlement creation at new-game world-gen runs
        // in PARALLEL and fires OnSettlementOwnerChanged/HeroKilled for every object; touching our
        // (non-thread-safe) dictionaries from those concurrent callbacks corrupted the managed heap and
        // native-crashed world-gen (0xC0000005, no managed exception). Every handler no-ops until the
        // session is launched, after which campaign events run single-threaded on the main thread.
        private bool _ready;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => { _ready = true; RefreshLeaderSnapshot(); });
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => { if (_ready) TYTLog.Guard("Tenure.Daily", RefreshLeaderSnapshot); });
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => { if (_ready) TYTLog.Guard("Tenure.Weekly", OnWeeklyTick); });
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        // ── Law queries ──────────────────────────────────────────────────────────────
        public TenureLaw GetLaw(Kingdom k)
            => (k != null && _mansabdari.Contains(k.StringId)) ? TenureLaw.Mansabdari : TenureLaw.Feudal;

        public bool IsMansabdari(Kingdom k) => GetLaw(k) == TenureLaw.Mansabdari;

        private static double Today => CampaignTime.Now.ToDays;

        // ── Cost quote (Feudal -> Mansabdari) ──────────────────────────────────────────
        public struct EdictQuote
        {
            public bool Allowed;       // legitimacy floor met and not already Mansabdari
            public string Reason;
            public int Influence;
            public int Gold;
            public int AffectedNobles;
            public int Resisters;      // nobles too entrenched to be bought (reform is risky)
        }

        public EdictQuote QuoteMansabdari(Kingdom k)
        {
            var q = new EdictQuote();
            if (k?.Leader == null) { q.Reason = "No sovereign to issue the edict."; return q; }
            if (IsMansabdari(k)) { q.Reason = $"{k.Name} already holds to Mansabdari tenure."; return q; }

            float legit = LegitimacyBehavior.Instance != null ? LegitimacyBehavior.Instance.GetLegitimacy(k.Leader) : 50f;
            float floor = Tune.TenureLegitimacyFloor;
            float auth = ImperialAuthorityBehavior.Instance != null ? ImperialAuthorityBehavior.Instance.GetAuthority(k) : 50f;

            float opposition = 0f;
            int nobles = 0, resisters = 0;
            foreach (Clan clan in k.Clans)
            {
                if (clan == null || clan.IsEliminated || clan == k.RulingClan) continue;
                if (clan.IsUnderMercenaryService) continue;            // sellswords hold no land to lose
                Hero leader = clan.Leader;
                if (leader == null || !leader.IsAlive) continue;

                var fiefs = Fortifications(clan);
                if (fiefs.Count == 0) continue;                       // landless clans don't oppose

                nobles++;
                float rusukh = AverageRusukh(leader, fiefs);
                float relFactor = (CharacterRelationManager.GetHeroRelation(k.Leader, leader) + 100f) / 200f;
                opposition += MansabTenureMath.OppositionWeight(clan.CurrentTotalStrength, fiefs.Count, relFactor, rusukh);

                if (MansabTenureMath.WillResistEdict(rusukh, auth, legit, Tune.TenureResistThreshold))
                    resisters++;
            }

            q.AffectedNobles = nobles;
            q.Resisters = resisters;
            q.Influence = MansabTenureMath.EdictInfluenceCost(Tune.TenureEdictBaseInfluence, nobles);
            q.Gold = MansabTenureMath.EdictGoldCost(Tune.TenureEdictBaseGold, opposition, legit);

            if (!MansabTenureMath.MeetsLegitimacyFloor(legit, floor))
            { q.Reason = $"Your legitimacy ({legit:0}) falls short of the {floor:0} needed to rewrite tenure law."; return q; }

            q.Allowed = true;
            q.Reason = resisters > 0
                ? $"{resisters} of {nobles} magnates are too entrenched to be bought — they will resist."
                : "The court can carry the reform.";
            return q;
        }

        // ── Enacting / reverting the law ────────────────────────────────────────────────
        public bool TryEnactMansabdari(Kingdom k, out string reason)
        {
            reason = "";
            if (k?.Leader == null) { reason = "No sovereign to issue the edict."; return false; }
            EdictQuote q = QuoteMansabdari(k);
            if (!q.Allowed) { reason = q.Reason; return false; }

            Clan ruling = k.RulingClan;
            Hero ruler = k.Leader;
            if (ruling == null) { reason = "The realm has no ruling clan."; return false; }
            if (ruling.Influence < q.Influence)
            { reason = $"The court needs {q.Influence} influence for this edict (you have {ruling.Influence:0})."; return false; }
            if (ruler.Gold < q.Gold)
            { reason = $"The treasury needs {q.Gold} rupees to buy off the magnates (you have {ruler.Gold})."; return false; }

            try
            {
                ChangeClanInfluenceAction.Apply(ruling, -q.Influence);
                if (q.Gold > 0) ruler.ChangeHeroGold(-q.Gold);
                _mansabdari.Add(k.StringId);
                // Start every sitting holder's rotation clock NOW, so long-serving lords aren't
                // rotated the instant the edict passes.
                foreach (Settlement s in k.Settlements.Where(s => s != null && (s.IsTown || s.IsCastle)))
                    _appointed[s.StringId] = Today;
                TYTLog.Info($"Tenure: {k.Name} -> Mansabdari (inf {q.Influence}, gold {q.Gold}, {q.AffectedNobles} nobles, {q.Resisters} resisters).");
            }
            catch (Exception e) { TYTLog.Error("TryEnactMansabdari failed", e); reason = "The edict faltered in the court."; return false; }

            string body = $"By imperial edict, the fiefs of {k.Name} are no longer the birthright of any house. " +
                          $"Henceforth every jagir is a grant of the crown, tied to a mansab and held at the sovereign's " +
                          $"pleasure — to revert on a holder's death and to rotate as the throne sees fit.";
            if (q.Resisters > 0)
                body += $" {q.Resisters} entrenched magnates submit only under protest; their loyalty is bought, not won.";
            RoyalFarmaan.FromRuler(k, "Edict of Mansabdari Tenure", body, "As the throne commands");
            return true;
        }

        public bool RevertToFeudal(Kingdom k, out string reason)
        {
            reason = "";
            if (k == null) { reason = "No realm."; return false; }
            if (!IsMansabdari(k)) { reason = $"{k.Name} already holds to feudal tenure."; return false; }
            _mansabdari.Remove(k.StringId);
            TYTLog.Info($"Tenure: {k.Name} -> Feudal.");
            RoyalFarmaan.FromRuler(k, "Restoration of Feudal Tenure",
                $"The crown restores the old custom: the fiefs of {k.Name} are once more the inheritance of their houses. " +
                $"The magnates rejoice.", "The houses are grateful");
            return true;
        }

        // ── Inheritance interception (non-hereditary) ───────────────────────────────────
        // When a clan LEADER of a Mansabdari realm dies, the heir inherits the house but NOT its
        // crown-granted fiefs: each reverts to the crown and is re-granted by mansab rank.
        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool show)
        {
            try
            {
                if (!_ready) return;   // never act during parallel world-gen
                if (victim?.Clan == null || victim == Hero.MainHero) return;
                Clan clan = victim.Clan;
                if (clan == Clan.PlayerClan) return;                     // player death ends the game; leave it
                if (!_leaderOf.TryGetValue(clan.StringId, out string wasLeader) || wasLeader != victim.StringId) return;

                Kingdom k = clan.Kingdom;
                if (k == null || clan.IsEliminated || clan == k.RulingClan || !IsMansabdari(k)) return;

                var fiefs = Fortifications(clan);
                foreach (Settlement s in fiefs)
                {
                    Settlement fief = s;
                    ResolveRecipient(k, fief, clan, $"{victim.Name} has died; {fief.Name} reverts to the crown.",
                        succ => { if (succ != null && succ != clan.Leader) TransferFief(fief, succ); });
                }
                if (fiefs.Count > 0)
                {
                    TYTLog.Info($"Tenure: {clan.Name} leader died under Mansabdari — {fiefs.Count} fief(s) reverted to the crown of {k.Name}.");
                    if (k.Leader != Hero.MainHero)
                        RoyalFarmaan.FromRuler(k, "A Mansab Falls Vacant",
                            $"With the death of {victim.Name}, the fiefs he held by mansab return to the crown of {k.Name}, " +
                            $"to be re-granted to deserving servants. His house keeps its name, not its jagirs.", "Such is the law");
                }
            }
            catch (Exception e) { TYTLog.Error("Tenure.OnHeroKilled failed", e); }
        }

        // ── Rotation clock ──────────────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            int interval = Math.Max(1, Tune.TenureRotationIntervalDays);
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && IsMansabdari(k)))
            {
                // One town/castle rotation AND one village-jagir rotation per realm per week —
                // keep the upheaval paced. Village jagirs (the zamindari layer) rotate on the
                // same term clock but through the feudal layer, not engine ownership.
                Settlement due = OverdueFiefs(k, interval).FirstOrDefault();
                if (due != null) TYTLog.Guard("Tenure.Rotate", () => IssueRotation(k, due));

                Settlement dueVillage = OverdueVillageJagirs(k, interval).FirstOrDefault();
                if (dueVillage != null) TYTLog.Guard("Tenure.RotateVillage", () => IssueVillageRotation(k, dueVillage));
            }
        }

        // Opinion records on a rotation: the one who receives the grant feels a Favor toward the
        // crown; the one dragged off his fief holds a Grudge (only in the friction cases). These
        // ride alongside the raw relation change — the personal ledger the grievance dialogue reads.
        private static void RotationOpinions(Hero ruler, Hero displaced, Hero successor, bool grudge, float grudgeMag = 0f)
        {
            var op = OpinionBehavior.Instance;
            if (op == null) return;
            if (successor != null && ruler != null && successor != ruler)
                op.AddOpinion(successor, ruler, OpinionMath.OpinionType.Favor);
            if (grudge && displaced != null && ruler != null && displaced != ruler)
                op.AddOpinion(displaced, ruler, OpinionMath.OpinionType.Grudge, grudgeMag);
        }

        private IEnumerable<Settlement> OverdueFiefs(Kingdom k, int interval)
            => k.Settlements
                .Where(s => s != null && (s.IsTown || s.IsCastle)
                            && s.OwnerClan != null && s.OwnerClan != k.RulingClan
                            && !s.OwnerClan.IsUnderMercenaryService
                            && s.OwnerClan.Leader != null && s.OwnerClan.Leader.IsAlive)
                .Where(s => Today - AppointedDay(s) >= interval)
                .OrderByDescending(s => Today - AppointedDay(s));

        // What the crown must spend to move this house on: the rank of the office and the depth of the
        // holder's roots (Rusukh), then multiplied by his CLAIM on the fief (ch.30 §2). A house that has
        // sat somewhere twenty years costs three times as much to shift as a newcomer.
        public int RotationCost(Kingdom k, Settlement s, Clan holderClan)
        {
            int rank = MansabdariBehavior.Instance?.GetRankIndex(holderClan) ?? 1;
            float rusukh = RusukhBehavior.Instance?.GetRusukh(holderClan?.Leader, s) ?? 0f;
            int baseCost = MansabTenureMath.EnforcementInfluenceCost(rank, rusukh, Tune.TenureRotationBaseInfluence);
            return ClaimMath.RotationInfluenceCost(baseCost, ClaimsBehavior.Instance?.HolderClaim(s) ?? 0f);
        }

        private void IssueRotation(Kingdom k, Settlement s)
        {
            Clan holderClan = s.OwnerClan;
            Hero holder = holderClan?.Leader;
            if (holder == null) return;

            Hero auto = ChooseSuccessor(k, s, holderClan);
            // No one qualified and not the player's call -> defer by resetting the clock.
            if (auto == null && k.Leader != Hero.MainHero) { _appointed[s.StringId] = Today; return; }

            // The review has happened; restart the clock regardless of how it resolves so the order
            // isn't re-issued every single week.
            _appointed[s.StringId] = Today;

            // THE PRICE OF THE WRIT (ch.30 §2). Rotation used to be free; a claim now makes an entrenched
            // house dear to move — and past the crown's purse, immovable. This is what turns Mansabdari
            // into a race: rotate them early and cheaply, or watch the jagirdars entrench past recall.
            Clan ruling = k.RulingClan;
            int cost = RotationCost(k, s, holderClan);
            float claim = ClaimsBehavior.Instance?.HolderClaim(s) ?? 0f;
            bool playerRules = k.Leader == Hero.MainHero;

            if (!ClaimMath.CanAffordRotation(ruling?.Influence ?? 0f, cost))
            {
                if (playerRules)
                    RoyalFarmaan.Issue("The Writ Stops at His Gate", $"Concerning {s.Name}",
                        $"{holder.Name} has held {s.Name} long enough to be part of it — {ClaimMath.Describe(claim)}, " +
                        $"{claim:0.0} years of standing. To move him on would cost the court {cost} influence, and the " +
                        $"crown has but {ruling?.Influence ?? 0f:0}. The order cannot be issued. He sits where he sits.",
                        seal: "The writ of the crown reaches only so far", primary: "Then he keeps it — for now",
                        dedupeKey: "rotfail:" + s.StringId, cooldownDays: 60);
                TYTLog.Info($"Tenure: rotation of {holder.Name} from {s.Name} UNAFFORDABLE (cost {cost}, purse {ruling?.Influence ?? 0f:0}, claim {claim:0.0}).");
                return;
            }

            // The player's own house is being rotated by an AI crown: the crown pays, then he chooses to
            // comply or defy (the sovereign-choice below is for when the PLAYER wears the crown).
            if (holderClan == Clan.PlayerClan) { ChargeRotation(ruling, cost); PlayerRotationChoice(k, s, auto); return; }

            // The player wears the crown: rotation is a JUDGEMENT, not an automatic tick. He is shown the
            // price and decides whether this house is worth the influence.
            if (playerRules) { SovereignRotationChoice(k, s, holderClan, holder, cost, claim); return; }

            // An AI crown pays and proceeds.
            ChargeRotation(ruling, cost);
            ResolveRecipient(k, s, holderClan, $"The tenure of {holder.Name} at {s.Name} has run its term.",
                succ => { if (succ != null) AiRotation(k, s, holderClan, holder, succ); });
        }

        private static void ChargeRotation(Clan ruling, int cost)
        {
            if (ruling != null && cost > 0) ChangeClanInfluenceAction.Apply(ruling, -cost);
        }

        // The sovereign weighs the price. Shifting an entrenched magnate is expensive; leaving him is
        // free but lets his roots go deeper still — and every year he keeps the seat, his claim grows.
        private void SovereignRotationChoice(Kingdom k, Settlement s, Clan holderClan, Hero holder, int cost, float claim)
        {
            Clan ruling = k.RulingClan;
            RoyalFarmaan.Issue("A Tenure Has Run Its Term", $"Concerning {s.Name}",
                $"{holder.Name} has held {s.Name} for his full term. He has {ClaimMath.Describe(claim)} upon it — " +
                $"{claim:0.0} years of standing — and to move him on will cost the court {cost} influence " +
                $"(the crown holds {ruling?.Influence ?? 0f:0}). Leave him, and his roots go deeper: every year he " +
                "keeps the seat, his house's claim upon it grows, and the price of ever moving him rises.",
                seal: "The law of Mansabdari",
                primary: $"Order the transfer ({cost} influence)",
                onPrimary: () => TYTLog.Guard("Tenure.SovereignRotate", () =>
                {
                    // Re-check the purse: the farmaan may have sat unanswered while influence was spent.
                    if (!ClaimMath.CanAffordRotation(ruling?.Influence ?? 0f, cost))
                    { Notify($"The court no longer has the {cost} influence to move {holder.Name} from {s.Name}.", true); return; }
                    ChargeRotation(ruling, cost);
                    ResolveRecipient(k, s, holderClan, $"The tenure of {holder.Name} at {s.Name} has run its term.",
                        succ => { if (succ != null) AiRotation(k, s, holderClan, holder, succ); });
                }),
                secondary: "Leave him where he sits",
                onSecondary: () => Notify($"{holder.Name} keeps {s.Name}. His house sinks another year's roots into it.", false));
        }

        // AI holder: resolve the full risk ladder against his Rusukh-driven defiance.
        private void AiRotation(Kingdom k, Settlement s, Clan holderClan, Hero holder, Hero successor)
        {
            float chance = RusukhBehavior.Instance != null ? RusukhBehavior.Instance.DefianceChance(holder, s) : 0f;
            int rank = MansabdariBehavior.Instance?.GetRankIndex(holderClan) ?? 1;
            var result = MansabTenureMath.ResolveRotationOrder(chance, MBRandom.RandomFloat);
            bool playerRules = k == Hero.MainHero?.Clan?.Kingdom && k.Leader == Hero.MainHero;

            switch (result)
            {
                case MansabTenureMath.RotationResult.Complied:
                    TransferFief(s, successor);
                    RotationOpinions(k.Leader, holder, successor, grudge: false);
                    if (playerRules) Notify($"By your edict, {holder.Name} surrenders {s.Name}; {successor.Name} is appointed in his place.", false);
                    break;

                case MansabTenureMath.RotationResult.Reprimand:
                    TransferFief(s, successor);
                    if (k.Leader != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(holder, k.Leader, -5);
                    RotationOpinions(k.Leader, holder, successor, grudge: true);
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -MansabTenureMath.AuthorityPenaltyForDefiance(rank, 0.5f), "a mansabdar protested his transfer");
                    if (playerRules) Notify($"{holder.Name} protests his transfer from {s.Name} but yields; the court's standing is dented.", false);
                    break;

                case MansabTenureMath.RotationResult.Dismissal:
                    TransferFief(s, successor);
                    if (k.Leader != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(holder, k.Leader, -10);
                    if (holderClan != null) ChangeClanInfluenceAction.Apply(holderClan, -20f);
                    RotationOpinions(k.Leader, holder, successor, grudge: true, grudgeMag: -18f);
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -MansabTenureMath.AuthorityPenaltyForDefiance(rank, 1f), "a mansabdar was dismissed by force");
                    if (playerRules) Notify($"{holder.Name} refuses to leave {s.Name}; you dismiss him by force and seat {successor.Name}.", true);
                    break;

                case MansabTenureMath.RotationResult.Traitor:
                    // His roots run too deep to be moved — forcing him means rebellion. The fief stays
                    // with him as he secedes.
                    DeclareTraitor(k, s, holderClan, holder);
                    break;
            }
        }

        // Player as a VASSAL being rotated: the power-fantasy choice — comply, or defy and gamble his
        // Rusukh against the crown.
        private void PlayerRotationChoice(Kingdom k, Settlement s, Hero successor)
        {
            Hero ruler = k.Leader;
            string body = $"By the law of Mansabdari, the crown of {k.Name} orders you to surrender {s.Name} and take a " +
                          $"posting elsewhere. {successor.Name} is named to succeed you. Will you comply, or defy the farmaan?";
            RoyalFarmaan.FromRuler(k, "Order of Transfer", body,
                "I comply, and surrender the fief", () => TYTLog.Guard("Tenure.PlayerComply", () =>
                {
                    TransferFief(s, successor);
                    if (ruler != null && ruler != Hero.MainHero)
                        OpinionBehavior.Instance?.AddOpinion(ruler, Hero.MainHero, OpinionMath.OpinionType.Favor);
                    Notify($"You surrender {s.Name} to {successor.Name} as the crown commands.", false);
                }),
                "I defy the order", () => TYTLog.Guard("Tenure.PlayerDefy", () => PlayerDefy(k, s, ruler)));
        }

        private void PlayerDefy(Kingdom k, Settlement s, Hero ruler)
        {
            float chance = RusukhBehavior.Instance != null ? RusukhBehavior.Instance.DefianceChance(Hero.MainHero, s) : 0f;
            bool held = MansabTenureMath.Resolve(chance, MBRandom.RandomFloat) == MansabTenureMath.RotationOutcome.Defied;
            if (held)
            {
                // The notables stand with you; the order cannot be enforced — but the crown is humiliated.
                if (ruler != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler, -8);
                    if (ruler != Hero.MainHero) OpinionBehavior.Instance?.AddOpinion(ruler, Hero.MainHero, OpinionMath.OpinionType.Grudge);
                }
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -3f, "a mansabdar defied a transfer order");
                Notify($"Your roots in {s.Name} hold firm — the notables stand with you and the crown's order comes to nothing. " +
                       "The court will remember the slight.", false);
            }
            else
            {
                // Your footing was too shallow; the crown strips you of the fief.
                Hero successor = ChooseSuccessor(k, s, Clan.PlayerClan) ?? ruler;
                if (successor != null) TransferFief(s, successor);
                if (ruler != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler, -5);
                    if (ruler != Hero.MainHero) OpinionBehavior.Instance?.AddOpinion(ruler, Hero.MainHero, OpinionMath.OpinionType.Grudge, -8f);
                }
                ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -20f);
                Notify($"Your defiance falters — without deep enough roots in {s.Name}, the crown enforces the transfer and strips you of the fief.", true);
            }
        }

        // ── Village-jagir rotation (the zamindari layer) ────────────────────────────────
        // Villages are held through the feudal layer (a seated zamindar), not engine ownership,
        // so they rotate on their own pass: reassign the zamindar through FeudalTitlesBehavior.
        private IEnumerable<Settlement> OverdueVillageJagirs(Kingdom k, int interval)
        {
            var ft = FeudalTitlesBehavior.Instance;
            if (ft == null) return Enumerable.Empty<Settlement>();
            return Settlement.All
                .Where(s => s != null && s.IsVillage && s.MapFaction == k)
                .Where(s =>
                {
                    Hero lord = ft.GetVillageLord(s);
                    return lord != null && lord.IsAlive && lord.IsLord && lord.Clan != null
                           && lord.Clan != k.RulingClan && lord.Clan.Kingdom == k
                           && !lord.Clan.IsUnderMercenaryService;
                })
                .Where(s => Today - AppointedDay(s) >= interval)
                .OrderByDescending(s => Today - AppointedDay(s));
        }

        private void IssueVillageRotation(Kingdom k, Settlement v)
        {
            var ft = FeudalTitlesBehavior.Instance;
            Hero holder = ft?.GetVillageLord(v);
            Clan holderClan = holder?.Clan;
            if (holder == null || holderClan == null) return;

            Hero successor = ChooseVillageSuccessor(k, v, holderClan);
            if (successor == null && k.Leader != Hero.MainHero) { _appointed[v.StringId] = Today; return; }

            _appointed[v.StringId] = Today; // review has happened; restart the clock

            if (holderClan == Clan.PlayerClan) { PlayerVillageRotationChoice(k, v, successor); return; }

            float chance = RusukhBehavior.Instance != null ? RusukhBehavior.Instance.DefianceChance(holder, v) : 0f;
            int rank = MansabdariBehavior.Instance?.GetRankIndex(holderClan) ?? 1;
            var result = MansabTenureMath.ResolveRotationOrder(chance, MBRandom.RandomFloat);
            bool playerRules = k.Leader == Hero.MainHero;
            Hero ruler = k.Leader;

            switch (result)
            {
                case MansabTenureMath.RotationResult.Complied:
                    ReseatZamindar(v, successor);
                    RotationOpinions(ruler, holder, successor, grudge: false);
                    if (playerRules) Notify($"By your edict, {holder.Name} yields the zamindari of {v.Name}; {successor.Name} is seated in his place.", false);
                    break;

                case MansabTenureMath.RotationResult.Reprimand:
                    ReseatZamindar(v, successor);
                    if (ruler != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(holder, ruler, -4);
                    RotationOpinions(ruler, holder, successor, grudge: true);
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -MansabTenureMath.AuthorityPenaltyForDefiance(rank, 0.25f), "a zamindar protested his transfer");
                    if (playerRules) Notify($"{holder.Name} grumbles at losing the zamindari of {v.Name}, but yields.", false);
                    break;

                case MansabTenureMath.RotationResult.Dismissal:
                    ReseatZamindar(v, successor);
                    if (ruler != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(holder, ruler, -8);
                    if (holderClan != null) ChangeClanInfluenceAction.Apply(holderClan, -10f);
                    RotationOpinions(ruler, holder, successor, grudge: true, grudgeMag: -18f);
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -MansabTenureMath.AuthorityPenaltyForDefiance(rank, 0.5f), "a zamindar was dismissed by force");
                    if (playerRules) Notify($"{holder.Name} clings to {v.Name}; you strip him of the zamindari and seat {successor.Name}.", true);
                    break;

                case MansabTenureMath.RotationResult.Traitor:
                    // A village zamindar hasn't the strength of a great amir to secede — his defiance
                    // ends in forced removal and a lasting grudge, not a rebel kingdom.
                    ReseatZamindar(v, successor);
                    if (ruler != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(holder, ruler, -12);
                    RotationOpinions(ruler, holder, successor, grudge: true, grudgeMag: -24f);
                    if (playerRules) Notify($"{holder.Name} bitterly resists losing {v.Name} — but a zamindar cannot defy the crown as an amir might. He is removed, and will not forget it.", true);
                    break;
            }
        }

        private void PlayerVillageRotationChoice(Kingdom k, Settlement v, Hero successor)
        {
            Hero ruler = k.Leader;
            if (successor == null)
            { Notify($"The crown would rotate your zamindari of {v.Name}, but finds no fit successor; you keep it for now.", false); return; }

            string body = $"By the law of Mansabdari, the crown of {k.Name} orders you to surrender the zamindari of {v.Name}. " +
                          $"{successor.Name} is named to succeed you. Will you comply, or defy the farmaan?";
            RoyalFarmaan.FromRuler(k, "Order of Transfer (Zamindari)", body,
                "I comply, and surrender the village", () => TYTLog.Guard("Tenure.PlayerVillageComply", () =>
                {
                    ReseatZamindar(v, successor);
                    if (ruler != null && ruler != Hero.MainHero)
                        OpinionBehavior.Instance?.AddOpinion(ruler, Hero.MainHero, OpinionMath.OpinionType.Favor);
                    Notify($"You surrender the zamindari of {v.Name} to {successor.Name} as the crown commands.", false);
                }),
                "I defy the order", () => TYTLog.Guard("Tenure.PlayerVillageDefy", () => PlayerVillageDefy(k, v, ruler, successor)));
        }

        private void PlayerVillageDefy(Kingdom k, Settlement v, Hero ruler, Hero successor)
        {
            float chance = RusukhBehavior.Instance != null ? RusukhBehavior.Instance.DefianceChance(Hero.MainHero, v) : 0f;
            bool held = MansabTenureMath.Resolve(chance, MBRandom.RandomFloat) == MansabTenureMath.RotationOutcome.Defied;
            if (held)
            {
                if (ruler != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler, -6);
                    if (ruler != Hero.MainHero) OpinionBehavior.Instance?.AddOpinion(ruler, Hero.MainHero, OpinionMath.OpinionType.Grudge);
                }
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -2f, "a zamindar defied a transfer order");
                Notify($"Your roots in {v.Name} hold — the local gentry stand with you and the order comes to nothing. The court remembers the slight.", false);
            }
            else
            {
                ReseatZamindar(v, successor ?? ruler);
                if (ruler != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler, -4);
                    if (ruler != Hero.MainHero) OpinionBehavior.Instance?.AddOpinion(ruler, Hero.MainHero, OpinionMath.OpinionType.Grudge, -8f);
                }
                ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -10f);
                Notify($"Your defiance falters — the crown enforces the transfer and strips you of the zamindari of {v.Name}.", true);
            }
        }

        // The most deserving clan to receive a village zamindari: a landed-eligible lord loyal to
        // the crown who holds the fewest villages (spread the land), never the outgoing house.
        private Hero ChooseVillageSuccessor(Kingdom k, Settlement v, Clan exclude)
        {
            var ft = FeudalTitlesBehavior.Instance;
            Hero ruler = k.Leader;
            var candidates = k.Clans.Where(c =>
                c != null && !c.IsEliminated && c != exclude && c != k.RulingClan
                && !c.IsUnderMercenaryService && c.Leader != null && c.Leader.IsAlive && c.Leader.IsLord
                && MansabdariBehavior.Instance?.CanHold(c, v) != false).ToList();
            if (candidates.Count == 0) return null;
            return candidates
                .OrderBy(c => ft?.GetVillagesLordedBy(c.Leader).Count ?? 0)
                .ThenByDescending(c => ruler != null ? CharacterRelationManager.GetHeroRelation(c.Leader, ruler) : 0)
                .First().Leader;
        }

        private void ReseatZamindar(Settlement v, Hero successor)
        {
            if (successor == null) return;
            FeudalTitlesBehavior.Instance?.AssignZamindar(v, successor);
            _appointed[v.StringId] = Today;
        }

        private void DeclareTraitor(Kingdom k, Settlement s, Clan holderClan, Hero holder)
        {
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -5f, "a mansabdar rose in revolt against a transfer order");
            if (RevoltCascadeBehavior.Instance != null)
            {
                Kingdom rebel = RevoltCascadeBehavior.Instance.CreateRebelKingdom(holderClan, s, $"Dominion of {holderClan.Name}");
                if (rebel != null)
                {
                    RevoltCascadeBehavior.Instance.EnsureAtWar(rebel, k);
                    if (k == Hero.MainHero?.Clan?.Kingdom)
                        Notify($"{holder.Name} refuses the crown's transfer order and raises {rebel.Name} in open revolt!", true);
                    TYTLog.Info($"Tenure: {holderClan.Name} declared traitor over a transfer of {s.Name}; seceded as {rebel.Name}.");
                    return;
                }
            }
            // Fallback if secession couldn't be staged: treat as a forced dismissal.
            Hero successor = ChooseSuccessor(k, s, holderClan) ?? k.RulingClan?.Leader;
            if (successor != null) TransferFief(s, successor);
        }

        // ── Successor selection (by mansab rank) ────────────────────────────────────────
        // The most deserving clan to receive a crown grant: loyal to the ruler, qualified by mansab,
        // and holding the least already (spread the land), never the outgoing house or a mercenary.
        private Hero ChooseSuccessor(Kingdom k, Settlement s, Clan exclude)
        {
            int required = MansabdariBehavior.Instance?.RequiredRankIndex(s) ?? 0;
            Hero ruler = k.Leader;

            var candidates = k.Clans.Where(c =>
                c != null && !c.IsEliminated && c != exclude && c != k.RulingClan
                && !c.IsUnderMercenaryService && c.Leader != null && c.Leader.IsAlive
                && (MansabdariBehavior.Instance?.GetRankIndex(c) ?? 0) >= required).ToList();

            if (candidates.Count == 0) return null;

            return candidates
                .OrderByDescending(c => ruler != null ? CharacterRelationManager.GetHeroRelation(c.Leader, ruler) : 0)
                .ThenBy(c => Fortifications(c).Count)
                .ThenByDescending(c => MansabdariBehavior.Instance?.GetRankIndex(c) ?? 0)
                .First().Leader;
        }

        // Decide who receives a reverting/rotating fief. A PLAYER-sovereign is given the choice (any
        // qualified vassal, or keep it to the crown); an AI sovereign auto-assigns by ChooseSuccessor.
        private void ResolveRecipient(Kingdom k, Settlement s, Clan exclude, string reason, Action<Hero> then)
        {
            if (k == null || s == null) { then(null); return; }
            Hero auto = ChooseSuccessor(k, s, exclude);
            if (k.Leader != Hero.MainHero) { then(auto ?? k.RulingClan?.Leader); return; }

            int required = MansabdariBehavior.Instance?.RequiredRankIndex(s) ?? 0;
            var elements = new List<InquiryElement>
            {
                new InquiryElement(k.Leader, $"Keep {s.Name} to the crown", null, true, "Hold the fief in your own demesne for now."),
            };
            foreach (Clan c in k.Clans
                .Where(c => c != null && !c.IsEliminated && c != exclude && c != k.RulingClan
                            && !c.IsUnderMercenaryService && c.Leader != null && c.Leader.IsAlive)
                .OrderByDescending(c => MansabdariBehavior.Instance?.GetRankIndex(c) ?? 0)
                .ThenByDescending(c => CharacterRelationManager.GetHeroRelation(k.Leader, c.Leader))
                .Take(12))
            {
                int idx = MansabdariBehavior.Instance?.GetRankIndex(c) ?? 0;
                elements.Add(new InquiryElement(c.Leader,
                    $"{c.Leader.Name} — {MansabdariBehavior.Instance?.GetTitle(c) ?? "lord"}", null, idx >= required,
                    $"Mansab {idx} (needs {required}). Relation {CharacterRelationManager.GetHeroRelation(k.Leader, c.Leader)}, {Fortifications(c).Count} fief(s)."));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Grant {s.Name}", $"{reason} As sovereign, to whom do you grant it?",
                elements, true, 1, 1, "Grant", "Keep to crown",
                sel => then(sel != null && sel.Count > 0 && sel[0].Identifier is Hero h ? h : k.Leader),
                _ => then(k.Leader), "", false), false, false);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────
        private static List<Settlement> Fortifications(Clan clan)
            => clan?.Settlements?.Where(s => s != null && (s.IsTown || s.IsCastle)).ToList() ?? new List<Settlement>();

        private static float AverageRusukh(Hero leader, List<Settlement> fiefs)
        {
            if (RusukhBehavior.Instance == null || fiefs.Count == 0) return 0f;
            float sum = 0f;
            foreach (var s in fiefs) sum += RusukhBehavior.Instance.GetRusukh(leader, s);
            return sum / fiefs.Count;
        }

        private double AppointedDay(Settlement s)
        {
            if (s == null) return Today;
            if (_appointed.TryGetValue(s.StringId, out double d)) return d;
            _appointed[s.StringId] = Today;   // lazy init — never instantly "overdue"
            return Today;
        }

        private bool TransferFief(Settlement s, Hero newHolder)
        {
            if (s?.OwnerClan?.Leader == newHolder || newHolder == null) return false;
            try
            {
                ChangeOwnerOfSettlementAction.ApplyByGift(s, newHolder);
                _appointed[s.StringId] = Today;   // (also set by OnSettlementOwnerChanged; be explicit)
                return true;
            }
            catch (Exception e) { TYTLog.Error($"Tenure.TransferFief({s?.Name}) failed", e); return false; }
        }

        private void OnSettlementOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!_ready) return;   // world-gen fires this for every fief in parallel — do NOT touch the dict
            if (s != null && (s.IsTown || s.IsCastle)) _appointed[s.StringId] = Today;
        }

        private void RefreshLeaderSnapshot()
        {
            foreach (Clan c in Clan.All)
            {
                if (c?.Leader == null) continue;
                _leaderOf[c.StringId] = c.Leader.StringId;
            }
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Save/load ──────────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var ids = _mansabdari.ToList();
            dataStore.SyncData("tyt_tenure_mansabdari", ref ids);

            var apptIds = _appointed.Keys.ToList();
            var apptDays = _appointed.Values.ToList();
            dataStore.SyncData("tyt_tenure_apptIds", ref apptIds);
            dataStore.SyncData("tyt_tenure_apptDays", ref apptDays);

            if (!dataStore.IsSaving)
            {
                _mansabdari = new HashSet<string>(ids ?? new List<string>());
                _appointed = new Dictionary<string, double>();
                if (apptIds != null && apptDays != null)
                    for (int i = 0; i < apptIds.Count && i < apptDays.Count; i++) _appointed[apptIds[i]] = apptDays[i];
            }
        }

        // ── Console (testing) ──────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("tenure", "hindostan")]
        public static string TenureStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Clan.PlayerClan?.Kingdom;
            if (k == null) return "You serve no realm.";
            var sb = new StringBuilder();
            sb.AppendLine($"{k.Name}: tenure law = {Instance.GetLaw(k)}");
            if (Instance.IsMansabdari(k))
            {
                sb.AppendLine("Already Mansabdari. 'hindostan.tenure_feudal' reverts; 'hindostan.tenure_rotate' / 'hindostan.tenure_rotate_village' force a review.");
                int interval = Math.Max(1, Tune.TenureRotationIntervalDays);
                var overdue = Instance.OverdueFiefs(k, interval).ToList();
                var overdueVil = Instance.OverdueVillageJagirs(k, interval).ToList();
                sb.AppendLine($"Overdue town/castle fiefs ({interval}-day term): {overdue.Count}" +
                              (overdue.Count > 0 ? " — " + string.Join(", ", overdue.Take(5).Select(s => s.Name.ToString())) : ""));
                sb.AppendLine($"Overdue village jagirs: {overdueVil.Count}" +
                              (overdueVil.Count > 0 ? " — " + string.Join(", ", overdueVil.Take(5).Select(s => s.Name.ToString())) : ""));
                return sb.ToString();
            }
            var q = Instance.QuoteMansabdari(k);
            sb.AppendLine($"To impose Mansabdari: influence {q.Influence}, gold {q.Gold}, nobles affected {q.AffectedNobles}, resisters {q.Resisters}.");
            sb.AppendLine(q.Allowed ? "Allowed. 'hindostan.tenure_mansabdari' to enact." : "Blocked: " + q.Reason);
            return sb.ToString();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("tenure_mansabdari", "hindostan")]
        public static string EnactCmd(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Clan.PlayerClan?.Kingdom;
            if (k == null) return "You serve no realm.";
            return Instance.TryEnactMansabdari(k, out string reason) ? $"{k.Name} is now under Mansabdari tenure." : "Failed: " + reason;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("tenure_feudal", "hindostan")]
        public static string RevertCmd(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Clan.PlayerClan?.Kingdom;
            if (k == null) return "You serve no realm.";
            return Instance.RevertToFeudal(k, out string reason) ? $"{k.Name} is restored to feudal tenure." : "Failed: " + reason;
        }

        // Force one rotation review in the player's realm (ignores the term clock) for testing.
        [CommandLineFunctionality.CommandLineArgumentFunction("tenure_rotate", "hindostan")]
        public static string RotateCmd(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Clan.PlayerClan?.Kingdom;
            if (k == null) return "You serve no realm.";
            if (!Instance.IsMansabdari(k)) return $"{k.Name} is not under Mansabdari tenure.";
            Settlement s = k.Settlements
                .Where(x => x != null && (x.IsTown || x.IsCastle) && x.OwnerClan != null && x.OwnerClan != k.RulingClan
                            && x.OwnerClan.Leader != null && x.OwnerClan.Leader.IsAlive)
                .OrderByDescending(x => Instance.Today2(x)).FirstOrDefault();
            if (s == null) return "No eligible fief to rotate.";
            Instance.IssueRotation(k, s);
            return $"Issued a rotation review for {s.Name} (held by {s.OwnerClan?.Leader?.Name}).";
        }

        // Force one village-jagir rotation review in the player's realm (ignores the term clock).
        [CommandLineFunctionality.CommandLineArgumentFunction("tenure_rotate_village", "hindostan")]
        public static string RotateVillageCmd(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Clan.PlayerClan?.Kingdom;
            if (k == null) return "You serve no realm.";
            if (!Instance.IsMansabdari(k)) return $"{k.Name} is not under Mansabdari tenure.";
            var ft = FeudalTitlesBehavior.Instance;
            Settlement v = Settlement.All
                .Where(x => x != null && x.IsVillage && x.MapFaction == k)
                .Where(x => { Hero l = ft?.GetVillageLord(x); return l != null && l.IsAlive && l.IsLord && l.Clan != null && l.Clan != k.RulingClan; })
                .OrderByDescending(x => Instance.Today2(x)).FirstOrDefault();
            if (v == null) return "No eligible village jagir to rotate (need an AI lord zamindar in your realm).";
            Instance.IssueVillageRotation(k, v);
            return $"Issued a village-jagir rotation review for {v.Name} (zamindar {ft?.GetVillageLord(v)?.Name}).";
        }

        private double Today2(Settlement s) => Today - AppointedDay(s);
    }
}
