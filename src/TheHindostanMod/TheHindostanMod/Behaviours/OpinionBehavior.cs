using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // The personal layer over the engine's clan-shaped world: what one HERO thinks of
    // another, as typed, dated, decaying records (an oath sworn, a grudge pressed, an
    // empty place at a ceremony). EffectiveOpinion = vanilla relation + live modifiers,
    // so nothing existing breaks — but records can target ANY hero (princes, spouses,
    // notables), which is what lets individuals matter apart from their clan.
    // Decay curves and magnitudes live in Util.OpinionMath (unit-tested).
    public class OpinionBehavior : CampaignBehaviorBase
    {
        public static OpinionBehavior Instance { get; private set; }

        private class Record
        {
            public string A, B;          // A's feeling ABOUT B (directional)
            public int Type;             // (int)OpinionMath.OpinionType
            public float Magnitude;
            public int Day;              // campaign day the record was made
        }

        private List<Record> _records = new List<Record>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Opinion.WeeklyTick", OnWeeklyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            var a = _records.Select(r => r.A).ToList();
            var b = _records.Select(r => r.B).ToList();
            var t = _records.Select(r => r.Type).ToList();
            var m = _records.Select(r => r.Magnitude).ToList();
            var d = _records.Select(r => r.Day).ToList();
            dataStore.SyncData("hind_op_a", ref a);
            dataStore.SyncData("hind_op_b", ref b);
            dataStore.SyncData("hind_op_type", ref t);
            dataStore.SyncData("hind_op_mag", ref m);
            dataStore.SyncData("hind_op_day", ref d);
            if (!dataStore.IsSaving)
            {
                _records = new List<Record>();
                for (int i = 0; i < a.Count && i < b.Count && i < t.Count && i < m.Count && i < d.Count; i++)
                    _records.Add(new Record { A = a[i], B = b[i], Type = t[i], Magnitude = m[i], Day = d[i] });
            }
        }

        // ── Writing ──────────────────────────────────────────────────────────────────
        // Records A's feeling about B. magnitudeOverride 0 = the type's default; pass a
        // signed value to flip CourtRuling (against) or scale a gift.
        public void AddOpinion(Hero a, Hero b, OpinionMath.OpinionType type, float magnitudeOverride = 0f)
        {
            if (a == null || b == null || a == b) return;
            float magnitude = magnitudeOverride != 0f ? magnitudeOverride : OpinionMath.DefaultMagnitude(type);
            _records.Add(new Record
            {
                A = a.StringId, B = b.StringId, Type = (int)type,
                Magnitude = magnitude, Day = (int)CampaignTime.Now.ToDays,
            });
        }

        // Clears live records of one type between two heroes (e.g. a grudge mended).
        public void ClearOpinion(Hero a, Hero b, OpinionMath.OpinionType type)
        {
            if (a == null || b == null) return;
            _records.RemoveAll(r => r.A == a.StringId && r.B == b.StringId && r.Type == (int)type);
        }

        // ── Reading ──────────────────────────────────────────────────────────────────
        public float ModifierSum(Hero a, Hero b)
        {
            if (a == null || b == null) return 0f;
            int today = (int)CampaignTime.Now.ToDays;
            float sum = 0f;
            foreach (Record r in _records)
                if (r.A == a.StringId && r.B == b.StringId)
                {
                    float v = OpinionMath.CurrentValue((OpinionMath.OpinionType)r.Type, r.Magnitude, today - r.Day);
                    if (!OpinionMath.IsDead(v)) sum += v;
                }
            return sum;
        }

        // What A effectively thinks of B: vanilla relation plus A's live records about B.
        public float EffectiveOpinion(Hero a, Hero b)
        {
            if (a == null || b == null) return 0f;
            return OpinionMath.Effective(CharacterRelationManager.GetHeroRelation(a, b), ModifierSum(a, b));
        }

        public List<(OpinionMath.OpinionType type, float value)> TopModifiers(Hero a, Hero b, int n)
        {
            var result = new List<(OpinionMath.OpinionType, float)>();
            if (a == null || b == null) return result;
            int today = (int)CampaignTime.Now.ToDays;
            foreach (Record r in _records)
                if (r.A == a.StringId && r.B == b.StringId)
                {
                    float v = OpinionMath.CurrentValue((OpinionMath.OpinionType)r.Type, r.Magnitude, today - r.Day);
                    if (!OpinionMath.IsDead(v)) result.Add(((OpinionMath.OpinionType)r.Type, v));
                }
            return result.OrderByDescending(x => Math.Abs(x.Item2)).Take(n).ToList();
        }

        public bool HasLive(Hero a, Hero b, OpinionMath.OpinionType type)
        {
            if (a == null || b == null) return false;
            int today = (int)CampaignTime.Now.ToDays;
            return _records.Any(r => r.A == a.StringId && r.B == b.StringId && r.Type == (int)type
                && !OpinionMath.IsDead(OpinionMath.CurrentValue((OpinionMath.OpinionType)r.Type, r.Magnitude, today - r.Day)));
        }

        // The strongest live NEGATIVE record between two heroes, either direction —
        // the "shadow between us" the grievance dialogue surfaces.
        public bool TryGetGrievance(Hero a, Hero b, out OpinionMath.OpinionType type, out float value, out bool aHoldsIt)
        {
            type = OpinionMath.OpinionType.Grudge; value = 0f; aHoldsIt = true;
            foreach (var (t, v) in TopModifiers(a, b, 8))
                if (v < value) { value = v; type = t; aHoldsIt = true; }
            foreach (var (t, v) in TopModifiers(b, a, 8))
                if (v < value) { value = v; type = t; aHoldsIt = false; }
            return value < -1f;
        }

        // ── Weekly upkeep + organic court life ───────────────────────────────────────
        private void OnWeeklyTick()
        {
            int today = (int)CampaignTime.Now.ToDays;

            // Sweep dead records and records whose heroes are gone.
            _records.RemoveAll(r =>
                OpinionMath.IsDead(OpinionMath.CurrentValue((OpinionMath.OpinionType)r.Type, r.Magnitude, today - r.Day))
                || Hero.AllAliveHeroes.All(h => h.StringId != r.A)
                || Hero.AllAliveHeroes.All(h => h.StringId != r.B));

            // Organic life: a few quarrels a week somewhere in the world, between lords
            // of one realm whose tempers clash (the WarAims "wickedness" shape).
            int seeded = 0;
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated))
            {
                if (seeded >= 3) break;
                if (MBRandom.RandomFloat >= 0.15f) continue;
                var lords = k.Clans.Where(c => !c.IsEliminated && c.Leader != null)
                    .Select(c => c.Leader).ToList();
                if (lords.Count < 2) continue;
                Hero x = lords[MBRandom.RandomInt(lords.Count)];
                Hero y = lords[MBRandom.RandomInt(lords.Count)];
                if (x == y) continue;
                int clash = Math.Abs(Wickedness(x) - Wickedness(y));
                if (clash < 2) continue;
                AddOpinion(x, y, OpinionMath.OpinionType.Insult);
                AddOpinion(y, x, OpinionMath.OpinionType.Insult);
                seeded++;
                if (x == Hero.MainHero || y == Hero.MainHero)
                    TYTLog.Info($"Opinion: words exchanged at court between {x.Name} and {y.Name}.");
            }
        }

        private static int Wickedness(Hero h)
            => -h.GetTraitLevel(DefaultTraits.Mercy) - h.GetTraitLevel(DefaultTraits.Honor)
               + h.GetTraitLevel(DefaultTraits.Calculating);
    }
}
