using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Library;

namespace TheHindostanMod.UI
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
            starter.AddGameMenuOption("town", "hindostan_hierarchy_town",
                "{=!}Survey the imperial hierarchy",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                args => HierarchyScreen.Open(), false, 7);

            starter.AddGameMenuOption("castle", "hindostan_hierarchy_castle",
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
