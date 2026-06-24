using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace TakhtyaTaboot
{
    // Developer console commands for testing the succession system.
    // Open the console with the tilde key (cheats must be enabled) and type:
    //   hindostan.force_succession_crisis              -> player's kingdom
    //   hindostan.force_succession_crisis empire        -> a named kingdom (StringId)
    //   hindostan.succession_status                     -> list all active crises
    //   hindostan.advance_succession 30                 -> push active crises forward N days
    public static class SuccessionCheats
    {
        // Reliably kill a kingdom's ruler to test the DEATH -> succession path (vanilla campaign.kill_hero
        // is finicky with names and faction leaders). Fires HeroKilledEvent, the same hook a natural death
        // uses, so the law-aware trigger / clean-accession logic runs for real.
        [CommandLineFunctionality.CommandLineArgumentFunction("kill_ruler", "hindostan")]
        public static string KillRuler(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            Kingdom k = (args != null && args.Count > 0)
                ? Kingdom.All.FirstOrDefault(x => x.StringId == args[0])
                : Hero.MainHero?.Clan?.Kingdom;
            if (k == null) return "No such kingdom. Pass a StringId, e.g. hindostan.kill_ruler empire";
            Hero ruler = k.Leader;
            if (ruler == null || !ruler.IsAlive) return $"{k.Name} has no living ruler.";
            KillCharacterAction.ApplyByMurder(ruler, null, false);
            return $"{ruler.Name}, ruler of {k.Name}, is slain. Watch for the succession (law: " +
                   $"{SuccessionLawBehavior.LawName(SuccessionLawBehavior.Instance?.GetLaw(k) ?? Util.SuccessionLaw.Undeclared)}).";
        }

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

        // ── Temp-clan de-risk: create a cadet clan from a prince, then dissolve it back ──────
        // Run on a BACKUP save. If creation or dissolve crashes, clan restructuring is unsafe in this
        // build and the succession civil war stays on the (safe) champion model.
        private static Clan _testClan;

        [CommandLineFunctionality.CommandLineArgumentFunction("test_tempclan", "hindostan")]
        public static string TestTempClan(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            Hero h = null;
            if (args != null && args.Count > 0)
                h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == args[0]);
            else
            {
                // A non-leader adult lord in the player's realm — moving him orphans no clan.
                Kingdom k = Hero.MainHero?.Clan?.Kingdom;
                h = k?.Clans?.Where(c => !c.IsEliminated)
                     .SelectMany(c => c.Heroes ?? Enumerable.Empty<Hero>())
                     .FirstOrDefault(x => x != null && x.IsAlive && x.IsLord && !x.IsChild
                                          && x.Clan != null && x.Clan.Leader != x && x != Hero.MainHero);
            }
            if (h == null) return "No suitable non-leader lord found. Pass a hero id: hindostan.test_tempclan <id>";

            Clan origin = h.Clan;
            _testClan = Util.ClaimantClan.Create(h, h.Culture);
            if (_testClan == null) return $"Temp clan creation FAILED for {h.Name} (see Logs/tyt_log.txt).";
            return $"OK: created '{_testClan.Name}' (id {_testClan.StringId}) — leader {_testClan.Leader?.Name}, " +
                   $"members {_testClan.Heroes?.Count}, moved from '{origin?.Name}'. " +
                   $"Now run hindostan.test_tempclan_dissolve and check the clan/encyclopedia screens.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_tempclan_dissolve", "hindostan")]
        public static string TestTempClanDissolve(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (_testClan == null) return "No test temp clan to dissolve (run hindostan.test_tempclan first).";
            string id = _testClan.StringId;
            Util.ClaimantClan.Dissolve(_testClan);
            _testClan = null;
            return $"Dissolved {id}: heroes returned to their houses, clan destroyed. Verify nothing is broken.";
        }

        // ── Temp-KINGDOM lifecycle de-risk: the real succession mechanic ─────────────────────
        // A claimant's temp clan founds a breakaway kingdom (proven RevoltCascade path), goes to war
        // with the parent, then is merged back (peace + destroy kingdom + dissolve clan). Run on a
        // BACKUP save. Confirms the full claimant-war lifecycle before it is wired into succession.
        private static Kingdom _testKingdom;

        [CommandLineFunctionality.CommandLineArgumentFunction("test_tempkingdom", "hindostan")]
        public static string TestTempKingdom(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (RevoltCascadeBehavior.Instance == null) return "Revolt system is not running.";
            if (_testClan == null) return "Run hindostan.test_tempclan first to create a claimant clan.";

            // The parent we secede from: the claimant's original kingdom if known, else the player's.
            Kingdom parent = _testClan.Leader?.Clan?.Kingdom ?? Hero.MainHero?.Clan?.Kingdom
                             ?? Kingdom.All.FirstOrDefault(k => !k.IsEliminated);
            Settlement origin = _testClan.HomeSettlement ?? parent?.Settlements?.FirstOrDefault(s => s.IsTown)
                                ?? Settlement.All.FirstOrDefault(s => s.IsTown);
            if (origin == null) return "No settlement available to seat the rebel kingdom.";

            _testKingdom = RevoltCascadeBehavior.Instance.CreateRebelKingdom(
                _testClan, origin, "Claim of " + (_testClan.Leader?.Name?.ToString() ?? "the Pretender"));
            if (_testKingdom == null) return "Breakaway kingdom creation FAILED (see Logs/tyt_log.txt).";

            if (parent != null) RevoltCascadeBehavior.Instance.EnsureAtWar(_testKingdom, parent);

            return $"OK: '{_testKingdom.Name}' founded by '{_testClan.Name}' (leader {_testKingdom.Leader?.Name}), " +
                   $"at war with {parent?.Name?.ToString() ?? "no one"}. " +
                   "Check the kingdoms/diplomacy/encyclopedia screens and the campaign map, then run hindostan.test_tempkingdom_dissolve.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_tempkingdom_dissolve", "hindostan")]
        public static string TestTempKingdomDissolve(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (_testKingdom == null) return "No test kingdom to dissolve (run hindostan.test_tempkingdom first).";

            string kid = _testKingdom.StringId;
            // Merge back: make peace with everyone, then destroy the kingdom and the temp clan.
            foreach (Kingdom other in Kingdom.All.Where(k => k != _testKingdom && !k.IsEliminated && k.IsAtWarWith(_testKingdom)).ToList())
                try { MakePeaceAction.Apply(_testKingdom, other); } catch { }
            try { DestroyKingdomAction.Apply(_testKingdom); } catch { }
            _testKingdom = null;

            if (_testClan != null) { Util.ClaimantClan.Dissolve(_testClan); _testClan = null; }
            return $"Merged back: kingdom {kid} destroyed, temp clan dissolved, lord returned to his house. Verify nothing is broken.";
        }
    }
}
