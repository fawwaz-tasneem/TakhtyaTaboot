using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    public class HindostanSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            // Detailed crash log: opened first, and a fatal unhandled exception anywhere is
            // captured with its full stack trace so a crash can be isolated from tyt_log.txt.
            TYTLog.Init();
            System.AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TYTLog.Info("Takht ya Taboot loading. Log at: " + (TYTLog.Path ?? "<none>"));

            // Load authored hero biographies (the character text= attributes the engine drops).
            Util.Biographies.EnsureLoaded();

            // UIExtenderEx — enables encyclopedia link-clicks on the hero Info section.
            try
            {
                var extender = Bannerlord.UIExtenderEx.UIExtender.Create("TakhtyaTaboot");
                extender.Register(typeof(HindostanSubModule).Assembly);
                extender.Enable();
                TYTLog.Info("UIExtenderEx registered.");
            }
            catch (System.Exception e) { TYTLog.Error("UIExtenderEx registration FAILED", e); }

            // Patch each [HarmonyPatch] class individually: a single bad target (e.g. a
            // method signature that changed between game versions) must not disable every
            // other patch, which is what a lone PatchAll inside try/catch did.
            var harmony = new Harmony("com.hindostanmod");
            int patchedOk = 0, patchedFailed = 0;
            foreach (var type in typeof(HindostanSubModule).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    patchedOk++;
                }
                catch (System.Exception e)
                {
                    patchedFailed++;
                    TYTLog.Error($"Harmony patch FAILED for {type.FullName} — this patch is disabled, others continue", e);
                }
            }
            var patched = new System.Collections.Generic.List<string>();
            foreach (var m in harmony.GetPatchedMethods())
                patched.Add((m.DeclaringType?.Name ?? "?") + "." + m.Name);
            TYTLog.Info($"Harmony: {patchedOk} patch class(es) applied, {patchedFailed} failed — {patched.Count} method(s) patched:\n  "
                        + string.Join("\n  ", patched));
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            // Track the player's personal kills in campaign battles, for valour (Util.PlayerKillValourLogic).
            if (Campaign.Current != null && mission != null)
                mission.AddMissionBehavior(new Util.PlayerKillValourLogic());
        }

        private static void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as System.Exception;
            TYTLog.Error($"UNHANDLED EXCEPTION (terminating={e.IsTerminating})", ex);
            TYTLog.WriteCrashReport($"AppDomain unhandled exception (terminating={e.IsTerminating})", ex);
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);
            if (!(game.GameType is Campaign)) return;
            TYTLog.Info("OnGameStart: registering Takht ya Taboot campaign behaviours.");

            var starter = (CampaignGameStarter)gameStarter;

            // MUST be first: closes the world-gen gate before any other behavior's handlers can fire
            // during the engine's parallel hero/clan/settlement creation (see Util/WorldGen.cs).
            Util.WorldGen.Ready = false;
            starter.AddBehavior(new Util.WorldGenGuardBehavior());

            starter.AddBehavior(new Util.SaveGuardBehavior());
            if (Config.Tune.EnableDebugVerification)
                starter.AddBehavior(new CultureVerificationBehavior()); // debug-only culture audit (MCM "Debug" group)
            starter.AddBehavior(new ReligionBehavior());
            starter.AddBehavior(new FactionRelationsBehavior());
            starter.AddBehavior(new UI.HierarchyMenuBehavior());

            // Phase 1 — Fief ownership
            starter.AddBehavior(new FiefHierarchyBehavior());
            starter.AddBehavior(new FeudalTitlesBehavior());
            starter.AddBehavior(new VillageDevelopmentBehavior());

            // Phase 2 — Lord ranks
            starter.AddBehavior(new MansabdariBehavior());
            starter.AddBehavior(new CareerProgressionBehavior());
            starter.AddBehavior(new RusukhBehavior()); // local entrenchment of fief-holders
            starter.AddBehavior(new MansabdariTenureBehavior()); // Feudal <-> Mansabdari tenure edict

            // Phase 4 — Political foundation
            starter.AddBehavior(new LegitimacyBehavior());
            starter.AddBehavior(new ImperialAuthorityBehavior());
            starter.AddBehavior(new RoyalDecisionsBehavior());
            starter.AddBehavior(new WarfareBehavior());
            starter.AddBehavior(new Util.WarAimsBehavior()); // trait-driven affronts -> casus belli -> judgement

            // Phase 3 — Economy
            starter.AddBehavior(new NazranaBehavior()); // the courtly gift cycle
            // Monsoon party speed ships as Patches/MonsoonSpeedPatch (Harmony postfix on the
            // vanilla speed model) rather than a GameModel override — see the patch header.

            // Phase 5 — Succession
            starter.AddBehavior(new SuccessionLawBehavior()); // per-kingdom succession law; consulted by the engine below
            starter.AddBehavior(new SuccessionBehavior());
            starter.AddBehavior(new AccessionWarBehavior());
            starter.AddBehavior(new ImperialSuccessionEventBehavior()); // scripted 1707 emperor cascade

            // Phase 6 — Revolts and estates
            starter.AddBehavior(new RevoltCascadeBehavior());
            starter.AddBehavior(new CourtFactionsBehavior()); // the court's four parties
            // starter.AddBehavior(new EstatesBehavior()); // deferred: overlaps council/authority; see wiki Ch.19

            // Phase 7 — Military/political depth
            starter.AddBehavior(new CouncilBehavior());
            starter.AddBehavior(new ImperialCourtBehavior());
            starter.AddBehavior(new PartyOrdersBehavior());
            starter.AddBehavior(new CivilWarBehavior());          // AI leadership challenges (Ch.16)
            starter.AddBehavior(new ReligiousToleranceBehavior()); // realm faith policy + jizya (Ch.17)

            // Phase 8 — QoL systems
            // starter.AddBehavior(new TradeRouteBehavior());
            // starter.AddBehavior(new FoodSecurityBehavior());
            // starter.AddBehavior(new EpidemicBehavior());
            // starter.AddBehavior(new FestivalBehavior());

            // Phase 9 — Character depth
            // starter.AddBehavior(new CharacterTraitsBehavior());
            // starter.AddBehavior(new UlemaFatwaBehavior());
            // starter.AddBehavior(new PilgrimageBehavior());
            // starter.AddBehavior(new GreatWorksBehavior());
            // starter.AddBehavior(new IntrigueBehavior());

            // Phase 10 — End-game / main quest
            // starter.AddBehavior(new WarScoreBehavior());
            // starter.AddBehavior(new PeaceNegotiationBehavior());
            // starter.AddBehavior(new SuzeraintyBehavior());
            // starter.AddBehavior(new MainQuestBehavior());

            // Phase 11 — LLM news (optional)
            // starter.AddBehavior(new EventCaptureBehavior());

            TYTLog.Info("OnGameStart: behaviours registered.");
        }
    }
}
