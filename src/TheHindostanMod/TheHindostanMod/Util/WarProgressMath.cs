using System;
using System.Collections.Generic;
using System.Linq;

namespace TakhtyaTaboot.Util
{
    // WAR PROGRESS, AND WHY (wiki ch.30 §3a).
    //
    // THE RULE: every number the player sees must be able to explain itself. A war carries a progress
    // bar toward its aim, and hovering it lists exactly what moved it. If a contribution cannot be named
    // in the tooltip, it must not silently move the bar.
    //
    // That is an architectural constraint, not a UI nicety. The old scorer did `_score[id] += delta` —
    // an anonymous float that a tooltip cannot be reverse-engineered out of. So scoring is an ITEMIZED
    // LEDGER of Contributions (day / kind / delta / subject). The bar is derived from the ledger; the
    // ledger is never derived from the bar.
    //
    // And the bar means a DIFFERENT thing in each war, because the aim is the win condition:
    //   • ProvincialConquest — targets held / targets sought. It is won when the named fiefs are taken.
    //   • Tribute / Revenge  — war score toward decisive. Won when the foe is beaten enough to dictate.
    //   • TotalSubjugation   — the BETTER of two roads: the collapse gate (WarAimMath.SubjugationAllowed)
    //                          or every last fief taken. The player sees both, and how near each is.
    //   • Defence            — denial: 100% minus the aggressor's progress toward HIS aim. A defender
    //                          wins by holding, and may dictate the truce when the aggressor is spent.
    //
    // This same model resolves the AI's wars, so the bar can never disagree with the outcome.
    // PURE and engine-free (unit-tested); WarfareBehavior gathers the inputs and the UI renders them.
    public static class WarProgressMath
    {
        public const float DecisiveScore = 30f;   // score at which a Tribute/Revenge war may be dictated
        public const int   MaxLedgerRows = 200;   // beyond this, old rows fold into per-kind running totals

        // Everything that may move a war's score. If it is not on this list, it may not move the bar.
        public enum Kind
        {
            BattleWon, BattleLost, SiegeTaken, SiegeLost,
            FiefTaken, FiefLost, VillageRaided, KingCaptured,
            TargetTaken, TargetRetaken, TributeWithheld, AffrontAvenged, TimeGrind
        }

        // One thing that happened, and what it was worth. `Subject` names the town, the lord, the battle —
        // whatever the tooltip must be able to point at.
        public struct Contribution
        {
            public int Day;
            public Kind Kind;
            public float Delta;
            public string Subject;

            public Contribution(int day, Kind kind, float delta, string subject = null)
            { Day = day; Kind = kind; Delta = delta; Subject = subject; }
        }

        // One line of the hover breakdown.
        public struct Line
        {
            public string Label;
            public string Value;
            public string Detail;   // may be null

            public Line(string label, string value, string detail = null)
            { Label = label; Value = value; Detail = detail; }
        }

        // The engine-free snapshot of a war. WarfareBehavior fills this; everything below is pure.
        public struct Snapshot
        {
            public WarAim Aim;
            public bool   IsDefender;        // we are defending against the aggressor's aim

            public float  Score;             // net score from the ledger

            // ProvincialConquest
            public int TargetsTotal;
            public int TargetsHeld;

            // TotalSubjugation
            public int   EnemyFiefsTotal;
            public int   EnemyFiefsTaken;
            public bool  EnemyKingFallen;    // captured or dead
            public float EnemyKingLegitimacy;
            public float LoyalLordFraction;  // fraction of their lords at >= +10 with us
        }

        // ── The default worth of each kind ───────────────────────────────────────────
        // Taking a fief the war was DECLARED FOR is worth far more than taking a random one: the aim is
        // the point, and the score should say so.
        public static float DefaultDelta(Kind k)
        {
            switch (k)
            {
                case Kind.BattleWon:      return 10f;
                case Kind.BattleLost:     return -10f;
                case Kind.SiegeTaken:     return 18f;
                case Kind.SiegeLost:      return -18f;
                case Kind.FiefTaken:      return 20f;
                case Kind.FiefLost:       return -20f;
                case Kind.VillageRaided:  return 3f;
                case Kind.KingCaptured:   return 25f;
                case Kind.TargetTaken:    return 30f;
                case Kind.TargetRetaken:  return -30f;
                case Kind.TributeWithheld: return 5f;
                case Kind.AffrontAvenged: return 15f;
                case Kind.TimeGrind:      return -0.1f;   // a war that drags favours nobody
                default:                  return 0f;
            }
        }

