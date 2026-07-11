using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace TakhtyaTaboot
{
    // Developer console commands for testing the akhbaar scout system. Enable cheats, open
    // the console (tilde ~), and type any of these. List of commands:
    //   hindostan.akhbaar_status        — list scouts on the road and days until each report
    //   hindostan.akhbaar_arrive        — force every pending akhbaar to arrive now
    //   hindostan.akhbaar_send <name>   — dispatch a free scout after the first lord matching <name>
    public static class AkhbaarCheats
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("akhbaar_status", "hindostan")]
        public static string Status(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            return AkhbaarScoutBehavior.Instance?.DescribeStatus() ?? "Akhbaar system not running.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("akhbaar_arrive", "hindostan")]
        public static string Arrive(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            return AkhbaarScoutBehavior.Instance?.ForceArrive() ?? "Akhbaar system not running.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("akhbaar_send", "hindostan")]
        public static string Send(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (args == null || args.Count == 0) return "Usage: hindostan.akhbaar_send <lord name part>";
            return AkhbaarScoutBehavior.Instance?.DebugDispatch(string.Join(" ", args))
                   ?? "Akhbaar system not running.";
        }
    }
}
