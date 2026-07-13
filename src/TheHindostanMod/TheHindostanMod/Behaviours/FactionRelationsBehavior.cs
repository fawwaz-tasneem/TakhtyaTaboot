using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;

namespace TakhtyaTaboot
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
        // The three Mughal-successor realms (the Empire, Bengal, Hyderabad). Public so the
        // war-declaration block (NoMughalCivilWarPatch) keys off the same single source.
        public static readonly string[] MughalIds = { "empire", "empire_w", "empire_s" };
        private const string MarathaId = "battania";
        private const string SikhId = "khuzait";

        // Floor the three realms' rulers are kept above so kinship never sours into hostility.
        private const int MughalRelationFloor = 30;

        // True if the faction is one of the three original Mughal realms (not a breakaway/rebel).
        public static bool IsMughalKingdom(IFaction f)
            => f is Kingdom k && k.StringId != null && System.Array.IndexOf(MughalIds, k.StringId) >= 0;

        public static FactionRelationsBehavior Instance { get; private set; }

        private bool _initialised;
        // The Deccan war is a one-time historical fact, not a standing rule (see EnsureWarsAndPeace).
        private bool _deccanWarDeclared;

        public override void RegisterEvents()
        {
            Instance = this;
            // No OnNewGameCreated pass: it declared wars and set relations while the engine was
            // still creating kingdoms/heroes on parallel threads (see Util/WorldGen.cs) — and it
            // targeted the XML-default leaders anyway, not the emperor the scripted cascade seats
            // on the first live tick. Session launch does the kingdom-level pass; the cascade
            // calls ApplyLeaderRelations() again whenever a new emperor accedes.
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("FactionRelations.WeeklyTick", OnWeeklyTick));
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (!_initialised)
            {
                EnsureWarsAndPeace();     // kingdom-level: leader-independent
                ApplyLeaderRelations();   // provisional; re-run when the scripted emperor is seated
                _initialised = true;
            }
        }

        // Re-assert the full stance on demand — used by UnifiedEmpireBehavior when the
        // unified premise sunders and Bengal/Hyderabad re-enter the world as live realms.
        public void ReassertStance()
        {
            EnsureWarsAndPeace();
            ApplyLeaderRelations();
        }

        // Kingdom-level stance: intra-Mughal peace, and the Maratha war — which is DECLARED ONCE, at the
        // opening of the campaign, and never again.
        //
        // It used to be re-asserted on every ReassertStance() call, which meant the war was permanently
        // unconditional: any peace the Marathas and the Mughals ever made would be silently torn up the
        // next time the unified premise sundered. With the diplomacy layer that is intolerable — a truce
        // the player WON by concluding a war on his own terms would simply evaporate (ch.30 §6). So the
        // war is now a historical FACT (the Deccan war rages in 1707, under Aurangzeb, with the empire
        // whole), after which the two powers make war and peace like anyone else.
        //
        // Intra-Mughal peace is a different thing and IS still enforced: the kin never fight.
        private void EnsureWarsAndPeace()
        {
            var mughals = MughalIds.Select(Find)
                .Where(k => k != null && !k.IsEliminated && !UnifiedEmpireBehavior.IsDormant(k)).ToList();
            if (mughals.Count == 0) return;

            for (int i = 0; i < mughals.Count; i++)
                for (int j = i + 1; j < mughals.Count; j++)
                    EnsurePeace(mughals[i], mughals[j]);

            OpenTheDeccanWar(mughals);
        }

        // The Deccan war, declared once and only once: Aurangzeb's long war in the south is the state of
        // the world the campaign opens in. Thereafter it is an ordinary war and may be ended like one.
        private void OpenTheDeccanWar(System.Collections.Generic.List<Kingdom> mughals)
        {
            if (_deccanWarDeclared) return;
            Kingdom maratha = Find(MarathaId);
            if (maratha == null) return;

            _deccanWarDeclared = true;
            foreach (Kingdom m in mughals) EnsureWar(maratha, m);
            Util.TYTLog.Info("FactionRelations: the Deccan war is opened (once). Maratha/Mughal war and peace are free hereafter.");
        }

        // Ruler-to-ruler relations. Re-runnable: called at session launch and again by
        // ImperialSuccessionEventBehavior each time a new emperor is seated, so the stance
        // always binds the CURRENT leaders rather than whoever ruled when the map was made.
        public void ApplyLeaderRelations()
        {
            var mughals = MughalIds.Select(Find)
                .Where(k => k != null && !k.IsEliminated && !UnifiedEmpireBehavior.IsDormant(k)).ToList();
            Kingdom maratha = Find(MarathaId);
            Kingdom sikh = Find(SikhId);
            if (mughals.Count == 0) return;

            // Mughal kin — warm relations.
            for (int i = 0; i < mughals.Count; i++)
                for (int j = i + 1; j < mughals.Count; j++)
                    SetLeaderRelation(mughals[i], mughals[j], 40);

            // The Sikhs — bad blood with every Mughal realm.
            if (sikh != null)
                foreach (Kingdom m in mughals) SetLeaderRelation(sikh, m, -40);

            // The Marathas — the great enemy.
            if (maratha != null)
                foreach (Kingdom m in mughals) SetLeaderRelation(maratha, m, -70);
        }

        private void OnWeeklyTick()
        {
            // Keep the Mughal kin from tearing at each other. Only the three original
            // realms are pacified — rebel kingdoms and accession wars are left alone.
            // War between them is blocked at the source (NoMughalCivilWarPatch); this is a
            // belt-and-braces sweep for any war that predates the patch (e.g. an old save),
            // and it keeps the rulers' relations from drifting below the kinship floor.
            var mughals = MughalIds.Select(Find)
                .Where(k => k != null && !k.IsEliminated && !UnifiedEmpireBehavior.IsDormant(k)).ToList();
            for (int i = 0; i < mughals.Count; i++)
                for (int j = i + 1; j < mughals.Count; j++)
                {
                    if (mughals[i].IsAtWarWith(mughals[j]))
                        EnsurePeace(mughals[i], mughals[j]);
                    RaiseLeaderRelationTo(mughals[i], mughals[j], MughalRelationFloor);
                }
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

        // Lift the two rulers' relation UP to a floor if it has drifted below — never lowers
        // a naturally warmer bond, so the kinship can grow but not sour.
        private static void RaiseLeaderRelationTo(Kingdom a, Kingdom b, int floor)
        {
            Hero ha = a?.Leader, hb = b?.Leader;
            if (ha == null || hb == null) return;
            int current = CharacterRelationManager.GetHeroRelation(ha, hb);
            if (current < floor)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(ha, hb, floor - current, false);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_factionrel_init", ref _initialised);
            dataStore.SyncData("hind_factionrel_deccan", ref _deccanWarDeclared);
        }
    }
}
