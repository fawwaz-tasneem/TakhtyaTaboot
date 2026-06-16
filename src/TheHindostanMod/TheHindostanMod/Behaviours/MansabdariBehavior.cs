using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;

namespace TakhtyaTaboot
{
    // The Mughal mansabdari rank ladder. A clan QUALIFIES for a rank by its sawar
    // (total field troops), but the rank must be GRANTED by the kingdom's ruler
    // ("the Emperor"). AI clans are rubber-stamped automatically; the player must
    // petition. Ranks are tracked realm-wide and drive fief eligibility, military/
    // economic bonuses, and the Amir-ul-Umara leadership challenge.
    public class MansabdariBehavior : CampaignBehaviorBase
    {
        public struct Rank
        {
            public int Mansab;          // 100..5000
            public string Title;
            public int SawarRequired;   // troops to be granted this rank
            public int Retention;       // fall below and you are demoted (75% of req)
            public Rank(int m, string t, int req, int ret) { Mansab = m; Title = t; SawarRequired = req; Retention = ret; }
        }

        // Index 0 = unranked. Ascending.
        public static readonly Rank[] Ranks =
        {
            new Rank(0,    "Unranked",             0,   0),
            new Rank(100,  "Zamindar",             25,  19),
            new Rank(500,  "Mansabdar-e-Panjsad",  100, 75),
            new Rank(1000, "Qiledar",              200, 150),
            new Rank(2000, "Faujdar",              350, 263),
            new Rank(3000, "Subahdar",             500, 375),
            new Rank(5000, "Amir-ul-Umara",        600, 450),
        };
        private const int MaxIndex = 6;

        // Fief eligibility by rank index: villages>=1 (Zamindar), castles>=3 (Qiledar), towns>=5 (Subahdar).
        private const int CastleIndex = 3;
        private const int TownIndex = 5;

        private Dictionary<string, int> _rankIndex = new Dictionary<string, int>(); // clanId -> rank index
        private Dictionary<string, float> _valour = new Dictionary<string, float>(); // clanId -> battlefield valour

        // Player muster/stipend bookkeeping.
        private int _daysUnderMuster;     // consecutive days below the retention floor
        private bool _warnedUnderMuster;  // suppresses warning spam within an under-muster spell
        private int _lastStipendDay = -1;

        public static MansabdariBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        // ── Troop count (sawar) ───────────────────────────────────────────────────
        public static int GetClanTotalTroops(Clan clan)
        {
            if (clan == null) return 0;
            int total = 0;
            foreach (var wpc in clan.WarPartyComponents)
            {
                var party = wpc.MobileParty;
                if (party != null && party.IsActive)
                    total += party.MemberRoster.TotalRegulars;
            }
            return total;
        }

        // ── Rank queries ──────────────────────────────────────────────────────────
        public int GetRankIndex(Clan clan)
            => clan != null && _rankIndex.TryGetValue(clan.StringId, out int i) ? i : 0;

        public int GetMansab(Clan clan) => Ranks[GetRankIndex(clan)].Mansab;
        public string GetTitle(Clan clan) => Ranks[GetRankIndex(clan)].Title;

        // ── Troop target of a rank ──────────────────────────────────────────────────
        // Each mansab carries an ABSOLUTE troop target. The party-size patch sets the
        // clan leader's cap to exactly this, so the rank can never demand more men than it
        // lets you field (this is what breaks the old promote→over-cap→demote spiral).
        public static int RequiredTroopsForIndex(int idx)
        {
            idx = Math.Max(0, Math.Min(MaxIndex, idx));
            return (int)Math.Round(Ranks[idx].SawarRequired * Config.Tune.TroopCapacityMultiplier);
        }

        public int GetRequiredTroops(Clan clan) => RequiredTroopsForIndex(GetRankIndex(clan));

        // The muster you must keep to avoid the demotion clock (a fraction of the target).
        public int GetRetentionFloor(Clan clan)
            => (int)Math.Round(GetRequiredTroops(clan) * Config.Tune.RetentionFraction);

        // ── Valour (battlefield achievement) ────────────────────────────────────────
        // Earned in battle (with a large bonus for capturing or routing an enemy king),
        // valour is the merit that — together with the clan's renown and the Emperor's
        // favour — earns elevation. It is spent when you rise.
        public float GetValour(Clan clan)
            => clan != null && _valour.TryGetValue(clan.StringId, out float v) ? v : 0f;

