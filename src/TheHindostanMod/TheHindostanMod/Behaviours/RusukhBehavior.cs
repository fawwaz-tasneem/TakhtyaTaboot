using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Rusukh — a fief-holder's local entrenchment (see RusukhMath for the curves, and
    // wiki/26-Policy-Tenure-Succession-Design). Per (holder, fief), 0..100: grows daily while held
    // (faster with stewardship and good standing with the local notables), decays faster once the
    // holder is removed. Entrenchment buys weekly notable backing — influence, seasonal gold, and
    // a levy bonus — and feeds the Mansabdari transfer-defiance roll (DefianceChance).
    //
    // All maths are in the pure, unit-tested RusukhMath; this behavior owns the state and the
    // engine effects, and is guarded throughout.
    public class RusukhBehavior : CampaignBehaviorBase
    {
        public static RusukhBehavior Instance { get; private set; }

        private const float GrowthPerDay = 0.30f;   // on-fief
        private const float DecayPerDay  = 0.90f;   // off-fief — 3x faster; influence fades
        private const int   GoldEveryNWeeks = 3;    // a Bannerlord season — seasonal gold backing

        private Dictionary<string, float> _rusukh = new Dictionary<string, float>();
        private int _weekCounter;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, Daily);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Rusukh.Weekly", Weekly));
        }

        private static string Key(Hero h, Settlement s) => h.StringId + "|" + s.StringId;

        // The lord who holds a settlement: a village's zamindar (our feudal layer) or a town/castle's
        // owning-clan leader.
        public static Hero Holder(Settlement s)
        {
            if (s == null) return null;
            if (s.IsVillage) return FeudalTitlesBehavior.Instance?.GetVillageLord(s) ?? s.OwnerClan?.Leader;
            return s.OwnerClan?.Leader;
        }

        // ── Public API (Mansabdari, levies, UI) ──────────────────────────────────────
        public float GetRusukh(Hero h, Settlement s)
            => (h != null && s != null && _rusukh.TryGetValue(Key(h, s), out float v)) ? v : 0f;

        public float GetLevyMultiplier(Settlement s)
        {
            Hero h = Holder(s);
            return h == null ? 1f : RusukhMath.LevyMultiplier(GetRusukh(h, s));
        }

        // Odds (0..1) the current holder could defy a crown transfer order from this fief.
        public float DefianceChance(Hero h, Settlement s)
        {
            Kingdom k = s?.OwnerClan?.Kingdom;
            float auth  = (k != null && ImperialAuthorityBehavior.Instance != null) ? ImperialAuthorityBehavior.Instance.GetAuthority(k) : 50f;
            float legit = (k?.Leader != null && LegitimacyBehavior.Instance != null) ? LegitimacyBehavior.Instance.GetLegitimacy(k.Leader) : 50f;
            return RusukhMath.DefianceChance(GetRusukh(h, s), auth, legit);
        }

        // ── Ticks ────────────────────────────────────────────────────────────────────
        private void Daily(Settlement s)
        {
            try
            {
                if (s == null || (!s.IsTown && !s.IsCastle && !s.IsVillage)) return;
                Hero h = Holder(s);
                if (h == null || !h.IsAlive) return;
                string key = Key(h, s);
                float cur = _rusukh.TryGetValue(key, out float v) ? v : 0f;
                _rusukh[key] = RusukhMath.Growth(cur, GrowthPerDay, h.GetSkillValue(DefaultSkills.Steward), RelationFactor(h, s));
            }
            catch { /* never break a settlement tick */ }
        }

        private void Weekly()
        {
            _weekCounter++;
            bool goldWeek = _weekCounter % GoldEveryNWeeks == 0;

            var spent = new List<string>();
            foreach (var kv in _rusukh.ToList())
            {
                string[] p = kv.Key.Split('|');
                Hero h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == p[0]);
                Settlement s = (p.Length > 1) ? Settlement.All.FirstOrDefault(x => x.StringId == p[1]) : null;

                if (h == null || s == null || Holder(s) != h)
                {
                    float dec = RusukhMath.Decay(kv.Value, DecayPerDay * 7f);   // a week off-fief
                    if (dec <= 0f) spent.Add(kv.Key);
                    else _rusukh[kv.Key] = dec;
                }
                else
                {
                    ApplyBenefits(h, kv.Value, goldWeek);
                }
            }
            foreach (string k in spent) _rusukh.Remove(k);
        }

        private static void ApplyBenefits(Hero h, float rusukh, bool goldWeek)
        {
            int inf = RusukhMath.InfluenceBonus(rusukh);
            if (inf > 0 && h.Clan != null) ChangeClanInfluenceAction.Apply(h.Clan, inf);
            if (goldWeek)
            {
                int gold = RusukhMath.GoldBacking(rusukh);
                if (gold > 0) h.ChangeHeroGold(gold);
            }
        }

        private static float RelationFactor(Hero holder, Settlement s)
        {
            var notables = s.Notables;
            if (notables == null || notables.Count == 0) return 0.5f;
            float sum = 0f; int n = 0;
            foreach (Hero notable in notables)
            {
                if (notable == null || !notable.IsAlive) continue;
                sum += CharacterRelationManager.GetHeroRelation(holder, notable);
                n++;
            }
            return n == 0 ? 0.5f : (sum / n + 100f) / 200f;   // avg relation (-100..100) -> 0..1
        }

        public override void SyncData(IDataStore ds)
        {
            var keys = _rusukh.Keys.ToList();
            var vals = _rusukh.Values.ToList();
            ds.SyncData("tyt_rusukh_keys", ref keys);
            ds.SyncData("tyt_rusukh_vals", ref vals);
            ds.SyncData("tyt_rusukh_week", ref _weekCounter);
            if (!ds.IsSaving)
            {
                _rusukh = new Dictionary<string, float>();
                for (int i = 0; i < keys.Count && i < vals.Count; i++) _rusukh[keys[i]] = vals[i];
            }
        }
    }
}
