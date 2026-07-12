using System;
using System.Collections.Generic;
using System.Linq;

namespace TakhtyaTaboot.Util
{
    // Succession law (design doc §C) — a per-kingdom constitution that selects BOTH the candidate pool
    // and the resolution rule the existing war-of-princes engine (SuccessionBehavior) then runs:
    //
    //   Undeclared        — no formal law: the open contest (takht-ya-taboot) the engine runs today.
    //   MalePrimogeniture — the eldest son of the dynasty; a clean line yields a near-uncontested heir.
    //   PrincelyElection  — the dynasty's princes stand; the great lords vote (weighted).
    //   MagnateElection   — any great house may be raised; an open, dynasty-favoured magnate vote.
    //   AppointedHeir     — the ruler names a Wali Ahd (+ optional Naib Wali Ahd fallback) who accedes.
    //
    // This is the PURE, engine-free core (unit-tested in TheHindostanMod.Tests). The thin
    // SuccessionLawBehavior owns the per-kingdom law/heir state and the engine actions; every rule and
    // curve lives here so it can be proven in isolation. No engine types leak in.
    public enum SuccessionLaw { Undeclared, MalePrimogeniture, PrincelyElection, MagnateElection, AppointedHeir }

    // Whose pride a law change wounds (relation/authority consequence applied by the behavior).
    public enum AngeredEstate { None, Princes, Magnates }

    public static class SuccessionLawMath
    {
        public const int DefaultMaxPool = 3;

        // A claimant the engine has gathered, reduced to the plain facts the rules need. Category mirrors
        // SuccessionBehavior.FindClaimants: 0 incumbent, 1 son, 2 brother, 3 nephew, 4 clan-leader, 5 magnate.
        public struct LawCandidate
        {
            public string Id;
            public int Category;
            public bool IsDynasty;   // of the ruling house
            public int SonRank;      // 0 = eldest son; -1 if not a son
            public float Power;      // ClanPower weight
            public int RankIndex;    // mansab rank
            public bool IsWali;      // the named Wali Ahd
            public bool IsNaib;      // the named Naib Wali Ahd
        }

        // ── Candidate pool per law ──────────────────────────────────────────────────────
        // The engine gathers the universe in its usual priority order (dynasty agnatic first, then
        // magnates) and tags each entry; this returns the ordered ids the crisis should actually run with.
        public static List<string> OrderedPool(SuccessionLaw law, IReadOnlyList<LawCandidate> universe, int maxPool = DefaultMaxPool)
        {
            if (universe == null || universe.Count == 0) return new List<string>();
            var dynasty = universe.Where(c => c.IsDynasty).ToList();
            var magnates = universe.Where(c => !c.IsDynasty).OrderByDescending(c => c.Power).ToList();
            var pool = new List<LawCandidate>();

            switch (law)
            {
                case SuccessionLaw.MalePrimogeniture:
                    // A clean line is near-uncontested: at most two stand, dynasty first; a single outside
                    // rival is admitted only when the dynasty cannot field a second contender of its own.
                    pool.AddRange(dynasty.Take(2));
                    if (pool.Count < 2 && magnates.Count > 0) pool.Add(magnates[0]);
                    break;

                case SuccessionLaw.PrincelyElection:
                    // Only princes of the blood stand; the magnates merely vote. Fill to a contest if the
                    // dynasty is too thin to field two.
                    pool.AddRange(dynasty.Where(c => c.Category >= 1 && c.Category <= 4).Take(maxPool));
                    foreach (var m in magnates) { if (pool.Count >= 2) break; pool.Add(m); }
                    break;

                case SuccessionLaw.MagnateElection:
                    // Any great house may be raised, but the dynasty's heir always stands (dynasty-favoured).
                    if (dynasty.Count > 0) pool.Add(dynasty[0]);
                    foreach (var m in magnates) { if (pool.Count >= maxPool) break; pool.Add(m); }
                    break;

                case SuccessionLaw.AppointedHeir:
                    // The named heir first, his deputy next, then the agnatic line as a fallback.
                    var wali = universe.Where(c => c.IsWali);
                    var naib = universe.Where(c => c.IsNaib && !c.IsWali);
                    pool.AddRange(wali);
                    pool.AddRange(naib);
                    foreach (var c in universe) { if (pool.Count >= maxPool) break; if (!c.IsWali && !c.IsNaib) pool.Add(c); }
                    break;

                default: // Undeclared — the engine's existing open contest, untouched.
                    pool.AddRange(universe.Take(maxPool));
                    break;
            }

            return pool.Select(c => c.Id).Where(id => !string.IsNullOrEmpty(id)).Distinct().Take(maxPool).ToList();
        }