        public void AddValour(Clan clan, float amount)
        {
            if (clan == null || amount == 0f) return;
            _valour[clan.StringId] = Math.Max(0f, GetValour(clan) + amount);
            if (clan == Clan.PlayerClan) TryAutoElevatePlayer();
        }

        // ── Elevation criteria: valour AND renown AND the Emperor's favour ──────────
        public bool MeetsElevationCriteria(Clan clan, out int nextIdx, out string reason)
        {
            reason = ""; nextIdx = 0;
            if (clan == null) { reason = "No clan."; return false; }
            int idx = GetRankIndex(clan);
            if (idx >= MaxIndex) { reason = "You already hold the highest mansab."; return false; }
            if (clan.Kingdom == null) { reason = "You must serve an empire to hold a mansab."; return false; }
            nextIdx = idx + 1;

            float valReq = Config.Tune.ValourPerRankStep * nextIdx;
            float val = GetValour(clan);
            if (val < valReq)
            { reason = $"You need {valReq:0} valour for {Ranks[nextIdx].Title} (you have {val:0})."; return false; }

            float renownReq = Config.Tune.RenownPerRankStep * nextIdx;
            if (clan.Renown < renownReq)
            { reason = $"Your house needs {renownReq:0} renown for {Ranks[nextIdx].Title} (you have {clan.Renown:0})."; return false; }

            bool isRuler = clan.Kingdom.Leader == clan.Leader;
            if (!isRuler)
            {
                int rel = CharacterRelationManager.GetHeroRelation(clan.Leader, clan.Kingdom.Leader);
                if (rel < Config.Tune.MinRelationForElevation)
                { reason = $"The Emperor's favour is too cold for {Ranks[nextIdx].Title} (relation {rel}, needs {Config.Tune.MinRelationForElevation})."; return false; }
            }
            return true;
        }

        // Raise the player one rank, consuming the valour the rise demanded.
        private void ElevatePlayer(int nextIdx)
        {
            var clan = Clan.PlayerClan;
            SetRankIndex(clan, nextIdx);
            float spent = Config.Tune.ValourPerRankStep * nextIdx;
            _valour[clan.StringId] = Math.Max(0f, GetValour(clan) - spent);

            Kingdom k = clan.Kingdom;
            bool isRuler = k != null && k.Leader == clan.Leader;
            if (isRuler)
                Notify($"You assume the mansab of {Ranks[nextIdx].Title} by your own decree.", false);
            else if (k != null)
                RoyalFarmaan.FromRuler(k, "Grant of Mansab",
                    $"For your valour and the standing of your house, {clan.Leader.Name} is raised to the mansab of " +
                    $"{Ranks[nextIdx].Title} (zat {Ranks[nextIdx].Mansab}). Your contingent swells to " +
                    $"{RequiredTroopsForIndex(nextIdx)} men. Bear its honours and its duties faithfully.",
                    "I am honoured");
        }

        // The court elevates the player the moment all three criteria are met.
        public void TryAutoElevatePlayer()
        {
            if (Clan.PlayerClan != null && MeetsElevationCriteria(Clan.PlayerClan, out int nextIdx, out _))
                ElevatePlayer(nextIdx);
        }

        private void SetRankIndex(Clan clan, int idx)
        {
            if (clan == null) return;
            _rankIndex[clan.StringId] = Math.Max(0, Math.Min(MaxIndex, idx));
        }

        // For console testing: set a clan's mansab rank directly.
        public string DebugSetRank(Clan clan, int idx)
        {
            if (clan == null) return "No clan.";
            SetRankIndex(clan, idx);
            return $"{clan.Name} set to {Ranks[GetRankIndex(clan)].Title} (mansab {Ranks[GetRankIndex(clan)].Mansab}).";
        }

        public static int MaxRankIndex => MaxIndex;

        public bool CanHold(Clan clan, Settlement s)
        {
            int idx = GetRankIndex(clan);
            if (s == null) return true;
            if (s.IsTown) return idx >= TownIndex;
            if (s.IsCastle) return idx >= CastleIndex;
            return idx >= 1; // villages
        }

        // The mansab rank index a fief of this kind requires (town=Subahdar, castle=Qiledar, village=Zamindar).
        public int RequiredRankIndex(Settlement s)
        {
            if (s == null) return 0;
            if (s.IsTown) return TownIndex;
            if (s.IsCastle) return CastleIndex;
            return 1;
        }

