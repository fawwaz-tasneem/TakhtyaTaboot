using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace TheHindostanMod
{
    // Developer console commands for testing the succession system.
    // Open the console with the tilde key (cheats must be enabled) and type:
    //   hindostan.force_succession_crisis              -> player's kingdom
    //   hindostan.force_succession_crisis empire        -> a named kingdom (StringId)
    //   hindostan.succession_status                     -> list all active crises
    //   hindostan.advance_succession 30                 -> push active crises forward N days
    public static class SuccessionCheats
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("force_succession_crisis", "hindostan")]
        public static string ForceCrisis(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (SuccessionBehavior.Instance == null) return "Succession system is not running.";

            Kingdom k;
            if (args != null && args.Count > 0)
            {
                k = Kingdom.All.FirstOrDefault(x => x.StringId == args[0]);
                if (k == null) return $"No kingdom with id '{args[0]}'. Try one of: " +
                    string.Join(", ", Kingdom.All.Where(x => !x.IsEliminated).Select(x => x.StringId));
            }
            else
            {
                k = Hero.MainHero?.Clan?.Kingdom;
                if (k == null) return "You serve no kingdom. Pass a kingdom id, e.g. hindostan.force_succession_crisis empire";
            }
            return SuccessionBehavior.Instance.DebugForceCrisis(k);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("succession_status", "hindostan")]
        public static string Status(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (SuccessionBehavior.Instance == null) return "Succession system is not running.";

            var lines = new List<string>();
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated))
            {
                var state = SuccessionBehavior.Instance.GetCrisisState(k);
                if (state == CrisisState.None) continue;
                var claimants = SuccessionBehavior.Instance.GetClaimants(k);
                lines.Add($"{k.Name} [{state}]: " + string.Join(", ",
                    claimants.Select(c => $"{c.Name} {SuccessionBehavior.Instance.GetSupportPercent(k, c):0}%")));
            }
            return lines.Count == 0 ? "No active succession crises." : string.Join("\n", lines);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("advance_succession", "hindostan")]
        public static string Advance(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (SuccessionBehavior.Instance == null) return "Succession system is not running.";
            int days = 7;
            if (args != null && args.Count > 0) int.TryParse(args[0], out days);
            return SuccessionBehavior.Instance.DebugAdvance(days);
        }
    }
}