        // The voice of each kind in the tooltip. Plural, because the breakdown rolls them up.
        public static string Describe(Kind k)
        {
            switch (k)
            {
                case Kind.BattleWon:      return "Battles won";
                case Kind.BattleLost:     return "Battles lost";
                case Kind.SiegeTaken:     return "Walls stormed";
                case Kind.SiegeLost:      return "Walls lost";
                case Kind.FiefTaken:      return "Fiefs taken";
                case Kind.FiefLost:       return "Fiefs lost";
                case Kind.VillageRaided:  return "Villages harried";
                case Kind.KingCaptured:   return "Their king broken";
                case Kind.TargetTaken:    return "War aims secured";
                case Kind.TargetRetaken:  return "War aims lost back";
                case Kind.TributeWithheld: return "Tribute withheld";
                case Kind.AffrontAvenged: return "Affronts avenged";
                case Kind.TimeGrind:      return "The grind of time";
                default:                  return "Other";
            }
        }

        // ── The score ────────────────────────────────────────────────────────────────
        public static float Score(IEnumerable<Contribution> ledger)
            => ledger == null ? 0f : ledger.Sum(c => c.Delta);

        // Roll the ledger up by kind for display: "Battles won x14 -> +140", not fourteen lines.
        // Ordered by absolute weight, so the tooltip leads with what actually decided the war.
        public static List<(Kind kind, int count, float total)> Rollup(IEnumerable<Contribution> ledger)
        {
            if (ledger == null) return new List<(Kind, int, float)>();
            return ledger.GroupBy(c => c.Kind)
                         .Select(g => (g.Key, g.Count(), g.Sum(c => c.Delta)))
                         .OrderByDescending(t => Math.Abs(t.Item3))
                         .ToList();
        }

        // The ledger must stay bounded: a decade-long war would otherwise carry thousands of rows into
        // the save. Fold everything older than the most recent MaxLedgerRows into one summary row per
        // kind (dated at the oldest day folded), so the totals stay exact and only the fine grain is lost.
        public static List<Contribution> Compact(IEnumerable<Contribution> ledger)
        {
            var all = ledger?.ToList() ?? new List<Contribution>();
            if (all.Count <= MaxLedgerRows) return all;

            var ordered = all.OrderBy(c => c.Day).ToList();
            int foldCount = ordered.Count - MaxLedgerRows;
            var old = ordered.Take(foldCount).ToList();
            var kept = ordered.Skip(foldCount).ToList();

            var folded = old.GroupBy(c => c.Kind)
                .Select(g => new Contribution(g.Min(c => c.Day), g.Key, g.Sum(c => c.Delta), null))
                .ToList();

            folded.AddRange(kept);
            return folded;
        }

        // ── Progress ─────────────────────────────────────────────────────────────────
        // The aggressor's progress toward his aim, 0..100.
        public static float AggressorPercent(Snapshot s)
        {
            switch (s.Aim)
            {
                case WarAim.ProvincialConquest:
                    if (s.TargetsTotal <= 0) return 0f;
                    return Clamp100(100f * s.TargetsHeld / s.TargetsTotal);

                case WarAim.TotalSubjugation:
                    // The better of the two roads — the player should see which is nearer.
                    return Clamp100(Math.Max(CollapseReadiness(s), FiefsTakenPercent(s)));

                case WarAim.Succession:
                    return 0f;   // throne wars are binary and settle by their own deadline

                default:         // Tribute, Revenge — beaten enough to dictate terms
                    return Clamp100(100f * s.Score / DecisiveScore);
            }
        }

        // What the player actually sees on his bar: a defender's bar fills as he DENIES.
        public static float Percent(Snapshot s)
        {
            float aggressor = AggressorPercent(s);
            if (!s.IsDefender) return aggressor;
            if (s.Aim == WarAim.Succession) return 0f;
            return Clamp100(100f - aggressor);
        }

        // Is the aim achieved? (For a defender: has the aggressor's aim been made unreachable — his
        // gains all clawed back, his score broken.)
        public static bool Complete(Snapshot s)
        {
            if (s.Aim == WarAim.Succession) return false;

            if (s.IsDefender)
                return AggressorPercent(s) <= 0f && s.Score >= DecisiveScore;

            switch (s.Aim)
            {
                case WarAim.ProvincialConquest:
                    return s.TargetsTotal > 0 && s.TargetsHeld >= s.TargetsTotal;

                case WarAim.TotalSubjugation:
                    // Either their throne has collapsed (the default gate), or every last stone has fallen.
                    return WarAimMath.SubjugationAllowed(s.EnemyKingFallen, s.EnemyKingLegitimacy, s.LoyalLordFraction)
                        || (s.EnemyFiefsTotal > 0 && s.EnemyFiefsTaken >= s.EnemyFiefsTotal);

                default:
                    return s.Score >= DecisiveScore;
            }
        }