        // The title a fief of this kind requires, for messaging.
        public string RequiredTitle(Settlement s) => Ranks[Math.Min(MaxIndex, RequiredRankIndex(s))].Title;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void OnNewGame(CampaignGameStarter starter)
        {
            // Seed every clan with a rank that matches its starting army, granted by its ruler.
            foreach (Clan clan in Clan.All.Where(c => !c.IsEliminated && !c.IsBanditFaction && c.Leader != null))
                SetRankIndex(clan, QualifiedIndex(GetClanTotalTroops(clan)));
        }

        private void OnWeeklyTick()
        {
            foreach (Clan clan in Clan.All.Where(c => !c.IsEliminated && !c.IsBanditFaction && c.Leader != null))
            {
                // The player's rank moves only through valour-based elevation (below) and
                // the daily muster clock (OnDailyTick) — never the AI troop ladder.
                if (clan != Clan.PlayerClan)
                {
                    int sawar = GetClanTotalTroops(clan);
                    int idx = GetRankIndex(clan);

                    // Demotion: drop while below the retention floor of the current rank.
                    while (idx > 0 && sawar < Ranks[idx].Retention) idx--;

                    // AI auto-promotion (emperor rubber-stamps qualified vassals).
                    if (clan.Kingdom != null)
                    {
                        int q = QualifiedIndex(sawar);
                        if (q > idx) idx = q;
                    }
                    if (idx != GetRankIndex(clan)) SetRankIndex(clan, idx);
                }

                // Economic benefit: weekly imperial influence scaled by rank.
                int rank = GetRankIndex(clan);
                if (rank > 0) ChangeClanInfluenceAction.Apply(clan, rank * 1f);
            }

            // The court reviews the player's merits weekly as a backstop (renown or favour
            // may have caught up to valour already earned).
            TryAutoElevatePlayer();
        }

        // ── Player muster clock & stipend (daily) ───────────────────────────────────
        private void OnDailyTick()
        {
            var clan = Clan.PlayerClan;
            if (clan == null) return;
            int today = (int)CampaignTime.Now.ToDays;
            if (_lastStipendDay < 0) _lastStipendDay = today;

            int idx = GetRankIndex(clan);

            // Must keep the retention floor of the current rank, or be demoted after the grace window.
            if (idx >= 1 && MobileParty.MainParty != null)
            {
                int floor = GetRetentionFloor(clan);
                int have = MobileParty.MainParty.MemberRoster.TotalManCount;
                if (have < floor)
                {
                    _daysUnderMuster++;
                    int remaining = Config.Tune.DemoteGraceDays - _daysUnderMuster;
                    if (!_warnedUnderMuster || remaining == 7 || remaining == 3)
                        WarnUnderMuster(remaining, floor, have);
                    if (_daysUnderMuster >= Config.Tune.DemoteGraceDays)
                    {
                        _daysUnderMuster = 0; _warnedUnderMuster = false;
                        DemotePlayerForMuster();
                    }
                }
                else
                {
                    if (_daysUnderMuster > 0 && _warnedUnderMuster)
                        Notify("Your muster is restored to your mansab's strength; the threat of demotion passes.", false);
                    _daysUnderMuster = 0; _warnedUnderMuster = false;
                }
            }
            else _daysUnderMuster = 0;

            // A stipend from the treasury, proportional to the contingent you must keep, every 30 days.
            if (today - _lastStipendDay >= 30)
            {
                _lastStipendDay = today;
                PayStipend(clan);
            }
        }

        private void WarnUnderMuster(int remaining, int floor, int have)
        {
            _warnedUnderMuster = true;
            Hero comp = Clan.PlayerClan?.Companions?.FirstOrDefault(h => h != null && h.IsAlive)
                        ?? Clan.PlayerClan?.Heroes?.FirstOrDefault(h => h != null && h.IsAlive && h != Hero.MainHero);
            string who = comp != null ? comp.Name.ToString() : "Your captain";
            string line = remaining > 0
                ? $"{who}: \"My lord, we muster but {have} of the {floor} men your mansab demands. Fill the ranks within {remaining} days, or the court will strip your rank.\""
                : $"{who}: \"My lord, our muster is short and the court's patience is spent.\"";
            InformationManager.DisplayMessage(new InformationMessage(line, Color.FromUint(0xFFCC4400)));
        }

