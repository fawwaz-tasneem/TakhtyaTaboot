using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace TakhtyaTaboot
{
    public class HindostanSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            var harmony = new Harmony("com.hindostanmod");
            string log = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory),
                "hindostan_patch_debug.txt");
            try
            {
                harmony.PatchAll(typeof(HindostanSubModule).Assembly);
                var patched = new System.Collections.Generic.List<string>();
                foreach (var m in harmony.GetPatchedMethods())
                    patched.Add((m.DeclaringType?.Name ?? "?") + "." + m.Name);
                System.IO.File.WriteAllText(log,
                    "PatchAll OK. Patched methods:\n" + string.Join("\n", patched) + "\n");
            }
            catch (System.Exception e)
            {
                System.IO.File.WriteAllText(log, "PatchAll THREW:\n" + e + "\n");
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);
            if (!(game.GameType is Campaign)) return;

            var starter = (CampaignGameStarter)gameStarter;

            starter.AddBehavior(new CultureVerificationBehavior());
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

            // Phase 4 — Political foundation
            starter.AddBehavior(new LegitimacyBehavior());
            starter.AddBehavior(new ImperialAuthorityBehavior());
            starter.AddBehavior(new RoyalDecisionsBehavior());
            starter.AddBehavior(new WarfareBehavior());

            // Phase 3 — Economy
            // starter.AddBehavior(new NazranaBehavior());
            // starter.AddModel(new HindostanPartySpeedModel());

            // Phase 5 — Succession
            starter.AddBehavior(new SuccessionBehavior());
            starter.AddBehavior(new AccessionWarBehavior());

            // Phase 6 — Revolts and estates
            starter.AddBehavior(new RevoltCascadeBehavior());
            // starter.AddBehavior(new CourtFactionsBehavior());
            // starter.AddBehavior(new EstatesBehavior());

            // Phase 7 — Military/political depth
            starter.AddBehavior(new CouncilBehavior());
            starter.AddBehavior(new ImperialCourtBehavior());
            // starter.AddBehavior(new CivilWarBehavior());
            // starter.AddBehavior(new ReligiousToleranceBehavior());

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
        }
    }
}
