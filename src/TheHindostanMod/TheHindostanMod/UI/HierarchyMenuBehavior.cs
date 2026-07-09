using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Library;

namespace TakhtyaTaboot.UI
{
    // Adds an entry point for the hierarchy viewer in settlement menus, and a
    // developer console command (hindostan.hierarchy).
    public class HierarchyMenuBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
            => CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);

        public override void SyncData(IDataStore dataStore) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Lives under the consolidated court menu (CourtMenuBehavior), not the
            // settlement root — one option, both settlement kinds.
            starter.AddGameMenuOption(CourtMenuBehavior.MenuId, "hindostan_hierarchy",
                "{=!}Survey the imperial hierarchy",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                args => HierarchyScreen.Open(), false, 7);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("hierarchy", "hindostan")]
        public static string OpenHierarchy(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            HierarchyScreen.Open();
            return "Opening the imperial hierarchy.";
        }
    }
}
