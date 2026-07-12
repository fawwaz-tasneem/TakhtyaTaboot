using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Coronation ceremonies — the darbar of accession, held IN THE HALL (playtest round 5:
    // "It should be an event where lords are physically present inside the hall in my keep").
    //
    //   • Player is the NEW sovereign — the accession SUMMONS the darbar rather than resolving
    //     it in a popup. He travels to a keep of his realm and holds court: the attending house
    //     heads stand bodily in the lord's hall (materialised as LocationCharacters, the native
    //     keep-notable recipe — their map parties are not moved) and each swears fealty IN
    //     DIALOGUE, warmly or through his teeth by his regard. Who attended and who left an
    //     empty place is only learned by holding court. Leaving the hall closes the ceremony:
    //     attendees are entered in the registers (SworeFealty; +1 relation more if the oath was
    //     heard in person), absentees are noted (MissedCeremony) and a late oath may be demanded.
    //     Past the deadline (CoronationMath) the oaths are taken by courier — the old instant
    //     resolution, kept as the fallback so a ceremony can never dangle.
    //   • Player is a VASSAL of the new sovereign — he is summoned to swear: seek the sovereign
    //     out and bend the knee in dialogue (better received than any courier), or stay away
    //     and be remembered. Never answering the summons counts as staying away.
    // AI-only accessions resolve silently — this is a player-facing ceremony, not court spam.
    // Attendance/late-oath odds are deterministic and tested (CoronationMath).
    public class CoronationBehavior : CampaignBehaviorBase
    {
        public static CoronationBehavior Instance { get; private set; }

        // kingdomId -> the ruler we last saw on that throne. Seeded at session launch so no
        // ceremony fires for the thrones already standing when the campaign loads.
        private Dictionary<string, string> _lastRuler = new Dictionary<string, string>();

        // The summoned-but-unheld player coronation (serialized): his realm and the day of summons.
        private string _pendingKingdomId = "";
        private float _pendingSummonDay = -1f;

        // The player's own unanswered summons to an AI sovereign's coronation (serialized).
        private string _oathSovereignId = "";
        private float _oathSummonDay = -1f;

        // The live ceremony, mission-scoped and deliberately NOT serialized: the game cannot
        // save inside the hall mission, so this state can never dangle across a load.
        private bool _ceremonyLive;
        private List<Hero> _ceremonyAttended = new List<Hero>();
        private List<Hero> _ceremonyAbsent = new List<Hero>();
        private readonly HashSet<string> _ghostIds = new HashSet<string>();
        private readonly HashSet<string> _swornIds = new HashSet<string>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Coronation.DailyTick", OnDailyTick));
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this,
                m => TYTLog.Guard("Coronation.MissionEnd", () => { if (_ceremonyLive) FinishCeremony(); }));
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Snapshot();
            AddHallMenuOptions(starter);
            AddOathDialogs(starter);
        }

        private void Snapshot()
        {
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null))
                _lastRuler[k.StringId] = k.Leader.StringId;
        }

        // ── Accession detection ──────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (!Util.WorldGen.Ready) return;
            TickPendingCeremony();
            TickPendingOath();

            // The scripted 1707 cascade crowns and kills emperors in rapid succession; don't
            // stage a darbar on every beat of it. Keep the snapshot fresh and move on.
            if (Util.ScriptedSuccession.InProgress) { Snapshot(); return; }

            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null).ToList())
            {
                string now = k.Leader.StringId;
                if (!_lastRuler.TryGetValue(k.StringId, out string prev))
                {
                    _lastRuler[k.StringId] = now;
                    // A realm we have never seen: kingdoms standing at session launch were
                    // seeded by Snapshot(), so this is a kingdom FOUNDED mid-campaign — a
                    // genuine accession. The mod's LIVE claim kingdoms are a war measure, not
                    // a throne — no darbar for them (a GRADUATED secession state is a real
                    // realm and crowns; ThroneWar.IsRebelKingdom knows the difference).
                    if (!Util.ThroneWar.IsRebelKingdom(k) && k.Leader.IsAlive && !k.Leader.IsChild)
                        TYTLog.Guard("Coronation.Found:" + k.Name, () => HoldCoronation(k));
                    continue;
                }
                if (prev == now) continue;

                _lastRuler[k.StringId] = now;
                if (k.Leader.IsAlive && !k.Leader.IsChild)
                    TYTLog.Guard("Coronation.Hold:" + k.Name, () => HoldCoronation(k));
            }

            // Forget thrones that have fallen, so a re-formed kingdom with the same id crowns afresh.
            foreach (string id in _lastRuler.Keys.ToList())
                if (Kingdom.All.FirstOrDefault(x => x.StringId == id)?.IsEliminated ?? true)
                    _lastRuler.Remove(id);
        }

        // A newly independent realm's founding darbar (secession graduation calls this).
        public void HoldFoundingDarbar(Kingdom k)
        {
            if (k?.Leader == null) return;
            _lastRuler[k.StringId] = k.Leader.StringId;
            TYTLog.Guard("Coronation.Founding:" + k.Name, () => HoldCoronation(k));
        }

        private void HoldCoronation(Kingdom k)
        {
            if (k?.Leader == null) return;
            Hero sovereign = k.Leader;

            if (sovereign == Hero.MainHero) { SummonCoronation(k); return; }

            // Player is a vassal of this realm (a member clan, not the ruling one): he is summoned.
            if (Hero.MainHero?.Clan?.Kingdom == k && Hero.MainHero.Clan != k.RulingClan)
                VassalSummons(k, sovereign);

            // Otherwise an AI accession elsewhere — resolved silently.
        }

        // ── The player's coronation is summoned ──────────────────────────────────────
        private void SummonCoronation(Kingdom k)
        {
            _pendingKingdomId = k.StringId;
            _pendingSummonDay = (float)CampaignTime.Now.ToDays;

            RoyalFarmaan.Issue("The Coronation Darbar Is Summoned", $"The court of {k.Name}",
                $"{RoyalFarmaan.NameWithHonorific(Hero.MainHero)} takes the throne of {k.Name}, and word rides to " +
                "every great house: the darbar of accession is summoned. Go to a keep of your realm and hold court " +
                "in the lord's hall — the houses will assemble before the throne, and each will swear (or leave an " +
                $"empty place) as its regard for you dictates. If no court is held within {CoronationMath.CeremonyDeadlineDays} " +
                "days, the oaths are taken by courier and the moment's majesty is lost.",
                seal: "Proclaimed this day, " + RoyalFarmaan.CurrentDate(),
                primary: "The court shall assemble in my hall",
                secondary: "Take the oaths by farmaan — forgo the ceremony",
                onSecondary: () => TYTLog.Guard("Coronation.Instant", () => ResolveByCourier(k, "at your own word")),
                priority: FarmaanPriority.Ceremonial);
            TYTLog.Info($"Coronation: darbar summoned for {k.Name}; awaiting the hall.");
        }

        private Kingdom PendingKingdom()
            => string.IsNullOrEmpty(_pendingKingdomId) ? null
               : Kingdom.All.FirstOrDefault(x => x.StringId == _pendingKingdomId);

        private void ClearPending() { _pendingKingdomId = ""; _pendingSummonDay = -1f; }

        private void TickPendingCeremony()
        {
            if (string.IsNullOrEmpty(_pendingKingdomId)) return;
            Kingdom k = PendingKingdom();
            // Dethroned, dead, or the realm fell before the court could sit: the summons is void.
            if (k == null || k.IsEliminated || k.Leader != Hero.MainHero) { ClearPending(); return; }
            if (CoronationMath.SummonsLapsed(_pendingSummonDay, (float)CampaignTime.Now.ToDays))
                ResolveByCourier(k, "the summons aged with no court held");
        }

        // ── The hall ceremony ────────────────────────────────────────────────────────
        private void AddHallMenuOptions(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town_keep", "hindostan_coronation_town", "{=!}Hold your coronation darbar in the hall",
                CeremonyMenuCondition, _ => TYTLog.Guard("Coronation.Stage", StageCeremony), false, 1);
            starter.AddGameMenuOption("castle", "hindostan_coronation_castle", "{=!}Hold your coronation darbar in the hall",
                CeremonyMenuCondition, _ => TYTLog.Guard("Coronation.Stage", StageCeremony), false, 1);
        }

        private bool CeremonyMenuCondition(MenuCallbackArgs args)
        {
            Kingdom k = PendingKingdom();
            Settlement s = Settlement.CurrentSettlement;
            if (k == null || s == null || k.Leader != Hero.MainHero) return false;
            if (s.MapFaction != k) return false;
            args.optionLeaveType = GameMenuOption.LeaveType.Mission;
            return true;
        }

        private void StageCeremony()
        {
            Kingdom k = PendingKingdom();
            if (k == null || k.Leader != Hero.MainHero) { ClearPending(); return; }

            Location hall = LocationComplex.Current?.GetLocationWithId("lordshall");
            if (hall == null || PlayerEncounter.LocationEncounter == null)
            {
                // No hall to hold it in (should not happen from these menus) — fall back.
                ResolveByCourier(k, "no hall could receive the court");
                return;
            }

            RollAttendance(k, out _ceremonyAttended, out _ceremonyAbsent);
            _ghostIds.Clear();
            _swornIds.Clear();

            // Materialise the attending house heads in the hall. Their map parties stay where
            // they are — this is the court assembled, the native keep-notable recipe.
            foreach (Hero head in _ceremonyAttended)
            {
                if (LocationComplex.Current.GetLocationOfCharacter(head) != null) continue; // already bodily here
                hall.AddCharacter(MakeCourtCharacter(head));
                _ghostIds.Add(head.StringId);
            }

            _ceremonyLive = true;
            Notify(_ceremonyAttended.Count > 0
                ? "The great houses assemble before the throne. Hear their oaths — and mark the empty places."
                : "The hall is dressed for the darbar, though no vassal houses yet answer to your throne.", false);
            TYTLog.Info($"Coronation: ceremony staged in {Settlement.CurrentSettlement?.Name}; " +
                        $"{_ceremonyAttended.Count} attend, {_ceremonyAbsent.Count} absent, {_ghostIds.Count} materialised.");

            // Open the lord's hall mission exactly as the native keep menu does.
            Campaign.Current.GameMenuManager.NextLocation = hall;
            Campaign.Current.GameMenuManager.PreviousLocation = LocationComplex.Current.GetLocationWithId("center");
            PlayerEncounter.LocationEncounter.CreateAndOpenMissionController(hall);
            Campaign.Current.GameMenuManager.NextLocation = null;
            Campaign.Current.GameMenuManager.PreviousLocation = null;
        }

        // Who is summoned: the realm's house heads who could conceivably come. An imprisoned
        // head can neither attend nor be blamed for the empty place — he is not counted.
        private void RollAttendance(Kingdom k, out List<Hero> attended, out List<Hero> absent)
        {
            attended = new List<Hero>();
            absent = new List<Hero>();
            var op = OpinionBehavior.Instance;
            foreach (Clan c in k.Clans)
            {
                Hero head = c?.Leader;
                if (c == null || c.IsEliminated || c.IsMinorFaction || head == null) continue;
                if (head == Hero.MainHero || !head.IsAlive || head.IsChild || head.IsPrisoner) continue;
                float opinion = op?.EffectiveOpinion(head, Hero.MainHero) ?? 0f;
                if (CoronationMath.Attends(opinion, MBRandom.RandomFloat)) attended.Add(head);
                else absent.Add(head);
            }
        }

        // The native keep-notable recipe (HeroAgentSpawnCampaignBehavior), verified against the
        // game DLLs: a settlement-suffixed monster, no horse, faction colours, the lord action
        // set, fixed on a notable spawn point.
        private static LocationCharacter MakeCourtCharacter(Hero h)
        {
            AgentData agentData = new AgentData(new SimpleAgentOrigin(h.CharacterObject))
                .Monster(FaceGen.GetMonsterWithSuffix(h.CharacterObject.Race, "_settlement"))
                .NoHorses(noHorses: true);
            uint colour = h.MapFaction?.Color ?? 0xFFCCB58Fu;
            agentData.ClothingColor1(colour).ClothingColor2(colour);
            return new LocationCharacter(agentData,
                SandBoxManager.Instance.AgentBehaviorManager.AddFixedCharacterBehaviors,
                "sp_notable", true, LocationCharacter.CharacterRelations.Neutral,
                ActionSetCode.GenerateActionSetNameWithSuffix(agentData.AgentMonster, h.IsFemale, "_lord"),
                useCivilianEquipment: true);
        }

        // ── The oaths, spoken in the hall ────────────────────────────────────────────
        private void AddOathDialogs(CampaignGameStarter starter)
        {
            // An attending house head, approached during the ceremony, swears before the throne.
            starter.AddDialogLine("hind_coron_oath", "start", "hind_coron_oath_r",
                "{=!}{HIND_CORON_OATH}", OathCondition, OathSworn, 200);
            starter.AddPlayerLine("hind_coron_oath_r", "hind_coron_oath_r", "close_window",
                "{=!}Rise. The throne receives your oath, and the registers remember it.", () => true, null, 200);

            // The player, summoned to an AI sovereign's coronation, bends the knee in person.
            starter.AddPlayerLine("hind_coron_vassal", "hero_main_options", "hind_coron_vassal_r",
                "{=!}Jahanpanah, I answer the summons — I come to swear my oath at your coronation.",
                VassalOathCondition, null, 110);
            starter.AddDialogLine("hind_coron_vassal_r", "hind_coron_vassal_r", "close_window",
                "{=!}Rise. An oath sworn face to face is the old courtesy — the throne remembers it, and so shall I.",
                () => true,
                () => TYTLog.Guard("Coronation.VassalOath", VassalOathSworn), 200);
        }

        private bool OathCondition()
        {
            Hero p = Hero.OneToOneConversationHero;
            if (!_ceremonyLive || p == null || _swornIds.Contains(p.StringId)) return false;
            if (!_ceremonyAttended.Contains(p)) return false;
            float opinion = OpinionBehavior.Instance?.EffectiveOpinion(p, Hero.MainHero) ?? 0f;
            string oath;
            switch (CoronationMath.RegisterOf(opinion))
            {
                case CoronationMath.OathRegister.Warm:
                    oath = "Padishah! I bend the knee with a glad heart. My sword, my salt, and the strength of my " +
                           "house are yours — let every soul in this hall hear it."; break;
                case CoronationMath.OathRegister.Cold:
                    oath = "...I bend the knee, padishah. The oath is sworn before these witnesses — let the " +
                           "register say so, and let that suffice."; break;
                default:
                    oath = "Padishah. Before the assembled court I swear my oath: my sword and my salt are yours " +
                           "while you keep the faith of the throne."; break;
            }
            MBTextManager.SetTextVariable("HIND_CORON_OATH", oath, false);
            return true;
        }

        private void OathSworn()
        {
            Hero p = Hero.OneToOneConversationHero;
            if (p == null) return;
            _swornIds.Add(p.StringId);
            // Heard in person: a shade warmer than an oath merely counted from the dais.
            OpinionBehavior.Instance?.AddOpinion(Hero.MainHero, p, OpinionMath.OpinionType.SworeFealty);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, p, +3, false);
        }

        // Leaving the hall closes the ceremony: the registers are written.
        private void FinishCeremony()
        {
            _ceremonyLive = false;
            Kingdom k = PendingKingdom();
            ClearPending();

            // Dismiss the materialised court before anything else.
            var complex = Settlement.CurrentSettlement?.LocationComplex;
            if (complex != null)
                foreach (string id in _ghostIds)
                {
                    Hero ghost = _ceremonyAttended.FirstOrDefault(h => h.StringId == id);
                    if (ghost != null) complex.RemoveCharacterIfExists(ghost);
                }
            _ghostIds.Clear();

            if (k == null) return;
            var op = OpinionBehavior.Instance;
            foreach (Hero head in _ceremonyAttended.Where(h => !_swornIds.Contains(h.StringId)))
            {
                // Attended but was not approached: the oath still counts, entered from the dais.
                op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.SworeFealty);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, +2, false);
            }
            foreach (Hero head in _ceremonyAbsent)
            {
                op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.MissedCeremony);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, -3, false);
            }

            IssueCourtVerdict(k, _ceremonyAttended, _ceremonyAbsent, heldInHall: true);
            TYTLog.Info($"Coronation: hall ceremony closed for {k.Name}; " +
                        $"{_ceremonyAttended.Count} swore ({_swornIds.Count} in person), {_ceremonyAbsent.Count} absent.");
            _swornIds.Clear();
            _ceremonyAttended = new List<Hero>();
            _ceremonyAbsent = new List<Hero>();
        }

        // ── The courier fallback (the old instant resolution) ────────────────────────
        private void ResolveByCourier(Kingdom k, string why)
        {
            ClearPending();
            if (k?.Leader != Hero.MainHero) return;
            RollAttendance(k, out var attended, out var absent);
            var op = OpinionBehavior.Instance;
            foreach (Hero head in attended)
            {
                op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.SworeFealty);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, +2, false);
            }
            foreach (Hero head in absent)
            {
                op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.MissedCeremony);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, -3, false);
            }
            IssueCourtVerdict(k, attended, absent, heldInHall: false);
            TYTLog.Info($"Coronation: resolved by courier for {k.Name} ({why}); {attended.Count} swore, {absent.Count} absent.");
        }

        // The written verdict of the darbar, hall-held or by courier, with the late-oath demand.
        private void IssueCourtVerdict(Kingdom k, List<Hero> attended, List<Hero> absent, bool heldInHall)
        {
            int summoned = attended.Count + absent.Count;
            LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero,
                summoned == 0 ? 0f : (attended.Count - absent.Count) * 1.5f, "the coronation darbar");

            string opening = heldInHall
                ? $"The darbar of accession is held in the hall, and the court of {k.Name} has seen its sovereign enthroned."
                : $"No court was assembled; the oaths of {k.Name} were taken by courier and entered in the registers.";
            string body =
                $"{opening}\n \n{CoronationMath.LoyaltyVerdict(attended.Count, summoned)}\n \n" +
                (summoned == 0
                    ? "No vassal houses yet answer to your throne."
                    : $"Bent the knee ({attended.Count}): {NameList(attended)}.\n \n" +
                      (absent.Count == 0
                          ? "Not one house left an empty place."
                          : $"Left an empty place ({absent.Count}): {NameList(absent)}."));

            if (absent.Count > 0)
                RoyalFarmaan.Issue("Your Coronation Darbar", $"The court of {k.Name}", body,
                    seal: "Held this day, " + RoyalFarmaan.CurrentDate(),
                    primary: "Demand a late oath of the absent houses",
                    onPrimary: () => TYTLog.Guard("Coronation.LateOath", () => DemandLateOath(absent)),
                    secondary: "Let their absence stand — and be remembered",
                    onSecondary: () => Notify("You let the empty places stand. The court will not forget who was missing.", true),
                    priority: FarmaanPriority.Ceremonial);
            else
                RoyalFarmaan.Issue("Your Coronation Darbar", $"The court of {k.Name}", body,
                    seal: "Held this day, " + RoyalFarmaan.CurrentDate(),
                    primary: "Rise as sovereign", priority: FarmaanPriority.Ceremonial);
        }

        private void DemandLateOath(List<Hero> absent)
        {
            var op = OpinionBehavior.Instance;
            var bent = new List<Hero>();
            var defied = new List<Hero>();
            foreach (Hero head in absent.Where(h => h != null && h.IsAlive))
            {
                float opinion = op?.EffectiveOpinion(head, Hero.MainHero) ?? 0f;
                if (CoronationMath.AcceptsLateOath(opinion, MBRandom.RandomFloat))
                {
                    bent.Add(head);
                    op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.SworeFealty);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, +3, false);
                }
                else
                {
                    defied.Add(head);
                    op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.Grudge);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, -5, false);
                }
            }

            string body =
                (bent.Count > 0 ? $"Bent the knee at your demand ({bent.Count}): {NameList(bent)}.\n \n" : "") +
                (defied.Count > 0
                    ? $"Defied you openly ({defied.Count}): {NameList(defied)}. The grudge is set down in the register; take it up with them at court if you would."
                    : "None dared defy the demand.");
            RoyalFarmaan.Issue("The Late Oath", "The court's demand answered", body,
                primary: "Noted", priority: FarmaanPriority.Ceremonial);
            TYTLog.Info($"Coronation: late oath — {bent.Count} bent, {defied.Count} defied.");
        }

        // ── The player is summoned to a new sovereign's coronation ───────────────────
        private void VassalSummons(Kingdom k, Hero sovereign)
        {
            RoyalFarmaan.FromRuler(k, "A Summons to the Coronation",
                $"{RoyalFarmaan.NameWithHonorific(sovereign)} accedes to the throne of {k.Name} and summons the vassals " +
                "of the realm to the darbar to renew their oath. Seek the new sovereign out and bend the knee in person " +
                $"within {CoronationMath.CeremonyDeadlineDays} days — an oath sworn face to face is remembered warmly — " +
                "or send your regrets and keep your distance.",
                primary: "I will travel to court and swear in person",
                onPrimary: () => TYTLog.Guard("Coronation.OathPending", () => BeginVassalOath(sovereign)),
                secondary: "Send regrets — stay away",
                onSecondary: () => TYTLog.Guard("Coronation.Snub", () => SwearToSovereign(sovereign, false)),
                priority: FarmaanPriority.Ceremonial);
        }

        private void BeginVassalOath(Hero sovereign)
        {
            if (sovereign == null || !sovereign.IsAlive) return;
            _oathSovereignId = sovereign.StringId;
            _oathSummonDay = (float)CampaignTime.Now.ToDays;
            string where = sovereign.CurrentSettlement != null
                ? $"He holds court at {sovereign.CurrentSettlement.Name}."
                : "He is in the field — seek him where the akhbaar place him.";
            Notify($"You will swear before {sovereign.Name} in person. {where}", false);
        }

        private Hero OathSovereign()
            => string.IsNullOrEmpty(_oathSovereignId) ? null
               : Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _oathSovereignId);

        private void ClearOath() { _oathSovereignId = ""; _oathSummonDay = -1f; }

        private void TickPendingOath()
        {
            if (string.IsNullOrEmpty(_oathSovereignId)) return;
            Hero s = OathSovereign();
            if (s == null || !s.IsAlive || s.Clan?.Kingdom?.Leader != s || Hero.MainHero?.Clan?.Kingdom != s.Clan.Kingdom)
            { ClearOath(); return; }
            if (CoronationMath.SummonsLapsed(_oathSummonDay, (float)CampaignTime.Now.ToDays))
            {
                ClearOath();
                SwearToSovereign(s, false); // the summons aged unanswered: an empty place after all
            }
        }

        private bool VassalOathCondition()
        {
            Hero p = Hero.OneToOneConversationHero;
            return p != null && !string.IsNullOrEmpty(_oathSovereignId) && p.StringId == _oathSovereignId;
        }

        private void VassalOathSworn()
        {
            Hero s = OathSovereign();
            ClearOath();
            if (s == null || !s.IsAlive) return;
            OpinionBehavior.Instance?.AddOpinion(s, Hero.MainHero, OpinionMath.OpinionType.SworeFealty);
            // Sworn face to face: warmer than the courier's +5 the old popup gave.
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, s, +6, false);
            Notify($"You bend the knee before {s.Name} in person. The new sovereign marks your loyalty — and your courtesy.", false);
        }

        private void SwearToSovereign(Hero sovereign, bool sworn)
        {
            var op = OpinionBehavior.Instance;
            if (sovereign == null || !sovereign.IsAlive) return;
            if (sworn)
            {
                op?.AddOpinion(sovereign, Hero.MainHero, OpinionMath.OpinionType.SworeFealty);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, sovereign, +5, false);
                Notify($"You bend the knee before {sovereign.Name}. Your oath is renewed, and the new sovereign marks your loyalty.", false);
            }
            else
            {
                op?.AddOpinion(sovereign, Hero.MainHero, OpinionMath.OpinionType.MissedCeremony);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, sovereign, -8, false);
                Notify($"You stay away from the coronation of {sovereign.Name}. He marks the empty place where you should have stood.", true);
            }
        }

        // ── The sovereign on his throne (vassal register line) ───────────────────────
        // Whether the player still owes an oath to this hero's coronation (for other systems).
        public bool OwesOathTo(Hero h) => h != null && h.StringId == _oathSovereignId;

        // ── Helpers ──────────────────────────────────────────────────────────────────
        private static string NameList(List<Hero> heroes)
        {
            var names = heroes.Where(h => h != null).Select(h => h.Name.ToString()).ToList();
            if (names.Count <= 6) return string.Join(", ", names);
            return string.Join(", ", names.Take(6)) + $", and {names.Count - 6} more";
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        public override void SyncData(IDataStore dataStore)
        {
            var ids = _lastRuler.Keys.ToList();
            var rulers = _lastRuler.Values.ToList();
            dataStore.SyncData("hind_coron_kids", ref ids);
            dataStore.SyncData("hind_coron_rulers", ref rulers);
            dataStore.SyncData("hind_coron_pend_kid", ref _pendingKingdomId);
            dataStore.SyncData("hind_coron_pend_day", ref _pendingSummonDay);
            dataStore.SyncData("hind_coron_oath_sid", ref _oathSovereignId);
            dataStore.SyncData("hind_coron_oath_day", ref _oathSummonDay);
            if (!dataStore.IsSaving)
            {
                _lastRuler = new Dictionary<string, string>();
                for (int i = 0; i < ids.Count && i < rulers.Count; i++) _lastRuler[ids[i]] = rulers[i];
                if (_pendingKingdomId == null) _pendingKingdomId = "";
                if (_oathSovereignId == null) _oathSovereignId = "";
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("coronation_test", "hindostan")]
        public static string CoronationTest(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k == null) return "You serve no realm.";
            Instance.HoldCoronation(k);
            return k.Leader == Hero.MainHero
                ? $"Coronation darbar SUMMONED for {k.Name} — go to a keep of your realm and hold court in the hall (or wait {CoronationMath.CeremonyDeadlineDays} days for the courier fallback)."
                : $"Coronation staged for {k.Name} (as if {k.Leader?.Name} just acceded).";
        }
    }
}