        // ── Starting support (law layer, added on top of the engine's category base) ────
        // A named/lawful heir starts with a commanding lead; a magnate election tilts toward the dynasty.
        public static float LawSupportBonus(SuccessionLaw law, LawCandidate c, float heirBoost, float dynastyFavour)
        {
            switch (law)
            {
                case SuccessionLaw.AppointedHeir:
                    if (c.IsWali) return heirBoost;
                    if (c.IsNaib) return heirBoost * 0.5f;
                    return 0f;
                case SuccessionLaw.MalePrimogeniture:
                    return (c.Category == 1 && c.SonRank == 0) ? heirBoost : 0f;
                case SuccessionLaw.MagnateElection:
                    return c.IsDynasty ? dynastyFavour : 0f;
                default:
                    return 0f;
            }
        }

        // ── Weighted magnate vote (the election laws' resolution rule) ──────────────────
        public struct VoteResult
        {
            public string WinnerId;
            public float WinnerVotes;
            public float RunnerUpVotes;
            public float TotalVotes;
        }

        // An elector's weight at the assembly: his mansab rank and his house's tier both lend a louder voice.
        public static float ElectorWeight(int mansabRank, int clanTier)
            => Math.Max(1, mansabRank + 1) + Math.Max(0, clanTier);

        // How strongly an elector favours a candidate: relation is the spine, shared faith pulls, and the
        // dynasty enjoys a standing premium so the throne usually stays in the family.
        public static float CandidatePreference(int relation, bool sameReligion, bool candidateIsDynasty, float dynastyFavour)
            => relation + (sameReligion ? 15f : 0f) + (candidateIsDynasty ? dynastyFavour : 0f);

        // Tally ballots (each already resolved to a single chosen candidate + the elector's weight).
        public static VoteResult Tally(IEnumerable<(string candidateId, float weight)> ballots)
        {
            var tally = new Dictionary<string, float>();
            float total = 0f;
            if (ballots != null)
                foreach (var (id, w) in ballots)
                {
                    if (string.IsNullOrEmpty(id) || w <= 0f) continue;
                    tally[id] = (tally.TryGetValue(id, out float cur) ? cur : 0f) + w;
                    total += w;
                }

            var ranked = tally.OrderByDescending(kv => kv.Value).ToList();
            return new VoteResult
            {
                WinnerId      = ranked.Count > 0 ? ranked[0].Key : null,
                WinnerVotes   = ranked.Count > 0 ? ranked[0].Value : 0f,
                RunnerUpVotes = ranked.Count > 1 ? ranked[1].Value : 0f,
                TotalVotes    = total,
            };
        }

        // A vote settles the throne only if the winner clears the runner-up by the decisive margin; a
        // near-tie throws the realm into civil war (the engine's existing path).
        public static bool IsDecisive(VoteResult r, float marginRatio)
            => r.WinnerId != null && (r.RunnerUpVotes <= 0f || r.WinnerVotes >= r.RunnerUpVotes * Math.Max(1f, marginRatio));

        // ── Law-change edict (gate + price + who it angers) ─────────────────────────────
        public static bool MeetsLegitimacyFloor(float legitimacy, float floor) => legitimacy >= floor;

