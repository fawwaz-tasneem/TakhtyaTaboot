using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;

namespace TakhtyaTaboot
{
    // Developer console commands for testing the feudal system. Enable cheats, open
    // the console (tilde ~), and type any of these. List of commands:
    //   hindostan.grant_fief [name]        — grant the named (or current) settlement to you
    //   hindostan.grant_town               — grant the nearest town to you
    //   hindostan.grant_castle             — grant the nearest castle to you
    //   hindostan.grant_village            — grant the nearest village to you
    //   hindostan.set_rank <0-6>           — set your mansab rank
    //   hindostan.feudal_status            — show your liege, fiefs, vassals, standing
    //   hindostan.summon                   — force a call-to-arms farmaan
    //   hindostan.tax_now                  — force a tribute-demand farmaan
    //   hindostan.farmaan_test             — show a sample royal farmaan
    //   hindostan.list_fiefs               — list every settlement and its holder
    public static class FeudalCheats
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("grant_fief", "hindostan")]
        public static string GrantFief(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (FiefHierarchyBehavior.Instance == null) return "Feudal system not running.";
            Settlement s = ResolveByNameOrCurrent(args, out string err);
            if (s == null) return err;
            FiefHierarchyBehavior.Instance.GrantFief(s, Hero.MainHero, true);
            return $"{s.Name} ({Kind(s)}) granted to you.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("grant_town", "hindostan")]
        public static string GrantTown(List<string> args) => GrantOne(s => s.IsTown, "town");

        [CommandLineFunctionality.CommandLineArgumentFunction("grant_castle", "hindostan")]
        public static string GrantCastle(List<string> args) => GrantOne(s => s.IsCastle, "castle");

        [CommandLineFunctionality.CommandLineArgumentFunction("grant_village", "hindostan")]
        public static string GrantVillage(List<string> args) => GrantOne(s => s.IsVillage, "village");

        [CommandLineFunctionality.CommandLineArgumentFunction("set_rank", "hindostan")]
        public static string SetRank(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (MansabdariBehavior.Instance == null) return "Mansabdari system not running.";
            if (args == null || args.Count == 0 || !int.TryParse(args[0], out int idx))
                return $"Usage: hindostan.set_rank <0-{MansabdariBehavior.MaxRankIndex}>";
            return MansabdariBehavior.Instance.DebugSetRank(Clan.PlayerClan, idx);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("feudal_status", "hindostan")]
        public static string FeudalStatus(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            return FiefHierarchyBehavior.Instance?.DescribePlayerStanding() ?? "Feudal system not running.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("summon", "hindostan")]
        public static string Summon(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            return FiefHierarchyBehavior.Instance?.ForceCallToArms() ?? "Feudal system not running.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("tax_now", "hindostan")]
        public static string TaxNow(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            return FiefHierarchyBehavior.Instance?.ForceTax() ?? "Feudal system not running.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("farmaan_test", "hindostan")]
        public static string FarmaanTest(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k != null)
                RoyalFarmaan.FromRuler(k, "A Test Decree",
                    "This is a sample royal farmaan. It demonstrates how the sovereign and your liege " +
                    "address you. A decree may simply be acknowledged, or it may demand a choice.",
                    "So be it", null, "Refuse", () =>
                        InformationManager.DisplayMessage(new InformationMessage("You refused the test decree.")));
            else
                RoyalFarmaan.Issue("A Test Decree", "By the Imperial Court",
                    "This is a sample royal farmaan.", "Sealed in test");
            return "Test farmaan issued.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("list_fiefs", "hindostan")]
        public static string ListFiefs(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            var lines = Settlement.All
                .Where(s => s.IsTown || s.IsCastle || s.IsVillage)
                .OrderBy(s => s.MapFaction?.Name?.ToString() ?? "")
                .Select(s => $"{s.Name} [{Kind(s)}] — {(s.Owner?.Name?.ToString() ?? s.OwnerClan?.Name?.ToString() ?? "unowned")}")
                .Take(60);
            return string.Join("\n", lines);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────
        private static string GrantOne(Func<Settlement, bool> filter, string kind)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (FiefHierarchyBehavior.Instance == null) return "Feudal system not running.";
            Kingdom pk = Hero.MainHero?.Clan?.Kingdom;
            // Prefer the settlement you're standing in, then one in your realm, then any.
            Settlement s = null;
            if (Settlement.CurrentSettlement != null && filter(Settlement.CurrentSettlement))
                s = Settlement.CurrentSettlement;
            s = s ?? Settlement.All.FirstOrDefault(x => filter(x) && x.OwnerClan != Clan.PlayerClan && x.MapFaction == pk)
                  ?? Settlement.All.FirstOrDefault(x => filter(x) && x.OwnerClan != Clan.PlayerClan);
            if (s == null) return $"No {kind} found to grant.";
            FiefHierarchyBehavior.Instance.GrantFief(s, Hero.MainHero, true);
            return $"{s.Name} ({kind}) granted to you.";
        }

        private static Settlement ResolveByNameOrCurrent(List<string> args, out string err)
        {
            err = "";
            if (args != null && args.Count > 0)
            {
                string q = string.Join(" ", args);
                Settlement match = Settlement.All.FirstOrDefault(s =>
                    (s.IsTown || s.IsCastle || s.IsVillage) &&
                    (s.Name?.ToString() ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match == null) err = $"No settlement matching '{q}'.";
                return match;
            }
            if (Settlement.CurrentSettlement != null) return Settlement.CurrentSettlement;
            err = "Enter a settlement, or pass part of its name: hindostan.grant_fief <name>";
            return null;
        }

        private static string Kind(Settlement s) => s.IsTown ? "town" : s.IsCastle ? "castle" : "village";
    }
}