        private void DemotePlayerForMuster()
        {
            var clan = Clan.PlayerClan;
            int idx = GetRankIndex(clan);
            if (idx <= 0) return;
            SetRankIndex(clan, idx - 1);
            Kingdom k = clan.Kingdom;
            if (k != null)
                RoyalFarmaan.FromRuler(k, "Reduction of Mansab",
                    $"For {Config.Tune.DemoteGraceDays} days your muster has fallen short of your station. By order of the court " +
                    $"your mansab is reduced to {Ranks[idx - 1].Title}. Restore your sawar and earn your rank anew.",
                    "As the court wills");
            else Notify($"Your mansab is reduced to {Ranks[idx - 1].Title} for want of men.", true);
        }

        private void PayStipend(Clan clan)
        {
            int idx = GetRankIndex(clan);
            if (idx < 1 || clan.Kingdom == null) return;
            Hero king = clan.Kingdom.Leader;
            if (king == null || king == Hero.MainHero) return; // the sovereign IS the treasury
            int amount = (int)Math.Round(Config.Tune.StipendPerTroop * GetRequiredTroops(clan));
            if (amount <= 0) return;

            int pay = Math.Min(amount, Math.Max(0, king.Gold));
            if (pay <= 0)
            {
                RoyalFarmaan.FromRuler(clan.Kingdom, "The Treasury Is Bare",
                    "The day for your mansab's stipend comes, but the imperial treasury cannot meet it. The court regrets the lapse.",
                    "So it is");
                return;
            }
            GiveGoldAction.ApplyBetweenCharacters(king, Hero.MainHero, pay, true);
            RoyalFarmaan.FromRuler(clan.Kingdom, "A Stipend from the Treasury",
                $"As is owed to your mansab of {Ranks[idx].Title}, the treasury disburses {pay} dinars toward the upkeep of " +
                $"your {GetRequiredTroops(clan)}-man contingent.",
                "I am grateful");
        }

        private static int QualifiedIndex(int sawar)
        {
            int idx = 0;
            for (int i = MaxIndex; i >= 1; i--)
                if (sawar >= Ranks[i].SawarRequired) { idx = i; break; }
            return idx;
        }

        // ── Player petition (called from menu) ────────────────────────────────────
        // Elevation is normally automatic once valour, renown and the Emperor's favour
        // all suffice (TryAutoElevatePlayer). This lets the player press the claim in
        // person at a cost of influence — the criteria are the same.
        public bool PlayerCanPetition(out string reason)
            => MeetsElevationCriteria(Clan.PlayerClan, out _, out reason);

        public void PlayerPetition()
        {
            if (!MeetsElevationCriteria(Clan.PlayerClan, out int nextIdx, out string reason)) { Notify(reason, true); return; }
            var clan = Clan.PlayerClan;
            float cost = nextIdx * 20f;
            if (clan.Influence < cost)
            { Notify($"The court expects {cost:0} influence to process the petition (you have {clan.Influence:0}).", true); return; }
            ChangeClanInfluenceAction.Apply(clan, -cost);
            ElevatePlayer(nextIdx);
        }

        // ── Amir-ul-Umara leadership challenge (simplified; full system is Chapter 16) ──
        public bool PlayerCanChallenge(out string reason)
        {
            reason = "";
            var clan = Clan.PlayerClan;
            if (GetRankIndex(clan) < MaxIndex) { reason = "Only an Amir-ul-Umara may challenge for the throne."; return false; }
            var kingdom = clan.Kingdom;
            if (kingdom == null || kingdom.Leader == clan.Leader) { reason = "You do not serve under an emperor."; return false; }
            return true;
        }

        public void PlayerChallenge()
        {
            if (!PlayerCanChallenge(out string reason)) { Notify(reason, true); return; }
            // The throne is no longer a bloodless tally of troops. The challenge opens a
            // War of Accession: the realm takes sides, the Emperor raises his host and
            // marches to crush you, and the field decides who wears the crown.
            if (AccessionWarBehavior.Instance != null)
                AccessionWarBehavior.Instance.StartChallenge();
            else
                Notify("The court is in disarray; the challenge cannot be raised.", true);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Menu ──────────────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town", "hindostan_mansab_town", "{=!}Review your mansabdari rank",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage; return true; },
                args => GameMenu.SwitchToMenu("hindostan_mansab"), false, 4);
            starter.AddGameMenuOption("castle", "hindostan_mansab_castle", "{=!}Review your mansabdari rank",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage; return true; },
                args => GameMenu.SwitchToMenu("hindostan_mansab"), false, 4);

            starter.AddGameMenu("hindostan_mansab", "{=!}{HINDOSTAN_MANSAB_TEXT}", MansabMenuInit);

