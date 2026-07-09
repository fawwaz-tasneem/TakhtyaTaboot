using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // The court's parties (wiki Ch.19 §4). Every lord leans — by his traits — toward the
    // War Party, the Peace Party, the Reformers, or the Orthodox; their pooled clan renown
    // (doubled for a council seat, tying the parties to CouncilBehavior) decides which
    // party dominates each realm's court. Monthly, the dominant party petitions a
    // player-ruler; granting or refusing moves relations with its lords. AI rulers feel
    // the parties only through the existing meters (the Orthodox lean is read by the
    // tolerance system's yearly review). Affinity math in Util.CourtFactionMath (tested).
    public class CourtFactionsBehavior : CampaignBehaviorBase
    {
        public static CourtFactionsBehavior Instance { get; private set; }

        private Dictionary<string, int> _dominant = new Dictionary<string, int>(); // kingdomId -> (int)CourtFaction
        private int _lastPetitionDay = -1;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("CourtFactions.WeeklyTick", OnWeeklyTick));
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("CourtFactions.DailyTick", OnDailyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            var kIds = _dominant.Keys.ToList();
            var kVals = _dominant.Values.ToList();
            dataStore.SyncData("hind_cf_kIds", ref kIds);
            dataStore.SyncData("hind_cf_dominant", ref kVals);
            dataStore.SyncData("hind_cf_lastPetitionDay", ref _lastPetitionDay);
            if (!dataStore.IsSaving)
            {
                _dominant = new Dictionary<string, int>();
                for (int i = 0; i < kIds.Count && i < kVals.Count; i++) _dominant[kIds[i]] = kVals[i];
            }
        }

        // ── Queries ──────────────────────────────────────────────────────────────────
        public CourtFactionMath.CourtFaction AffinityOf(Hero lord)
        {
            if (lord == null) return CourtFactionMath.CourtFaction.Peace;
            return CourtFactionMath.Affinity(
                lord.GetTraitLevel(DefaultTraits.Valor),
                lord.GetTraitLevel(DefaultTraits.Calculating),
                lord.GetTraitLevel(DefaultTraits.Generosity),
                lord.GetTraitLevel(DefaultTraits.Honor),
                lord.StringId?.GetHashCode() ?? 0);
        }

        public CourtFactionMath.CourtFaction GetDominant(Kingdom k)
            => k != null && _dominant.TryGetValue(k.StringId, out int v)
                ? (CourtFactionMath.CourtFaction)v : CourtFactionMath.CourtFaction.Peace;

        private IEnumerable<Hero> FactionLords(Kingdom k, CourtFactionMath.CourtFaction f)
            => k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != k.Leader)
                .Select(c => c.Leader)
                .Where(h => AffinityOf(h) == f);

        // ── Weekly strength tally ────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null))
            {
                var strengths = new float[4];
                foreach (Clan c in k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != k.Leader))
                {
                    bool seated = CouncilBehavior.Instance?.GetPostOf(c.Leader) != null;
                    strengths[(int)AffinityOf(c.Leader)] += CourtFactionMath.MemberWeight(c.Renown, seated);
                }
                _dominant[k.StringId] = CourtFactionMath.Dominant(strengths);
            }
        }

        // ── Monthly petition to a player-ruler ───────────────────────────────────────
        private void OnDailyTick()
        {
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k == null || k.Leader != Hero.MainHero) return;

            int today = (int)CampaignTime.Now.ToDays;
            if (_lastPetitionDay < 0) { _lastPetitionDay = today; return; }
            if (today - _lastPetitionDay < CourtFactionMath.PetitionIntervalDays) return;
            _lastPetitionDay = today;

            var faction = GetDominant(k);
            var lords = FactionLords(k, faction).ToList();
            if (lords.Count == 0) return;
            Hero speaker = lords.OrderByDescending(h => h.Clan?.Renown ?? 0f).First();

            switch (faction)
            {
                case CourtFactionMath.CourtFaction.War:
                    Petition(k, faction, speaker,
                        "The war party fills the court. The amirs demand campaigns, plunder, and glory — a realm at " +
                        "peace, they say, is a realm growing soft.",
                        "The sword will have its season",
                        () => { ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 2f, "the war party is heartened"); Please(lords, 5); },
                        "The realm needs quiet, not glory",
                        () => Displease(lords, 5));
                    break;
                case CourtFactionMath.CourtFaction.Peace:
                    Petition(k, faction, speaker,
                        "The men of the pen dominate the court. They counsel treaties, trade, and full registers — " +
                        "wars, they say, burn the very silver they are fought for.",
                        "Peace shall be our policy",
                        () => { LegitimacyBehavior.Instance?.ModifyLegitimacy(k.Leader, 2f, "the court praises your statecraft"); Please(lords, 5); },
                        "A realm is held by the sword",
                        () => Displease(lords, 5));
                    break;
                case CourtFactionMath.CourtFaction.Reform:
                    Petition(k, faction, speaker,
                        "The reformers press for clean registers and honest revenue: an outlay of 2000 dinars on the " +
                        "diwan's clerks, they promise, will return in a firmer imperial writ.",
                        "Fund the reforms (2000 dinars)",
                        () =>
                        {
                            if (Hero.MainHero.Gold < 2000) { Notify("You cannot spare the silver; the reformers withdraw, unimpressed.", true); Displease(lords, 3); return; }
                            Hero.MainHero.ChangeHeroGold(-2000);
                            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 3f, "the registers are set in order");
                            Please(lords, 5);
                        },
                        "The registers can wait",
                        () => Displease(lords, 5));
                    break;
                case CourtFactionMath.CourtFaction.Orthodox:
                    Petition(k, faction, speaker,
                        "The ulema hold the court. They press for strict orthodoxy — the realm's law bent to the " +
                        "faith, the other creeds put in their place.",
                        "The realm shall follow the strict path",
                        () =>
                        {
                            ReligiousToleranceBehavior.Instance?.SetStance(k, ToleranceMath.Stance.Strict);
                            Please(lords, 5);
                        },
                        "The realm holds its present course",
                        () => Displease(lords, 5));
                    break;
            }
        }

        private void Petition(Kingdom k, CourtFactionMath.CourtFaction f, Hero speaker, string body,
            string accept, Action onAccept, string refuse, Action onRefuse)
            => RoyalFarmaan.Issue($"A Petition from {CourtFactionMath.FactionName(f)}",
                $"Presented by {speaker.Name}", body, seal: "Laid before the throne",
                primary: accept, onPrimary: onAccept, secondary: refuse, onSecondary: onRefuse);

        private static void Please(List<Hero> lords, int amount)
        {
            foreach (Hero h in lords)
            {
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, h, amount);
                // Also a PERSONAL memory: the faction's lords remember whether their
                // petition was heard (feeds the opinion ledger and everything reading it).
                OpinionBehavior.Instance?.AddOpinion(h, Hero.MainHero,
                    amount >= 0 ? OpinionMath.OpinionType.Favor : OpinionMath.OpinionType.Grudge,
                    amount >= 0 ? amount : amount * 1.5f);
            }
        }

        private static void Displease(List<Hero> lords, int amount) => Please(lords, -amount);

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));
    }
}
