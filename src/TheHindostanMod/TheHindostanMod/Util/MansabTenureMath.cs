using System;

namespace TakhtyaTaboot.Util
{
    // Mansabdari tenure — the imperial edict that makes a mansab a ROTATIONAL office, not a
    // hereditary fief. Each appointment is held for a fixed term; once the term runs out the
    // crown may rotate the holder on to break the local roots he has sunk (his Rusukh). An
    // entrenched holder costs more influence and gold to dislodge, and may DEFY the order
    // outright — the defiance odds come from RusukhMath.DefianceChance (roots vs crown grip).
    //
    // This is the PURE, engine-free core (unit-tested in TheHindostanMod.Tests). The thin
    // MansabdariTenureBehavior owns the per-holder term clock and the engine actions; every
    // formula and guard lives here so it can be proven in isolation. Composes with RusukhMath.
    public static class MansabTenureMath
    {
        // ── Term of office ───────────────────────────────────────────────────────────
        // A mansab is held for a term; Bannerlord years are 360 days, so this is ~3 years.
        public const int DefaultTermDays = 1080;

        // Has the appointment run its full term?
        public static bool TermExpired(int daysHeld, int termDays)
            => termDays > 0 && daysHeld >= termDays;

        // Days left before the holder is due for rotation (0 once the term is up).
        public static int DaysUntilRotation(int daysHeld, int termDays)
            => Math.Max(0, termDays - daysHeld);

        // How far PAST the term the holder has clung on, as a fraction of the term (0 while
        // still in term). Lets the crown weigh how overdue — and how entrenched — a post is.
        public static float OverdueFraction(int daysHeld, int termDays)
        {
            if (termDays <= 0) return 0f;
            float over = (daysHeld - termDays) / (float)termDays;
            return over < 0f ? 0f : over;
        }

        // ── Edict cost engine (switching a realm Feudal -> Mansabdari) ───────────────
        // The central "influence their decisions" price (design doc §A / cross-cutting cost engine):
        // converting a realm to rotational tenure must BUY OFF every noble who loses his hereditary
        // hold, and is gated/discounted by the crown's legitimacy.

        // Only a secure throne may rewrite tenure at all.
        public static bool MeetsLegitimacyFloor(float legitimacy, float floor) => legitimacy >= floor;

        // One noble's opposition to losing hereditary tenure: scales with the power he wields, the
        // stake he holds (his fiefs), how little he likes the crown (1 - relationFactor, 0..1), and
        // how deep his local roots run (Rusukh). A loyal OR rootless client costs nothing to convert;
        // a powerful, entrenched, resentful magnate is dear.
        public static float OppositionWeight(float power, int heldFiefs, float relationFactor, float rusukh)
        {
            float stake = Math.Max(0, heldFiefs);
            float dislike = 1f - Clamp01(relationFactor);
            float roots = Clamp01(rusukh / RusukhMath.Max);
            return Math.Max(0f, power) * stake * dislike * roots;
        }

        // Gold to buy off the realm: a base plus the summed opposition, discounted up to half by the
        // crown's legitimacy (a secure throne bends the nobles more cheaply).
        public static int EdictGoldCost(float baseGold, float totalOpposition, float legitimacy)
        {
            float discount = 1f - 0.5f * Clamp01(legitimacy / 100f);
            float raw = (baseGold + Math.Max(0f, totalOpposition)) * discount;
            return (int)Math.Round(Math.Max(0f, raw));
        }

        // A flat influence outlay plus a little per noble the court must persuade.
        public static int EdictInfluenceCost(float baseInfluence, int affectedNobles)
            => (int)Math.Round(Math.Max(0f, baseInfluence) + Math.Max(0, affectedNobles));

