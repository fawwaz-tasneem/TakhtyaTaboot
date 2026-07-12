using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Applies the historical cast's web (Util/HistoricalCast, tested) ONCE per campaign:
    // the starting relations — the friendships and rivalries the round-7 encyclopedia
    // biographies describe, within and across kingdoms — and the personality traits that
    // match each lord's historical character. Idempotent by a serialized flag, so an old
    // save picks the web up exactly once on its next load and new campaigns start with it.
    // Missing heroes (a shifted cast list) are skipped and counted, never fatal.
    public class HistoricalCastBehavior : CampaignBehaviorBase
    {
        public static HistoricalCastBehavior Instance { get; private set; }

        private bool _applied;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this,
                _ => TYTLog.Guard("HistoricalCast.Apply", Apply));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_cast_applied", ref _applied);
        }

        private void Apply()
        {
            if (_applied) return;
            _applied = true;

            var heroes = new Dictionary<string, Hero>();
            foreach (Hero h in Hero.AllAliveHeroes)
                if (!heroes.ContainsKey(h.StringId)) heroes[h.StringId] = h;

            int relSet = 0, relSkipped = 0;
            foreach (var (a, b, relation) in HistoricalCast.Relations)
            {
                if (heroes.TryGetValue(a, out Hero ha) && heroes.TryGetValue(b, out Hero hb))
                { CharacterRelationManager.SetHeroRelation(ha, hb, relation); relSet++; }
                else relSkipped++;
            }

            int traitSet = 0, traitSkipped = 0;
            foreach (var (id, traitName, level) in HistoricalCast.Traits)
            {
                TraitObject trait = TraitOf(traitName);
                if (trait != null && heroes.TryGetValue(id, out Hero h))
                { h.SetTraitLevel(trait, level); traitSet++; }
                else traitSkipped++;
            }

            TYTLog.Info($"HistoricalCast: applied {relSet} relations ({relSkipped} skipped), " +
                        $"{traitSet} traits ({traitSkipped} skipped).");
        }

        private static TraitObject TraitOf(string name)
        {
            switch (name)
            {
                case "Honor": return DefaultTraits.Honor;
                case "Valor": return DefaultTraits.Valor;
                case "Mercy": return DefaultTraits.Mercy;
                case "Generosity": return DefaultTraits.Generosity;
                case "Calculating": return DefaultTraits.Calculating;
                default: return null;
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("cast_reapply", "hindostan")]
        public static string CastReapply(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Instance._applied = false;
            Instance.Apply();
            return "Historical cast re-applied (relations + traits).";
        }
    }
}
