using System;
using System.Collections.Generic;

namespace TakhtyaTaboot.Util
{
    // A CLAIM is a house's standing pretension to a place (wiki ch.30). It belongs to the CLAN, never
    // the man — a successor inherits his house's claims — and it is denominated in YEARS OF STANDING:
    //
    //   • Governing a fief accrues claim at AccrualPerYear, capped at MaxClaimYears.
    //   • LOSING the fief does not erase it: the claim freezes and DECAYS at DecayPerYear — half the
    //     accrual rate, so a grudge outlives the holding that made it. Decayed to nothing, the
    //     conquest is legitimate at last.
    //   • A claim can also be MANUFACTURED without ever holding the place: a clan leaves a companion
    //     in a town as its wakil, who cultivates the merchant houses until two-thirds of them stand
    //     at MerchantThreshold. That grants a (weaker) external claim, live for a 2-year window.
    //
    // Claims accrue identically under BOTH tenure laws — the law changes what the claim DOES:
    // under Feudal it awards the conquest to the claimant house; under Mansabdari it makes the
    // sitting holder EXPENSIVE TO ROTATE (RotationInfluenceMultiplier), and past the crown's purse,
    // impossible. A house that has held Golconda for twenty years is beyond the crown's reach.
    //
    // This is the PURE, engine-free core (unit-tested in TheHindostanMod.Tests). ClaimsBehavior owns
    // the ledger and the campaign hooks.
    public static class ClaimMath
    {
        // ── Accrual & decay ──────────────────────────────────────────────────────────
        public const float AccrualPerYear = 1f;    // years of standing gained per year governed
        public const float DecayPerYear   = 0.5f;  // ...and lost per year dispossessed (half as fast)
        public const float MaxClaimYears  = 20f;   // beyond a generation, a claim is simply "ancient"
        public const float Forgotten      = 0.05f; // below this the claim is forgotten entirely

        public const int DaysPerYear = 365;

        // A house's claim deepens for every day it governs — never past the cap.
        public static float Accrue(float current, int daysHeld)
        {
            if (daysHeld <= 0) return Clamp(current);
            return Clamp(current + daysHeld / (float)DaysPerYear * AccrualPerYear);
        }

        // ...and fades for every day it does not. Never below zero.
        public static float Decay(float current, int daysLost)
        {
            if (daysLost <= 0) return Clamp(current);
            float v = current - daysLost / (float)DaysPerYear * DecayPerYear;
            return v < 0f ? 0f : v;
        }

        // A claim so faint that nobody would ride to war for it.
        public static bool IsForgotten(float claim) => claim < Forgotten;

        // How long (in days) a claim of this size takes to fade to nothing once the fief is lost.
        // The grudge horizon: a 20-year claim outlives its holder by forty years.
        public static int DaysToForget(float claim)
            => claim <= 0f ? 0 : (int)Math.Ceiling(claim / DecayPerYear * DaysPerYear);

        private static float Clamp(float v)
            => v < 0f ? 0f : v > MaxClaimYears ? MaxClaimYears : v;

        // ── Seeding at 1707 ──────────────────────────────────────────────────────────
        // No clan has tenure history at world-gen, so the ledger is seeded: every clan holding a
        // settlement gets a claim on it drawn from a NORMAL distribution over 0..10 years. Historical
        // holders therefore begin with real, varied pretensions and the map has grudges to act on from
        // the first campaign year — rather than a flat zero for a decade.
        public const float SeedMeanYears  = 5f;
        public const float SeedStdDevYears = 2.5f;
        public const float SeedMinYears   = 0f;
        public const float SeedMaxYears   = 10f;

        // Takes a STANDARD normal sample (mean 0, σ 1) so the draw is deterministic and testable;
        // the behaviour feeds it StandardNormal(MBRandom, MBRandom).
        public static float SeedYears(float standardNormalSample)
        {
            float years = SeedMeanYears + standardNormalSample * SeedStdDevYears;
            return years < SeedMinYears ? SeedMinYears : years > SeedMaxYears ? SeedMaxYears : years;
        }

