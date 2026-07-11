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
            //
            // Patches on campaign GameComponents models are NOT applied here. Preparing a
            // target method runs its class's static initializer, and TaleWorlds' model
            // classes initialize static TextObjects via GameTexts.FindText — which throws
            // before any Game exists. A cctor that throws poisons the type for the whole
            // process (TypeInitializationException at first use, e.g. the first party-speed
            // calculation when the map spawns), while Harmony still reports the patch as
            // applied. Those classes are patched in OnGameStart, after GameTexts.Initialize.
            _harmony = new Harmony("com.hindostanmod");
            int okNow = ApplyPatchClasses(deferred: false, out int failedNow);
            var patched = new System.Collections.Generic.List<string>();
            foreach (var m in _harmony.GetPatchedMethods())
                patched.Add((m.DeclaringType?.Name ?? "?") + "." + m.Name);
            TYTLog.Info($"Harmony (load phase): {okNow} patch class(es) applied, {failedNow} failed — {patched.Count} method(s) patched:\n  "
                        + string.Join("\n  ", patched));
        }

        private static Harmony _harmony;
        private static bool _gameStartPatchesApplied;

        // Patch classes whose TARGET types need a live Game to initialize (see comment in
        // OnSubModuleLoad). Any new patch on a TaleWorlds.CampaignSystem.GameComponents.*
        // model belongs in this set.
        private static readonly System.Collections.Generic.HashSet<System.Type> GameStartPatches =
            new System.Collections.Generic.HashSet<System.Type>
            {
                typeof(MonsoonSpeedPatch),      // DefaultPartySpeedCalculatingModel: static GameTexts.FindText
                typeof(AuthorityCohesionPatch), // DefaultArmyManagementCalculationModel: static GameTexts.FindText
                typeof(AuthorityTaxPatch),      // DefaultClanFinanceModel (statics safe today; policy)
                typeof(ToleranceTaxPatch),      // DefaultClanFinanceModel (statics safe today; policy)
                typeof(PartySizeMansabPatch),   // DefaultPartySizeLimitModel (statics safe today; policy)
            };

        private static int ApplyPatchClasses(bool deferred, out int failed)
        {
            int ok = 0; failed = 0;
            foreach (var type in typeof(HindostanSubModule).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                if (GameStartPatches.Contains(type) != deferred) continue;
                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (System.Exception e)
                {
                    failed++;
                    TYTLog.Error($"Harmony patch FAILED for {type.FullName} — this patch is disabled, others continue", e);
                }
            }
            return ok;
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

        // ── UX / information-architecture charter ─────────────────────────────────────────
        // Every feature names its surface BEFORE implementation, by this rule:
        //   • Person-to-person acts (oaths, gifts, grievances, invitations) → DIALOGUE
        //     (HindostanDialogsBehavior, hero_main_options).
        //   • Place-bound acts (court business, decrees, works) → SETTLEMENT MENUS, all
        //     town/castle entries consolidated under ONE "hindostan_court" submenu
        //     (CourtMenuBehavior); villages keep their own hindostan_village menu.
        //   • Realm overviews → GAUNTLET SCREENS (Council, Hierarchy).
        //   • Events & decrees → FARMAANS: paused, deduped and digest-managed by
        //     FarmaanDirectorBehavior (Util/FarmaanFlow rules).
        //   • Ambient information → the message log (InformationManager), never a popup.
        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);

            // Model patches: applied here because Game.Initialize (which runs GameTexts.Initialize)
            // has completed by the time submodules get OnGameStart — the target classes' static
            // initializers can now run safely. Once per process; targets live in the game assembly.
            if (!_gameStartPatchesApplied && _harmony != null)
            {
                _gameStartPatchesApplied = true;
                int ok = ApplyPatchClasses(deferred: true, out int failed);
                TYTLog.Info($"Harmony (game phase): {ok} model patch class(es) applied, {failed} failed.");
            }

            if (!(game.GameType is Campaign)) return;
            TYTLog.Info("OnGameStart: registering Takht ya Taboot campaign behaviours.");

            var starter = (CampaignGameStarter)gameStarter;

            // MUST be first: closes the world-gen gate before any other behavior's handlers can fire
            // during the engine's parallel hero/clan/settlement creation (see Util/WorldGen.cs).
            Util.WorldGen.Ready = false;
            starter.AddBehavior(new Util.WorldGenGuardBehavior());

            starter.AddBehavior(new Util.SaveGuardBehavior());
            starter.AddBehavior(new FarmaanDirectorBehavior()); // decree dedup/cooldowns + Court Circular digest
            // Early, before every political session-launch pass (dynasty sovereign roll, faction
            // stance, court seeding): on a new campaign this folds Bengal/Hyderabad into the
            // empire FIRST, so those passes see the shells dormant rather than as live realms.
            starter.AddBehavior(new UnifiedEmpireBehavior());
            starter.AddBehavior(new ClanSafetyNetBehavior()); // no noble house stands masterless
            starter.AddBehavior(new CourtMenuBehavior());       // the ONE court submenu — must precede every behavior that adds options to it
            starter.AddBehavior(new OpinionBehavior());         // personal opinion ledger (individuals, not clans)
            starter.AddBehavior(new DynastyBehavior());         // dynasty registry, royal styles, cadet houses
            starter.AddBehavior(new HindostanDialogsBehavior()); // the court dialogue pack
            if (Config.Tune.EnableDebugVerification)
                starter.AddBehavior(new CultureVerificationBehavior()); // debug-only culture audit (MCM "Debug" group)
            starter.AddBehavior(new ReligionBehavior());
            starter.AddBehavior(new FactionRelationsBehavior());
            starter.AddBehavior(new UI.HierarchyMenuBehavior());

            // Phase 1 — Fief ownership
            starter.AddBehavior(new FiefHierarchyBehavior());
            starter.AddBehavior(new FeudalTitlesBehavior());
            starter.AddBehavior(new VillageDevelopmentBehavior());
            starter.AddBehavior(new SlaveLabourBehavior()); // bonded-labour gangs; adds options to hindostan_village (must follow it)

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
            starter.AddBehavior(new SiegeParleyBehavior()); // the attacker's envoy at the walls
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
            starter.AddBehavior(new CoronationBehavior()); // the accession darbar: summons, oaths, snubs

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
            starter.AddBehavior(new AkhbaarScoutBehavior()); // pay a harkara to trail a named lord; akhbaar report via farmaan
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