        // A flat influence outlay plus a little for every house whose expectations the new law overturns,
        // discounted up to half by a secure throne's legitimacy.
        public static int LawChangeInfluenceCost(float baseInfluence, int affectedHouses, float legitimacy)
        {
            float discount = 1f - 0.5f * Clamp01(legitimacy / 100f);
            float raw = (Math.Max(0f, baseInfluence) + Math.Max(0, affectedHouses)) * discount;
            return (int)Math.Round(Math.Max(0f, raw));
        }

        // Princes resent any law that loosens the dynasty's grip (especially an open magnate election);
        // magnates resent losing their vote when the crown turns to appointment or strict primogeniture.
        public static AngeredEstate WhoIsAngered(SuccessionLaw from, SuccessionLaw to)
        {
            if (to == from) return AngeredEstate.None;
            bool toElection   = to == SuccessionLaw.PrincelyElection || to == SuccessionLaw.MagnateElection;
            bool fromElection = from == SuccessionLaw.PrincelyElection || from == SuccessionLaw.MagnateElection;

            if (to == SuccessionLaw.MagnateElection && from != SuccessionLaw.MagnateElection) return AngeredEstate.Princes;
            if (toElection && !fromElection) return AngeredEstate.Princes;   // the heir loses his certainty
            if (!toElection && fromElection) return AngeredEstate.Magnates;  // the lords lose their voice
            return AngeredEstate.None;
        }

        // ── Soft suppression of the war-of-princes ──────────────────────────────────────
        // A formal law with a valid heir LOWERS the odds of a contest but never abolishes it; a collapsed
        // throne is always contested. `roll` is a 0..1 sample — a contest fires when roll < the chance.
        // `minContestChance` (0..1, tunable) is the FLOOR: even a secure law with a valid heir always
        // carries at least this chance of a contested accession on a ruler's death, so death is never a
        // guaranteed-quiet handover. Set it to 0 to allow fully clean accessions.
        public static bool ShouldContest(SuccessionLaw law, bool heirValid, float legitimacy, float authority, float roll, float minContestChance = 0f)
        {
            if (authority < 15f && legitimacy < 40f) return true;   // collapse always invites the vultures
            if (!heirValid) return true;                            // no clean line -> open contest
            if (law == SuccessionLaw.Undeclared) return true;       // no law to settle it -> as today

            // Doubt rises as legitimacy falls below 60; a formal, accepted law dampens it — but never to
            // below the configured floor.
            float baseChance = Clamp01((60f - legitimacy) / 60f);
            float suppression = (law == SuccessionLaw.MalePrimogeniture || law == SuccessionLaw.AppointedHeir) ? 0.35f : 0.6f;
            float chance = Math.Max(Clamp01(minContestChance), baseChance * suppression);
            return roll < chance;
        }

        // ── Buying off a rival claimant (player persuasion) ─────────────────────────────
        // A bribe is never certain: a rival weighs what he is offered against the worth of the claim he
        // is asked to surrender, coloured by how he regards the briber. The offer is valued in gold-
        // equivalents; influence and a gift of men each carry a fixed worth, a fief's worth is supplied by
        // the caller (it depends on the settlement). The "going rate" the UI prefills is (fiefs × 50,000).
        public const float PersuadeInfluenceValue = 2000f;   // 1 influence ≈ this many rupees to a claimant
        public const float PersuadeTroopValue     = 2000f;   // 1 soldier   ≈ this many rupees

        // The total worth of an offer, in gold-equivalents.
        public static float OfferValue(int gold, int influence, int troops, float fiefGold)
            => Math.Max(0, gold)
             + Math.Max(0, influence) * PersuadeInfluenceValue
             + Math.Max(0, troops)    * PersuadeTroopValue
             + Math.Max(0f, fiefGold);

        // What it costs to buy this rival out: the realm's going rate scaled by how strong his claim is.
        // A front-runner (large share of the contest's support) holds out for far more than an also-ran.
        public static float RivalPrice(float baseGold, float supportFraction)
            => Math.Max(1f, baseGold) * (0.5f + Clamp01(supportFraction));

