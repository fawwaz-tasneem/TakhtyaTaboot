using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // The realm's religious policy (wiki Ch.17 §6), the axis Mughal history turned on:
    //   Sulh-e-Kul (Tolerant)  — Akbar's universal peace: mismatched towns warm, courts cost a tithe.
    //   The Middle Road        — the default posture of most realms.
    //   Mulk-e-Sharia (Strict) — Aurangzeb's orthodoxy: other-faith towns seethe, their lords
    //                            are squeezed, and the jizya may be enacted for revenue at a
    //                            steady cost in authority and goodwill.
    // Effects: daily town-loyalty drift for faith-mismatched settlements (direct writes, no
    // model override), a clan-income factor (Patches/ToleranceTaxPatch), one-time relation
    // shifts on a stance change, and the jizya toggle. Numbers in Util.ToleranceMath (tested).
    public class ReligiousToleranceBehavior : CampaignBehaviorBase
    {
        public static ReligiousToleranceBehavior Instance { get; private set; }

        private Dictionary<string, int> _stance = new Dictionary<string, int>();  // kingdomId -> (int)Stance
        private Dictionary<string, bool> _jizya = new Dictionary<string, bool>(); // kingdomId -> enacted
        private int _lastAiReviewDay = -1;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Tolerance.DailyTick", OnDailyTick));
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Tolerance.WeeklyTick", OnWeeklyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            var kIds = _stance.Keys.ToList();
            var kVals = _stance.Values.ToList();
            var jIds = _jizya.Keys.ToList();
            var jVals = _jizya.Values.ToList();
            dataStore.SyncData("hind_tol_kIds", ref kIds);
            dataStore.SyncData("hind_tol_stances", ref kVals);
            dataStore.SyncData("hind_tol_jIds", ref jIds);
            dataStore.SyncData("hind_tol_jizya", ref jVals);
            dataStore.SyncData("hind_tol_lastReview", ref _lastAiReviewDay);
            if (!dataStore.IsSaving)
            {
                _stance = new Dictionary<string, int>();
                for (int i = 0; i < kIds.Count && i < kVals.Count; i++) _stance[kIds[i]] = kVals[i];
                _jizya = new Dictionary<string, bool>();
                for (int i = 0; i < jIds.Count && i < jVals.Count; i++) _jizya[jIds[i]] = jVals[i];
            }
        }

        // ── Queries ──────────────────────────────────────────────────────────────────
        public ToleranceMath.Stance GetStance(Kingdom k)
            => k != null && _stance.TryGetValue(k.StringId, out int v)
                ? (ToleranceMath.Stance)v : ToleranceMath.Stance.Moderate;

        public bool JizyaEnacted(Kingdom k)
            => k != null && _jizya.TryGetValue(k.StringId, out bool v) && v
               && GetStance(k) == ToleranceMath.Stance.Strict;

        public Religion RulerFaith(Kingdom k)
            => k?.Leader != null ? ReligionBehavior.Instance?.GetReligion(k.Leader) ?? Religion.None : Religion.None;

        public bool ClanFaithMatchesRuler(Clan c)
        {
            Kingdom k = c?.Kingdom;
            if (k == null || c.Leader == null) return true;
            Religion rf = RulerFaith(k);
            Religion cf = ReligionBehavior.Instance?.GetReligion(c.Leader) ?? Religion.None;
            return rf == Religion.None || cf == Religion.None || rf == cf;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────────
        // Idempotent seed: the post-Aurangzeb empire realms begin Strict, everyone else
        // on the middle road. Never overwrites a stored stance.
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated))
                if (!_stance.ContainsKey(k.StringId))
                    _stance[k.StringId] = (int)(FactionRelationsBehavior.IsMughalKingdom(k)
                        ? ToleranceMath.Stance.Strict : ToleranceMath.Stance.Moderate);

            AddMenus(starter);
        }

        private void OnDailyTick()
        {
            // Faith-mismatched towns drift by their realm's stance.
            foreach (Settlement s in Settlement.All)
            {
                if (!s.IsTown || s.Town == null) continue;
                if (!(s.MapFaction is Kingdom k) || k.IsEliminated) continue;
                Religion rf = RulerFaith(k);
                if (rf == Religion.None) continue;
                Religion pf = ReligionBehavior.Instance?.GetCultureReligion(s.Culture) ?? Religion.None;
                if (pf == Religion.None) continue;

                float drift = ToleranceMath.LoyaltyDriftPerDay(GetStance(k), pf == rf);
                if (drift != 0f)
                    s.Town.Loyalty = MathF.Max(0f, MathF.Min(100f, s.Town.Loyalty + drift));
            }
        }

        private void OnWeeklyTick()
        {
            // The jizya's steady political price.
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated))
                if (JizyaEnacted(k))
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(k,
                        -ToleranceMath.JizyaWeeklyAuthorityCost, "the jizya breeds resentment");

            // AI rulers reconsider yearly: a shaky throne softens its stance to calm the provinces.
            int today = (int)CampaignTime.Now.ToDays;
            if (_lastAiReviewDay >= 0 && today - _lastAiReviewDay < 360) return;
            _lastAiReviewDay = today;
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null && x.Leader != Hero.MainHero))
            {
                float legit = LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 60f;
                ToleranceMath.Stance cur = GetStance(k);
                if (legit < 40f && cur == ToleranceMath.Stance.Strict)
                    SetStance(k, ToleranceMath.Stance.Moderate, announce: false);
                else if (legit > 75f && cur == ToleranceMath.Stance.Moderate
                         && RulerFaith(k) == Religion.Islam && FactionRelationsBehavior.IsMughalKingdom(k)
                         && MBRandom.RandomFloat < 0.25f)
                    SetStance(k, ToleranceMath.Stance.Strict, announce: false);
            }
        }

        // ── Stance changes ───────────────────────────────────────────────────────────
        public void SetStance(Kingdom k, ToleranceMath.Stance to, bool announce = true)
        {
            if (k == null) return;
            ToleranceMath.Stance from = GetStance(k);
            if (from == to) return;
            _stance[k.StringId] = (int)to;
            if (to != ToleranceMath.Stance.Strict) _jizya[k.StringId] = false; // jizya dies with orthodoxy

            // The other faith's houses remember who softened — and who hardened.
            int shift = ToleranceMath.StanceChangeRelationShift(from, to);
            if (shift != 0 && k.Leader != null)
                foreach (Clan c in k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != k.Leader))
                    if (!ClanFaithMatchesRuler(c))
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(k.Leader, c.Leader, shift);

            if (announce && Clan.PlayerClan?.Kingdom == k)
                RoyalFarmaan.FromRuler(k, "The Realm's Faith Policy Is Decreed",
                    $"By decree of the throne, the realm now follows {ToleranceMath.StanceName(to)}. " +
                    (to == ToleranceMath.Stance.Strict
                        ? "The ulema are ascendant; the other faiths bow — or seethe."
                        : to == ToleranceMath.Stance.Tolerant
                            ? "All faiths stand equal before the throne; the temples reopen."
                            : "The realm steers between the ulema and the temples."),
                    "So it is decreed");
            TYTLog.Info($"Tolerance: {k.Name} -> {to}.");
        }

        public void EnactJizya(Kingdom k)
        {
            if (k == null || GetStance(k) != ToleranceMath.Stance.Strict || JizyaEnacted(k)) return;
            _jizya[k.StringId] = true;
            if (k.Leader != null)
            {
                LegitimacyBehavior.Instance?.ModifyLegitimacy(k.Leader, -ToleranceMath.JizyaEnactLegitimacyCost, "the jizya is enacted");
                foreach (Clan c in k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != k.Leader))
                    if (!ClanFaithMatchesRuler(c))
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(k.Leader, c.Leader, ToleranceMath.JizyaEnactRelationHit);
            }
            if (Clan.PlayerClan?.Kingdom == k)
                RoyalFarmaan.FromRuler(k, "The Jizya Is Enacted",
                    "The poll-tax upon the other faiths is restored. The treasury swells — and the provinces murmur.",
                    "Let it be collected");
        }

        public void RepealJizya(Kingdom k)
        {
            if (k == null || !_jizya.TryGetValue(k.StringId, out bool on) || !on) return;
            _jizya[k.StringId] = false;
            if (Clan.PlayerClan?.Kingdom == k)
                RoyalFarmaan.FromRuler(k, "The Jizya Is Repealed",
                    "The poll-tax upon the other faiths is lifted. The registers thin, but the murmuring quiets.",
                    "Let it be known");
        }

        // ── Player UX (ruler-only decree from a town) ────────────────────────────────
        private void AddMenus(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town", "hindostan_tolerance_decree", "{=!}Decree the realm's religious policy",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Manage;
                    Kingdom k = Hero.MainHero?.Clan?.Kingdom;
                    return k != null && k.Leader == Hero.MainHero;
                },
                args => OpenDecreeDialog(), false, 7);
        }

        private void OpenDecreeDialog()
        {
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k == null || k.Leader != Hero.MainHero) return;
            ToleranceMath.Stance cur = GetStance(k);

            var elements = new List<InquiryElement>();
            foreach (ToleranceMath.Stance s in new[] { ToleranceMath.Stance.Tolerant, ToleranceMath.Stance.Moderate, ToleranceMath.Stance.Strict })
                elements.Add(new InquiryElement(s, ToleranceMath.StanceName(s) + (s == cur ? "  (current)" : ""), null,
                    s != cur, StanceHint(s)));
            if (cur == ToleranceMath.Stance.Strict)
                elements.Add(JizyaEnacted(k)
                    ? new InquiryElement("jizya_off", "Repeal the jizya", null, true, "Lift the poll-tax on the other faiths.")
                    : new InquiryElement("jizya_on", "Enact the jizya", null, true,
                        "+15% income for your house; costs legitimacy, authority each week, and the other faiths' goodwill."));

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Realm's Faith Policy",
                $"The realm now follows {ToleranceMath.StanceName(cur)}" + (JizyaEnacted(k) ? " with the jizya enacted." : ".") +
                " What is your decree?",
                elements, true, 1, 1, "Decree it", "Leave it be",
                sel =>
                {
                    if (sel == null || sel.Count == 0) return;
                    if (sel[0].Identifier is ToleranceMath.Stance s) SetStance(k, s);
                    else if ((sel[0].Identifier as string) == "jizya_on") EnactJizya(k);
                    else if ((sel[0].Identifier as string) == "jizya_off") RepealJizya(k);
                },
                _ => { }, "", false), false, false);
        }

        private static string StanceHint(ToleranceMath.Stance s)
            => s == ToleranceMath.Stance.Strict
                ? "Other-faith towns lose loyalty daily and their lords are squeezed; unlocks the jizya."
             : s == ToleranceMath.Stance.Tolerant
                ? "Other-faith towns warm to your rule; all your lords pay a small tithe for the even-handed courts."
                : "No favours, no persecutions.";
    }
}
