using System;

namespace TakhtyaTaboot.Util
{
    // Pure math for the village-fief layer (taxes, construction, threat, AI priorities).
    // NO TaleWorlds types: this file is <Compile>-linked into TheHindostanMod.Tests and
    // unit-tested there (VillageFiefMathTests), like RusukhMath and the other math libs.
    public static class VillageFiefMath
    {
        // ── Zamindar-as-governor modifiers ───────────────────────────────────────────
        // A capable zamindar makes the fief work: Engineering speeds construction,
        // Steward raises the yield, Charm+Steward keep the district quiet.

        public static float BuildSpeedFactor(int engineering)
            => Clamp(1f + Math.Max(0, engineering) / 300f, 1f, 1.5f);

        public static float TaxYieldFactor(int steward)
            => Clamp(1f + Math.Max(0, steward) / 400f, 1f, 1.4f);

        public static float ThreatDecayBonus(int charm, int steward)
            => Clamp(Math.Max(0, charm) / 150f + Math.Max(0, steward) / 150f, 0f, 2f);

        // ── Taxes ────────────────────────────────────────────────────────────────────
        // Daily dinars into the village coffer. Order ~1-4/day for a healthy village at
        // default tuning (hearth 400-900), so the coffer supplements rather than dwarfs
        // the existing economy. High threat chokes collection down to half.
        public static float DailyTax(float hearth, float taxBonusPct, float threat,
                                     float authorityRate, int zamindarSteward, float taxPerHearth)
        {
            if (hearth <= 0f || taxPerHearth <= 0f) return 0f;
            float threatFactor = 1f - 0.5f * Clamp(threat, 0f, 100f) / 100f;
            float raw = hearth * taxPerHearth
                        * (1f + Math.Max(0f, taxBonusPct) / 100f)
                        * TaxYieldFactor(zamindarSteward)
                        * Clamp(authorityRate, 0f, 1f)
                        * threatFactor;
            return Math.Max(0f, raw);
        }

        // What the zamindar owes UP each season for one village (the coffer is what the
        // village yields DOWN — both scale with hearth so a maintained village nets
        // positive). Replaces the old flat 120/village: hearth 1000 -> the same 120.
        public static int SeasonalTributeForVillage(float hearth)
            => (int)Clamp(40f + Math.Max(0f, hearth) * 0.08f, 40f, 200f);

        // ── Threat ───────────────────────────────────────────────────────────────────
        // One day of bandit-threat evolution. Mirrors the long-standing behavior rules:
        // relief crushes it fast; otherwise it grows, war feeds it, defences and the
        // lord's presence suppress it, and watch-works scale the remainder down.
        public static float ThreatStep(float current, bool reliefActive, bool atWar,
                                       float flatReduction, float watchMultiplier,
                                       float defence, bool lordPresent)
        {
            if (reliefActive) return Math.Max(0f, current - 8f);
            float t = current + 1f;                 // bandits always return
            if (atWar) t += 2f;
            t -= Math.Max(0f, flatReduction);
            if (lordPresent) t -= 5f;
            t -= Math.Max(0f, defence);
            t *= Clamp(watchMultiplier, 0.1f, 1f);
            return Clamp(t, 0f, 100f);
        }

        // How hard threat throttles hearth growth: full bleed above 90, frozen above 80,
        // halved above 60.
        public static float HearthGrowthFactor(float threat)
            => threat > 90f ? -1f : threat > 80f ? 0f : threat > 60f ? 0.5f : 1f;

        // ── AI development ───────────────────────────────────────────────────────────
        // What an AI zamindar should build next: 0 = defence works, 1 = food/hearth
        // works, 2 = economy/faith works.
        public const int PriorityDefence = 0;
        public const int PriorityFood = 1;
        public const int PriorityEconomy = 2;

        public static int AiPriorityCategory(float threat, float hearth)
            => threat >= 50f ? PriorityDefence
             : hearth < 300f ? PriorityFood
             : PriorityEconomy;

        // AI weekly coffer split: this share goes to the zamindar's purse, the rest
        // stays in the coffer as the build budget.
        public const float AiCofferDrawShare = 0.7f;

        // Gold an AI zamindar keeps back for himself before funding works.
        public static int AiGoldFloor(bool isLord) => isLord ? 5000 : 2000;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
