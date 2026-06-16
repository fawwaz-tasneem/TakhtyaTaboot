using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;

namespace TheHindostanMod
{
    // Sets and keeps the geopolitical fault-lines of 1719 Hindostan:
    //   • The three Mughal-successor realms (the Empire, Bengal, Hyderabad) are kin —
    //     they hold the peace among themselves and rarely turn on one another.
    //   • The Sikh Confederation stands on bad terms with the Mughals.
    //   • The Maratha Saamrajya is the great enemy — relations are very bad and the
    //     two are at war from the outset.
    // Relations are set once at game start; intra-Mughal peace is then enforced weekly
    // (only among the three original realms, so revolts/accession wars still play out).
    public class FactionRelationsBehavior : CampaignBehaviorBase
    {
        private static readonly string[] MughalIds = { "empire", "empire_w", "empire_s" };
        private const string MarathaId = "battania";
        private const string SikhId = "khuzait";

        private bool _initialised;

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        }

        private void OnNewGame(CampaignGameStarter starter) => ApplyInitialRelations();
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (!_initialised) ApplyInitialRelations(); // backfill older saves
        }

        private void ApplyInitialRelations()
        {
            var mughals = MughalIds.Select(Find).Where(k => k != null).ToList();
            Kingdom maratha = Find(MarathaId);
            Kingdom sikh = Find(SikhId);
            if (mughals.Count == 0) return;

            // Mughal kin — peace and warm relations.
            for (int i = 0; i < mughals.Count; i++)
                for (int j = i + 1; j < mughals.Count; j++)
                {
                    EnsurePeace(mughals[i], mughals[j]);
                    SetLeaderRelation(mughals[i], mughals[j], 40);
                }

            // The Sikhs — bad blood with every Mughal realm.
            if (sikh != null)
                foreach (Kingdom m in mughals) SetLeaderRelation(sikh, m, -40);

            // The Marathas — the great enemy. Very bad, and at war.
            if (maratha != null)
                foreach (Kingdom m in mughals)
                {
                    SetLeaderRelation(maratha, m, -70);
                    EnsureWar(maratha, m);
                }

            _initialised = true;
        }

        private void OnWeeklyTick()
        {
            // Keep the Mughal kin from tearing at each other. Only the three original
            // realms are pacified — rebel kingdoms and accession wars are left alone.
            var mughals = MughalIds.Select(Find).Where(k => k != null && !k.IsEliminated).ToList();
            for (int i = 0; i < mughals.Count; i++)
                for (int j = i + 1; j < mughals.Count; j++)
                    if (mughals[i].IsAtWarWith(mughals[j]))
                        EnsurePeace(mughals[i], mughals[j]);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────
        private static Kingdom Find(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);

        private static void EnsurePeace(Kingdom a, Kingdom b)
        {
            if (a == null || b == null || a == b) return;
            if (a.IsAtWarWith(b)) MakePeaceAction.Apply(a, b);
        }

        private static void EnsureWar(Kingdom a, Kingdom b)
        {
            if (a == null || b == null || a == b) return;
            if (!a.IsAtWarWith(b)) DeclareWarAction.ApplyByDefault(a, b);
        }

        // Drive the two rulers' relation toward a target (so AI war/peace leans correctly).
        private static void SetLeaderRelation(Kingdom a, Kingdom b, int target)
        {
            Hero ha = a?.Leader, hb = b?.Leader;
            if (ha == null || hb == null) return;
            int current = CharacterRelationManager.GetHeroRelation(ha, hb);
            if (current != target)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(ha, hb, target - current, false);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_factionrel_init", ref _initialised);
        }
    }
}
