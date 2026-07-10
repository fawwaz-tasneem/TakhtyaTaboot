using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;
using TakhtyaTaboot.Config;

namespace TakhtyaTaboot
{
    // War of Princes — Succession Crisis. Design: wiki/SuccessionCrisis-Design.md
    //
    // A crisis triggers when a ruler dies weak or heirless, is held prisoner too
    // long, or when imperial authority collapses under an illegitimate ruler.
    // Claimants emerge from the dynasty, lords align by relation/religion/rank,
    // and the crisis resolves through a brokered succession or civil war.
    //
    // The civil war is abstract (weekly skirmishes between the claimant blocs)
    // because the engine cannot put two clans of the same kingdom at war.

    public enum CrisisState { None, Brewing, Active, CivilWar, Resolved }

    public class SuccessionBehavior : CampaignBehaviorBase
    {
        public static SuccessionBehavior Instance { get; private set; }

        private const float BrewingDays = 21f;   // Brewing -> Active
        private const float DeadlineDays = 60f;  // Active -> CivilWar if unresolved
        private const float AiBrokerDay = 45f;   // AI kingmaker steps in
        private const int   CivilWarDays = 90;   // backstop: strongest side prevails after this
        private const float DynastyFavour = 20f; // the reigning house's standing premium in elections

        // ── State (parallel lists — engine can't serialize Dictionary<string,T>) ──
        private List<string> _crisisKingdomIds = new List<string>();
        private List<int>    _crisisStates     = new List<int>();
        private List<float>  _crisisDays       = new List<float>();
        private List<string> _claimantIds      = new List<string>(); // comma-joined per crisis
        private List<string> _incumbentIds     = new List<string>(); // sitting ruler at crisis start ("" if died)

        // Civil-war breakaway kingdoms, parallel to _crisisKingdomIds. Each entry is a comma-joined
        // list of "rebelKingdomId=claimantHeroId" — the champion houses warring for each claimant.
        // "" until the crisis reaches the CivilWar phase (or if no separate house can field a host).
        private List<string> _warBreakaways    = new List<string>();
        private List<int>    _warStartDay      = new List<int>();     // day civil war began (-1 if not)

        private List<string> _alignHeroIds     = new List<string>(); // lordId -> claimantId
        private List<string> _alignTargetIds   = new List<string>();

        private List<string> _supportHeroIds   = new List<string>(); // claimantId -> score
        private List<float>  _supportScores    = new List<float>();

        private List<string> _rulerKingdomIds  = new List<string>(); // kingdomId -> last known rulerId
        private List<string> _rulerHeroIds     = new List<string>();

        private List<string> _prisonKingdomIds = new List<string>(); // kingdomId -> days ruler imprisoned
        private List<float>  _prisonDays       = new List<float>();

        // ── Public API ────────────────────────────────────────────────────────────

        public CrisisState GetCrisisState(Kingdom k)
        {
            int i = CrisisIndex(k);
            return i < 0 ? CrisisState.None : (CrisisState)_crisisStates[i];
        }

        public List<Hero> GetClaimants(Kingdom k)
        {
            int i = CrisisIndex(k);
            var result = new List<Hero>();
            if (i < 0 || string.IsNullOrEmpty(_claimantIds[i])) return result;
            foreach (string id in _claimantIds[i].Split(','))
            {
                Hero h = FindHero(id);
                if (h != null) result.Add(h);
            }
            return result;
        }

        public float GetSupport(Hero claimant)
        {
            int i = _supportHeroIds.IndexOf(claimant?.StringId ?? "");
            return i < 0 ? 0f : _supportScores[i];
        }

        public float GetSupportPercent(Kingdom k, Hero claimant)
        {
            var all = GetClaimants(k);
            float sum = all.Sum(c => GetSupport(c));
            return sum <= 0f ? 0f : GetSupport(claimant) / sum * 100f;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        public override void RegisterEvents()
        {
            Instance = this;
            // No OnNewGameCreated snapshot: Kingdom.All is still being built on parallel world-gen
            // threads at that point (see Util/WorldGen.cs); OnSessionLaunched snapshots safely.
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("Succession.DailyTick", OnDailyTick));
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("Succession.WeeklyTick", OnWeeklyTick));
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("suc_kingdomIds",   ref _crisisKingdomIds);
            dataStore.SyncData("suc_states",       ref _crisisStates);
            dataStore.SyncData("suc_days",         ref _crisisDays);
            dataStore.SyncData("suc_claimants",    ref _claimantIds);
            dataStore.SyncData("suc_incumbents",   ref _incumbentIds);
            dataStore.SyncData("suc_warBreakaways", ref _warBreakaways);
            dataStore.SyncData("suc_warStartDay",   ref _warStartDay);
            dataStore.SyncData("suc_alignHeroes",  ref _alignHeroIds);
            dataStore.SyncData("suc_alignTargets", ref _alignTargetIds);
            dataStore.SyncData("suc_suppHeroes",   ref _supportHeroIds);
            dataStore.SyncData("suc_suppScores",   ref _supportScores);
            dataStore.SyncData("suc_rulerKIds",    ref _rulerKingdomIds);
            dataStore.SyncData("suc_rulerHIds",    ref _rulerHeroIds);
            dataStore.SyncData("suc_prisonKIds",   ref _prisonKingdomIds);
            dataStore.SyncData("suc_prisonDays",   ref _prisonDays);

            // ClaimantClan's hero->origin-clan map lives in a static helper; persist it here so
            // that a temp cadet clan surviving a save/load can still be dissolved back correctly.
            Util.ClaimantClan.Export(out var originHeroes, out var originClans);
            dataStore.SyncData("hind_claimorigin_heroes", ref originHeroes);
            dataStore.SyncData("hind_claimorigin_clans",  ref originClans);
            if (!dataStore.IsSaving)
                Util.ClaimantClan.Import(originHeroes, originClans);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SnapshotRulers();
            RegisterMenus(starter);
        }

