using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Fief petitions (roadmap B.2) — replaces the instant "claim your due". A fief is no longer
    // bought for a flat influence fee: the player FILES a petition with the sovereign, staking a
    // gold gift (nazrana) and influence, and the court weighs it week by week. A lavish, well-
    // backed suit from a favoured servant is granted quickly; a meagre one waits; and below a
    // floor of the sovereign's regard the court refuses outright and keeps the stake.
    //
    // The weekly court tick is the queue engine: while a qualifying fief is available it rolls the
    // court's approval (FiefPetitionMath); when none is free the petition simply waits. Eligibility
    // and the grant itself are reused from CareerProgressionBehavior — this class owns only the
    // petition state, its stakes, and the weekly resolution. Surface: the mansab menu.
    public class FiefPetitionBehavior : CampaignBehaviorBase
    {
        public static FiefPetitionBehavior Instance { get; private set; }

        private bool _open;
        private int _gift;        // gold already paid to the court on filing (a gift, non-refundable)
        private int _influence;   // influence staked (refunded on withdrawal, forfeit on refusal)
        private int _tier;        // 0 village, 1 castle, 2 town — what was petitioned for
        private int _filedDay;

        public bool HasPetition => _open;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("FiefPetition.Weekly", OnWeeklyTick));
        }

        // ── Filing (from the mansab menu) ────────────────────────────────────────────
        public bool CanOpenPetitionMenu(out string label)
        {
            label = _open ? "Review your standing fief petition" : "Petition the court for a fief";
            if (_open) return true;
            return CareerProgressionBehavior.Instance?.TryFindClaimTarget(out _, out _, out _) ?? false;
        }

        public void OpenPetitionFlow()
        {
            if (_open) { ShowStanding(); return; }

            var career = CareerProgressionBehavior.Instance;
            if (career == null) { Notify("The court is not in session.", true); return; }
            if (!career.TryFindClaimTarget(out Settlement target, out int tier, out string reason))
            { Notify(reason, true); return; }

            Kingdom k = Clan.PlayerClan?.Kingdom;
            Hero sovereign = k?.Leader;
            float regard = sovereign != null && sovereign != Hero.MainHero
                ? (OpinionBehavior.Instance?.EffectiveOpinion(sovereign, Hero.MainHero) ?? 0f) : 100f;

            string kind = tier == 0 ? "a village zamindari" : tier == 1 ? "a castle" : "a town";
            var elements = new List<InquiryElement>();
            foreach (var (name, giftMul, infMul) in new[] { ("Modest", 0.5f, 0.6f), ("Handsome", 1f, 1f), ("Lavish", 2f, 1.5f) })
            {
                int gift = (int)(FiefPetitionMath.TierGiftBase(tier) * giftMul);
                int inf = (int)(FiefPetitionMath.TierInfluenceBase(tier) * infMul);
                bool affordable = Hero.MainHero.Gold >= gift && Clan.PlayerClan.Influence >= inf;
                float chance = FiefPetitionMath.ApprovalChancePerWeek(gift, inf, regard);
                elements.Add(new InquiryElement(
                    new int[] { gift, inf }, $"{name} — {gift} rupees + {inf} influence", null, affordable,
                    $"About {chance * 100f:0}% chance per week the court grants it once a fief is free."
                    + (affordable ? "" : " (you cannot afford this)")));
            }

            string warn = FiefPetitionMath.BelowRelationFloor(regard)
                ? "\n \nBEWARE: the sovereign's regard for you is below what the court will hear — a petition now will be refused, and the stake forfeit. Mend relations first."
                : "";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Petition for a Fief",
                $"You would petition {(sovereign != null ? sovereign.Name.ToString() : "the crown")} for {kind} befitting your rank " +
                $"({(target != null ? target.Name.ToString() : "an eligible fief")} is open now). Stake a gift and influence; the court " +
                "will weigh your suit week by week. The gift is given whatever the outcome; the influence is returned only if you withdraw." + warn,
                elements, true, 1, 1, "File the petition", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is int[] stake) TYTLog.Guard("FiefPetition.File", () => File(tier, stake[0], stake[1])); },
                _ => { }, "", false), false, false);
        }

        private void File(int tier, int gift, int influence)
        {
            if (Hero.MainHero.Gold < gift || Clan.PlayerClan.Influence < influence)
            { Notify("You cannot meet that stake.", true); return; }
            Hero.MainHero.ChangeHeroGold(-gift);
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -influence);
            _open = true; _gift = gift; _influence = influence; _tier = tier;
            _filedDay = (int)CampaignTime.Now.ToDays;

            Kingdom k = Clan.PlayerClan?.Kingdom;
            Notify($"Your petition for a fief is filed with the court of {k?.Name} — {gift} rupees gifted, {influence} influence staked. " +
                   "The court will weigh it in the weeks to come.", false);
            TYTLog.Info($"FiefPetition: filed tier {tier}, gift {gift}, inf {influence}.");
        }

        // ── The weekly court (the queue engine) ──────────────────────────────────────
        private void OnWeeklyTick()
        {
            if (!_open) return;
            var career = CareerProgressionBehavior.Instance;
            Kingdom k = Clan.PlayerClan?.Kingdom;
            Hero sovereign = k?.Leader;

            // The realm may have fallen out from under the petition (exile, kingdom gone).
            if (career == null || k == null) { Withdraw(silent: true); return; }

            // If the player has become sovereign, the petition is moot — he grants fiefs now.
            if (sovereign == Hero.MainHero)
            {
                RefundInfluence();
                Close();
                Notify("You now hold the throne — a sovereign bestows fiefs, he does not petition for them. Your staked influence is returned.", false);
                return;
            }

            float regard = sovereign != null ? (OpinionBehavior.Instance?.EffectiveOpinion(sovereign, Hero.MainHero) ?? 0f) : 0f;

            // Is a fief the player qualifies for actually available this week?
            if (!career.TryFindClaimTarget(out _, out _, out _)) return; // nothing free yet — the petition waits

            // Below the floor of regard, the court refuses outright and keeps the stake.
            if (FiefPetitionMath.BelowRelationFloor(regard))
            {
                Close();
                if (k != null)
                    RoyalFarmaan.FromRuler(k, "Your Petition Is Refused",
                        $"The court has weighed your petition for a fief and rejects it. You presume too much on too little regard; " +
                        $"the gift is kept for the crown's trouble, and your stake with it. Earn the sovereign's favour before you ask again.",
                        "I withdraw, chastened");
                TYTLog.Info("FiefPetition: refused (below relation floor).");
                return;
            }

            if (!FiefPetitionMath.CourtGrants(_gift, _influence, regard, MBRandom.RandomFloat)) return; // not this week

            if (career.GrantEligibleFief(out string desc, out string reason))
            {
                Close();
                if (k != null)
                    RoyalFarmaan.FromRuler(k, "Your Petition Is Granted",
                        $"The court has weighed your suit and finds in your favour: you are granted {desc}. Serve faithfully, and " +
                        "greater honours may follow.", "I accept the charge, with thanks");
                TYTLog.Info($"FiefPetition: granted ({desc}).");
            }
            // If the grant failed at the last step (e.g. the fief was taken between checks), the
            // petition simply stays open for next week.
        }

        // ── Withdrawal & bookkeeping ─────────────────────────────────────────────────
        public void Withdraw(bool silent = false)
        {
            if (!_open) return;
            RefundInfluence();
            int gift = _gift;
            Close();
            if (!silent) Notify($"You withdraw your fief petition. Your {_influenceRefunded} influence is returned; the {gift}-rupee gift stays with the court.", false);
        }

        private int _influenceRefunded;
        private void RefundInfluence()
        {
            _influenceRefunded = _influence;
            if (_influence > 0) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, _influence);
        }

        private void Close() { _open = false; _gift = 0; _influence = 0; _tier = 0; _filedDay = 0; }

        private void ShowStanding()
        {
            Kingdom k = Clan.PlayerClan?.Kingdom;
            Hero sovereign = k?.Leader;
            float regard = sovereign != null && sovereign != Hero.MainHero
                ? (OpinionBehavior.Instance?.EffectiveOpinion(sovereign, Hero.MainHero) ?? 0f) : 0f;
            int days = (int)CampaignTime.Now.ToDays - _filedDay;
            string chance = $"{FiefPetitionMath.ApprovalChancePerWeek(_gift, _influence, regard) * 100f:0}% per week";
            string kind = _tier == 0 ? "a village zamindari" : _tier == 1 ? "a castle" : "a town";
            string floor = FiefPetitionMath.BelowRelationFloor(regard)
                ? "\n\nThe sovereign's regard is BELOW the floor — the court will refuse this petition the next time a fief comes free. Withdraw, or mend relations."
                : "";
            InformationManager.ShowInquiry(new InquiryData(
                "Your Standing Petition",
                $"You have petitioned for {kind} for {days} day(s): {_gift} rupees gifted, {_influence} influence staked. " +
                $"When a fief you qualify for is free, the court grants it at about {chance}." + floor,
                true, true, "Keep the petition standing", "Withdraw it (refund influence)",
                null, () => TYTLog.Guard("FiefPetition.Withdraw", () => Withdraw())), true);
        }

        // Text summary for the mansab menu.
        public string StandingLine()
        {
            if (!_open) return null;
            string kind = _tier == 0 ? "village zamindari" : _tier == 1 ? "castle" : "town";
            return $"Fief petition standing: for a {kind} — {_gift} rupees gifted, {_influence} influence staked.";
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_fpet_open", ref _open);
            dataStore.SyncData("hind_fpet_gift", ref _gift);
            dataStore.SyncData("hind_fpet_inf", ref _influence);
            dataStore.SyncData("hind_fpet_tier", ref _tier);
            dataStore.SyncData("hind_fpet_filed", ref _filedDay);
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("petition_status", "hindostan")]
        public static string PetitionStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (!Instance._open) return "No standing fief petition. Open the mansab menu to file one.";
            Kingdom k = Clan.PlayerClan?.Kingdom;
            Hero sov = k?.Leader;
            float regard = sov != null && sov != Hero.MainHero ? (OpinionBehavior.Instance?.EffectiveOpinion(sov, Hero.MainHero) ?? 0f) : 0f;
            bool avail = CareerProgressionBehavior.Instance?.TryFindClaimTarget(out _, out _, out _) ?? false;
            return $"Petition open: tier {Instance._tier}, gift {Instance._gift}, influence {Instance._influence}.\n" +
                   $"Sovereign regard: {regard:0} (floor {FiefPetitionMath.RelationFloor}). Fief available now: {avail}.\n" +
                   $"Weekly grant chance: {FiefPetitionMath.ApprovalChancePerWeek(Instance._gift, Instance._influence, regard) * 100f:0}%.";
        }

        // Force the weekly court to consider the petition now (testing).
        [CommandLineFunctionality.CommandLineArgumentFunction("petition_resolve", "hindostan")]
        public static string PetitionResolve(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (!Instance._open) return "No standing petition.";
            Instance.OnWeeklyTick();
            return Instance._open ? "Court considered the petition; not granted this pass." : "Petition resolved (granted, refused, or moot).";
        }
    }
}
