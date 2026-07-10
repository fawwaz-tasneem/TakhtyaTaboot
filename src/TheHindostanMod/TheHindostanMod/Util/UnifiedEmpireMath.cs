using System.Collections.Generic;

namespace TakhtyaTaboot.Util
{
    // The unified-empire premise (roadmap A.1) as PURE logic, unit-tested in
    // TheHindostanMod.Tests. At the campaign's open Bengal (empire_w) and Hyderabad (empire_s)
    // fold into the Mughal Empire as vassal clans; when Aurangzeb dies the fold reverses and
    // the two realms stand apart again. This class owns the phase machine and the selection
    // rules; UnifiedEmpireBehavior owns the engine actions.
    public static class UnifiedEmpireMath
    {
        public enum Phase
        {
            NotArmed = 0,   // old save, or the premise never applied — behave as before the feature
            Unified  = 1,   // the fold happened; Bengal/Hyderabad are dormant shells
            Sundered = 2,   // Aurangzeb is dead; the realms stand apart (terminal)
        }

        // A campaign is "fresh" only within its first day. An old save loaded under a new mod
        // version is far past this window, so it is never retro-unified.
        public const double FreshCampaignWindowDays = 1.0;

        public static bool ShouldUnify(int phase, double campaignAgeDays)
            => phase == (int)Phase.NotArmed
               && campaignAgeDays >= 0.0
               && campaignAgeDays < FreshCampaignWindowDays;

        // Which recorded clans actually return home at the breakaway: only those still alive and
        // still serving the empire — a clan that died or defected in the interim is left where
        // history put it. Order of the recorded list is preserved; duplicates collapse.
        public static List<string> SelectReturning(IEnumerable<string> recordedClanIds, ISet<string> aliveInEmpire)
        {
            var returning = new List<string>();
            if (recordedClanIds == null || aliveInEmpire == null) return returning;
            foreach (string id in recordedClanIds)
                if (!string.IsNullOrEmpty(id) && aliveInEmpire.Contains(id) && !returning.Contains(id))
                    returning.Add(id);
            return returning;
        }

        // The clan seated on the revived throne: the recorded ruling clan if it returns,
        // otherwise the caller's first candidate (callers pass the list strongest-first).
        public static string ChooseRuler(string recordedRulerId, IList<string> returning)
        {
            if (returning == null || returning.Count == 0) return null;
            if (!string.IsNullOrEmpty(recordedRulerId) && returning.Contains(recordedRulerId))
                return recordedRulerId;
            return returning[0];
        }

        // Colour records for the unified window: each folded clan's own Color/Color2 packed as
        // "clanId:color:color2" triples, so the breakaway can dress the returning houses in
        // their ancestral colours again.
        public static string PackColour(string clanId, uint color, uint color2)
            => $"{clanId}:{color}:{color2}";

        public static bool TryUnpackColour(string entry, out string clanId, out uint color, out uint color2)
        {
            clanId = null; color = 0; color2 = 0;
            if (string.IsNullOrEmpty(entry)) return false;
            string[] parts = entry.Split(':');
            if (parts.Length != 3 || parts[0].Length == 0) return false;
            if (!uint.TryParse(parts[1], out color) || !uint.TryParse(parts[2], out color2)) return false;
            clanId = parts[0];
            return true;
        }

        // CSV pack/unpack for SyncData (strings only — the save-safe primitive convention).
        public static string Pack(IEnumerable<string> ids)
            => ids == null ? "" : string.Join(",", ids);

        public static List<string> Unpack(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(csv)) return list;
            foreach (string part in csv.Split(','))
                if (part.Length > 0) list.Add(part);
            return list;
        }
    }
}