        // Remember who rules each kingdom so a death can be matched to a throne
        // even after the engine has already promoted a successor.
        private void SnapshotRulers()
        {
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null))
            {
                int i = _rulerKingdomIds.IndexOf(k.StringId);
                if (i >= 0) _rulerHeroIds[i] = k.Leader.StringId;
                else { _rulerKingdomIds.Add(k.StringId); _rulerHeroIds.Add(k.Leader.StringId); }
            }
        }

        // ── Trigger detection ─────────────────────────────────────────────────────

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null) return;
            // World-gen fires HeroKilled in PARALLEL for object setup; never touch crisis state or (worse)
            // mutate a ruling clan from here until the session is live. Nothing meaningful exists to act on
            // during world-gen anyway (no crises, empty ruler snapshot).
            if (!Util.WorldGen.Ready) return;

            // The scripted 1707 emperor cascade crowns the heir and then kills the outgoing
            // emperor in one scripted act; that death must not open a generic succession crisis
            // on the very day the appointed heir is enthroned. Keep the snapshot fresh though,
            // so the new emperor is tracked from here on.
            if (Util.ScriptedSuccession.InProgress) { SnapshotRulers(); return; }

            // Was the victim a known ruler?
            int ri = _rulerHeroIds.IndexOf(victim.StringId);
            if (ri >= 0)
            {
                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == _rulerKingdomIds[ri]);
                if (k != null && !k.IsEliminated && GetCrisisState(k) == CrisisState.None)
                {
                    float legitimacy = LegitimacyBehavior.Instance?.GetLegitimacy(victim) ?? 60f;
                    bool hasHeir = victim.Children.Any(c => c.IsAlive && !c.IsFemale && !c.IsChild);
                    SuccessionLaw law = Law(k);

                    if (law == SuccessionLaw.Undeclared)
                    {
                        // No formal law: the realm's open contest, exactly as before.
                        if (legitimacy < 60f || !hasHeir)
                            TriggerCrisis(k, victim, includeIncumbent: false);
                    }
                    else
                    {
                        // A formal law softly suppresses the war-of-princes: a valid lawful heir usually
                        // accedes cleanly, but a shaky or collapsed throne can still be contested.
                        Hero lawfulHeir = SuccessionLawBehavior.Instance?.LawfulHeir(k);
                        float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(k) ?? 75f;
                        if (SuccessionLawMath.ShouldContest(law, lawfulHeir != null, legitimacy, auth, MBRandom.RandomFloat, Tune.SuccessionContestFloor))
                            TriggerCrisis(k, victim, includeIncumbent: false);
                        else if (lawfulHeir != null)
                            Util.TYTLog.Guard("Succession.CleanAccession", () => CleanAccession(k, lawfulHeir, law));
                    }
                }
                SnapshotRulers();
                return;
            }

            // Was the victim a claimant in an ongoing crisis?
            for (int i = 0; i < _crisisKingdomIds.Count; i++)
            {
                var ids = _claimantIds[i].Split(',').ToList();
                if (!ids.Remove(victim.StringId)) continue;

                _claimantIds[i] = string.Join(",", ids);
                // Lords backing the dead claimant become neutral.
                for (int a = 0; a < _alignTargetIds.Count; a++)
                    if (_alignTargetIds[a] == victim.StringId) _alignTargetIds[a] = "";

                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == _crisisKingdomIds[i]);
                if (k == null) { RemoveCrisis(i); return; }

                var remaining = GetClaimants(k);
                if (remaining.Count == 1)
                {
                    Notify($"With {victim.Name} dead, the path to the throne of {k.Name} lies open.", false);
                    ResolveCrisis(k, remaining[0], "their rival's death");
                }
                else if (remaining.Count == 0) RemoveCrisis(i);
                return;
            }
        }

        private void OnDailyTick()
        {
            // Prisoner-ruler trigger: a captive emperor with weak authority invites a crisis.
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null))
            {
                if (!Util.TYTLog.Valid(k) || !Util.TYTLog.Valid(k.Leader)) continue;
                Util.TYTLog.Crumb("succession scan " + k.StringId);
                int pi = _prisonKingdomIds.IndexOf(k.StringId);
                if (k.Leader.IsPrisoner)
                {
                    if (pi < 0) { _prisonKingdomIds.Add(k.StringId); _prisonDays.Add(1f); pi = _prisonDays.Count - 1; }
                    else _prisonDays[pi] += 1f;

                    float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(k) ?? 75f;
                    if (_prisonDays[pi] > 30f && auth < 30f && GetCrisisState(k) == CrisisState.None)
                        TriggerCrisis(k, k.Leader, includeIncumbent: true);
                }
                else if (pi >= 0) _prisonDays[pi] = 0f;
            }

            // Advance crisis clocks and state transitions.
            for (int i = _crisisKingdomIds.Count - 1; i >= 0; i--)
            {
                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == _crisisKingdomIds[i]);
                if (k == null || k.IsEliminated) { RemoveCrisis(i); continue; }

                _crisisDays[i] += 1f;
                var state = (CrisisState)_crisisStates[i];

                if (state == CrisisState.Brewing && _crisisDays[i] >= BrewingDays)
                {
                    _crisisStates[i] = (int)CrisisState.Active;
                    Notify($"The succession crisis in {k.Name} breaks into the open. The throne is contested!", true);
                }
                else if (state == CrisisState.Active && _crisisDays[i] >= DeadlineDays)
                {
                    SuccessionLaw law = Law(k);
                    bool elective = law == SuccessionLaw.PrincelyElection || law == SuccessionLaw.MagnateElection;

                    if (elective)
                    {
                        // The assembled magnates cast their voices; a decisive result settles the throne,
                        // a near-tie throws the realm into civil war.
                        var vote = RunMagnateVote(k);
                        Hero winner = FindHero(vote.WinnerId);
                        if (winner != null && SuccessionLawMath.IsDecisive(vote, Tune.MagnateElectionDecisiveMargin))
                            ResolveCrisis(k, winner, "the voices of the assembled lords");
                        else
                        {
                            _crisisStates[i] = (int)CrisisState.CivilWar;
                            Notify($"The election in {k.Name} ends in deadlock — civil war! The lords take up arms for rival claimants.", true);
                            StartCivilWar(k, i);
                        }
                    }
                    else
                    {
                        Hero leader = LeadingClaimant(k);
                        if (leader != null && GetSupportPercent(k, leader) >= 55f)
                            ResolveCrisis(k, leader, "the acclaim of the great lords");
                        else
                        {
                            _crisisStates[i] = (int)CrisisState.CivilWar;
                            Notify($"Civil war! The lords of {k.Name} take up arms for rival claimants.", true);
                            StartCivilWar(k, i);
                        }
                    }
                }
            }
        }

        private void OnWeeklyTick()
        {
            SnapshotRulers();

            // Authority-collapse trigger.
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null))
            {
                if (GetCrisisState(k) != CrisisState.None) continue;
                float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(k) ?? 75f;
                float leg = LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 60f;
                if (auth < 15f && leg < 40f)
                    TriggerCrisis(k, k.Leader, includeIncumbent: true);
            }

            bool anyCrisis = _crisisKingdomIds.Count > 0;

            for (int i = _crisisKingdomIds.Count - 1; i >= 0; i--)
            {
                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == _crisisKingdomIds[i]);
                if (k == null) { RemoveCrisis(i); continue; }
                var state = (CrisisState)_crisisStates[i];

                // Authority drain while the realm is paralysed.
                float drain = state == CrisisState.CivilWar ? -5f : -3f;
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, drain, "succession crisis");

                // Support drifts toward whoever the lords already back.
                foreach (Hero c in GetClaimants(k))
                    AddSupport(c, 0.5f * CountBackers(c));

                if (state == CrisisState.Active && _crisisDays[i] >= AiBrokerDay)
                    TryAiBroker(k);

                if (state == CrisisState.CivilWar)
                    RunCivilWar(k, i);
            }

            // Stability premium: untroubled realms look better while a rival burns.
            if (anyCrisis)
                foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null))
                    if (GetCrisisState(k) == CrisisState.None)
                        ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 1f, "stability amid rivals' crises");
        }

        // Force a crisis regardless of trigger conditions (console / testing).
        // Returns a human-readable result string.
        public string DebugForceCrisis(Kingdom k)
        {
            if (k == null || k.IsEliminated || k.Leader == null) return "No valid kingdom.";
            if (GetCrisisState(k) != CrisisState.None) return $"{k.Name} is already in a succession crisis.";
            int before = _crisisKingdomIds.Count;
            TriggerCrisis(k, k.Leader, includeIncumbent: true);
            if (_crisisKingdomIds.Count == before)
                return $"{k.Name} has too few eligible claimants for a crisis (need at least 2).";
            return $"Succession crisis forced in {k.Name}. Claimants: " +
                   string.Join(", ", GetClaimants(k).Select(c => c.Name.ToString()));
        }

        // Simulate N days of crisis progression (console / testing). Runs the same
        // daily state machine plus a weekly pass every 7th day.
        public string DebugAdvance(int days)
        {
            if (_crisisKingdomIds.Count == 0) return "No active crises to advance.";
            for (int d = 0; d < days; d++)
            {
                OnDailyTick();
                if ((d + 1) % 7 == 0) OnWeeklyTick();
            }
            return $"Advanced {days} day(s). " + Summary();
        }

        private string Summary()
        {
            if (_crisisKingdomIds.Count == 0) return "All crises resolved.";
            var parts = new List<string>();
            for (int i = 0; i < _crisisKingdomIds.Count; i++)
            {
                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == _crisisKingdomIds[i]);
                if (k != null) parts.Add($"{k.Name} [{(CrisisState)_crisisStates[i]}, day {(int)_crisisDays[i]}]");
            }
            return string.Join("; ", parts);
        }

        // ── Crisis lifecycle ──────────────────────────────────────────────────────

        private void TriggerCrisis(Kingdom k, Hero ruler, bool includeIncumbent)
        {
            var claimants = FindClaimants(k, ruler, includeIncumbent, out var categories);
            if (claimants.Count <= 1) return; // a lone heir succeeds quietly

            _crisisKingdomIds.Add(k.StringId);
            _crisisStates.Add((int)CrisisState.Brewing);
            _crisisDays.Add(0f);
            _claimantIds.Add(string.Join(",", claimants.Select(c => c.StringId)));
            // Record the sitting ruler so we know who was "deposed" if they lose.
            _incumbentIds.Add(ruler != null && ruler.IsAlive ? ruler.StringId : "");
            _warBreakaways.Add("");
            _warStartDay.Add(-1);

            InitialiseSupport(k, claimants, categories);
            AlignLords(k, claimants);

            // First alignment impressions feed the starting scores.
            foreach (Hero c in claimants) AddSupport(c, 2f * CountBackers(c));

            // All claims start disputed.
            foreach (Hero c in claimants)
                LegitimacyBehavior.Instance?.SetLegitimacy(c, 40f);

            Notify($"Whispers fill the court of {k.Name}. The succession is not secure — " +
                   $"{claimants.Count} princes eye the throne.", true);
        }

        // Gather the candidate UNIVERSE in agnatic priority — sons (eldest first) -> brothers -> nephews
        // -> clan leader — followed by the realm's mightiest houses as pretenders, then let the kingdom's
        // SUCCESSION LAW choose the actual contested pool from it (SuccessionLawMath.OrderedPool). With no
        // formal law the universe's first three stand, exactly as before; primogeniture narrows to the
        // line, the election laws widen to the magnates, and an appointed Wali Ahd leads his own pool.
        private static List<Hero> FindClaimants(Kingdom k, Hero ruler, bool includeIncumbent, out List<int> categories)
        {
            var seen = new HashSet<Hero>();
            var heroes = new List<Hero>();
            var cats = new List<int>();      // 0 incumbent, 1 son, 2 brother, 3 nephew, 4 clan-leader, 5 powerful lord
            var sonRanks = new List<int>();  // 0 = eldest son; -1 otherwise
            int sonCounter = 0;

            void Add(Hero h, int cat)
            {
                if (h != null && h.IsAlive && !h.IsChild && !h.IsFemale && seen.Add(h))
                { heroes.Add(h); cats.Add(cat); sonRanks.Add(cat == 1 ? sonCounter++ : -1); }
            }
            // Powerful-lord fallback allows any adult clan leader (a formidable Rani may press a claim too).
            void AddLord(Hero h, int cat)
            {
                if (h != null && h.IsAlive && !h.IsChild && seen.Add(h))
                { heroes.Add(h); cats.Add(cat); sonRanks.Add(-1); }
            }

            if (includeIncumbent) Add(ruler, 0);
            foreach (Hero son in ruler.Children.Where(c => !c.IsFemale).OrderByDescending(c => c.Age))
                Add(son, 1);
            var brothers = (ruler.Father?.Children ?? new List<Hero>())
                .Where(b => b != ruler && !b.IsFemale).OrderByDescending(b => b.Age).ToList();
            foreach (Hero b in brothers) Add(b, 2);
            foreach (Hero b in brothers)
                foreach (Hero n in b.Children.Where(c => !c.IsFemale).OrderByDescending(c => c.Age))
                    Add(n, 3);
            Add(ruler.Clan?.Leader, 4);

            // Always append the realm's mightiest houses to the universe (so the election laws have a real
            // field); appended AFTER the dynasty, the open-contest default's first three are unchanged.
            if (k != null)
                foreach (Hero lord in k.Clans
                    .Where(c => !c.IsEliminated && !c.IsMinorFaction && c.Leader != null && !c.Leader.IsChild)
                    .OrderByDescending(ClanPower).Select(c => c.Leader).Take(6))
                    AddLord(lord, 5);

            // Hand the universe to the law to pick the contested pool.
            Hero wali = SuccessionLawBehavior.Instance?.GetWaliAhd(k);
            Hero naib = SuccessionLawBehavior.Instance?.GetNaib(k);
            var universe = new List<SuccessionLawMath.LawCandidate>();
            for (int i = 0; i < heroes.Count; i++)
                universe.Add(new SuccessionLawMath.LawCandidate
                {
                    Id = heroes[i].StringId,
                    Category = cats[i],
                    IsDynasty = cats[i] <= 4,
                    SonRank = sonRanks[i],
                    Power = ClanPower(heroes[i].Clan),
                    RankIndex = MansabdariBehavior.Instance?.GetRankIndex(heroes[i].Clan) ?? 0,
                    IsWali = wali != null && heroes[i] == wali,
                    IsNaib = naib != null && heroes[i] == naib,
                });

            SuccessionLaw law = SuccessionLawBehavior.Instance?.GetLaw(k) ?? SuccessionLaw.Undeclared;
            var orderedIds = SuccessionLawMath.OrderedPool(law, universe, 3);

            var byId = new Dictionary<string, int>();
            for (int i = 0; i < heroes.Count; i++) byId[heroes[i].StringId] = i;

            var resultHeroes = new List<Hero>();
            var resultCats = new List<int>();
            foreach (string id in orderedIds)
                if (byId.TryGetValue(id, out int idx)) { resultHeroes.Add(heroes[idx]); resultCats.Add(cats[idx]); }

            categories = resultCats;
            return resultHeroes;
        }

        // A rough measure of a house's weight at court: host strength, influence, fiefs, and coin.
        private static float ClanPower(Clan c)
        {
            if (c == null) return 0f;
            float fiefs = c.Settlements?.Count(s => s.IsTown || s.IsCastle) ?? 0;
            return c.CurrentTotalStrength + c.Influence * 3f + fiefs * 200f + (c.Leader?.Gold ?? 0) * 0.01f;
        }

        private void InitialiseSupport(Kingdom k, List<Hero> claimants, List<int> categories)
        {
            SuccessionLaw law = Law(k);
            Hero wali = SuccessionLawBehavior.Instance?.GetWaliAhd(k);
            Hero naib = SuccessionLawBehavior.Instance?.GetNaib(k);
            int sonRank = 0;
            for (int i = 0; i < claimants.Count; i++)
            {
                Hero c = claimants[i];
                int thisSonRank = categories[i] == 1 ? sonRank++ : -1;
                float score = categories[i] switch
                {
                    0 => 50f,                       // sitting (if disputed-while-alive) ruler
                    1 => thisSonRank == 0 ? 60f : 45f - 5f * thisSonRank,
                    2 => 30f,                       // brother
                    3 => 20f,                       // nephew
                    _ => 40f,                       // clan-leader fallback
                };

                if ((MansabdariBehavior.Instance?.GetRankIndex(c.Clan) ?? 0) >= 5) score += 15f;
                score -= 5f * Kingdom.All.Count(o => o != k && !o.IsEliminated && k.IsAtWarWith(o));
                score += 3f * (c.Clan?.Fiefs?.Count(f => f.IsTown) ?? 0);

                // Favour of the realm's chief Amir carries weight at court.
                Hero amir = TopAmir(k, exclude: c);
                if (amir != null && CharacterRelationManager.GetHeroRelation(c, amir) > 20) score += 10f;

                // The succession law's own thumb on the scale: a named/primogeniture heir leads, and a
                // magnate election still tilts toward the reigning house.
                score += SuccessionLawMath.LawSupportBonus(law, new SuccessionLawMath.LawCandidate
                {
                    Category = categories[i], SonRank = thisSonRank, IsDynasty = categories[i] <= 4,
                    IsWali = wali != null && c == wali, IsNaib = naib != null && c == naib,
                }, Tune.HeirSupportBoost, DynastyFavour);

                SetSupport(c, Math.Max(5f, score));
            }
        }

        private void AlignLords(Kingdom k, List<Hero> claimants)
        {
            foreach (Clan clan in k.Clans.Where(c => !c.IsEliminated && c.Leader != null && !c.IsMinorFaction))
            {
                Hero lord = clan.Leader;
                if (claimants.Contains(lord)) continue;

                Hero best = null; float bestW = float.MinValue;
                foreach (Hero c in claimants)
                {
                    float w = CharacterRelationManager.GetHeroRelation(lord, c);
                    if (ReligionBehavior.Instance != null &&
                        ReligionBehavior.Instance.GetReligion(lord) == ReligionBehavior.Instance.GetReligion(c))
                        w += 15f;
                    w += (MansabdariBehavior.Instance?.GetRankIndex(clan) ?? 0) * 3f;
                    w += MBRandom.RandomFloatRanged(-10f, 10f);
                    if (w > bestW) { bestW = w; best = c; }
                }
                SetAlignment(lord, bestW < 0f ? null : best);
            }
        }

        private void TryAiBroker(Kingdom k)
        {
            Hero leader = LeadingClaimant(k);
            if (leader == null || GetSupportPercent(k, leader) < 45f) return;

            Hero amir = TopAmir(k, exclude: leader);
            if (amir == null || amir == Hero.MainHero) return; // the player brokers by hand
            if ((MansabdariBehavior.Instance?.GetRankIndex(amir.Clan) ?? 0) < 6) return;
            if (amir.Clan.Influence < 200f) return;

            ChangeClanInfluenceAction.Apply(amir.Clan, -200f);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(amir, leader, 15);
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 10f, "succession brokered");
            Notify($"{amir.Name}, Amir-ul-Umara, brokers the succession of {k.Name} in favour of {leader.Name}.", false);
            ResolveCrisis(k, leader, $"the patronage of {amir.Name}");
        }

        // Abstract weekly clash between the two strongest claimant blocs.
        private void RunSkirmish(Kingdom k)
        {
            var ranked = GetClaimants(k).OrderByDescending(c => GetSupport(c)).ToList();
            if (ranked.Count < 2)
            {
                if (ranked.Count == 1) ResolveCrisis(k, ranked[0], "want of any rival");
                return;
            }

            Hero a = ranked[0], b = ranked[1];
            float strA = BlocStrength(a), strB = BlocStrength(b);
            Hero winner = strA >= strB ? a : b;
            Hero loser = winner == a ? b : a;

            AddSupport(loser, -(6f + MBRandom.RandomFloatRanged(0f, 4f)));
            AddSupport(winner, 2f);

            // A losing claimant may be captured in the field, ending the war at a stroke.
            if (MBRandom.RandomFloat < 0.10f)
            {
                Notify($"{loser.Name} is captured in battle. The war of princes in {k.Name} is decided.", false);
                ResolveCrisis(k, winner, "victory in the field", hostile: true);
                return;
            }

            if (GetSupportPercent(k, loser) <= 10f || GetSupportPercent(k, winner) >= 70f)
                ResolveCrisis(k, winner, "the collapse of all opposition", hostile: true);
        }

        // ── Real civil war: rival claimants raise their OWN banners as breakaway kingdoms ──────
        // Bannerlord kingdoms are led by clans, so a prince who is only a MEMBER of the dynasty clan
        // cannot head a kingdom. Each fighting claimant therefore gets a host clan to secede with:
        //  • a prince who already leads his own landed house -> that house secedes directly;
        //  • a prince with no house of his own (e.g. the king's son) -> a TEMPORARY cadet clan is
        //    split off for him (Util.ClaimantClan), which founds the breakaway kingdom.
        // A claimant may DECLINE to fight (WillFight) and remain a peaceful court claimant. On
        // resolution the cadet clans either merge back into the dynasty (FoldBack) or, for an
        // embittered surviving pretender, stand on as a PERMANENT split realm.
        private void StartCivilWar(Kingdom k, int i)
        {
            try
            {
                if (k == null || k.Leader == null || RevoltCascadeBehavior.Instance == null) return;
                var claimants = GetClaimants(k);
                Hero throne = k.Leader;
                var pairs = new List<string>();                 // "rebelKingdomId=claimantId"
                var formed = new Dictionary<string, Kingdom>();  // claimantId -> rebel kingdom

                foreach (Hero c in claimants)
                {
                    if (c == null || !c.IsAlive || c == throne || c == Hero.MainHero) continue; // player prompted below

                    var backers = AlignedBackerClans(c, k);
                    bool ownHouse = c.Clan != null && c.Clan != k.RulingClan && c.Clan.Leader == c
                                    && !c.Clan.IsMinorFaction && c.Clan.Kingdom == k && HasFief(c.Clan);
                    if (!ownHouse && !backers.Any(HasFief)) continue;   // no viable host -> court claimant
                    if (!WillFight(c, k)) continue;                     // chooses the court over the field

                    Clan host; bool temp = false;
                    if (ownHouse) host = c.Clan;
                    else { host = Util.ClaimantClan.Create(c, k.Culture); temp = true; if (host == null) continue; }

                    Settlement seat = host.Settlements.FirstOrDefault(s => s.IsTown)
                        ?? host.Settlements.FirstOrDefault(s => s.IsCastle)
                        ?? host.Settlements.FirstOrDefault()
                        ?? backers.SelectMany(b => b.Settlements).FirstOrDefault(s => s.IsTown)
                        ?? backers.SelectMany(b => b.Settlements).FirstOrDefault()
                        ?? c.HomeSettlement;
                    if (seat == null) { if (temp) Util.ClaimantClan.Dissolve(host); continue; }

                    Kingdom rebel = RevoltCascadeBehavior.Instance.CreateRebelKingdom(host, seat, $"{c.Name}'s Claim");
                    if (rebel == null) { if (temp) Util.ClaimantClan.Dissolve(host); continue; }

                    foreach (Clan b in backers)
                        if (b != host && b.Kingdom == k)
                            try { ChangeKingdomAction.ApplyByJoinToKingdom(b, rebel, default(CampaignTime), false); } catch { }

                    RevoltCascadeBehavior.Instance.EnsureAtWar(rebel, k);
                    pairs.Add(rebel.StringId + "=" + c.StringId);
                    formed[c.StringId] = rebel;
                }

                // Pretenders war each other only if their claimants are hostile; otherwise they tolerate
                // one another and concentrate on the throne.
                var ids = formed.Keys.ToList();
                for (int x = 0; x < ids.Count; x++)
                    for (int y = x + 1; y < ids.Count; y++)
                    {
                        Hero ca = FindHero(ids[x]), cb = FindHero(ids[y]);
                        if (ca != null && cb != null && CharacterRelationManager.GetHeroRelation(ca, cb) < 0
                            && !formed[ids[x]].IsAtWarWith(formed[ids[y]]))
                            try { DeclareWarAction.ApplyByDefault(formed[ids[x]], formed[ids[y]]); } catch { }
                    }

                if (i >= 0 && i < _warBreakaways.Count)
                {
                    _warBreakaways[i] = string.Join(",", pairs);
                    _warStartDay[i] = (int)CampaignTime.Now.ToDays;
                }

                // Player agency: a claimant-player is offered the choice to raise his own banner;
                // otherwise, if he pledged to a breakaway claimant, he rides to that banner.
                if (claimants.Contains(Hero.MainHero) && Hero.MainHero != throne && Clan.PlayerClan != k.RulingClan)
                    PromptPlayerClaim(k, i);
                else
                {
                    Hero pick = GetAlignment(Hero.MainHero);
                    if (pick != null && formed.TryGetValue(pick.StringId, out Kingdom pk)
                        && Clan.PlayerClan != null && Clan.PlayerClan.Kingdom == k && Clan.PlayerClan != k.RulingClan)
                        try { ChangeKingdomAction.ApplyByJoinToKingdom(Clan.PlayerClan, pk, default(CampaignTime), false); } catch { }
                }

                if (pairs.Count > 0)
                    Notify($"The war of princes in {k.Name} spills onto the map — {pairs.Count} pretender realm(s) march against the throne.", true);
            }
            catch (Exception e) { Util.TYTLog.Error("StartCivilWar failed", e); }
        }

        // A claimant raises arms only if his cause has momentum; a near-hopeless prince keeps to the
        // court (where he can still be brokered in). Randomised so the same crisis can play out anew.
        private bool WillFight(Hero c, Kingdom k)
        {
            if (c == null) return false;
            float drive = GetSupportPercent(k, c) + CountBackers(c) * 8f + MBRandom.RandomFloatRanged(-15f, 15f);
            return drive >= 25f;
        }

        private static bool IsTempClaimClan(Clan c)
            => c?.StringId != null && c.StringId.StartsWith("tyt_claim_");

        // ── Player as claimant: choose to raise your own banner or hold at court ───────────
        private void PromptPlayerClaim(Kingdom k, int i)
        {
            if (k == null || Hero.MainHero == null) return;
            InformationManager.ShowInquiry(new InquiryData(
                "Raise Your Banner?",
                $"{k.Name} has fallen into a war of princes, and you are among the claimants to the throne. " +
                "Will you raise your own banner and fight for the crown — or hold at court and let the great lords decide?",
                true, true, "Raise my banner", "Hold at court",
                () => PlayerRaiseBanner(k, i), () => { }), true);
        }

        private void PlayerRaiseBanner(Kingdom k, int i)
        {
            try
            {
                if (RevoltCascadeBehavior.Instance == null || Clan.PlayerClan == null || k == null) return;
                if (Clan.PlayerClan.Kingdom != k) { Notify("You are no longer of this realm.", true); return; }

                var backers = AlignedBackerClans(Hero.MainHero, k);
                Settlement seat = Clan.PlayerClan.Settlements.FirstOrDefault(s => s.IsTown)
                    ?? Clan.PlayerClan.Settlements.FirstOrDefault()
                    ?? backers.SelectMany(b => b.Settlements).FirstOrDefault(s => s.IsTown)
                    ?? backers.SelectMany(b => b.Settlements).FirstOrDefault()
                    ?? Hero.MainHero.HomeSettlement;
                if (seat == null) { Notify("You hold no seat from which to raise your banner.", true); return; }

                Kingdom rebel = RevoltCascadeBehavior.Instance.CreateRebelKingdom(
                    Clan.PlayerClan, seat, $"{Hero.MainHero.Name}'s Claim");
                if (rebel == null) { Notify("Your banner could not be raised.", true); return; }

                foreach (Clan b in backers)
                    if (b != Clan.PlayerClan && b.Kingdom == k)
                        try { ChangeKingdomAction.ApplyByJoinToKingdom(b, rebel, default(CampaignTime), false); } catch { }

                RevoltCascadeBehavior.Instance.EnsureAtWar(rebel, k);
                AppendBreakaway(i, rebel, Hero.MainHero);
                Notify($"You raise your banner against the throne of {k.Name}. The war of princes is joined — win it, and the crown is yours.", true);
            }
            catch (Exception e) { Util.TYTLog.Error("PlayerRaiseBanner failed", e); }
        }

        private void AppendBreakaway(int i, Kingdom rebel, Hero claimant)
        {
            if (i < 0 || i >= _warBreakaways.Count || rebel == null || claimant == null) return;
            string entry = rebel.StringId + "=" + claimant.StringId;
            _warBreakaways[i] = string.IsNullOrEmpty(_warBreakaways[i]) ? entry : _warBreakaways[i] + "," + entry;
            if (i < _warStartDay.Count && _warStartDay[i] < 0) _warStartDay[i] = (int)CampaignTime.Now.ToDays;
        }

        // A throne and one of its active succession breakaways cannot sign a white peace — the war for the
        // crown is binary. Any truce the engine concludes between them is reversed at once.
        private void OnMakePeace(IFaction f1, IFaction f2, MakePeaceAction.MakePeaceDetail detail)
            => Util.TYTLog.Guard("Succession.OnMakePeace", () =>
            {
                if (!(f1 is Kingdom a) || !(f2 is Kingdom b)) return;
                if (!IsSuccessionWarPair(a, b)) return;
                try { DeclareWarAction.ApplyByDefault(a, b); } catch { }
                Kingdom pk = Hero.MainHero?.Clan?.Kingdom;
                if (pk == a || pk == b)
                    Notify("A war for the throne cannot be ended by treaty — only by victory in the field. The war goes on.", true);
            });

        // Are these two kingdoms a throne and one of its own active succession breakaways?
        private bool IsSuccessionWarPair(Kingdom x, Kingdom y)
        {
            if (x == null || y == null || x == y) return false;
            for (int i = 0; i < _crisisKingdomIds.Count; i++)
            {
                Kingdom throne = Kingdom.All.FirstOrDefault(z => z.StringId == _crisisKingdomIds[i]);
                if (throne == null || (throne != x && throne != y)) continue;
                Kingdom other = throne == x ? y : x;
                if (GetBreakaways(i).Any(p => p.Item1 == other)) return true;
            }
            return false;
        }

        private void RunCivilWar(Kingdom k, int i)
        {
            string raw = (i >= 0 && i < _warBreakaways.Count) ? _warBreakaways[i] : "";
            if (string.IsNullOrEmpty(raw)) { RunSkirmish(k); return; }   // no real host formed -> abstract

            // Prune pretenders whose realm has been broken.
            foreach (var p in GetBreakaways(i).ToList())
                if (p.Item1 == null || p.Item1.IsEliminated || !p.Item1.Settlements.Any())
                {
                    FoldBack(p.Item1, k);
                    RemoveBreakaway(i, p.Item2);
                    if (p.Item2 != null) AddSupport(p.Item2, -25f);
                }

            var bw = GetBreakaways(i);
            if (bw.Count == 0) { ResolveCivilWar(k, i, null, "the pretenders' hosts were broken"); return; }

            // A war for the throne is fought to the finish — there is no white peace and no quiet
            // partition. If the engine or a diplomacy AI has slipped a truce in, re-light the war: the
            // crown is won or lost, never simply walked away with.
            foreach (var p in bw)
                if (p.Item1 != null && !p.Item1.IsEliminated && p.Item1 != k && k != null && !p.Item1.IsAtWarWith(k))
                {
                    RevoltCascadeBehavior.Instance?.EnsureAtWar(p.Item1, k);
                    Notify($"There can be no peace in a war for the throne of {k.Name} — only victory. The fighting resumes.", true);
                }

            // Throne fallen (capital lost, eliminated, or the king taken)?
            if (k == null || k.IsEliminated || !k.Settlements.Any() || k.Leader == null || k.Leader.IsPrisoner)
            {
                var champ = bw.OrderByDescending(p => KingdomStrength(p.Item1)).First();
                ResolveCivilWar(k, i, champ.Item2, "the throne has fallen");
                return;
            }

            // Backstop: after a long war the strongest side prevails.
            int start = (i < _warStartDay.Count) ? _warStartDay[i] : -1;
            if (start >= 0 && (int)CampaignTime.Now.ToDays - start >= CivilWarDays)
            {
                var top = bw.OrderByDescending(p => KingdomStrength(p.Item1)).First();
                if (KingdomStrength(top.Item1) > ThroneStrength(k) * 1.1f)
                    ResolveCivilWar(k, i, top.Item2, "a long war breaks the throne");
                else
                    ResolveCivilWar(k, i, null, "the throne outlasts the pretenders");
            }
        }

        // winnerClaimant == null  =>  the throne held and the crisis ends with the sitting ruler.
        private void ResolveCivilWar(Kingdom k, int i, Hero winnerClaimant, string how)
        {
            try
            {
                var breakaways = GetBreakaways(i);
                if (i < _warBreakaways.Count) _warBreakaways[i] = "";
                if (i < _warStartDay.Count) _warStartDay[i] = -1;

                if (winnerClaimant == null || !winnerClaimant.IsAlive)
                {
                    foreach (var p in breakaways) FoldBack(p.Item1, k);
                    int idx = CrisisIndex(k);
                    if (idx >= 0) ClearCrisisData(idx, GetClaimants(k));
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 5f, "the throne survived the war of princes");
                    Notify($"The throne of {k?.Name} weathers the war of princes; {k?.Leader?.Name} reigns still.", false);
                    return;
                }

                // A pretender prevails: his own host folds home for the crowning; rivals submit if not
                // too embittered, otherwise their realm stands on, now at war with the new sovereign.
                foreach (var p in breakaways)
                {
                    if (p.Item2 == winnerClaimant) { FoldBack(p.Item1, k); continue; }
                    int rel = p.Item2 != null ? CharacterRelationManager.GetHeroRelation(p.Item2, winnerClaimant) : 0;
                    if (rel >= -20) FoldBack(p.Item1, k);   // placated — bends the knee
                }

                ResolveCrisis(k, winnerClaimant, how, hostile: true); // won by war: crowns winner, deposed fate, clears crisis

                foreach (var p in breakaways)
                    if (p.Item1 != null && !p.Item1.IsEliminated && p.Item1 != k && p.Item2 != winnerClaimant)
                    {
                        RevoltCascadeBehavior.Instance?.EnsureAtWar(p.Item1, k);
                        Notify($"{p.Item2?.Name} refuses to bend the knee to {winnerClaimant.Name} — the war goes on.", true);
                    }
            }
            catch (Exception e) { Util.TYTLog.Error("ResolveCivilWar failed", e); }
        }

        // Merge a breakaway realm back into the throne's realm. Backer houses rejoin with their fiefs;
        // a claimant's TEMPORARY cadet clan is dissolved back into the dynasty (its conquests pass to
        // the throne so nothing is orphaned), leaving the dynasty whole again.
        private void FoldBack(Kingdom rebel, Kingdom k)
        {
            if (rebel == null || k == null || rebel == k) return;
            try
            {
                if (rebel.IsAtWarWith(k)) Util.ThroneWar.WithInternalPeace(() => MakePeaceAction.Apply(rebel, k));

                Clan cadet = rebel.RulingClan;
                bool temp = IsTempClaimClan(cadet);

                // Backer houses (and a non-temp seceded house) rejoin the throne with their lands.
                foreach (Clan c in rebel.Clans.ToList())
                    if (!(temp && c == cadet))
                        try { ChangeKingdomAction.ApplyByJoinToKingdom(c, k, default(CampaignTime), false); } catch { }

                // The cadet clan's conquests pass to the throne, then the cadet dissolves into the dynasty.
                if (temp && cadet != null)
                {
                    Hero throneLord = k.RulingClan?.Leader ?? k.Leader;
                    if (throneLord != null)
                        foreach (Settlement s in cadet.Settlements.ToList())
                            try { ChangeOwnerOfSettlementAction.ApplyByDefault(throneLord, s); } catch { }
                    Util.ClaimantClan.Dissolve(cadet);   // returns the prince to the dynasty, destroys the cadet
                }

                if (rebel != k && !rebel.Settlements.Any() && !rebel.Clans.Any())
                    try { DestroyKingdomAction.Apply(rebel); } catch { }
            }
            catch { }
        }

        private List<Tuple<Kingdom, Hero>> GetBreakaways(int i)
        {
            var result = new List<Tuple<Kingdom, Hero>>();
            if (i < 0 || i >= _warBreakaways.Count || string.IsNullOrEmpty(_warBreakaways[i])) return result;
            foreach (string pair in _warBreakaways[i].Split(','))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2) continue;
                result.Add(Tuple.Create(Kingdom.All.FirstOrDefault(x => x.StringId == kv[0]), FindHero(kv[1])));
            }
            return result;
        }

        private void RemoveBreakaway(int i, Hero claimant)
        {
            if (i < 0 || i >= _warBreakaways.Count || claimant == null) return;
            var kept = GetBreakaways(i)
                .Where(p => p.Item2 != claimant && p.Item1 != null && p.Item2 != null)
                .Select(p => p.Item1.StringId + "=" + p.Item2.StringId);
            _warBreakaways[i] = string.Join(",", kept);
        }

        private List<Clan> AlignedBackerClans(Hero c, Kingdom k)
        {
            var result = new List<Clan>();
            if (c == null) return result;
            for (int a = 0; a < _alignHeroIds.Count; a++)
                if (_alignTargetIds[a] == c.StringId)
                {
                    Hero lord = FindHero(_alignHeroIds[a]);
                    Clan cl = lord?.Clan;
                    if (cl != null && !cl.IsEliminated && cl != k.RulingClan && !cl.IsMinorFaction
                        && cl.Leader == lord && cl.Kingdom == k)
                        result.Add(cl);
                }
            return result;
        }

        private Hero GetAlignment(Hero lord)
        {
            if (lord == null) return null;
            int idx = _alignHeroIds.IndexOf(lord.StringId);
            return idx < 0 ? null : FindHero(_alignTargetIds[idx]);
        }

        private static bool HasFief(Clan c)
            => c?.Settlements != null && c.Settlements.Any(s => s.IsTown || s.IsCastle);

        private float KingdomStrength(Kingdom kg)
        {
            if (kg == null) return 0f;
            float s = 0f;
            foreach (Clan c in kg.Clans) if (!c.IsEliminated) s += c.CurrentTotalStrength;
            return s;
        }

        private float ThroneStrength(Kingdom k) => KingdomStrength(k);

        // hostile: the incumbent fought to the end and lost by force. Only then does the victor
        // pronounce a fate (kill/banish/pardon). An incumbent who conceded — persuaded, bought
        // off, out-voted, brokered — abdicates with honour and keeps his place at court; putting
        // HIM to the decree was both senseless (he backed the winner) and, worse, the banish
        // branch could exile the newly crowned winner along with him (see CreateExileHouse).
        private void ResolveCrisis(Kingdom k, Hero winner, string how, bool hostile = false)
        {
            int i = CrisisIndex(k);
            if (i < 0 || winner == null || !winner.IsAlive) { if (i >= 0) RemoveCrisis(i); return; }

            float pct = GetSupportPercent(k, winner);
            var claimants = GetClaimants(k);

            // The sitting ruler, if they contested and lost, is now deposed.
            string incumbentId = (i < _incumbentIds.Count) ? _incumbentIds[i] : "";
            Hero deposed = string.IsNullOrEmpty(incumbentId) ? null : FindHero(incumbentId);

            // Defensive: if the winner is still in a temporary cadet clan (a path that did not fold
            // back first), dissolve it so he is crowned within the dynasty, not as a throwaway house.
            if (IsTempClaimClan(winner.Clan)) Util.ClaimantClan.Dissolve(winner.Clan);

            // Crown the winner.
            if (winner != k.Leader)
            {
                if (winner.Clan != null && winner.Clan != k.RulingClan)
                    ChangeRulingClanAction.Apply(k, winner.Clan);
                if (winner.Clan != null && winner.Clan.Leader != winner)
                    ChangeClanLeaderAction.ApplyWithSelectedNewLeader(winner.Clan, winner);
            }

            // An active king governs at once: fill his imperial council immediately rather than
            // leaving it vacant until the next weekly fill.
            if (k.Leader != null && k.Leader != Hero.MainHero)
                CouncilBehavior.Instance?.EnsureCouncil(k.Leader);

            LegitimacyBehavior.Instance?.SetLegitimacy(winner, 45f + pct * 0.4f);
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -20f, "scars of the succession crisis");

            // Court realigns: backers rewarded, opponents resented.
            for (int a = 0; a < _alignHeroIds.Count; a++)
            {
                Hero lord = FindHero(_alignHeroIds[a]);
                if (lord == null || !lord.IsAlive || lord == winner) continue;
                if (!claimants.Any(c => c.StringId == _alignTargetIds[a] || c == lord)) continue;
                int delta = _alignTargetIds[a] == winner.StringId ? 10 : -5;
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(lord, winner, delta);
            }

            // Defeated rivals do not forgive.
            foreach (Hero c in claimants.Where(c => c != winner && c.IsAlive))
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(c, winner, -20);

            Notify($"{winner.Name} is crowned ruler of {k.Name} by {how}. The crisis is over.", false);

            // The new sovereign proclaims their accession to the vassals of the realm.
            if (winner != Hero.MainHero && Hero.MainHero?.Clan?.Kingdom == k)
                RoyalFarmaan.FromRuler(k, "Proclamation of Accession",
                    $"Let it be known throughout the realm that {winner.Name} now sits upon the throne of {k.Name}. " +
                    "All lords and mansabdars are called to renew their oaths and to serve the new sovereign faithfully.",
                    "I renew my oath");

            ClearCrisisData(i, claimants);
            SnapshotRulers();

            // The victor decides the fate of the deposed emperor — but only over one who fought
            // to the end. A ruler who stood down keeps his honour, his house and a seat at court.
            if (deposed != null && deposed.IsAlive && deposed != winner && deposed.Clan != null)
            {
                if (hostile)
                {
                    if (winner == Hero.MainHero) PromptDeposedFate(k, winner, deposed);
                    else AiDecideDeposedFate(k, winner, deposed);
                }
                else
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(winner, deposed, 5);
                    OpinionBehavior.Instance?.AddOpinion(winner, deposed, Util.OpinionMath.OpinionType.Favor);
                    if (Hero.MainHero?.Clan?.Kingdom == k)
                        Notify($"{deposed.Name}, having stood down with grace, keeps his honour and a place at the court of {winner.Name}.", false);
                }
            }
        }

        // ── Fate of a deposed emperor: kill, banish, or pardon ─────────────────────

        private void PromptDeposedFate(Kingdom k, Hero winner, Hero deposed)
        {
            var elements = new List<InquiryElement>
            {
                new InquiryElement("kill", "Execute him", null, true,
                    "Royal blood spilled. The court fears you (+authority, +legitimacy) — but kin and chroniclers will not forget the act."),
                new InquiryElement("banish", "Banish him", null, true,
                    "He is stripped of all and cast out of Hindostan, never to trouble you again. No glory, no shame."),
                new InquiryElement("pardon", "Pardon him", null, true,
                    "A magnanimous mercy that wins goodwill — but a living rival, once an emperor, may yet rise against you."),
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"The Fate of {deposed.Name}",
                $"{deposed.Name} has been deposed as ruler of {k.Name}. As the new sovereign, the decision is yours.",
                elements, false, 1, 1, "Pronounce the decree", "",
                selected =>
                {
                    string choice = selected != null && selected.Count > 0 ? (string)selected[0].Identifier : "pardon";
                    ApplyDeposedFate(k, winner, deposed, choice);
                },
                null, ""), true);
        }

        private void AiDecideDeposedFate(Kingdom k, Hero winner, Hero deposed)
        {
            int rel = CharacterRelationManager.GetHeroRelation(winner, deposed);
            float roll = MBRandom.RandomFloat;
            // Bitter rivals are likeliest to execute; most depose into exile; a few show mercy.
            string choice = (rel < -20 && roll < 0.45f) ? "kill"
                          : (roll < 0.80f) ? "banish"
                          : "pardon";
            ApplyDeposedFate(k, winner, deposed, choice);
        }

        private void ApplyDeposedFate(Kingdom k, Hero winner, Hero deposed, string choice)
        {
            if (deposed == null || !deposed.IsAlive) return;
            bool playerSees = winner == Hero.MainHero || deposed == Hero.MainHero
                              || Hero.MainHero?.Clan?.Kingdom == k;

            switch (choice)
            {
                case "kill":
                    KillCharacterAction.ApplyByExecution(deposed, winner, playerSees, true);
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 8f, "the deposed emperor executed");
                    LegitimacyBehavior.Instance?.ModifyLegitimacy(winner, 5f, "no rival remains");
                    if (playerSees) Notify($"{deposed.Name} is put to death. The throne of {k.Name} stands unchallenged — for now.", false);
                    break;

                case "banish":
                {
                    // Stripped of crown, wealth and standing — but he LIVES, departing into exile with his
                    // family to found a destitute house that may yet scheme another day. Never "lost".
                    int purse = Math.Max(0, deposed.Gold - 1000);
                    if (purse > 0 && winner != null) GiveGoldAction.ApplyBetweenCharacters(deposed, winner, purse, true);

                    if (deposed.Clan == null || deposed.Clan == winner.Clan)
                    {
                        // A prince of the victor's own dynasty: split off a new exile house with his kin.
                        Clan exile = Util.ClaimantClan.CreateExileHouse(deposed);
                        if (exile != null)
                        {
                            if (exile.Influence > 0) ChangeClanInfluenceAction.Apply(exile, -exile.Influence);
                            if (exile.Kingdom != null) ChangeKingdomAction.ApplyByLeaveKingdom(exile, playerSees);
                        }
                    }
                    else
                    {
                        // He already heads his own house: strip its fiefs to the victor, then cast it out.
                        foreach (Settlement s in deposed.Clan.Settlements.Where(s => s.IsTown || s.IsCastle).ToList())
                            if (winner != null) try { ChangeOwnerOfSettlementAction.ApplyByGift(s, winner); } catch { }
                        if (deposed.Clan.Influence > 0) ChangeClanInfluenceAction.Apply(deposed.Clan, -deposed.Clan.Influence);
                        if (deposed.Clan.Kingdom != null) ChangeKingdomAction.ApplyByLeaveKingdom(deposed.Clan, playerSees);
                    }
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 3f, "the deposed emperor banished");
                    if (playerSees) Notify($"{deposed.Name} is banished from Hindostan — stripped of crown, wealth and standing, he departs with his family to found a house in exile.", false);
                    break;
                }

                default: // pardon
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(winner, deposed, 15);
                    LegitimacyBehavior.Instance?.ModifyLegitimacy(winner, -3f, "mercy read as weakness");
                    if (playerSees) Notify($"{deposed.Name} is pardoned and keeps a place at court. A merciful king — and a watchful rival.", false);
                    break;
            }
        }

        // ── Game menus ────────────────────────────────────────────────────────────

        private Kingdom MenuKingdom()
            => Settlement.CurrentSettlement?.OwnerClan?.Kingdom;

        private void RegisterMenus(CampaignGameStarter starter)
        {
            bool EntryCondition(MenuCallbackArgs args)
            {
                args.optionLeaveType = GameMenuOption.LeaveType.Manage;
                Kingdom k = MenuKingdom();
                return k != null && GetCrisisState(k) != CrisisState.None;
            }

            starter.AddGameMenuOption("town", "hindostan_succ_town", "{=!}Attend to the succession crisis",
                EntryCondition, args => GameMenu.SwitchToMenu("hindostan_succession"), false, 5);
            starter.AddGameMenuOption("castle", "hindostan_succ_castle", "{=!}Attend to the succession crisis",
                EntryCondition, args => GameMenu.SwitchToMenu("hindostan_succession"), false, 5);

            starter.AddGameMenu("hindostan_succession", "{=!}{HINDOSTAN_SUC_TEXT}", SuccessionMenuInit);

            for (int slot = 0; slot < 3; slot++)
            {
                int s = slot; // capture
                starter.AddGameMenuOption("hindostan_succession", $"hindostan_succ_back_{s}",
                    "{=!}{SUC_BACK_" + s + "}",
                    args =>
                    {
                        args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                        Kingdom k = MenuKingdom();
                        if (k == null) return false;
                        var state = GetCrisisState(k);
                        if (state != CrisisState.Brewing && state != CrisisState.Active) return false;
                        var cs = GetClaimants(k);
                        if (s >= cs.Count || cs[s] == Hero.MainHero) return false;
                        MBTextManager.SetTextVariable("SUC_BACK_" + s,
                            $"Back {cs[s].Name} (50 influence)", false);
                        return true;
                    },
                    args => { PlayerBack(s); SuccessionMenuInit(args); });
            }

            starter.AddGameMenuOption("hindostan_succession", "hindostan_succ_nominate",
                "{=!}Put forward a claimant (Amir-ul-Umara, 100 influence)",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Manage;
                    return PlayerCanNominate(out _);
                },
                args => { PlayerNominate(); SuccessionMenuInit(args); });

            starter.AddGameMenuOption("hindostan_succession", "hindostan_succ_persuade",
                "{=!}Persuade a rival claimant to stand down",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
                    return PlayerCanPersuade(out _);
                },
                args => { PlayerPersuade(); SuccessionMenuInit(args); });

            starter.AddGameMenuOption("hindostan_succession", "hindostan_succ_broker",
                "{=!}Broker the succession as Amir-ul-Umara (200 influence)",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Manage;
                    return PlayerCanBroker(out _);
                },
                args => { PlayerBroker(); SuccessionMenuInit(args); });

            starter.AddGameMenuOption("hindostan_succession", "hindostan_succ_leave", "{=!}Leave",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu(
                    Settlement.CurrentSettlement != null && Settlement.CurrentSettlement.IsTown ? "town" : "castle"),
                true);
        }

        private void SuccessionMenuInit(MenuCallbackArgs args)
        {
            Kingdom k = MenuKingdom();
            var sb = new StringBuilder();
            sb.AppendLine("The War of Princes");
            sb.AppendLine(" ");

            if (k != null)
            {
                string law = SuccessionLawBehavior.LawName(Law(k));
                Hero wali = SuccessionLawBehavior.Instance?.GetWaliAhd(k);
                sb.AppendLine($"Law of succession: {law}" + (wali != null ? $"    Wali Ahd: {wali.Name}" : ""));
                sb.AppendLine(" ");
            }

            if (k == null || GetCrisisState(k) == CrisisState.None)
                sb.AppendLine("The succession here is settled.");
            else
            {
                int i = CrisisIndex(k);
                var state = (CrisisState)_crisisStates[i];
                string phase = state switch
                {
                    CrisisState.Brewing => "Whispers in the court — factions form in secret.",
                    CrisisState.Active => "The throne is openly contested.",
                    CrisisState.CivilWar => "CIVIL WAR — the claimants' hosts clash in the field.",
                    _ => "",
                };
                sb.AppendLine($"Realm: {k.Name}    Day {(int)_crisisDays[i]} of the crisis");
                sb.AppendLine(phase);
                sb.AppendLine(" ");
                sb.AppendLine("— The Claimants —");
                foreach (Hero c in GetClaimants(k).OrderByDescending(c => GetSupport(c)))
                {
                    string you = c == Hero.MainHero ? "   <-- YOU" : "";
                    sb.AppendLine($"  {c.Name,-28} {GetSupportPercent(k, c),5:0.#}% support  ({CountBackers(c)} lords){you}");
                }
                if (state == CrisisState.Active)
                {
                    sb.AppendLine(" ");
                    sb.AppendLine($"If no claimant holds 55% by day {(int)DeadlineDays}, the realm falls into civil war.");
                }
            }

            MBTextManager.SetTextVariable("HINDOSTAN_SUC_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }

        private void PlayerBack(int slot)
        {
            Kingdom k = MenuKingdom();
            if (k == null) return;
            var cs = GetClaimants(k);
            if (slot >= cs.Count) return;
            Hero claimant = cs[slot];

            if (Clan.PlayerClan.Influence < 50f)
            { Notify("You lack the influence (50) to sway the court.", true); return; }

            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -50f);
            AddSupport(claimant, 10f);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, claimant, 5);
            SetAlignment(Hero.MainHero, claimant);
            Notify($"You pledge your standing to {claimant.Name}. Their cause strengthens.", false);
        }

        // ── Put forward your own claimant (Amir-ul-Umara) ──────────────────────────────
        public bool PlayerCanNominate(out string reason)
        {
            reason = "";
            Kingdom k = MenuKingdom();
            var state = k == null ? CrisisState.None : GetCrisisState(k);
            if (state != CrisisState.Brewing && state != CrisisState.Active)
            { reason = "A claimant can be put forward only while the succession is still being contested."; return false; }
            if ((MansabdariBehavior.Instance?.GetRankIndex(Clan.PlayerClan) ?? 0) < 6)
            { reason = "Only the Amir-ul-Umara may raise a lord to the lists of claimants."; return false; }
            if (Clan.PlayerClan.Influence < 100f)
            { reason = "Putting a claimant forward costs 100 influence."; return false; }
            return true;
        }

        private void PlayerNominate()
        {
            if (!PlayerCanNominate(out string reason)) { Notify(reason, true); return; }
            Kingdom k = MenuKingdom();
            var current = new HashSet<Hero>(GetClaimants(k));
            var candidates = k.Clans
                .Where(c => !c.IsEliminated && !c.IsMinorFaction && c.Leader != null && !c.Leader.IsChild && !current.Contains(c.Leader))
                .OrderByDescending(ClanPower).Take(8)
                .Select(c => new InquiryElement(c.Leader,
                    $"{c.Leader.Name} — {MansabdariBehavior.Instance?.GetTitle(c) ?? "a lord"}", null, true,
                    $"A power of {k.Name}: {c.Settlements.Count(s => s.IsTown || s.IsCastle)} fief(s), {c.Influence:0} influence. Press his claim and lend him your weight."))
                .ToList();
            if (candidates.Count == 0) { Notify("There is no other lord of standing to put forward.", true); return; }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Put Forward a Claimant for {k.Name}",
                "Name a powerful lord as a claimant to the throne and throw your support behind him.",
                candidates, true, 1, 1, "Proclaim him", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Hero h) DoNominate(k, h); },
                _ => { }, "", false), false, false);
        }

        private void DoNominate(Kingdom k, Hero hero)
        {
            if (k == null || hero == null) return;
            if (Clan.PlayerClan.Influence < 100f) { Notify("You lack the influence (100).", true); return; }
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -100f);
            AddClaimant(k, hero);
            AddSupport(hero, 20f);
            SetAlignment(Hero.MainHero, hero);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, hero, 10);
            Notify($"You put {hero.Name} forward as a claimant to the throne of {k.Name}, and pledge your support to his cause.", false);
        }

        // Add a claimant to an ongoing crisis, keeping the contested pool at no more than three by
        // dropping the weakest if it is already full.
        private void AddClaimant(Kingdom k, Hero hero)
        {
            int i = CrisisIndex(k);
            if (i < 0 || hero == null) return;
            var ids = string.IsNullOrEmpty(_claimantIds[i]) ? new List<string>() : _claimantIds[i].Split(',').ToList();
            if (ids.Contains(hero.StringId)) return;
            if (ids.Count >= 3)
            {
                Hero weakest = ids.Select(FindHero).Where(h => h != null).OrderBy(GetSupport).FirstOrDefault();
                if (weakest != null) ids.Remove(weakest.StringId);
            }
            ids.Add(hero.StringId);
            _claimantIds[i] = string.Join(",", ids);
            SetSupport(hero, Math.Max(GetSupport(hero), 25f));
            LegitimacyBehavior.Instance?.SetLegitimacy(hero, 40f);
        }

        // ── Persuade a rival claimant to stand down (a real deal: gold / influence / troops / a fief) ──────
        // You back a claimant; a rival is bought off — coin, standing, men, or land — and removed from the
        // contest, his following thrown behind YOUR candidate. The going price in gold is the realm's worth:
        // (towns + castles) × 50,000.
        public bool PlayerCanPersuade(out string reason)
        {
            reason = "";
            Kingdom k = MenuKingdom();
            var state = k == null ? CrisisState.None : GetCrisisState(k);
            if (state != CrisisState.Brewing && state != CrisisState.Active)
            { reason = "A claimant can be bought off only while the succession is still being contested at court."; return false; }
            Hero myCand = GetAlignment(Hero.MainHero);
            if (myCand == null || !GetClaimants(k).Contains(myCand))
            { reason = "First throw your weight behind a claimant — then you may clear his rivals from the field."; return false; }
            if (!GetClaimants(k).Any(c => c != myCand && c != Hero.MainHero))
            { reason = "There is no rival claimant left to persuade."; return false; }
            return true;
        }

        private int KingdomFiefCount(Kingdom k)
            => k?.Settlements?.Count(s => s != null && (s.IsTown || s.IsCastle)) ?? 0;

        private void PlayerPersuade()
        {
            if (!PlayerCanPersuade(out string reason)) { Notify(reason, true); return; }
            Kingdom k = MenuKingdom();
            Hero myCand = GetAlignment(Hero.MainHero);
            var rivals = GetClaimants(k).Where(c => c != myCand && c != Hero.MainHero)
                                        .OrderBy(GetSupport).ToList(); // weakest first
            if (rivals.Count == 0) { Notify("There is no rival to persuade.", true); return; }

            var elements = rivals.Select(c => new InquiryElement(c,
                $"{c.Name} — {GetSupportPercent(k, c):0.#}% support",
                null, true, $"Buy off {c.Name} so he stands down and backs {myCand.Name}.")).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Persuade a Claimant to Stand Down — {k.Name}",
                $"Whom shall you buy off, that he withdraw his claim and back {myCand.Name}?",
                elements, true, 1, 1, "Choose the terms", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Hero rival) OfferTerms(k, rival, myCand); },
                _ => { }, "", false), false, false);
        }

        // A bribe in progress: which resources the player chose to put on the table and how much of each.
        // Transient (UI only) — never serialized.
        private class Offer
        {
            public Kingdom K; public Hero Rival; public Hero MyCand;
            public int Fiefs; public int BaseGold;
            public bool WantGold, WantInfluence, WantTroops, WantFief;
            public int Gold, Influence, Troops; public Settlement Fief;
        }

        // Step 1 — pick WHICH things to offer (any combination). Amounts are set next, then the rival
        // weighs the whole purse against the worth of the claim he is asked to give up: it is never certain.
        private void OfferTerms(Kingdom k, Hero rival, Hero myCand)
        {
            if (k == null || rival == null || myCand == null) return;
            int fiefs = Math.Max(1, KingdomFiefCount(k));
            var o = new Offer { K = k, Rival = rival, MyCand = myCand, Fiefs = fiefs, BaseGold = fiefs * 50000 };

            int myGold = Hero.MainHero?.Gold ?? 0;
            float myInf = Clan.PlayerClan?.Influence ?? 0f;
            int myRegulars = MobileParty.MainParty?.MemberRoster?.TotalRegulars ?? 0;
            bool rivalHasParty = rival.PartyBelongedTo != null;
            int myFiefCount = Clan.PlayerClan?.Settlements?.Count(s => s.IsTown || s.IsCastle) ?? 0;

            var opts = new List<InquiryElement>
            {
                new InquiryElement("gold", "Gold", null, myGold > 0,
                    myGold > 0 ? $"A purse of dinars (you hold {myGold:n0}; the going rate is {o.BaseGold:n0})." : "You have no coin."),
                new InquiryElement("influence", "Influence at court", null, myInf >= 1f,
                    myInf >= 1f ? $"Court influence (you hold {myInf:0})." : "You have no influence to spend."),
                new InquiryElement("troops", "A gift of men", null, rivalHasParty && myRegulars > 1,
                    rivalHasParty ? (myRegulars > 1 ? $"Soldiers for his banner (you lead {myRegulars})." : "Your host is too small.") : "He keeps no host to receive men."),
                new InquiryElement("fief", "A fief of your own", null, myFiefCount > 0,
                    myFiefCount > 0 ? "Grant him one of your towns or castles." : "You hold no fief to grant."),
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Terms for {rival.Name}",
                $"What will you put on the table to make {rival.Name} withdraw in favour of {myCand.Name}? " +
                "Choose all you mean to offer — you will set each amount next.",
                opts, true, 1, opts.Count, "Set the amounts", "Cancel",
                sel =>
                {
                    if (sel == null || sel.Count == 0) return;
                    foreach (var e in sel)
                        switch (e.Identifier as string)
                        {
                            case "gold": o.WantGold = true; break;
                            case "influence": o.WantInfluence = true; break;
                            case "troops": o.WantTroops = true; break;
                            case "fief": o.WantFief = true; break;
                        }
                    CollectGold(o);
                },
                _ => { }, "", false), false, false);
        }

        // Step 2 — set each amount in turn (numeric entry, prefilled with the going rate, capped at what you
        // hold). Each collector hands off to the next, so only the chosen resources are asked for.
        private void CollectGold(Offer o)
        {
            if (!o.WantGold) { CollectInfluence(o); return; }
            int myGold = Hero.MainHero?.Gold ?? 0;
            PromptAmount("Purse of Gold", $"How many dinars for {o.Rival.Name}? (going rate {o.BaseGold:n0}; you hold {myGold:n0})",
                Math.Min(o.BaseGold, myGold), 0, myGold, v => { o.Gold = v; CollectInfluence(o); }, () => { });
        }

        private void CollectInfluence(Offer o)
        {
            if (!o.WantInfluence) { CollectTroops(o); return; }
            int myInf = (int)(Clan.PlayerClan?.Influence ?? 0f);
            PromptAmount("Court Influence", $"How much influence for {o.Rival.Name}? (you hold {myInf})",
                Math.Min(o.Fiefs * 25, myInf), 0, myInf, v => { o.Influence = v; CollectTroops(o); }, () => { });
        }

        private void CollectTroops(Offer o)
        {
            if (!o.WantTroops) { CollectFief(o); return; }
            int spare = (MobileParty.MainParty?.MemberRoster?.TotalRegulars ?? 1) - 1;
            if (spare < 1) { CollectFief(o); return; }
            PromptAmount("Gift of Men", $"How many soldiers to send to {o.Rival.Name}'s banner? (you can spare {spare})",
                Math.Min(o.Fiefs * 25, spare), 0, spare, v => { o.Troops = v; CollectFief(o); }, () => { });
        }

        private void CollectFief(Offer o)
        {
            if (!o.WantFief) { ConfirmOffer(o); return; }
            var myFiefs = Clan.PlayerClan?.Settlements?.Where(s => s.IsTown || s.IsCastle).ToList() ?? new List<Settlement>();
            if (myFiefs.Count == 0) { ConfirmOffer(o); return; }
            var elements = myFiefs.Select(s => new InquiryElement(s, $"{s.Name} ({(s.IsTown ? "town" : "castle")})", null, true,
                $"Grant {s.Name} to {o.Rival.Name}.")).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "A Fief to Grant", $"Which holding will you grant {o.Rival.Name}?",
                elements, true, 1, 1, "Choose", "None",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Settlement s) o.Fief = s; ConfirmOffer(o); },
                _ => { ConfirmOffer(o); }, "", false), false, false);
        }

        // A numeric prompt that prefills a suggestion and refuses anything outside [min, max].
        private void PromptAmount(string title, string prompt, int suggested, int min, int max, Action<int> onOk, Action onCancel)
        {
            suggested = Math.Max(min, Math.Min(max, suggested));
            InformationManager.ShowTextInquiry(new TextInquiryData(
                title, prompt, true, true, "Set", "Cancel",
                s => onOk(int.TryParse(s, out int v) ? Math.Max(min, Math.Min(max, v)) : suggested),
                () => onCancel?.Invoke(),
                false,
                s => new Tuple<bool, string>(int.TryParse(s, out int v) && v >= min && v <= max, $"Enter a whole number between {min} and {max}."),
                "", suggested.ToString()), false, false);
        }

        // Step 3 — weigh the whole offer against the rival's price and show the odds before he is asked.
        private void ConfirmOffer(Offer o)
        {
            if (o.Gold <= 0 && o.Influence <= 0 && o.Troops <= 0 && o.Fief == null)
            { Notify("You laid nothing on the table.", true); return; }

            float fiefGold = o.Fief == null ? 0f : (o.Fief.IsTown ? o.BaseGold : o.BaseGold * 0.5f);
            float offerValue = SuccessionLawMath.OfferValue(o.Gold, o.Influence, o.Troops, fiefGold);
            float price = SuccessionLawMath.RivalPrice(o.BaseGold, SupportFraction(o.K, o.Rival));
            int rel = CharacterRelationManager.GetHeroRelation(Hero.MainHero, o.Rival);
            float chance = SuccessionLawMath.PersuasionAcceptChance(offerValue, price, rel);

            var sb = new StringBuilder();
            if (o.Gold > 0) sb.Append($"{o.Gold:n0} dinars, ");
            if (o.Influence > 0) sb.Append($"{o.Influence} influence, ");
            if (o.Troops > 0) sb.Append($"{o.Troops} men, ");
            if (o.Fief != null) sb.Append($"the fief of {o.Fief.Name}, ");
            string parts = sb.ToString().TrimEnd(' ', ',');
            string mood = chance >= 0.66f ? "a tempting" : (chance >= 0.4f ? "a fair" : "a poor");

            InformationManager.ShowInquiry(new InquiryData(
                $"Offer to {o.Rival.Name}",
                $"You offer {parts}.\n\n{o.Rival.Name} judges this {mood} bargain — roughly a {chance * 100f:0}% chance he stands down. " +
                "Refuse, and your treasury is untouched, but he takes the affront and presses his claim the harder.\n\nPress the offer?",
                true, true, "Make the offer", "Withdraw",
                () => Util.TYTLog.Guard("Succession.Persuade", () => ResolveOffer(o, chance)),
                () => { }), false);
        }

        // Step 4 — the rival decides. On acceptance the goods change hands and he joins your candidate; on
        // refusal nothing is paid, his standing hardens, and relations sour.
        private void ResolveOffer(Offer o, float chance)
        {
            if (MBRandom.RandomFloat >= chance)
            {
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, o.Rival, -5);
                AddSupport(o.Rival, 3f);
                Notify($"{o.Rival.Name} spurns your terms and presses his claim the harder.", true);
                return;
            }

            if (o.Gold > 0 && (Hero.MainHero?.Gold ?? 0) >= o.Gold)
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, o.Rival, o.Gold, true);
            if (o.Influence > 0 && (Clan.PlayerClan?.Influence ?? 0f) >= o.Influence)
                ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -o.Influence);
            if (o.Troops > 0) GiftTroops(o.Rival, o.Troops);
            if (o.Fief != null)
                try { ChangeOwnerOfSettlementAction.ApplyByGift(o.Fief, o.Rival); }
                catch (Exception e) { Util.TYTLog.Error("Persuade fief gift failed", e); }

            ConvertClaimantToBacker(o.K, o.Rival, o.MyCand);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, o.Rival, 10);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(o.Rival, o.MyCand, 10);
            Notify($"{o.Rival.Name} accepts your terms, withdraws his claim, and throws his support behind {o.MyCand.Name}.", false);

            var remaining = GetClaimants(o.K);
            if (remaining.Count == 1)
                ResolveCrisis(o.K, remaining[0], "his rivals bought off and reconciled");
        }

        // The rival's share of the contest's total claim-support (0..1) — drives how dear he holds out.
        private float SupportFraction(Kingdom k, Hero rival)
        {
            var claimants = GetClaimants(k);
            if (claimants == null || claimants.Count == 0) return 1f;
            float total = claimants.Sum(c => Math.Max(0f, GetSupport(c)));
            if (total <= 0f) return 1f / claimants.Count;
            return Math.Max(0f, GetSupport(rival)) / total;
        }

        // Move regular soldiers from the player's host to a rival's, keeping at least one man in your own.
        private bool GiftTroops(Hero rival, int count)
        {
            MobileParty mine = MobileParty.MainParty, his = rival?.PartyBelongedTo;
            if (mine == null || his == null) return false;
            int budget = Math.Min(count, mine.MemberRoster.TotalRegulars - 1);
            if (budget <= 0) return false;

            var moves = new List<(CharacterObject ch, int n)>();
            foreach (TroopRosterElement e in mine.MemberRoster.GetTroopRoster())
            {
                if (budget <= 0) break;
                if (e.Character == null || e.Character.IsHero) continue;
                int take = Math.Min(budget, e.Number);
                if (take > 0) { moves.Add((e.Character, take)); budget -= take; }
            }
            foreach (var (ch, n) in moves)
            {
                mine.MemberRoster.AddToCounts(ch, -n);
                his.MemberRoster.AddToCounts(ch, n);
            }
            return moves.Count > 0;
        }

        // Remove a claimant from the contest and swing him (and his backers) behind the player's candidate.
        private void ConvertClaimantToBacker(Kingdom k, Hero rival, Hero myCand)
        {
            int i = CrisisIndex(k);
            if (i < 0 || rival == null || myCand == null) return;
            var ids = _claimantIds[i].Split(',').ToList();
            if (!ids.Remove(rival.StringId)) return;
            _claimantIds[i] = string.Join(",", ids);

            float gift = GetSupport(rival);
            SetSupport(rival, 0f);
            AddSupport(myCand, gift * 0.5f);   // half his following rallies to your candidate
            SetAlignment(rival, myCand);
            for (int a = 0; a < _alignTargetIds.Count; a++)
                if (_alignTargetIds[a] == rival.StringId) _alignTargetIds[a] = myCand.StringId;
        }

        public bool PlayerCanBroker(out string reason)
        {
            reason = "";
            Kingdom k = MenuKingdom();
            if (k == null || GetCrisisState(k) != CrisisState.Active)
            { reason = "Successions are brokered only once the crisis is in the open."; return false; }
            if ((MansabdariBehavior.Instance?.GetRankIndex(Clan.PlayerClan) ?? 0) < 6)
            { reason = "Only the Amir-ul-Umara may play kingmaker."; return false; }
            Hero leader = LeadingClaimant(k);
            if (leader == null || GetSupportPercent(k, leader) < 45f)
            { reason = "No claimant yet commands enough support (45%) to be installed."; return false; }
            if (Clan.PlayerClan.Influence < 200f)
            { reason = "Brokering a succession requires 200 influence."; return false; }
            return true;
        }

        // As Amir-ul-Umara the player does not merely anoint his favourite — he dictates the whole
        // settlement: crown the front-runner, install another claimant outright, broker a compromise
        // that binds the rivals, or partition the realm so a rival departs in peace with lands of his own.
        private void PlayerBroker()
        {
            if (!PlayerCanBroker(out string reason)) { Notify(reason, true); return; }
            Kingdom k = MenuKingdom();
            var claimants = GetClaimants(k).OrderByDescending(c => GetSupport(c)).ToList();
            Hero front = claimants.FirstOrDefault();
            if (k == null || front == null) return;
            Hero rival = claimants.FirstOrDefault(c => c != front);

            var options = new List<InquiryElement>
            {
                new InquiryElement("install_leader",
                    $"Crown {front.Name}, the front-runner  (200)", null, true,
                    $"Confirm the lords' choice. {front.Name} takes the throne with broad support."),
            };
            foreach (Hero c in claimants.Where(c => c != front))
                options.Add(new InquiryElement("install:" + c.StringId,
                    $"Crown {c.Name} over the front-runner  (300)", null, true,
                    $"Impose {c.Name} against the court's lean — the front-runner will resent it."));
            if (rival != null)
            {
                options.Add(new InquiryElement("compromise",
                    $"Broker a compromise  (350)", null, true,
                    $"Crown {front.Name}, but honour {rival.Name} with rank and reconciliation — the realm is spared a lasting feud."));
                options.Add(new InquiryElement("partition",
                    $"Partition the realm  (400)", null, true,
                    $"{front.Name} keeps {k.Name}; {rival.Name} departs in peace to rule his own realm. Hindostan splits in two."));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Broker the Succession of {k.Name}",
                "As Amir-ul-Umara, the settlement is yours to dictate. How shall the crisis end?",
                options, true, 1, 1, "Decree it", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is string id) ExecuteBroker(k, id, front); },
                _ => { }, "", false), false, false);
        }

        private void ExecuteBroker(Kingdom k, string choice, Hero front)
        {
            if (k == null || front == null) return;
            var claimants = GetClaimants(k).OrderByDescending(c => GetSupport(c)).ToList();
            Hero rival = claimants.FirstOrDefault(c => c != front);

            int cost = choice == "partition" ? 400 : choice == "compromise" ? 350 : choice.StartsWith("install:") ? 300 : 200;
            if (Clan.PlayerClan.Influence < cost)
            { Notify($"That settlement needs {cost} influence (you have {Clan.PlayerClan.Influence:0}).", true); return; }
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -cost);

            if (choice == "compromise" && rival != null)
            {
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(front, rival, 30);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, rival, 10);
                MansabdariBehavior.Instance?.AdjustRank(rival.Clan, +1);   // honoured with rank
                LegitimacyBehavior.Instance?.SetLegitimacy(front, 72f);    // a settled, accepted accession
                RewardBroker(k, front);
                Notify($"You broker a compromise: {front.Name} takes the throne, and {rival.Name} is honoured and reconciled. The realm is spared a feud.", false);
                ResolveCrisis(k, front, "a brokered compromise");
            }
            else if (choice == "partition" && rival != null)
            {
                bool split = SecedePeacefully(k, rival);
                RewardBroker(k, front);
                Notify(split
                    ? $"The realm is partitioned: {front.Name} keeps {k.Name}, and {rival.Name} departs in peace to rule his own realm."
                    : $"The partition could not be arranged; {front.Name} alone is crowned.", false);
                ResolveCrisis(k, front, "a brokered partition");
            }
            else if (choice.StartsWith("install:"))
            {
                Hero pick = FindHero(choice.Substring("install:".Length)) ?? front;
                if (pick != front) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(front, pick, -15);
                RewardBroker(k, pick);
                Notify($"By your hand, {pick.Name} ascends the throne of {k.Name} — over the front-runner. All Hindostan knows who made this king.", false);
                ResolveCrisis(k, pick, "your brokerage");
            }
            else
            {
                RewardBroker(k, front);
                Notify($"By your hand, {front.Name} ascends the throne of {k.Name}. All Hindostan knows who made this king.", false);
                ResolveCrisis(k, front, "your brokerage");
            }
        }

        private void RewardBroker(Kingdom k, Hero winner)
        {
            if (winner != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, winner, 15);
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 10f, "succession brokered");
            if (Clan.PlayerClan.Kingdom != null)
                LegitimacyBehavior.Instance?.ModifyLegitimacy(Clan.PlayerClan.Kingdom.Leader, 15f, "kingmaker's prestige");
        }

        // A claimant and his backers depart the realm in PEACE, founding their own kingdom (a partition,
        // not a war). Reuses the secession primitives; the new realm is immediately at peace with the old.
        private bool SecedePeacefully(Kingdom k, Hero rival)
        {
            try
            {
                if (rival == null || RevoltCascadeBehavior.Instance == null) return false;
                var backers = AlignedBackerClans(rival, k);
                bool ownHouse = rival.Clan != null && rival.Clan != k.RulingClan && rival.Clan.Leader == rival
                                && !rival.Clan.IsMinorFaction && rival.Clan.Kingdom == k && HasFief(rival.Clan);
                if (!ownHouse && !backers.Any(HasFief)) return false;

                Clan host; bool temp = false;
                if (ownHouse) host = rival.Clan;
                else { host = Util.ClaimantClan.Create(rival, k.Culture); temp = true; if (host == null) return false; }

                Settlement seat = host.Settlements.FirstOrDefault(s => s.IsTown)
                    ?? host.Settlements.FirstOrDefault(s => s.IsCastle)
                    ?? host.Settlements.FirstOrDefault()
                    ?? backers.SelectMany(b => b.Settlements).FirstOrDefault()
                    ?? rival.HomeSettlement;
                if (seat == null) { if (temp) Util.ClaimantClan.Dissolve(host); return false; }

                Kingdom newK = RevoltCascadeBehavior.Instance.CreateRebelKingdom(host, seat, $"{rival.Name}'s Realm");
                if (newK == null) { if (temp) Util.ClaimantClan.Dissolve(host); return false; }

                foreach (Clan b in backers)
                    if (b != host && b.Kingdom == k)
                        try { ChangeKingdomAction.ApplyByJoinToKingdom(b, newK, default(CampaignTime), false); } catch { }

                if (newK.IsAtWarWith(k)) try { Util.ThroneWar.WithInternalPeace(() => MakePeaceAction.Apply(newK, k)); } catch { }
                return true;
            }
            catch (Exception e) { Util.TYTLog.Error("SecedePeacefully failed", e); return false; }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private int CrisisIndex(Kingdom k) => _crisisKingdomIds.IndexOf(k?.StringId ?? "");

        // ── Succession-law integration ─────────────────────────────────────────────
        private static SuccessionLaw Law(Kingdom k)
            => SuccessionLawBehavior.Instance?.GetLaw(k) ?? SuccessionLaw.Undeclared;

        // A formal law's lawful heir accedes without a war: the engine enforces him over the vanilla
        // king-selection, crowning within (or, for a magnate-election upset, across) clans.
        private void CleanAccession(Kingdom k, Hero heir, SuccessionLaw law)
        {
            if (k == null || heir == null || !heir.IsAlive || k.IsEliminated) return;
            if (k.Leader != heir)
            {
                if (heir.Clan != null && heir.Clan != k.RulingClan) ChangeRulingClanAction.Apply(k, heir.Clan);
                if (heir.Clan != null && heir.Clan.Leader != heir)
                    ChangeClanLeaderAction.ApplyWithSelectedNewLeader(heir.Clan, heir);
            }
            float legit = LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 60f;
            LegitimacyBehavior.Instance?.SetLegitimacy(heir, Math.Max(55f, legit));
            if (k.Leader != null && k.Leader != Hero.MainHero) CouncilBehavior.Instance?.EnsureCouncil(k.Leader);
            SnapshotRulers();
            string by = law == SuccessionLaw.AppointedHeir ? "by the late sovereign's appointment" : "by the law of primogeniture";
            Notify($"{heir.Name} accedes to the throne of {k.Name} {by}, the succession settled without a contest.", false);
        }

        // Naming an heir (Wali Ahd) raises his claim in a live crisis and cools his rivals' support.
        public void NoteHeirNamed(Kingdom k, Hero heir)
        {
            if (k == null || heir == null) return;
            int i = CrisisIndex(k);
            if (i < 0) return;
            AddClaimant(k, heir);
            AddSupport(heir, Tune.HeirSupportBoost);
            foreach (Hero c in GetClaimants(k)) if (c != heir) AddSupport(c, -5f);
        }

        // The election laws' resolution: each great house casts a weighted voice for its favoured claimant.
        private SuccessionLawMath.VoteResult RunMagnateVote(Kingdom k)
        {
            var candidates = GetClaimants(k);
            var ballots = new List<(string, float)>();
            if (k == null || candidates.Count == 0) return SuccessionLawMath.Tally(ballots);

            foreach (Clan clan in k.Clans.Where(c => !c.IsEliminated && c.Leader != null && !c.IsMinorFaction))
            {
                Hero elector = clan.Leader;
                Hero best = null; float bestPref = float.MinValue;
                foreach (Hero c in candidates)
                {
                    int rel = CharacterRelationManager.GetHeroRelation(elector, c);
                    bool sameRel = ReligionBehavior.Instance != null &&
                                   ReligionBehavior.Instance.GetReligion(elector) == ReligionBehavior.Instance.GetReligion(c);
                    float pref = SuccessionLawMath.CandidatePreference(rel, sameRel, c.Clan == k.RulingClan, DynastyFavour);
                    if (c == elector) pref += 1000f; // a candidate always votes for himself
                    if (pref > bestPref) { bestPref = pref; best = c; }
                }
                if (best != null)
                {
                    float w = SuccessionLawMath.ElectorWeight(
                        MansabdariBehavior.Instance?.GetRankIndex(clan) ?? 0, (int)clan.Tier);
                    ballots.Add((best.StringId, w));
                }
            }
            return SuccessionLawMath.Tally(ballots);
        }

        private static Hero FindHero(string id)
            => Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id)
            ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => h.StringId == id);

        private Hero LeadingClaimant(Kingdom k)
            => GetClaimants(k).OrderByDescending(c => GetSupport(c)).FirstOrDefault();

        // Highest-ranked clan leader in the realm, the court's natural powerbroker.
        private Hero TopAmir(Kingdom k, Hero exclude)
            => k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != exclude && !c.IsMinorFaction)
                .OrderByDescending(c => MansabdariBehavior.Instance?.GetRankIndex(c) ?? 0)
                .ThenByDescending(c => c.Influence)
                .FirstOrDefault()?.Leader;

        private int CountBackers(Hero claimant)
        {
            int n = 0;
            for (int i = 0; i < _alignTargetIds.Count; i++)
                if (_alignTargetIds[i] == claimant.StringId) n++;
            return n;
        }

        private float BlocStrength(Hero claimant)
        {
            float s = MansabdariBehavior.GetClanTotalTroops(claimant.Clan);
            for (int i = 0; i < _alignHeroIds.Count; i++)
                if (_alignTargetIds[i] == claimant.StringId)
                    s += MansabdariBehavior.GetClanTotalTroops(FindHero(_alignHeroIds[i])?.Clan);
            return s;
        }

        private void SetSupport(Hero claimant, float value)
        {
            int i = _supportHeroIds.IndexOf(claimant.StringId);
            float v = Math.Max(0f, Math.Min(100f, value));
            if (i >= 0) _supportScores[i] = v;
            else { _supportHeroIds.Add(claimant.StringId); _supportScores.Add(v); }
        }

        private void AddSupport(Hero claimant, float delta) => SetSupport(claimant, GetSupport(claimant) + delta);

        private void SetAlignment(Hero lord, Hero claimant)
        {
            int i = _alignHeroIds.IndexOf(lord.StringId);
            string target = claimant?.StringId ?? "";
            if (i >= 0) _alignTargetIds[i] = target;
            else { _alignHeroIds.Add(lord.StringId); _alignTargetIds.Add(target); }
        }

        private void RemoveCrisis(int i)
        {
            _crisisKingdomIds.RemoveAt(i);
            _crisisStates.RemoveAt(i);
            _crisisDays.RemoveAt(i);
            _claimantIds.RemoveAt(i);
            if (i < _incumbentIds.Count) _incumbentIds.RemoveAt(i);
            if (i < _warBreakaways.Count) _warBreakaways.RemoveAt(i);
            if (i < _warStartDay.Count) _warStartDay.RemoveAt(i);
        }

        private void ClearCrisisData(int i, List<Hero> claimants)
        {
            var claimantIds = new HashSet<string>(claimants.Select(c => c.StringId));
            for (int a = _alignHeroIds.Count - 1; a >= 0; a--)
                if (claimantIds.Contains(_alignTargetIds[a]))
                { _alignHeroIds.RemoveAt(a); _alignTargetIds.RemoveAt(a); }
            for (int s = _supportHeroIds.Count - 1; s >= 0; s--)
                if (claimantIds.Contains(_supportHeroIds[s]))
                { _supportHeroIds.RemoveAt(s); _supportScores.RemoveAt(s); }
            RemoveCrisis(i);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));
    }
}
