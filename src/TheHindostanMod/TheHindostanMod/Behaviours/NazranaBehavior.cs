using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // The nazrana — the ceremonial gift cycle of the court (wiki Ch.17 §2). Distinct from
    // war indemnities (WarfareBehavior peace terms) and tributary payments (WarfareBehavior
    // tributaries): this is the periodic gift every vassal owes his sovereign as a mark of
    // submission and standing.
    //
    //   As a VASSAL: every cycle the court calls for your nazrana. Present a minimal,
    //   expected, or lavish gift (relation/influence follow accordingly) within 14 days —
    //   or be marked; three lapses and the court reduces your mansab.
    //   As a RULER: your lords' gifts trickle into your purse weekly, summarized monthly;
    //   lords who despise you withhold, and each withholder saps imperial authority.
    //
    // Amounts and effects live in Util.NazranaMath (unit-tested).
    public class NazranaBehavior : CampaignBehaviorBase
    {
        public static NazranaBehavior Instance { get; private set; }

        private int _nextCallDay = -1;      // day the next nazrana call fires
        private int _deadlineDay = -1;      // active call's deadline (-1 = no call pending)
        private int _missed;                // consecutive lapsed calls
        private int _lastRulerSweepDay = -1;
        private int _lastSummaryDay = -1;
        private int _monthGold;             // ruler: gold received since the last summary
        private int _monthWithholders;      // ruler: lords who withheld since the last summary

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Nazrana.DailyTick", OnDailyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_naz_nextDay", ref _nextCallDay);
            dataStore.SyncData("hind_naz_deadline", ref _deadlineDay);
            dataStore.SyncData("hind_naz_missed", ref _missed);
            dataStore.SyncData("hind_naz_lastSweep", ref _lastRulerSweepDay);
            dataStore.SyncData("hind_naz_lastSummary", ref _lastSummaryDay);
            dataStore.SyncData("hind_naz_monthGold", ref _monthGold);
            dataStore.SyncData("hind_naz_monthWithheld", ref _monthWithholders);
        }

        private static Kingdom PK => Hero.MainHero?.Clan?.Kingdom;
        private static bool IsRuler => PK != null && PK.Leader == Hero.MainHero;

        // ── In-person presentation (the dialogue pack's bridge) ─────────────────────
        private bool _inPerson; // transient: the current tier choice came face to face

        public bool HasPendingCall => _deadlineDay >= 0;

        // Presenting the nazrana in person, before the sovereign himself, carries more
        // weight than sending it with a courier: +2 relation over the farmaan path.
        public void PresentInPerson()
        {
            Kingdom k = PK;
            if (k == null || !HasPendingCall) return;
            int rank = MansabdariBehavior.Instance?.GetRankIndex(Clan.PlayerClan) ?? 0;
            _inPerson = true;
            OfferTiers(k, rank);
        }

        private void OnDailyTick()
        {
            if (!Config.Tune.NazranaEnabled) return;
            int today = (int)CampaignTime.Now.ToDays;

            if (IsRuler) { RulerSide(today); return; }
            VassalSide(today);
        }

        // ── The vassal's cycle ───────────────────────────────────────────────────────
        private void VassalSide(int today)
        {
            Kingdom k = PK;
            if (k == null || Clan.PlayerClan.IsUnderMercenaryService)
            { _nextCallDay = -1; _deadlineDay = -1; return; } // outside the cycle

            int rank = MansabdariBehavior.Instance?.GetRankIndex(Clan.PlayerClan) ?? 0;
            if (rank < 1) { _nextCallDay = -1; _deadlineDay = -1; return; } // unranked owes nothing

            if (_nextCallDay < 0) { _nextCallDay = today + Config.Tune.NazranaCycleDays; return; }

            // A call is pending: lapse?
            if (_deadlineDay >= 0)
            {
                if (today >= _deadlineDay) LapseCall(k);
                return;
            }

            if (today < _nextCallDay) return;

            // The call goes out.
            _deadlineDay = today + NazranaMath.DeadlineDays;
            int expected = NazranaMath.TierAmount(rank, NazranaMath.Tier.Expected, Config.Tune.NazranaBaseScale);
            RoyalFarmaan.FromRuler(k, "The Court Calls for Nazrana",
                $"By ancient custom every mansabdar presents his nazrana to the throne. The court expects a gift " +
                $"of some {expected} dinars of {Hero.MainHero.Name} within a fortnight. What is given — and how " +
                "generously — will be remembered.",
                "Present a gift", () => OfferTiers(k, rank),
                "Let them wait", null,
                dedupeKey: "nazrana_call", cooldownDays: 30);
        }

        private void OfferTiers(Kingdom k, int rank)
        {
            float scale = Config.Tune.NazranaBaseScale;
            var elements = new List<InquiryElement>();
            foreach (NazranaMath.Tier tier in new[] { NazranaMath.Tier.Minimal, NazranaMath.Tier.Expected, NazranaMath.Tier.Lavish })
            {
                int amount = NazranaMath.TierAmount(rank, tier, scale);
                var (rel, infl) = NazranaMath.TierEffects(tier);
                string label = tier == NazranaMath.Tier.Minimal ? $"A token gift ({amount} dinars)"
                    : tier == NazranaMath.Tier.Lavish ? $"A lavish gift ({amount} dinars)"
                    : $"The expected gift ({amount} dinars)";
                string hint = $"Relation with the sovereign {(rel >= 0 ? "+" : "")}{rel}, influence {(infl >= 0 ? "+" : "")}{infl}.";
                elements.Add(new InquiryElement(tier, label, null, Hero.MainHero.Gold >= amount,
                    Hero.MainHero.Gold >= amount ? hint : hint + " (You cannot afford this.)"));
            }
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Nazrana", "Choose the gift you will lay before the throne:",
                elements, true, 1, 1, "Present it", "Not yet",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is NazranaMath.Tier t) Present(k, rank, t); },
                _ => { }, "", false), false, false);
        }

        private void Present(Kingdom k, int rank, NazranaMath.Tier tier)
        {
            int amount = NazranaMath.TierAmount(rank, tier, Config.Tune.NazranaBaseScale);
            if (Hero.MainHero.Gold < amount) { Notify("You cannot afford that gift.", true); return; }

            if (amount > 0 && k.Leader != null)
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, k.Leader, amount, true);
            var (rel, infl) = NazranaMath.TierEffects(tier);
            if (_inPerson) { rel += 2; _inPerson = false; } // laid before the throne with your own hands
            if (k.Leader != null && rel != 0)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, k.Leader, rel);
            if (infl != 0) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, infl);

            _deadlineDay = -1;
            _missed = 0;
            _nextCallDay = (int)CampaignTime.Now.ToDays + Config.Tune.NazranaCycleDays;
            Notify(tier == NazranaMath.Tier.Lavish
                ? "Your lavish nazrana is the talk of the court."
                : tier == NazranaMath.Tier.Minimal
                    ? "Your token gift is received — and noted."
                    : "Your nazrana is received with due ceremony.", false);
        }

        private void LapseCall(Kingdom k)
        {
            _deadlineDay = -1;
            _missed++;
            _nextCallDay = (int)CampaignTime.Now.ToDays + Config.Tune.NazranaCycleDays;

            var (rel, infl) = NazranaMath.MissedEffects();
            if (k.Leader != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, k.Leader, rel);
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, infl);

            if (_missed >= NazranaMath.MissesBeforeDemotion)
            {
                _missed = 0;
                string newTitle = MansabdariBehavior.Instance?.AdjustRank(Clan.PlayerClan, -1);
                RoyalFarmaan.FromRuler(k, "A Mansab Reduced",
                    "Thrice the court has called for your nazrana, and thrice it has waited in vain. Such disdain " +
                    $"cannot pass: your mansab is reduced{(newTitle != null ? $" — you are now {newTitle}" : "")}. " +
                    "Mend your ways, and your rank may be restored.", "I hear the rebuke");
            }
            else
                Notify($"The nazrana went unpaid; the court marks your neglect ({_missed} of {NazranaMath.MissesBeforeDemotion}).", true);
        }

        // ── The ruler's income ───────────────────────────────────────────────────────
        private void RulerSide(int today)
        {
            _deadlineDay = -1; _nextCallDay = -1; // a sovereign owes no nazrana

            Kingdom k = PK;
            if (k?.Leader == null) return;

            if (_lastRulerSweepDay < 0) _lastRulerSweepDay = today;
            if (today - _lastRulerSweepDay >= 7)
            {
                _lastRulerSweepDay = today;
                float scale = Config.Tune.NazranaBaseScale;
                foreach (Clan c in k.Clans.Where(c => !c.IsEliminated && !c.IsUnderMercenaryService
                                                      && c.Leader != null && c != Clan.PlayerClan))
                {
                    int rank = MansabdariBehavior.Instance?.GetRankIndex(c) ?? 0;
                    // Whether a lord bows with a gift is a PERSONAL judgment (his opinion of
                    // you, oaths and grudges included), not a clan-book number.
                    int relation = (int)(OpinionBehavior.Instance?.EffectiveOpinion(c.Leader, Hero.MainHero)
                                         ?? CharacterRelationManager.GetHeroRelation(c.Leader, Hero.MainHero));
                    int pay = NazranaMath.WeeklyAiPayment(rank, relation, scale);
                    if (pay > 0 && c.Leader.Gold > pay)
                    {
                        GiveGoldAction.ApplyBetweenCharacters(c.Leader, Hero.MainHero, pay, true);
                        _monthGold += pay;
                    }
                    else if (rank >= 1 && NazranaMath.AiWithholds(relation))
                    {
                        _monthWithholders++;
                        ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -0.2f, "a lord withholds his nazrana");
                    }
                }
            }

            if (_lastSummaryDay < 0) _lastSummaryDay = today;
            if (today - _lastSummaryDay >= 30)
            {
                _lastSummaryDay = today;
                if (_monthGold > 0 || _monthWithholders > 0)
                    // A clean month is routine bookkeeping (digest); withheld gifts are a
                    // political signal that warrants the full decree.
                    RoyalFarmaan.Issue("The Month's Nazrana", "From the imperial treasury",
                        $"This month the lords of {k.Name} laid {_monthGold} dinars of nazrana before the throne." +
                        (_monthWithholders > 0
                            ? $" Yet {_monthWithholders} withholding(s) were recorded — there are lords who no longer bow."
                            : " Every house paid its due."),
                        seal: "Entered in the registers",
                        dedupeKey: "nazrana_summary",
                        priority: _monthWithholders > 0 ? Util.FarmaanPriority.Urgent : Util.FarmaanPriority.Routine,
                        cooldownDays: _monthWithholders > 0 ? 25 : 0);
                _monthGold = 0;
                _monthWithholders = 0;
            }
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));
    }
}