        // Opposition that can't simply be bought: a noble whose roots out-reach the crown's grip will
        // RESIST the reform outright (same roots-vs-grip test that governs rotation defiance), pushing
        // him toward an accession challenge or secession rather than compliance.
        public static bool WillResistEdict(float rusukh, float crownAuthority, float crownLegitimacy, float threshold)
            => RusukhMath.DefianceChance(rusukh, crownAuthority, crownLegitimacy) >= threshold;

        // ── Cost of enforcing a single rotation ──────────────────────────────────────
        // A greater office (higher mansab rank) costs more to reshuffle, and a holder with deep
        // roots (high Rusukh) costs more to dislodge. Both costs scale the same way so the model
        // is easy to reason about: baseCost * rank * (1 + roots).
        public static int EnforcementInfluenceCost(int rankIndex, float rusukh, float baseCost)
            => ScaleCost(rankIndex, rusukh, baseCost);

        public static int EnforcementGoldCost(int rankIndex, float rusukh, float baseGold)
            => ScaleCost(rankIndex, rusukh, baseGold);

        private static int ScaleCost(int rankIndex, float rusukh, float baseCost)
        {
            int rank = Math.Max(1, rankIndex);
            float roots = Clamp01(rusukh / RusukhMath.Max);
            return (int)Math.Round(baseCost * rank * (1f + roots));
        }

        // ── Eligibility guards ───────────────────────────────────────────────────────
        // The crown may enact a rotation only against a vassal whose term has run out and who
        // actually holds the post. The sovereign never rotates himself out of his own seat.
        public static bool CanEnactRotation(bool termExpired, bool holderIsSovereign, bool holderHoldsFief)
            => termExpired && !holderIsSovereign && holderHoldsFief;

        // A clan may RECEIVE a rotated post if it serves the realm, is alive, holds a mansab
        // high enough for the seat, and is not the outgoing holder (the point is to move it on).
        public static bool IsEligibleSuccessor(int candidateRankIndex, int requiredRankIndex,
                                               bool candidateInRealm, bool candidateAlive, bool isOutgoingHolder)
            => candidateInRealm && candidateAlive && !isOutgoingHolder
               && candidateRankIndex >= requiredRankIndex;

        // ── Resolving the order against defiance ─────────────────────────────────────
        public enum RotationOutcome { Complied, Defied }

        // Resolve a rotation farmaan against the holder's defiance odds (from
        // RusukhMath.DefianceChance). `roll` is a 0..1 sample; below the chance the holder defies.
        public static RotationOutcome Resolve(float defianceChance, float roll)
            => roll < Clamp01(defianceChance) ? RotationOutcome.Defied : RotationOutcome.Complied;

        // When a holder defies the crown the throne is publicly humiliated; imperial authority
        // suffers in proportion to the prestige of the office that was defied.
        public static float AuthorityPenaltyForDefiance(int rankIndex, float perRank)
            => Math.Max(1, rankIndex) * perRank;

        // ── The defiance risk ladder (design doc §B) ─────────────────────────────────
        // The full outcome of a rotation/transfer order. `roll` (0..1) decides WHETHER the holder
        // resists (he resists when roll < his defiance chance); the STRENGTH of his position (the
        // chance itself) decides how far the standoff escalates:
        //   Complied  — he obeys; the office rotates peacefully.
        //   Reprimand — token protest: the order stands but costs the crown standing.
        //   Dismissal — he won't move; the crown is strong enough to strip him outright, at a cost.
        //   Traitor   — his roots run so deep that forcing him means open rebellion (secession + war).
        public enum RotationResult { Complied, Reprimand, Dismissal, Traitor }

        public static RotationResult ResolveRotationOrder(float defianceChance, float roll)
        {
            float c = Clamp01(defianceChance);
            if (roll >= c) return RotationResult.Complied;   // obeys (always, when c == 0)
            if (c < 0.34f) return RotationResult.Reprimand;  // weak roots: grumbles, complies
            if (c < 0.67f) return RotationResult.Dismissal;  // moderate: forced out at a price
            return RotationResult.Traitor;                   // deep roots: stripping him is war
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