        // Box-Muller: two uniform (0,1) rolls -> one standard normal sample. Guarded against u1 == 0
        // (log(0) is -inf), which MBRandom.RandomFloat can genuinely return.
        public static float StandardNormal(float u1, float u2)
        {
            if (u1 <= 0f) u1 = 1e-6f;
            if (u1 > 1f) u1 = 1f;
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        // ── Mansabdari: the price of moving an entrenched house ──────────────────────
        // The deeper the sitting holder's claim on the fief he occupies, the more influence the crown
        // must spend to rotate him on. At the cap the price is MaxRotationSurcharge times the base.
        public const float MaxRotationSurcharge = 3f;  // a 20-year house costs 3x to shift

        public static float RotationInfluenceMultiplier(float holderClaim)
        {
            float c = holderClaim < 0f ? 0f : holderClaim > MaxClaimYears ? MaxClaimYears : holderClaim;
            return 1f + (MaxRotationSurcharge - 1f) * (c / MaxClaimYears);
        }

        // The influence the crown must find to rotate this holder.
        public static int RotationInfluenceCost(int baseCost, float holderClaim)
        {
            if (baseCost <= 0) return 0;
            return (int)Math.Ceiling(baseCost * RotationInfluenceMultiplier(holderClaim));
        }

        // If the ruling house cannot pay, the order cannot be issued at all — the holder sits where he
        // is, and the crown's writ stops at his gate. (The sovereign is always SHOWN the price and
        // chooses whether it is worth paying: rotation is a judgement, not an automatic tick.)
        public static bool CanAffordRotation(float rulingClanInfluence, int cost)
            => rulingClanInfluence >= cost;

        // ── The wakil: manufacturing an external claim ───────────────────────────────
        // A companion left in a town cultivates its merchant houses. His pace is his CHARM (a skill,
        // 0..~300) and his INTELLIGENCE (an attribute, 0..10) — a dull, graceless agent still makes
        // slow progress; a brilliant courtier turns a town in a season.
        public const float AgentBaseWeeklyGain = 1f;
        public const float AgentCharmWeight    = 2f;   // full charm doubles the base again
        public const float AgentIntWeight      = 2f;   // ...and so does a keen mind
        public const float AgentMaxWeeklyGain  = 5f;

        public static float AgentWeeklyRelationGain(int charmSkill, int intelligenceAttribute)
        {
            float charm = Norm(charmSkill, 300f);
            float wit   = Norm(intelligenceAttribute, 10f);
            float gain  = AgentBaseWeeklyGain + AgentCharmWeight * charm + AgentIntWeight * wit;
            return gain > AgentMaxWeeklyGain ? AgentMaxWeeklyGain : gain;
        }

        private static float Norm(int value, float max)
        {
            if (value <= 0) return 0f;
            float v = value / max;
            return v > 1f ? 1f : v;
        }

        // The town is turned when two-thirds of its merchants stand at +40 to the claimant.
        public const int   MerchantThreshold        = 40;
        public const float MerchantFractionRequired = 2f / 3f;

        public static bool ClaimEarned(int merchantsAtThreshold, int totalMerchants)
        {
            if (totalMerchants <= 0) return false;
            if (merchantsAtThreshold < 0) merchantsAtThreshold = 0;
            return merchantsAtThreshold / (float)totalMerchants >= MerchantFractionRequired;
        }

        // How many of a town's merchants must be won over. Ceiling: 2 of 2, 2 of 3, 3 of 4, 4 of 5.
        public static int MerchantsNeeded(int totalMerchants)
            => totalMerchants <= 0 ? 0 : (int)Math.Ceiling(totalMerchants * MerchantFractionRequired);

        // A manufactured claim is real but shallow — it is worth a few years' standing, never the
        // twenty a lifetime of governance earns.
        public const float ExternalClaimYears = 3f;

        // ...and it is PERISHABLE: act on it within two years — by your own war, or by petitioning the
        // crown to take it up — or it lapses to nothing. The window is the whole point: it forces the
        // claim to be USED rather than hoarded.
        public const int ExternalClaimWindowDays = 730;

        public static bool ExternalClaimLive(int grantedDay, int today)
            => today >= grantedDay && today - grantedDay < ExternalClaimWindowDays;

        public static int ExternalClaimDaysLeft(int grantedDay, int today)
        {
            int left = ExternalClaimWindowDays - (today - grantedDay);
            return left < 0 ? 0 : left;
        }

        // ── Comparing claims ─────────────────────────────────────────────────────────
        // Which house has the better pretension. Ties break on nothing — the caller decides (in
        // practice: the house that held it most recently, then the stronger house).
        public static bool Outranks(float a, float b) => a > b;

        // The strongest claim in a set, or 0 if none. Used to pick the casus belli a realm goes to
        // war on, and the target a conquest war is declared for.
        public static float Strongest(IEnumerable<float> claims)
        {
            float best = 0f;
            if (claims == null) return best;
            foreach (float c in claims) if (c > best) best = c;
            return best;
        }

        // A claim worth a war. Below this a realm has a pretension, not a cause — enough to argue at
        // court, not enough to march on.
        public const float ActionableClaim = 2f;

        public static bool WorthAWar(float claim) => claim >= ActionableClaim;

        // ── Description (the tooltip / farmaan voice) ────────────────────────────────
        public static string Describe(float claim)
            => IsForgotten(claim) ? "forgotten"
             : claim < ActionableClaim ? "a whisper of a claim"
             : claim < 5f  ? "a slender claim"
             : claim < 10f ? "a firm claim"
             : claim < 15f ? "a strong claim"
             : "an ancient claim";
    }
}
