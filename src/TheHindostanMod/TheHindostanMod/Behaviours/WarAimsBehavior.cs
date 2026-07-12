using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TakhtyaTaboot.Util
{
    // Pillar 3 of the war-aims design (wiki/27): trait-driven affronts give a kingdom a JUST CAUSE
    // (casus belli) for a war of revenge, and the victor may demand the culprit for judgement —
    // pardon, fine, imprison (2-5 yrs, his house weakening as he rots), or execute. The pure rules
    // live in the tested WarAimMath; this behavior is the thin, guarded campaign layer.
    //
    // Affronts are abstract (no fragile prisoner/party engine fiddling): a wicked lord of one realm
    // kidnaps a kinsman of another or loots its caravan; the wronged realm gains a year-long casus
    // belli. WarfareBehavior surfaces the "surrender the culprit" peace term and calls JudgeCulprit.
    public class WarAimsBehavior : CampaignBehaviorBase
    {
        public static WarAimsBehavior Instance { get; private set; }

        private const int CasusBelliDays = 365;        // a grievance justifies war for a year
        private const float WeeklyAffrontChance = 0.15f;

        // ── Casus belli (parallel lists; engine can't serialize dictionaries) ──
        private List<string> _cbVictimK  = new List<string>();  // wronged kingdom id
        private List<string> _cbCulpritK = new List<string>();  // offending kingdom id
        private List<string> _cbCulpritH = new List<string>();  // culprit hero id
        private List<int>    _cbType     = new List<int>();      // 0 kidnap, 1 caravan
        private List<int>    _cbExpiry   = new List<int>();      // day it lapses

        // ── Detentions: imprisoned culprits whose houses weaken until release ──
        private List<string> _detHero    = new List<string>();
        private List<int>    _detRelease = new List<int>();

        private bool _ready;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => _ready = true);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => { if (_ready) TYTLog.Guard("WarAims.Weekly", OnWeeklyTick); });
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("waim_cbVictimK",  ref _cbVictimK);
            dataStore.SyncData("waim_cbCulpritK", ref _cbCulpritK);
            dataStore.SyncData("waim_cbCulpritH", ref _cbCulpritH);
            dataStore.SyncData("waim_cbType",     ref _cbType);
            dataStore.SyncData("waim_cbExpiry",   ref _cbExpiry);
            dataStore.SyncData("waim_detHero",    ref _detHero);
            dataStore.SyncData("waim_detRelease", ref _detRelease);
        }

        // ── Public API (used by WarfareBehavior peace terms) ──────────────────────────
        public bool HasCasusBelli(Kingdom victim, Kingdom culprit, out Hero culpritHero)
        {
            culpritHero = null;
            int i = FindCasusBelli(victim, culprit);
            if (i < 0) return false;
            culpritHero = FindHero(_cbCulpritH[i]);
            return culpritHero != null && culpritHero.IsAlive;
        }

        // Grant a realm a just cause for a war of vengeance against another (used e.g. when its king is
        // executed). The culprit is the one who will answer for it if that realm later wins satisfaction.
        public void RegisterRevengeCasusBelli(Kingdom victim, Kingdom culpritKingdom, Hero culprit)
        {
            if (victim == null || culpritKingdom == null || culprit == null) return;
            RecordCasusBelli(victim, culpritKingdom, culprit, 0);
        }

        // The victor (victim realm) brings the culprit to judgement. Player king decides by hand;
        // an AI king decides by temperament. Always called for the player's realm (victim == PK).
        public void JudgeCulprit(Kingdom culpritKingdom)
        {
            try
            {
                Kingdom victim = Hero.MainHero?.Clan?.Kingdom;
                int i = FindCasusBelli(victim, culpritKingdom);
                if (i < 0) return;
                Hero culprit = FindHero(_cbCulpritH[i]);
                Hero judge = victim?.Leader;
                if (culprit == null || !culprit.IsAlive || judge == null) { RemoveCasusBelli(i); return; }

                var verdicts = WarAimMath.AvailableVerdicts(WarAim.Revenge).ToList();
                if (judge == Hero.MainHero) PromptVerdict(judge, culprit, verdicts, i);
                else ApplyVerdict(judge, culprit, AiVerdict(judge, culprit, verdicts), i);
            }
            catch (Exception e) { TYTLog.Error("JudgeCulprit failed", e); }
        }

        // ── Weekly: spawn affronts, lapse old grievances, weaken imprisoned houses ─────
        private void OnWeeklyTick()
        {
            ExpireCasusBelli();
            ProcessDetentions();
            if (MBRandom.RandomFloat <= WeeklyAffrontChance) MaybeGenerateAffront();
        }

        private void MaybeGenerateAffront()
        {
            var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null
                && k.Clans.Any(c => !c.IsEliminated && c.Leader != null)).ToList();
            if (kingdoms.Count < 2) return;

            Kingdom aggressor = kingdoms[MBRandom.RandomInt(kingdoms.Count)];
            Kingdom victim = kingdoms[MBRandom.RandomInt(kingdoms.Count)];
            if (aggressor == victim) return;

            // The likeliest culprit is the realm's most ruthless lord (low mercy/honor, calculating).
            Hero culprit = aggressor.Clans
                .Where(c => !c.IsEliminated && c.Leader != null && c.Leader != aggressor.Leader)
                .Select(c => c.Leader)
                .OrderByDescending(h => Wickedness(h) + MBRandom.RandomFloatRanged(-2f, 2f))
                .FirstOrDefault() ?? aggressor.Leader;
            if (culprit == null) return;

            int type = MBRandom.RandomInt(2); // 0 kidnap, 1 caravan
            RecordCasusBelli(victim, aggressor, culprit, type);

            Kingdom pk = Hero.MainHero?.Clan?.Kingdom;
            if (victim == pk)
            {
                string deed = type == 0
                    ? $"{culprit.Name} of {aggressor.Name} has seized and ransomed a kinsman of our realm"
                    : $"{culprit.Name} of {aggressor.Name} has waylaid and plundered one of our caravans";
                Notify($"An affront against {victim.Name}: {deed}. The realm now has just cause for war against {aggressor.Name}.", true);
            }
            else if (aggressor == pk && culprit != Hero.MainHero)
            {
                Notify($"{culprit.Name} has provoked {victim.Name} — they may now claim just cause against us.", true);
            }
        }

        private static int Wickedness(Hero h)
        {
            if (h == null) return 0;
            return -h.GetTraitLevel(DefaultTraits.Mercy)
                   - h.GetTraitLevel(DefaultTraits.Honor)
                   + h.GetTraitLevel(DefaultTraits.Calculating);
        }

        // ── Verdicts ──────────────────────────────────────────────────────────────────
        private void PromptVerdict(Hero judge, Hero culprit, List<WarVerdict> verdicts, int cbIndex)
        {
            var elements = new List<InquiryElement>();
            foreach (WarVerdict v in verdicts)
                elements.Add(new InquiryElement(v, VerdictLabel(v), null, true, VerdictHint(v)));

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"The Judgement of {culprit.Name}",
                $"{culprit.Name} has been surrendered to answer for the affront against your realm. Pronounce his fate.",
                elements, false, 1, 1, "Pronounce", "",
                sel =>
                {
                    WarVerdict v = (sel != null && sel.Count > 0 && sel[0].Identifier is WarVerdict vv) ? vv : WarVerdict.Pardon;
                    ApplyVerdict(judge, culprit, v, cbIndex);
                }, null, ""), true);
        }

        private WarVerdict AiVerdict(Hero judge, Hero culprit, List<WarVerdict> verdicts)
        {
            int rel = CharacterRelationManager.GetHeroRelation(judge, culprit);
            float roll = MBRandom.RandomFloat;
            if (rel < -30 && roll < 0.35f) return WarVerdict.Execute;
            if (roll < 0.45f) return WarVerdict.Imprison;
            if (roll < 0.80f) return WarVerdict.Fine;
            return WarVerdict.Pardon;
        }

        private void ApplyVerdict(Hero judge, Hero culprit, WarVerdict v, int cbIndex)
        {
            if (culprit == null || !culprit.IsAlive) { RemoveCasusBelli(cbIndex); return; }
            bool seen = judge == Hero.MainHero || culprit == Hero.MainHero;

            switch (v)
            {
                case WarVerdict.Execute:
                    KillCharacterAction.ApplyByExecution(culprit, judge, seen, true);
                    if (seen) Notify($"{culprit.Name} is put to death for his crimes. Let it be a warning.", false);
                    break;

                case WarVerdict.Imprison:
                {
                    int years = WarAimMath.ClampImprisonYears(MBRandom.RandomInt(WarAimMath.MinImprisonYears, WarAimMath.MaxImprisonYears + 1));
                    _detHero.Add(culprit.StringId);
                    _detRelease.Add((int)CampaignTime.Now.ToDays + years * 365);
                    if (culprit.Clan != null) ChangeClanInfluenceAction.Apply(culprit.Clan, -Math.Min(100f, culprit.Clan.Influence));
                    if (seen) Notify($"{culprit.Name} is cast into prison for {years} years. His house will wither in his absence.", false);
                    break;
                }

                case WarVerdict.Fine:
                {
                    int amount = Math.Min(culprit.Gold, 8000);
                    if (amount > 0 && judge != null) GiveGoldAction.ApplyBetweenCharacters(culprit, judge, amount, true);
                    if (culprit.Clan != null) ChangeClanInfluenceAction.Apply(culprit.Clan, -Math.Min(50f, culprit.Clan.Influence));
                    if (seen) Notify($"{culprit.Name} is fined {amount} rupees in reparation for the affront.", false);
                    break;
                }

                default: // Pardon
                    if (judge != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(judge, culprit, 10);
                    if (seen) Notify($"{culprit.Name} is pardoned. A magnanimous mercy — remembered by his house.", false);
                    break;
            }
            RemoveCasusBelli(cbIndex);
        }

        private static string VerdictLabel(WarVerdict v) => v == WarVerdict.Execute ? "Execute him"
            : v == WarVerdict.Imprison ? "Imprison him (2-5 years)" : v == WarVerdict.Fine ? "Fine him in reparation" : "Pardon him";

        private static string VerdictHint(WarVerdict v) => v == WarVerdict.Execute ? "Royal justice, brutally final — his kin will not forget."
            : v == WarVerdict.Imprison ? "He rots in your dungeon for years; his house weakens while he is gone."
            : v == WarVerdict.Fine ? "Gold in reparation, and his standing diminished." : "Mercy that wins goodwill — but the affront goes unpunished.";

        // ── Detentions: a jailed lord's house wanes until his release ──────────────────
        private void ProcessDetentions()
        {
            int today = (int)CampaignTime.Now.ToDays;
            for (int i = _detHero.Count - 1; i >= 0; i--)
            {
                Hero h = FindHero(_detHero[i]);
                if (h == null || !h.IsAlive || today >= _detRelease[i])
                {
                    if (h != null && h.IsAlive && Hero.MainHero?.Clan?.Kingdom != null)
                        Notify($"{h.Name} is released from confinement, his house diminished by the years lost.", false);
                    _detHero.RemoveAt(i); _detRelease.RemoveAt(i);
                    continue;
                }
                // Weakening: drain the imprisoned lord's clan influence week by week.
                if (h.Clan != null && !h.Clan.IsEliminated)
                    ChangeClanInfluenceAction.Apply(h.Clan, -Math.Min(8f, h.Clan.Influence));
            }
        }

        // ── State helpers ──────────────────────────────────────────────────────────────
        private int FindCasusBelli(Kingdom victim, Kingdom culprit)
        {
            if (victim == null || culprit == null) return -1;
            int today = (int)CampaignTime.Now.ToDays;
            for (int i = 0; i < _cbVictimK.Count; i++)
                if (_cbVictimK[i] == victim.StringId && _cbCulpritK[i] == culprit.StringId && _cbExpiry[i] > today)
                    return i;
            return -1;
        }

        private void RecordCasusBelli(Kingdom victim, Kingdom culpritKingdom, Hero culprit, int type)
        {
            int existing = FindCasusBelli(victim, culpritKingdom);
            int expiry = (int)CampaignTime.Now.ToDays + CasusBelliDays;
            if (existing >= 0)
            {
                _cbCulpritH[existing] = culprit.StringId; _cbType[existing] = type; _cbExpiry[existing] = expiry;
                return;
            }
            _cbVictimK.Add(victim.StringId); _cbCulpritK.Add(culpritKingdom.StringId);
            _cbCulpritH.Add(culprit.StringId); _cbType.Add(type); _cbExpiry.Add(expiry);
        }

        private void ExpireCasusBelli()
        {
            int today = (int)CampaignTime.Now.ToDays;
            for (int i = _cbExpiry.Count - 1; i >= 0; i--)
                if (_cbExpiry[i] <= today || FindHero(_cbCulpritH[i]) == null) RemoveCasusBelli(i);
        }

        private void RemoveCasusBelli(int i)
        {
            if (i < 0 || i >= _cbVictimK.Count) return;
            _cbVictimK.RemoveAt(i); _cbCulpritK.RemoveAt(i); _cbCulpritH.RemoveAt(i);
            _cbType.RemoveAt(i); _cbExpiry.RemoveAt(i);
        }

        private static Hero FindHero(string id)
            => string.IsNullOrEmpty(id) ? null
             : (Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id)
                ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => h.StringId == id));

        private static void Notify(string text, bool important)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                important ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));
    }
}
