using System;

namespace TakhtyaTaboot.Util
{
    // Pure math for the court factions (wiki Ch.19 §4): every lord leans toward one of
    // four parties, and their pooled renown — doubled for council seats — decides which
    // party dominates a realm's court. NO TaleWorlds types — linked into tests.
    public static class CourtFactionMath
    {
        // Serialized as ints in SyncData: order must never change.
        public enum CourtFaction { War = 0, Peace = 1, Reform = 2, Orthodox = 3 }

        public const int PetitionIntervalDays = 30;
        public const float CouncilSeatWeight = 2f;

        // A lord's leaning, derived deterministically from his traits (no storage):
        // the valorous ride with the war party, the calculating counsel peace, the
        // generous back reform, the honor-bound stand with the orthodox — and a lord
        // of no strong trait falls to a stable hash of his id.
        public static CourtFaction Affinity(int valor, int calculating, int generosity, int honor, int stableHash)
        {
            int best = Math.Max(Math.Max(valor, calculating), Math.Max(generosity, honor));
            if (best > 0)
            {
                if (valor == best) return CourtFaction.War;
                if (calculating == best) return CourtFaction.Peace;
                if (generosity == best) return CourtFaction.Reform;
                return CourtFaction.Orthodox;
            }
            int idx = ((stableHash % 4) + 4) % 4;
            return (CourtFaction)idx;
        }

        public static float MemberWeight(float clanRenown, bool leaderHoldsCouncilSeat)
            => Math.Max(0f, clanRenown) * (leaderHoldsCouncilSeat ? CouncilSeatWeight : 1f);

        // Index of the strongest party; ties go to the lower index (War first).
        public static int Dominant(float[] strengths)
        {
            if (strengths == null || strengths.Length == 0) return 0;
            int best = 0;
            for (int i = 1; i < strengths.Length; i++)
                if (strengths[i] > strengths[best]) best = i;
            return best;
        }

        public static string FactionName(CourtFaction f)
            => f == CourtFaction.War ? "the War Party (Ahl-e-Saif)"
             : f == CourtFaction.Peace ? "the Peace Party (Ahl-e-Qalam)"
             : f == CourtFaction.Reform ? "the Reformers (Islahi)"
             : "the Orthodox (Ulema)";
    }
}
