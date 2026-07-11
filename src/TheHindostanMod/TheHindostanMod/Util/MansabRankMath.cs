using System;

namespace TakhtyaTaboot.Util
{
    // The dual mansab rank, PURE and unit-tested. A mansabdar holds TWO ranks, as in the historical
    // system: his ZAT (personal rank / status — the mansab number) fixes his standing, gates the
    // fiefs he may hold, and sets the stipend the crown pays him; his SAWAR (cavalry obligation) is
    // the contingent he must keep mustered. The numbers were always tracked (Ranks[i].Mansab is the
    // zat, Ranks[i].SawarRequired the sawar) — this makes the split explicit and re-bases the stipend
    // on status (zat) rather than headcount. MansabdariBehavior owns the rank table and engine side.
    public static class MansabRankMath
    {
        // The stipend a mansabdar draws every 30 days is pay for his ZAT — his rank and standing —
        // not for how many men he happens to field. A high-zat noble is maintained richly even
        // between musters.
        public static int StipendForZat(int zat, float perZat)
            => (int)Math.Round(Math.Max(0, zat) * Math.Max(0f, perZat));

        // Compact "zat X / sawar Y" label for the dual rank.
        public static string DualRankLabel(int zat, int sawar) => $"zat {zat} / sawar {sawar}";
    }
}