        // Probability (0.05..0.95) the rival accepts: the offer measured against his price, nudged by
        // relation. Offering his price to a neutral rival is a coin-flip; doubling it makes it near-certain.
        public static float PersuasionAcceptChance(float offerValue, float rivalPrice, int relation)
        {
            float ratio = Math.Max(0f, offerValue) / Math.Max(1f, rivalPrice);
            float chance = 0.5f * ratio + relation / 200f;
            return chance < 0.05f ? 0.05f : (chance > 0.95f ? 0.95f : chance);
        }

        // ── The crisis economy: pressing the SITTING king (roadmap A.1 rework) ──────────
        // An incumbent is dearer than any pretender — he sells the throne he SITS on, and every
        // year of habit and dignity raises the price. A 49-year Alamgir costs ~6x a fresh
        // usurper: with a large realm's going rate that is millions, as it should be.
        public const float ReignYearPriceFactor = 0.10f;

        public static float IncumbentPrice(float baseGold, float supportFraction, float yearsReigned)
            => RivalPrice(baseGold, supportFraction) * (1f + ReignYearPriceFactor * Math.Max(0f, yearsReigned));

        // Pressing a SECURE king to abdicate is lèse-majesté. Below the gate (his loyal strength
        // under 3x yours) he hears you out like any rival; past it, a refusal may be answered
        // with a TREACHERY declaration — likelier the safer he sits.
        public const float TreacheryStrengthGate = 3f;

        public static float TreacheryChance(float kingToPlayerStrengthRatio)
        {
            if (kingToPlayerStrengthRatio < TreacheryStrengthGate) return 0f;
            float over = kingToPlayerStrengthRatio - TreacheryStrengthGate;
            float chance = 0.25f + over * 0.10f;
            return chance > 0.60f ? 0.60f : chance;
        }

        // The king's ransom for a captured traitor: always in the hundreds of thousands,
        // scaled by the realm's going rate and clamped so it is never trivial nor absurd.
        public static int RansomDemand(float baseGold)
        {
            float raw = Math.Max(0f, baseGold) * 0.6f;
            return (int)Math.Max(200000f, Math.Min(1000000f, raw));
        }

        // The victorious king's justice for the captured traitor, from a 0..1 roll: the worse
        // he regards the prisoner, the readier the sword; the fine is for kings who prefer
        // treasure to blood; the fort is the default — let the traitor rot until he is bought out.
        public enum TraitorFate { Execute, HeavyFine, Imprison }

        public static TraitorFate ChooseFate(int relation, float roll)
        {
            float execute = relation <= -60 ? 0.35f : relation <= -20 ? 0.20f : 0.10f;
            const float fine = 0.35f;
            if (roll < execute) return TraitorFate.Execute;
            if (roll < execute + fine) return TraitorFate.HeavyFine;
            return TraitorFate.Imprison;
        }

        // Death stalks an unransomed prisoner: each month in the fort carries this chance the
        // damp or the daggers end him.
        public const float PrisonDeathChancePerMonth = 0.05f;

        // ── AI law adoption (by dynasty size, design doc cross-cutting) ──────────────────
        // A broad dynasty trusts primogeniture (or, if the realm is fractured, election among its princes);
        // a thin line names an heir; a dynasty with no son falls to the magnates.
        public static SuccessionLaw ChooseLawForAi(int livingPrinces, float authority, float legitimacy, bool fragmented)
        {
            if (livingPrinces <= 0) return SuccessionLaw.MagnateElection;
            if (livingPrinces <= 2) return SuccessionLaw.AppointedHeir;
            // A wide dynasty: primogeniture if the throne is steady, election among princes if it is shaky.
            if (fragmented || legitimacy < 45f || authority < 40f) return SuccessionLaw.PrincelyElection;
            return SuccessionLaw.MalePrimogeniture;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
