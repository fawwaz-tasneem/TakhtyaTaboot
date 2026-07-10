using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // AI leadership challenges (wiki Ch.16) — the mirror of the player's War of Accession.
    // Each month a great amir who hates his ruler, or who sees a weak throne he is strong
    // enough to take (CivilWarMath, unit-tested), may raise the standard of revolt: his
    // coalition secedes into a hind_rebel_* kingdom (so the binary throne-war rules apply:
    // no white peace), and within 45 days the bid is settled by arms or by simulation.
    // The player, if of the realm, is asked to choose a side.
    //
    // One AI civil war at a time, realm-wide (v1): these are rare, defining convulsions,
    // not background noise.
    public class CivilWarBehavior : CampaignBehaviorBase
    {
        public static CivilWarBehavior Instance { get; private set; }

        private bool _active;
        private string _kingdomId = "";
        private string _rebelKingdomId = "";
        private string _challengerHeroId = "";
        private string _rulerHeroId = "";
        private int _deadlineDay = -1;
        private int _lastEvalDay = -1;

        public bool IsActive => _active;

        // The realm this rebel kingdom challenges, or null if it is not this behavior's war.
        // Read by ClanSafetyNetBehavior (scattered houses fold straight home).
        public Kingdom OriginRealmOf(string rebelKingdomId)
            => _active && !string.IsNullOrEmpty(rebelKingdomId) && rebelKingdomId == _rebelKingdomId
                ? TheKingdom : null;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("CivilWar.DailyTick", OnDailyTick));
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_cw_active", ref _active);
            dataStore.SyncData("hind_cw_kingdom", ref _kingdomId);
            dataStore.SyncData("hind_cw_rebel", ref _rebelKingdomId);
            dataStore.SyncData("hind_cw_challenger", ref _challengerHeroId);
            dataStore.SyncData("hind_cw_ruler", ref _rulerHeroId);
            dataStore.SyncData("hind_cw_deadline", ref _deadlineDay);
            dataStore.SyncData("hind_cw_lastEval", ref _lastEvalDay);
        }

        private Kingdom TheKingdom => Kingdom.All.FirstOrDefault(k => k.StringId == _kingdomId);
        private Kingdom RebelKingdom => Kingdom.All.FirstOrDefault(k => k.StringId == _rebelKingdomId);
        private static Hero AliveById(string id)
            => string.IsNullOrEmpty(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);

        // ── Detection (monthly) ──────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (!Util.WorldGen.Ready) return;
            if (_active) { RunWar(); return; }

            int today = (int)CampaignTime.Now.ToDays;
            if (_lastEvalDay >= 0 && today - _lastEvalDay < 30) return;
            _lastEvalDay = today;

            // One convulsion at a time, and never atop the player's own accession war.
            if (AccessionWarBehavior.Instance?.IsActive == true) return;

            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null))
            {
                if (ThroneWar.IsRebelKingdom(k)) continue;
                if (SuccessionBehavior.Instance?.GetCrisisState(k) != CrisisState.None) continue;
                // A realm already fighting one of its own breakaways is convulsed enough.
                if (Kingdom.All.Any(r => ThroneWar.IsRebelKingdom(r) && !r.IsEliminated && r.IsAtWarWith(k))) continue;

                float legitimacy = LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 60f;
                float rulingStrength = Math.Max(1f, k.RulingClan?.CurrentTotalStrength ?? 1f);

                foreach (Clan c in k.Clans)
                {
                    if (c == null || c.IsEliminated || c.IsUnderMercenaryService) continue;
                    if (c.Leader == null || c.Leader == k.Leader || c == Clan.PlayerClan) continue;
                    if ((MansabdariBehavior.Instance?.GetRankIndex(c) ?? 0) < MansabdariBehavior.MaxRankIndex) continue;
                    if (!c.Settlements.Any()) continue; // a bid needs a seat to raise the standard from

                    // The challenger's PERSONAL opinion of his ruler (oaths, grudges, favours),
                    // not just the clan ledger — a nursed grudge can push a loyal book-relation
                    // over the edge.
                    int relation = (int)(OpinionBehavior.Instance?.EffectiveOpinion(c.Leader, k.Leader)
                                         ?? CharacterRelationManager.GetHeroRelation(c.Leader, k.Leader));
                    float ratio = c.CurrentTotalStrength / rulingStrength;
                    if (!CivilWarMath.BidFires(relation, legitimacy, ratio, MBRandom.RandomFloat)) continue;

                    StartAiChallenge(k, c);
                    return; // one per month at most
                }
            }
        }

        private void StartAiChallenge(Kingdom kingdom, Clan challenger)
        {
            Settlement seat = challenger.Settlements.FirstOrDefault(s => s.IsTown)
                ?? challenger.Settlements.FirstOrDefault(s => s.IsCastle)
                ?? challenger.Settlements.FirstOrDefault();
            if (seat == null) return;

            Kingdom rebel = RevoltCascadeBehavior.Instance?.CreateRebelKingdom(challenger, seat, $"{challenger.Leader.Name}'s Claim");
            if (rebel == null) return;

            // Soldiers desert a lord who turns on his superior (Ch.16).
            ApplyDesertion(challenger, CivilWarMath.DesertionRate(
                MansabdariBehavior.Instance?.GetRankIndex(challenger) ?? 0, MansabdariBehavior.MaxRankIndex + 1));

            // Houses choose: the establishment holds to the throne, the rest ride with
            // whoever they favour more (same rule as the player's accession war).
            foreach (Clan c in kingdom.Clans.ToList())
            {
                if (c == null || c.IsEliminated || c.Leader == null) continue;
                if (c == challenger || c == kingdom.RulingClan || c == Clan.PlayerClan) continue;
                if (c.IsUnderMercenaryService) continue;
                bool establishment = CouncilBehavior.Instance?.GetPostOf(c.Leader) != null;
                float relChallenger = OpinionBehavior.Instance?.EffectiveOpinion(c.Leader, challenger.Leader)
                                      ?? CharacterRelationManager.GetHeroRelation(c.Leader, challenger.Leader);
                float relRuler = OpinionBehavior.Instance?.EffectiveOpinion(c.Leader, kingdom.Leader)
                                 ?? CharacterRelationManager.GetHeroRelation(c.Leader, kingdom.Leader);
                if (!establishment && relChallenger > relRuler)
                    try { ChangeKingdomAction.ApplyByJoinToKingdom(c, rebel, default(CampaignTime), false); } catch { }
            }
            RevoltCascadeBehavior.Instance?.EnsureAtWar(rebel, kingdom);

            _active = true;
            _kingdomId = kingdom.StringId;
            _rebelKingdomId = rebel.StringId;
            _challengerHeroId = challenger.Leader.StringId;
            _rulerHeroId = kingdom.Leader.StringId;
            _deadlineDay = (int)CampaignTime.Now.ToDays + CivilWarMath.WarDeadlineDays;

            TYTLog.Info($"AI civil war: {challenger.Leader.Name} challenges {kingdom.Leader.Name} for {kingdom.Name}.");

            // The player of the affected realm is asked to choose a side.
            if (Clan.PlayerClan?.Kingdom == kingdom && kingdom.Leader != Hero.MainHero)
            {
                Hero ruler = kingdom.Leader; Hero pretender = challenger.Leader;
                RoyalFarmaan.Issue("The Realm Takes Up Arms Against Itself",
                    $"Proclaimed at {seat.Name}",
                    $"{pretender.Name}, Amir-ul-Umara, has cast down the standard of {ruler.Name} and claims the throne " +
                    $"of {kingdom.Name}. Half the court wavers. Where do you stand?",
                    seal: "The realm holds its breath",
                    primary: "I stand with the throne",
                    onPrimary: () =>
                    {
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler, 5);
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, pretender, -10);
                    },
                    secondary: "I ride with the challenger",
                    onSecondary: () =>
                    {
                        Kingdom r = RebelKingdom;
                        if (r != null && !r.IsEliminated)
                            try { ChangeKingdomAction.ApplyByJoinToKingdom(Clan.PlayerClan, r, default(CampaignTime), false); } catch { }
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler, -20);
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, pretender, 10);
                    });
            }
            else if (Clan.PlayerClan?.Kingdom == kingdom)
                Notify($"{challenger.Leader.Name} rises against you — the realm is at civil war!", true);
        }

        private static void ApplyDesertion(Clan clan, float rate)
        {
            if (rate <= 0f) return;
            foreach (var wpc in clan.WarPartyComponents.ToList())
            {
                MobileParty party = wpc.MobileParty;
                if (party == null || !party.IsActive) continue;
                var roster = party.MemberRoster;
                int toRemove = (int)(roster.TotalRegulars * rate);
                for (int i = roster.Count - 1; i >= 0 && toRemove > 0; i--)
                {
                    var e = roster.GetElementCopyAtIndex(i);
                    if (e.Character == null || e.Character.IsHero) continue;
                    int take = Math.Min(e.Number, toRemove);
                    if (take > 0) { roster.AddToCounts(e.Character, -take); toRemove -= take; }
                }
            }
        }

        // ── Resolution ───────────────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool show)
        {
            if (!_active || victim == null) return;
            // ID compare — the victim is no longer in AllAliveHeroes (see AccessionWar A6).
            if (victim.StringId == _rulerHeroId) Resolve(rebelWins: true, "the throne stood empty before him");
            else if (victim.StringId == _challengerHeroId) Resolve(rebelWins: false, "the pretender is dead");
        }

        private void RunWar()
        {
            Kingdom rebel = RebelKingdom;
            Kingdom orig = TheKingdom;
            if (orig == null || orig.IsEliminated) { EndWar(); return; }
            if (rebel == null || rebel.IsEliminated || !rebel.Settlements.Any())
            { Resolve(rebelWins: false, "the rebel state was broken and scattered"); return; }
            if (AliveById(_rulerHeroId) == null) { Resolve(true, "the throne stood empty before him"); return; }
            if (AliveById(_challengerHeroId) == null) { Resolve(false, "the pretender is dead"); return; }

            if ((int)CampaignTime.Now.ToDays >= _deadlineDay)
            {
                float rebelStr = rebel.Clans.Where(c => !c.IsEliminated).Sum(c => c.CurrentTotalStrength);
                float loyalStr = orig.Clans.Where(c => !c.IsEliminated).Sum(c => c.CurrentTotalStrength);
                bool win = CivilWarMath.RebelWins(rebelStr, loyalStr, MBRandom.RandomFloat, MBRandom.RandomFloat);
                Resolve(win, win ? "a season of war broke the imperial host" : "the imperial host ground the rebels down");
            }
        }

        private void Resolve(bool rebelWins, string how)
        {
            Kingdom orig = TheKingdom;
            Kingdom rebel = RebelKingdom;
            Hero challenger = AliveById(_challengerHeroId);
            Clan challengerClan = challenger?.Clan
                ?? Clan.All.FirstOrDefault(c => c.Heroes.Any(h => h?.StringId == _challengerHeroId));
            EndWar();
            if (orig == null) return;

            try
            {
                if (rebel != null && orig != null && rebel.IsAtWarWith(orig))
                    ThroneWar.WithInternalPeace(() => MakePeaceAction.Apply(rebel, orig)); // never blocked (A4 pattern)
                if (rebel != null)
                    foreach (Clan c in rebel.Clans.ToList())
                        try { ChangeKingdomAction.ApplyByJoinToKingdom(c, orig, default(CampaignTime), false); } catch { }
                if (rebelWins && challengerClan != null && !challengerClan.IsEliminated)
                    ChangeRulingClanAction.Apply(orig, challengerClan);
                if (rebel != null && rebel != orig && !rebel.IsEliminated && !rebel.Settlements.Any())
                    try { DestroyKingdomAction.Apply(rebel); } catch { }
            }
            catch (Exception e) { TYTLog.Error("CivilWar.Resolve failed", e); }

            if (rebelWins)
            {
                if (challenger != null)
                {
                    LegitimacyBehavior.Instance?.SetLegitimacy(challenger, 45f); // a crown taken by arms sits uneasy
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(orig, -10f, "the throne changed hands by the sword");
                }
                AnnounceToRealm(orig, "The Throne Falls to the Sword",
                    $"By force of arms, {how}: {challenger?.Name.ToString() ?? "the pretender"} now sits the throne of {orig.Name}. " +
                    "All mansabdars are called to renew their oaths.");
            }
            else
            {
                if (challengerClan != null)
                {
                    MansabdariBehavior.Instance?.DebugSetRank(challengerClan, 1); // stripped to the lowest mansab
                    if (challenger != null && orig.Leader != null)
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(challenger, orig.Leader, -40);
                }
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(orig, 8f, "a pretender was crushed");
                AnnounceToRealm(orig, "The Pretender Humbled",
                    $"The bid of {challenger?.Name.ToString() ?? "the pretender"} for the throne of {orig.Name} is broken — {how}. " +
                    "His mansab is stripped and his standing lies in ruins.");
            }
        }

        private static void AnnounceToRealm(Kingdom k, string title, string body)
        {
            if (Clan.PlayerClan?.Kingdom == k)
                RoyalFarmaan.FromRuler(k, title, body, "So it is settled");
            else
                Notify($"{title}: {body}", false);
        }

        private void EndWar()
        {
            _active = false;
            _kingdomId = ""; _rebelKingdomId = ""; _challengerHeroId = ""; _rulerHeroId = "";
            _deadlineDay = -1;
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("force_ai_civil_war", "hindostan")]
        public static string ForceOne(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Instance._active) return "An AI civil war is already raging.";
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null && !ThroneWar.IsRebelKingdom(x)))
            {
                Clan c = k.Clans.FirstOrDefault(x => x != null && !x.IsEliminated && !x.IsUnderMercenaryService
                    && x.Leader != null && x.Leader != k.Leader && x != Clan.PlayerClan && x.Settlements.Any());
                if (c == null) continue;
                Instance.StartAiChallenge(k, c);
                return Instance._active ? $"{c.Leader.Name} rises against {k.Leader.Name} in {k.Name}." : "The challenge fizzled.";
            }
            return "No suitable challenger found.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("civil_war_status", "hindostan")]
        public static string Status(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (!Instance._active) return "No AI civil war is active.";
            return $"Civil war in {Instance.TheKingdom?.Name}: {AliveById(Instance._challengerHeroId)?.Name} vs " +
                   $"{AliveById(Instance._rulerHeroId)?.Name}, resolves by day {Instance._deadlineDay}.";
        }
    }
}
