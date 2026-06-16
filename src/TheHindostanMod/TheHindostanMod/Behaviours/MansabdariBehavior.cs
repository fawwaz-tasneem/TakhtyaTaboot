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

        public static MansabdariBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
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

        // Battlefield merit can raise a clan's mansab by one step, regardless of muster.
        // Returns the new title, or null if already at the summit.
        public string TryMeritPromotion(Clan clan)
        {
            if (clan == null) return null;
            int idx = GetRankIndex(clan);
            if (idx >= MaxIndex) return null;
            SetRankIndex(clan, idx + 1);
            return Ranks[idx + 1].Title;
        }

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
                int sawar = GetClanTotalTroops(clan);
                int idx = GetRankIndex(clan);

                // Demotion (all clans): drop while below the retention floor of the current rank.
                while (idx > 0 && sawar < Ranks[idx].Retention) idx--;

                // AI auto-promotion (emperor rubber-stamps qualified vassals). Player must petition.
                if (clan != Clan.PlayerClan && clan.Kingdom != null)
                {
                    int q = QualifiedIndex(sawar);
                    if (q > idx) idx = q;
                }

                if (idx != GetRankIndex(clan))
                {
                    bool demoted = idx < GetRankIndex(clan);
                    SetRankIndex(clan, idx);
                    if (clan == Clan.PlayerClan && demoted && clan.Kingdom != null)
                        RoyalFarmaan.FromRuler(clan.Kingdom, "Reduction of Mansab",
                            $"Your muster no longer sustains your rank. By order of the court your mansab is reduced to " +
                            $"{Ranks[idx].Title}. Restore your sawar to reclaim your former standing.",
                            "As the court wills");
                }

                // Economic benefit: weekly imperial influence scaled by rank.
                if (idx > 0) ChangeClanInfluenceAction.Apply(clan, idx * 1f);
            }
        }

        private static int QualifiedIndex(int sawar)
        {
            int idx = 0;
            for (int i = MaxIndex; i >= 1; i--)
                if (sawar >= Ranks[i].SawarRequired) { idx = i; break; }
            return idx;
        }

        // ── Player petition (called from menu) ────────────────────────────────────
        public bool PlayerCanPetition(out string reason)
        {
            reason = "";
            var clan = Clan.PlayerClan;
            int idx = GetRankIndex(clan);
            if (idx >= MaxIndex) { reason = "You already hold the highest mansab."; return false; }
            if (clan.Kingdom == null) { reason = "You must serve an empire to hold a mansab."; return false; }
            int sawar = GetClanTotalTroops(clan);
            if (sawar < Ranks[idx + 1].SawarRequired)
            { reason = $"You need {Ranks[idx + 1].SawarRequired} sawar for {Ranks[idx + 1].Title} (you have {sawar})."; return false; }
            return true;
        }

        public void PlayerPetition()
        {
            if (!PlayerCanPetition(out string reason)) { Notify(reason, true); return; }

            var clan = Clan.PlayerClan;
            int idx = GetRankIndex(clan);
            int nextIdx = idx + 1;
            float cost = nextIdx * 20f;
            if (clan.Influence < cost) { Notify($"The court expects {cost:0} influence to process the petition (you have {clan.Influence:0}).", true); return; }

            Hero emperor = clan.Kingdom.Leader;
            bool isRuler = emperor == clan.Leader;

            if (!isRuler)
            {
                int rel = CharacterRelationManager.GetHeroRelation(clan.Leader, emperor);
                int req = nextIdx * 5;
                // When the emperor's authority is weak, his grants come dearer — he must
                // be cajoled harder to elevate a vassal.
                float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(clan.Kingdom) ?? 75f;
                if (auth < 75f) req += (int)((75f - auth) / 5f);
                if (rel < req)
                {
                    ChangeClanInfluenceAction.Apply(clan, -cost / 2f);
                    string note = auth < 50f ? " His authority is too weak to elevate vassals freely." : "";
                    Notify($"{emperor.Name} denies your petition. {Ranks[nextIdx].Title} requires standing of {req} at court (yours is {rel}).{note}", true);
                    return;
                }
            }

            ChangeClanInfluenceAction.Apply(clan, -cost);
            SetRankIndex(clan, nextIdx);
            if (isRuler)
                Notify($"You assume the mansab of {Ranks[nextIdx].Title} by your own decree.", false);
            else
                RoyalFarmaan.FromRuler(clan.Kingdom, "Grant of Mansab",
                    $"By imperial favour, {clan.Leader.Name} is raised to the mansab of {Ranks[nextIdx].Title} " +
                    $"(zat {Ranks[nextIdx].Mansab}). Bear its honours and its duties faithfully.",
                    "I am honoured");
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
                "{=!}I have mustered the sawar that custom demands. I ask to be raised to the next mansab.",
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
            var clan = Clan.PlayerClan;
            ChangeClanInfluenceAction.Apply(clan, -_pendingCost);
            SetRankIndex(clan, _pendingIdx);
            _pendingGrant = false;
            Notify($"By the Emperor's word you are raised to {Ranks[_pendingIdx].Title}.", false);
        }

        private bool EvaluatePetition(out int nextIdx, out float cost, out string kingLine)
        {
            nextIdx = 0; cost = 0f; kingLine = "";
            var clan = Clan.PlayerClan;
            if (!PlayerCanPetition(out string why)) { kingLine = "I cannot grant this. " + why; return false; }
            int idx = GetRankIndex(clan);
            nextIdx = idx + 1;
            cost = nextIdx * 20f;
            Hero emperor = clan.Kingdom.Leader;
            if (clan.Influence < cost)
            { kingLine = $"You lack the standing to press such a suit — the court expects {cost:0} influence at the least."; return false; }
            int rel = CharacterRelationManager.GetHeroRelation(clan.Leader, emperor);
            int req = nextIdx * 5;
            float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(clan.Kingdom) ?? 75f;
            if (auth < 75f) req += (int)((75f - auth) / 5f);
            if (rel < req)
            { kingLine = $"You have not yet earned my favour for the rank of {Ranks[nextIdx].Title}. Serve me better, and ask again."; return false; }
            kingLine = $"Your sword has earned it. Rise — I name you {Ranks[nextIdx].Title}, with the zat of {Ranks[nextIdx].Mansab}. Bear it well.";
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
            string nextLine = idx < MaxIndex
                ? $"Next rank ({Ranks[idx + 1].Title}) needs {Ranks[idx + 1].SawarRequired} sawar."
                : "You hold the highest mansab in the empire.";
            sb.AppendLine(nextLine);
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
            if (!dataStore.IsSaving)
            {
                _rankIndex.Clear();
                for (int i = 0; i < ids.Count; i++)
                    _rankIndex[ids[i]] = i < vals.Count ? vals[i] : 0;
            }
        }
    }
}