            starter.AddGameMenuOption("hindostan_mansab", "hindostan_mansab_petition",
                "{=!}Petition the Emperor for promotion",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return PlayerCanPetition(out _); },
                args => { PlayerPetition(); MansabMenuInit(args); });

            starter.AddGameMenuOption("hindostan_mansab", "hindostan_mansab_claim",
                "{=!}Claim a fief befitting your rank",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage;
                          return CareerProgressionBehavior.Instance?.CanClaim(out _) ?? false; },
                args => { CareerProgressionBehavior.Instance?.ClaimFief(); MansabMenuInit(args); });

            starter.AddGameMenuOption("hindostan_mansab", "hindostan_mansab_challenge",
                "{=!}Challenge the Emperor for the throne",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.HostileAction; return PlayerCanChallenge(out _); },
                args => { PlayerChallenge(); MansabMenuInit(args); });

            starter.AddGameMenuOption("hindostan_mansab", "hindostan_mansab_leave", "{=!}Leave",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu(Settlement.CurrentSettlement != null && Settlement.CurrentSettlement.IsTown ? "town" : "castle"),
                true);

            AddPetitionDialog(starter);
        }

        // ── In-person petition: ask the Emperor for elevation face to face ──────────
        private int _pendingIdx;
        private float _pendingCost;
        private bool _pendingGrant;

        private void AddPetitionDialog(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_petition_start", "hero_main_options", "hind_petition_king",
                "{=!}Your Majesty, I would petition the court regarding my mansab.",
                ConversationWithMyEmperor, null, 110);

            starter.AddDialogLine("hind_petition_king", "hind_petition_king", "hind_petition_choice",
                "{=!}You come to me in person? Bold. Speak — what would you have of your sovereign?",
                () => true, null);

            starter.AddPlayerLine("hind_petition_ask", "hind_petition_choice", "hind_petition_answer",
                "{=!}By my valour in the field and my house's standing, I ask to be raised to the next mansab.",
                () => true, null);

            starter.AddPlayerLine("hind_petition_leave", "hind_petition_choice", "hero_main_options",
                "{=!}Nothing, Majesty. I beg your pardon for the intrusion.",
                () => true, null);

            starter.AddDialogLine("hind_petition_answer", "hind_petition_answer", "close_window",
                "{=!}{HINDOSTAN_PETITION_RESULT}",
                PreparePetitionResponse, CommitPetition);
        }

        private static bool ConversationWithMyEmperor()
        {
            Hero partner = Hero.OneToOneConversationHero;
            Kingdom k = Clan.PlayerClan?.Kingdom;
            return partner != null && k != null && partner == k.Leader && partner != Hero.MainHero;
        }

        // No side effects beyond text + a pending flag — safe if the dialog system
        // re-evaluates the condition. The grant itself happens in CommitPetition.
        private bool PreparePetitionResponse()
        {
            _pendingGrant = EvaluatePetition(out _pendingIdx, out _pendingCost, out string kingLine);
            MBTextManager.SetTextVariable("HINDOSTAN_PETITION_RESULT", kingLine, false);
            return true;
        }

        private void CommitPetition()
        {
            if (!_pendingGrant) return;
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -_pendingCost);
            ElevatePlayer(_pendingIdx);
            _pendingGrant = false;
        }

        private bool EvaluatePetition(out int nextIdx, out float cost, out string kingLine)
        {
            nextIdx = 0; cost = 0f; kingLine = "";
            var clan = Clan.PlayerClan;
            if (!MeetsElevationCriteria(clan, out nextIdx, out string why)) { kingLine = "I cannot grant this. " + why; return false; }
            cost = nextIdx * 20f;
            if (clan.Influence < cost)
            { kingLine = $"You lack the standing to press such a suit — the court expects {cost:0} influence at the least."; return false; }
            kingLine = $"Your valour and your house's name have earned it. Rise — I name you {Ranks[nextIdx].Title}, " +
                       $"with the zat of {Ranks[nextIdx].Mansab}. Bear it well.";
            return true;
        }

        private void MansabMenuInit(MenuCallbackArgs args)
        {
            var clan = Clan.PlayerClan;
            int idx = GetRankIndex(clan);
            int sawar = GetClanTotalTroops(clan);

            var sb = new StringBuilder();
            sb.AppendLine("The Mansabdari Court");
            sb.AppendLine(" ");
            sb.AppendLine($"Your rank: {Ranks[idx].Title} (mansab {Ranks[idx].Mansab})");
            sb.AppendLine($"Your sawar: {sawar} troops");
            if (idx >= 1)
            {
                int target = GetRequiredTroops(clan);
                int floor = GetRetentionFloor(clan);
                sb.AppendLine($"Your mansab grants a contingent of {target} men (you must keep at least {floor}).");
            }
            if (idx < MaxIndex)
            {
                int n = idx + 1;
                float val = GetValour(clan);
                float valReq = Config.Tune.ValourPerRankStep * n;
                float renownReq = Config.Tune.RenownPerRankStep * n;
                int rel = clan.Kingdom?.Leader != null && clan.Kingdom.Leader != clan.Leader
                    ? CharacterRelationManager.GetHeroRelation(clan.Leader, clan.Kingdom.Leader) : 0;
                sb.AppendLine($"To rise to {Ranks[n].Title}, the court weighs:");
                sb.AppendLine($"   Valour {val:0}/{valReq:0}    Renown {clan.Renown:0}/{renownReq:0}    Emperor's favour {rel} (needs {Config.Tune.MinRelationForElevation})");
            }
            else sb.AppendLine("You hold the highest mansab in the empire.");
            sb.AppendLine(" ");
            sb.AppendLine("You may hold: " + EligibilityLine(idx));
            sb.AppendLine(" ");
            sb.AppendLine("— The Ladder of Rank —");
            for (int i = MaxIndex; i >= 1; i--)
            {
                string marker = i == idx ? " >> " : "    ";
                string you = i == idx ? "   <-- YOU" : "";
                sb.AppendLine($"{marker}{Ranks[i].Title,-22} mansab {Ranks[i].Mansab,-5} ({Ranks[i].SawarRequired} sawar){you}");
            }

            MBTextManager.SetTextVariable("HINDOSTAN_MANSAB_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }

        private static string EligibilityLine(int idx)
        {
            if (idx >= TownIndex) return "villages, castles, and towns";
            if (idx >= CastleIndex) return "villages and castles";
            if (idx >= 1) return "villages";
            return "no fiefs (unranked)";
        }

        // ── Save/load ─────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var ids = _rankIndex.Keys.ToList();
            var vals = _rankIndex.Values.ToList();
            dataStore.SyncData("hind_mansab_ids", ref ids);
            dataStore.SyncData("hind_mansab_vals", ref vals);

            var vIds = _valour.Keys.ToList();
            var vVals = _valour.Values.ToList();
            dataStore.SyncData("hind_mansab_valIds", ref vIds);
            dataStore.SyncData("hind_mansab_valVals", ref vVals);

            dataStore.SyncData("hind_mansab_daysUnder", ref _daysUnderMuster);
            dataStore.SyncData("hind_mansab_warnedUnder", ref _warnedUnderMuster);
            dataStore.SyncData("hind_mansab_lastStipend", ref _lastStipendDay);

            if (!dataStore.IsSaving)
            {
                _rankIndex.Clear();
                for (int i = 0; i < ids.Count; i++)
                    _rankIndex[ids[i]] = i < vals.Count ? vals[i] : 0;

                _valour = new Dictionary<string, float>();
                for (int i = 0; i < vIds.Count && i < vVals.Count; i++) _valour[vIds[i]] = vVals[i];
            }
        }

        // ── Console (testing) ─────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("valour", "hindostan")]
        public static string ValourStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            var clan = Clan.PlayerClan;
            int idx = Instance.GetRankIndex(clan);
            var sb = new StringBuilder();
            sb.AppendLine($"Mansab: {Ranks[idx].Title} (index {idx})");
            sb.AppendLine($"Valour: {Instance.GetValour(clan):0}    Renown: {clan.Renown:0}");
            if (idx < MaxIndex)
                sb.AppendLine(Instance.MeetsElevationCriteria(clan, out _, out string why)
                    ? "Eligible for elevation now."
                    : "Not yet eligible: " + why);
            if (idx >= 1)
                sb.AppendLine($"Contingent target: {Instance.GetRequiredTroops(clan)}, retention floor: {Instance.GetRetentionFloor(clan)}, " +
                              $"days under muster: {Instance._daysUnderMuster}/{Config.Tune.DemoteGraceDays}");
            return sb.ToString();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("add_valour", "hindostan")]
        public static string AddValourCmd(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            float amt = 50f;
            if (args != null && args.Count > 0) float.TryParse(args[0], out amt);
            Instance.AddValour(Clan.PlayerClan, amt);
            return $"Added {amt:0} valour. Now {Instance.GetValour(Clan.PlayerClan):0}.";
        }
    }
}
