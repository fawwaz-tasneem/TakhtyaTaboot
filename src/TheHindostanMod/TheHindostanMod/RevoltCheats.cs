using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace TheHindostanMod
{
    // Developer console commands for testing the Revolt Cascade. Enable cheats, open the
    // console (tilde ~), and type:
    //   hindostan.set_revolt_pressure <0-100>  — set unrest at the settlement you're in
    //   hindostan.trigger_revolt               — force a revolt at the current settlement
    //   hindostan.list_unrest                  — show unrest and provisional rebel kingdoms
    //   hindostan.secede                       — raise your own standard of revolt
    //   hindostan.crush_rebels                 — instantly crush all provisional rebel kingdoms
    public static class RevoltCheats
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("set_revolt_pressure", "hindostan")]
        public static string SetPressure(List<string> args)
        {
            if (Campaign.Current == null || RevoltCascadeBehavior.Instance == null) return "Load a campaign first.";
            Settlement s = Settlement.CurrentSettlement;
            if (s == null) return "Enter a settlement first.";
            float v = 100f;
            if (args != null && args.Count > 0) float.TryParse(args[0], out v);
            RevoltCascadeBehavior.Instance.SetPressure(s, v);
            return $"{s.Name} revolt pressure set to {v:0}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("trigger_revolt", "hindostan")]
        public static string Trigger(List<string> args)
        {
            if (Campaign.Current == null || RevoltCascadeBehavior.Instance == null) return "Load a campaign first.";
            return RevoltCascadeBehavior.Instance.DebugTriggerRevolt();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("list_unrest", "hindostan")]
        public static string ListUnrest(List<string> args)
        {
            if (Campaign.Current == null || RevoltCascadeBehavior.Instance == null) return "Load a campaign first.";
            return RevoltCascadeBehavior.Instance.DescribeUnrest();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("secede", "hindostan")]
        public static string Secede(List<string> args)
        {
            if (Campaign.Current == null || RevoltCascadeBehavior.Instance == null) return "Load a campaign first.";
            return RevoltCascadeBehavior.Instance.PlayerSecede();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("crush_rebels", "hindostan")]
        public static string Crush(List<string> args)
        {
            if (Campaign.Current == null || RevoltCascadeBehavior.Instance == null) return "Load a campaign first.";
            return RevoltCascadeBehavior.Instance.DebugCrushAll();
        }
    }
}
