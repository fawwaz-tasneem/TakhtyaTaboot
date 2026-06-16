using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;

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

        // ── State (parallel lists — engine can't serialize Dictionary<string,T>) ──
        private List<string> _crisisKingdomIds = new List<string>();
        private List<int>    _crisisStates     = new List<int>();
        private List<float>  _crisisDays       = new List<float>();
        private List<string> _claimantIds      = new List<string>(); // comma-joined per crisis
        private List<string> _incumbentIds     = new List<string>(); // sitting ruler at crisis start ("" if died)

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
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("suc_kingdomIds",   ref _crisisKingdomIds);
            dataStore.SyncData("suc_states",       ref _crisisStates);
            dataStore.SyncData("suc_days",         ref _crisisDays);
            dataStore.SyncData("suc_claimants",    ref _claimantIds);
            dataStore.SyncData("suc_incumbents",   ref _incumbentIds);
            dataStore.SyncData("suc_alignHeroes",  ref _alignHeroIds);
            dataStore.SyncData("suc_alignTargets", ref _alignTargetIds);
            dataStore.SyncData("suc_suppHeroes",   ref _supportHeroIds);
            dataStore.SyncData("suc_suppScores",   ref _supportScores);
            dataStore.SyncData("suc_rulerKIds",    ref _rulerKingdomIds);
            dataStore.SyncData("suc_rulerHIds",    ref _rulerHeroIds);
            dataStore.SyncData("suc_prisonKIds",   ref _prisonKingdomIds);
            dataStore.SyncData("suc_prisonDays",   ref _prisonDays);
        }

        private void OnNewGame(CampaignGameStarter _) => SnapshotRulers();

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

            // Was the victim a known ruler?
            int ri = _rulerHeroIds.IndexOf(victim.StringId);
            if (ri >= 0)
            {
                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == _rulerKingdomIds[ri]);
                if (k != null && !k.IsEliminated && GetCrisisState(k) == CrisisState.None)
                {
                    float legitimacy = LegitimacyBehavior.Instance?.GetLegitimacy(victim) ?? 60f;
                    bool hasHeir = victim.Children.Any(c => c.IsAlive && !c.IsFemale && !c.IsChild);
                    if (legitimacy < 60f || !hasHeir)
                        TriggerCrisis(k, victim, includeIncumbent: false);
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
                    Hero leader = LeadingClaimant(k);
                    if (leader != null && GetSupportPercent(k, leader) >= 55f)
                        ResolveCrisis(k, leader, "the acclaim of the great lords");
                    else
                    {
                        _crisisStates[i] = (int)CrisisState.CivilWar;
                        Notify($"Civil war! The lords of {k.Name} take up arms for rival claimants.", true);
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
                    RunSkirmish(k);
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
            var claimants = FindClaimants(ruler, includeIncumbent, out var categories);
            if (claimants.Count <= 1) return; // a lone heir succeeds quietly

            _crisisKingdomIds.Add(k.StringId);
            _crisisStates.Add((int)CrisisState.Brewing);
            _crisisDays.Add(0f);
            _claimantIds.Add(string.Join(",", claimants.Select(c => c.StringId)));
            // Record the sitting ruler so we know who was "deposed" if they lose.
            _incumbentIds.Add(ruler != null && ruler.IsAlive ? ruler.StringId : "");

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

        // Agnatic priority: sons (eldest first) -> brothers -> nephews -> clan leader.
        private static List<Hero> FindClaimants(Hero ruler, bool includeIncumbent, out List<int> categories)
        {
            var seen = new HashSet<Hero>();
            var outp = new List<Hero>();
            var cats = new List<int>(); // 0 incumbent, 1 son, 2 brother, 3 nephew, 4 fallback

            void Add(Hero h, int cat)
            {
                if (h != null && h.IsAlive && !h.IsChild && !h.IsFemale && seen.Add(h))
                { outp.Add(h); cats.Add(cat); }
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
            if (outp.Count < 2) Add(ruler.Clan?.Leader, 4);

            categories = cats.Take(3).ToList();
            return outp.Take(3).ToList();
        }

        private void InitialiseSupport(Kingdom k, List<Hero> claimants, List<int> categories)
        {
            int sonRank = 0;
            for (int i = 0; i < claimants.Count; i++)
            {
                Hero c = claimants[i];
                float score = categories[i] switch
                {
                    0 => 50f,                       // sitting (if disputed-while-alive) ruler
                    1 => sonRank++ == 0 ? 60f : 45f - 5f * sonRank,
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
                ResolveCrisis(k, winner, "victory in the field");
                return;
            }

            if (GetSupportPercent(k, loser) <= 10f || GetSupportPercent(k, winner) >= 70f)
                ResolveCrisis(k, winner, "the collapse of all opposition");
        }

        private void ResolveCrisis(Kingdom k, Hero winner, string how)
        {
            int i = CrisisIndex(k);
            if (i < 0 || winner == null || !winner.IsAlive) { if (i >= 0) RemoveCrisis(i); return; }

            float pct = GetSupportPercent(k, winner);
            var claimants = GetClaimants(k);

            // The sitting ruler, if they contested and lost, is now deposed.
            string incumbentId = (i < _incumbentIds.Count) ? _incumbentIds[i] : "";
            Hero deposed = string.IsNullOrEmpty(incumbentId) ? null : FindHero(incumbentId);

            // Crown the winner.
            if (winner != k.Leader)
            {
                if (winner.Clan != null && winner.Clan != k.RulingClan)
                    ChangeRulingClanAction.Apply(k, winner.Clan);
                if (winner.Clan != null && winner.Clan.Leader != winner)
                    ChangeClanLeaderAction.ApplyWithSelectedNewLeader(winner.Clan, winner);
            }

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

            // The victor decides the fate of the deposed emperor.
            if (deposed != null && deposed.IsAlive && deposed != winner && deposed.Clan != null)
            {
                if (winner == Hero.MainHero) PromptDeposedFate(k, winner, deposed);
                else AiDecideDeposedFate(k, winner, deposed);
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
                    // A clan leader of a separate house can be cast out of the realm intact;
                    // otherwise the deposed simply vanishes from history.
                    if (deposed.Clan != winner.Clan && deposed.Clan.Leader == deposed && deposed.Clan.Kingdom == k)
                    {
                        ChangeClanInfluenceAction.Apply(deposed.Clan, -deposed.Clan.Influence);
                        ChangeKingdomAction.ApplyByLeaveKingdom(deposed.Clan, playerSees);
                    }
                    else
                    {
                        KillCharacterAction.ApplyByRemove(deposed, playerSees, true);
                    }
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 3f, "the deposed emperor banished");
                    if (playerSees) Notify($"{deposed.Name} is banished from Hindostan, stripped of title and standing.", false);
                    break;

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

        private void PlayerBroker()
        {
            if (!PlayerCanBroker(out string reason)) { Notify(reason, true); return; }
            Kingdom k = MenuKingdom();
            Hero leader = LeadingClaimant(k);

            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -200f);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leader, 15);
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 10f, "succession brokered");
            if (Clan.PlayerClan.Kingdom != null)
                LegitimacyBehavior.Instance?.ModifyLegitimacy(Clan.PlayerClan.Kingdom.Leader, 20f, "kingmaker's prestige");
            Notify($"By your hand, {leader.Name} ascends the throne of {k.Name}. All Hindostan knows who made this king.", false);
            ResolveCrisis(k, leader, "your brokerage");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private int CrisisIndex(Kingdom k) => _crisisKingdomIds.IndexOf(k?.StringId ?? "");

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