        // How near the collapse gate stands: partial credit for each of the three conditions, so the
        // player can see he is two-thirds of the way to being ALLOWED to demand submission.
        public static float CollapseReadiness(Snapshot s)
        {
            int met = 0;
            if (s.EnemyKingFallen) met++;
            if (s.EnemyKingLegitimacy < WarAimMath.SubjugationLegitimacyCeiling) met++;
            if (s.LoyalLordFraction >= WarAimMath.SubjugationLoyalLordFraction) met++;
            return 100f * met / 3f;
        }

        public static float FiefsTakenPercent(Snapshot s)
            => s.EnemyFiefsTotal <= 0 ? 0f : Clamp100(100f * s.EnemyFiefsTaken / s.EnemyFiefsTotal);

        // ── The breakdown (this IS the tooltip) ──────────────────────────────────────
        // Pure, so the tooltip text itself is unit-tested — the only way "the bar explains itself" stays
        // true after six more waves of tuning.
        public static List<Line> Breakdown(Snapshot s, IEnumerable<Contribution> ledger,
                                           IEnumerable<(string name, bool held)> targets = null)
        {
            var lines = new List<Line>();

            if (s.IsDefender)
            {
                lines.Add(new Line("You are defending", Headline(s),
                    $"Their aim: {AimName(s.Aim)}. You win by denying it — hold what is yours, and break them."));
                lines.Add(new Line("Their progress", $"{AggressorPercent(s):0}%"));
            }

            switch (s.Aim)
            {
                case WarAim.ProvincialConquest:
                    lines.Add(new Line("War aim", $"Conquest — {s.TargetsHeld} of {s.TargetsTotal} taken"));
                    if (targets != null)
                        foreach (var (name, held) in targets)
                            lines.Add(new Line("   " + name, held ? "TAKEN" : "still theirs"));
                    break;

                case WarAim.TotalSubjugation:
                {
                    lines.Add(new Line("War aim", "Total subjugation — absorb the realm entire"));
                    // BOTH roads, with live values, so no option is ever mysteriously greyed out.
                    lines.Add(new Line("   Their king", s.EnemyKingFallen ? "FALLEN (captured or dead)" : "still at liberty",
                        "The gate needs their king captured or killed."));
                    lines.Add(new Line("   Their legitimacy", $"{s.EnemyKingLegitimacy:0}",
                        $"The gate needs it below {WarAimMath.SubjugationLegitimacyCeiling:0}."));
                    lines.Add(new Line("   Their lords with you", $"{s.LoyalLordFraction * 100f:0}%",
                        $"The gate needs at least {WarAimMath.SubjugationLoyalLordFraction * 100f:0}% at +{WarAimMath.SubjugationLoyalRelation} or better."));
                    lines.Add(new Line("   — or take everything", $"{s.EnemyFiefsTaken} of {s.EnemyFiefsTotal} fiefs",
                        "If their throne will not collapse, every last fief must fall."));
                    break;
                }

                case WarAim.Succession:
                    lines.Add(new Line("War aim", "The throne itself",
                        "A war for the crown is won or lost — never settled by treaty."));
                    return lines;

                default:
                    lines.Add(new Line("War aim", AimName(s.Aim) + $" — score {s.Score:0} of {DecisiveScore:0}"));
                    break;
            }

            // What actually moved the number, heaviest first.
            foreach (var (kind, count, total) in Rollup(ledger))
            {
                if (Math.Abs(total) < 0.5f) continue;
                lines.Add(new Line(Describe(kind), $"{(total > 0 ? "+" : "")}{total:0}",
                    count > 1 ? $"{count} times" : null));
            }

            return lines;
        }

        public static string Headline(Snapshot s)
        {
            float p = Percent(s);
            if (Complete(s)) return s.IsDefender ? "their aim is broken" : "the aim is achieved";
            return p >= 80f ? "all but won"
                 : p >= 55f ? "the advantage is ours"
                 : p >= 30f ? "ground is being made"
                 : p > 0f   ? "barely begun"
                 : "nothing gained";
        }

        public static string AimName(WarAim aim)
        {
            switch (aim)
            {
                case WarAim.ProvincialConquest: return "Conquest";
                case WarAim.Tribute:            return "Tribute";
                case WarAim.Revenge:            return "Chastisement";
                case WarAim.TotalSubjugation:   return "Total subjugation";
                case WarAim.Succession:         return "The throne";
                default:                        return "War";
            }
        }

        private static float Clamp100(float v) => v < 0f ? 0f : v > 100f ? 100f : v;
    }
}
