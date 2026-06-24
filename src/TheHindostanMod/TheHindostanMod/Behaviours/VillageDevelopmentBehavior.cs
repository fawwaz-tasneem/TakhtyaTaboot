using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Village fief mechanics (design: wiki Chapter 15). A village the player's clan
    // holds carries a living bandit threat that the player must keep down by patrol
    // and by building works (watchtower, militia post). Works also raise the village's
    // hearths and the bound town's prosperity. All effects are applied directly to
    // hearth/prosperity each day — no model overrides needed.
    public class VillageDevelopmentBehavior : CampaignBehaviorBase
    {
        public enum VillageProject { Granary, Watchtower, IrrigationCanal, MilitiaPost, TradePost }

        private struct ProjectDef
        {
            public string Name; public int Cost; public int Days; public string Effect;
            public ProjectDef(string n, int c, int d, string e) { Name = n; Cost = c; Days = d; Effect = e; }
        }

        private static readonly Dictionary<VillageProject, ProjectDef> Defs = new Dictionary<VillageProject, ProjectDef>
        {
            { VillageProject.Granary,        new ProjectDef("Granary",         500, 30, "+0.6 hearth growth per day") },
            { VillageProject.Watchtower,     new ProjectDef("Watchtower",      300, 20, "bandit threat grows far slower") },
            { VillageProject.IrrigationCanal,new ProjectDef("Irrigation Canal",800, 45, "+1.0 hearth growth per day") },
            { VillageProject.MilitiaPost,    new ProjectDef("Militia Post",    600, 25, "passively lowers bandit threat") },
            { VillageProject.TradePost,      new ProjectDef("Trade Post",      400, 30, "raises the bound town's prosperity") },
        };

        public static VillageDevelopmentBehavior Instance { get; private set; }

        // In-memory state; serialized via parallel lists in SyncData.
        private Dictionary<string, float> _threat = new Dictionary<string, float>();           // villageId -> 0..100
        private Dictionary<string, string> _completed = new Dictionary<string, string>();        // villageId -> "Granary,Watchtower"
        private Dictionary<string, string> _buildProject = new Dictionary<string, string>();     // villageId -> project name
        private Dictionary<string, int> _buildDays = new Dictionary<string, int>();              // villageId -> days remaining
        private Dictionary<string, int> _lastPatrolDay = new Dictionary<string, int>();          // villageId -> day index
        private Dictionary<string, int> _reliefUntil = new Dictionary<string, int>();            // villageId -> day relief ends
        private Dictionary<string, int> _reliefTroops = new Dictionary<string, int>();           // villageId -> men to return
        private Dictionary<string, int> _lastOverwhelmDay = new Dictionary<string, int>();       // villageId -> day a plea was last sent
        private const int ReliefDays = 6;
        private const int OverwhelmCooldown = 12;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
        }

        // ── Queries ────────────────────────────────────────────────────────────────
        public float GetThreat(Settlement s) => s != null && _threat.TryGetValue(s.StringId, out float v) ? v : 0f;

        // The player may oversee a village he holds outright (engine owner) OR one he holds in
        // zamindari through our feudal layer — the latter covers a vassal granted a village
        // beneath an AI lord, where the engine owner is still that lord's clan.
        private bool IsPlayerVillage(Settlement s)
            => s != null && s.IsVillage
               && (s.OwnerClan == Clan.PlayerClan
                   || FeudalTitlesBehavior.Instance?.GetVillageLord(s) == Hero.MainHero);

        private bool HasProject(Settlement s, VillageProject p)
            => _completed.TryGetValue(s.StringId, out string list)
               && list.Split(',').Contains(Defs[p].Name);

        // ── Daily simulation ─────────────────────────────────────────────────────────
        private void OnDailyTickSettlement(Settlement s)
        {
            // We're inside the engine's settlement iteration: any farmaan this tick raises (a
            // bandit overwhelm plea) must be enqueued, not shown now. RoyalFarmaan.Pump() shows it
            // later from the campaign TickEvent, outside the iteration. See FarmaanScreen.
            UI.RoyalFarmaan.SuppressImmediate = true;
            try { TYTLog.Guard("VillageDev.DailyTick:" + (s?.Name?.ToString() ?? "?"), () => DailyTickSettlement(s)); }
            finally { UI.RoyalFarmaan.SuppressImmediate = false; }
        }

        private void DailyTickSettlement(Settlement s)
        {
            if (!IsPlayerVillage(s) || s.Village == null) return;

            // Per-step breadcrumbs: a NATIVE crash bypasses the outer Guard's try/catch and leaves
            // no managed report, so the heartbeat's last crumb is the only witness. Naming each step
            // turns "died somewhere in Khanna's tick" into "died in Khanna › MaybeOverwhelm".
            TYTLog.Crumb("UpdateThreat");        UpdateThreat(s);
            TYTLog.Crumb("AdvanceConstruction");  AdvanceConstruction(s);
            TYTLog.Crumb("ApplyHearthProsperity"); ApplyHearthAndProsperity(s);
            TYTLog.Crumb("HandleReliefReturn");   HandleReliefReturn(s);
            TYTLog.Crumb("MaybeOverwhelm");       MaybeOverwhelm(s);
            TYTLog.Crumb("NotifyThreat");         NotifyThreat(s);
        }

        private void UpdateThreat(Settlement s)
        {
            // While a relief force holds the district, the threat recedes steadily.
            if (_reliefUntil.TryGetValue(s.StringId, out int until) && (int)CampaignTime.Now.ToDays < until)
            {
                _threat[s.StringId] = MathF.Max(0f, GetThreat(s) - 8f);
                return;
            }

            float threat = GetThreat(s);
            threat += 1.0f; // bandits always return

            Kingdom owner = s.OwnerClan?.Kingdom;
            bool atWar = owner != null && Kingdom.All.Any(o => o != owner && !o.IsEliminated && owner.IsAtWarWith(o));
            if (atWar) threat += 2.0f;

            if (HasProject(s, VillageProject.MilitiaPost)) threat -= 1.0f;

            if (MobileParty.MainParty != null && MobileParty.MainParty.CurrentSettlement == s)
                threat -= 5.0f; // the lord's presence deters raiders

            // A capable zamindar and a standing militia keep the peace day to day.
            threat -= DefenceStrength(s);

            if (HasProject(s, VillageProject.Watchtower)) threat *= 0.6f;

            _threat[s.StringId] = MathF.Max(0f, MathF.Min(100f, threat));
        }

        // Daily suppression from the village's own defenders: its militia and the
        // stewardship of its zamindar. Scaled by the Militia & zamindar defence weight (MCM).
        private float DefenceStrength(Settlement s)
        {
            float w = Config.Tune.MilitiaDefenceWeight;
            if (w <= 0f) return 0f;
            float militia = s.Militia;
            Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
            int steward = z != null ? z.GetSkillValue(DefaultSkills.Steward) : 0;
            return w * (MathF.Min(2f, militia / 25f) + MathF.Min(2f, steward / 100f));
        }

        // ── Bandit raids overwhelm the militia; the zamindar pleads for relief ──────────
        private void MaybeOverwhelm(Settlement s)
        {
            if (_reliefUntil.ContainsKey(s.StringId)) return;     // already being relieved
            int today = (int)CampaignTime.Now.ToDays;
            if (_lastOverwhelmDay.TryGetValue(s.StringId, out int last) && today - last < OverwhelmCooldown) return;

            float t = GetThreat(s);
            if (t < 50f) return;
            // The weaker the village's own defence relative to the threat, the likelier it breaks.
            float exposure = MathF.Max(0.2f, 1f - DefenceStrength(s) / 6f);
            float chance = Config.Tune.PatrolOverwhelmChance * (t / 100f) * exposure;
            if (MBRandom.RandomFloat >= chance) return;
            TriggerOverwhelm(s);
        }

        private void TriggerOverwhelm(Settlement s)
        {
            _lastOverwhelmDay[s.StringId] = (int)CampaignTime.Now.ToDays;
            _threat[s.StringId] = MathF.Min(100f, GetThreat(s) + 25f); // the militia is broken
            if (s.Village != null) s.Village.Hearth = MathF.Max(0f, s.Village.Hearth - 10f);

            Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
            string sender = (z != null && z != Hero.MainHero) ? $"{z.Name}, your zamindar of {s.Name}," : $"The headman of {s.Name}";
            int men = Config.Tune.ReliefDetachmentSize;

            RoyalFarmaan.Issue("A Plea from the Village", $"{s.Name} is beset",
                $"{sender} sends a desperate letter: bandits have broken the militia and ravage the district. He begs for relief — " +
                $"will you send a commander with {men} men to drive them off, or ride to patrol the lands yourself?",
                seal: "Sealed in haste",
                primary: $"Send a commander with {men} men", onPrimary: () => DispatchRelief(s),
                secondary: "I will see to it myself", onSecondary: () => SelfRelief(s));
        }

        private void DispatchRelief(Settlement s)
        {
            MobileParty party = MobileParty.MainParty;
            if (party == null) return;
            int took = RemoveTroops(party, Config.Tune.ReliefDetachmentSize);
            if (took <= 0) { Notify($"You have no men to spare for the relief of {s.Name}.", true); return; }

            int today = (int)CampaignTime.Now.ToDays;
            _reliefUntil[s.StringId] = today + ReliefDays;
            _reliefTroops[s.StringId] = took;
            _threat[s.StringId] = MathF.Max(0f, GetThreat(s) - 40f);

            Hero comp = Clan.PlayerClan?.Companions?.FirstOrDefault(h => h != null && h.IsAlive);
            string who = comp != null ? comp.Name.ToString() : "your commander";
            Notify($"{who} rides to relieve {s.Name} with {took} men. They will return in {ReliefDays} days.", false);
        }

        private void SelfRelief(Settlement s)
            => Notify($"You resolve to see to {s.Name} yourself. Ride there and patrol the district to drive off the bandits.", false);

        private void HandleReliefReturn(Settlement s)
        {
            if (!_reliefUntil.TryGetValue(s.StringId, out int until)) return;
            if ((int)CampaignTime.Now.ToDays < until) return;
            int count = _reliefTroops.TryGetValue(s.StringId, out int c) ? c : 0;
            _reliefUntil.Remove(s.StringId);
            _reliefTroops.Remove(s.StringId);
            if (count > 0 && MobileParty.MainParty != null)
            {
                CharacterObject troop = (s.Culture ?? s.OwnerClan?.Culture)?.BasicTroop;
                if (troop != null) MobileParty.MainParty.MemberRoster.AddToCounts(troop, count);
                Notify($"Your {count} men return from the relief of {s.Name}.", false);
            }
        }

        // Remove up to 'want' regular (non-hero) troops from the party, returning how many left.
        private static int RemoveTroops(MobileParty p, int want)
        {
            int removed = 0;
            var roster = p.MemberRoster;
            for (int i = roster.Count - 1; i >= 0 && removed < want; i--)
            {
                var e = roster.GetElementCopyAtIndex(i);
                if (e.Character == null || e.Character.IsHero) continue;
                int take = Math.Min(e.Number, want - removed);
                if (take > 0) { roster.AddToCounts(e.Character, -take); removed += take; }
            }
            return removed;
        }

        private void AdvanceConstruction(Settlement s)
        {
            if (!_buildProject.TryGetValue(s.StringId, out string projName)) return;
            int days = _buildDays.TryGetValue(s.StringId, out int d) ? d - 1 : 0;
            if (days > 0) { _buildDays[s.StringId] = days; return; }

            // Completed.
            string list = _completed.TryGetValue(s.StringId, out string c) && !string.IsNullOrEmpty(c)
                ? c + "," + projName : projName;
            _completed[s.StringId] = list;
            _buildProject.Remove(s.StringId);
            _buildDays.Remove(s.StringId);

            if (s.OwnerClan == Clan.PlayerClan)
                Notify($"Construction complete in {s.Name}: the {projName} now stands.", false);
        }

        private void ApplyHearthAndProsperity(Settlement s)
        {
            float hearthDelta = 0f;
            if (HasProject(s, VillageProject.Granary)) hearthDelta += 0.6f;
            if (HasProject(s, VillageProject.IrrigationCanal)) hearthDelta += 1.0f;

            float threat = GetThreat(s);
            if (threat > 90f) hearthDelta = -1.0f;          // village actively bleeds
            else if (threat > 80f) hearthDelta = 0f;        // growth halts
            else if (threat > 60f) hearthDelta *= 0.5f;     // growth slowed

            if (hearthDelta != 0f)
                s.Village.Hearth = MathF.Max(0f, s.Village.Hearth + hearthDelta);

            if (HasProject(s, VillageProject.TradePost))
            {
                Town boundTown = s.Village.Bound?.Town;
                if (boundTown != null) boundTown.Prosperity += 0.3f;
            }
        }

        private void NotifyThreat(Settlement s)
        {
            if (s.OwnerClan != Clan.PlayerClan) return;
            float t = GetThreat(s);
            // Only warn on crossing into a worse band (cheap state: compare to stored band).
            // Simple daily nudge at high threat.
            if (t >= 80f)
                Notify($"Bandits overrun the country around {s.Name}. Patrol soon or the village will wither.", true);
            else if (t >= 60f && MBRandom.RandomFloat < 0.25f)
                Notify($"Banditry is rising around {s.Name}. Consider a patrol.", true);
        }

        // ── Player actions ───────────────────────────────────────────────────────────
        private void Patrol(Settlement s)
        {
            int today = (int)CampaignTime.Now.ToDays;
            if (_lastPatrolDay.TryGetValue(s.StringId, out int last) && last == today)
            { Notify("Your men have already swept the district today.", true); return; }
            _lastPatrolDay[s.StringId] = today;

            float reduction = 20f;
            if (MobileParty.MainParty != null && MobileParty.MainParty.MemberRoster.TotalManCount > 100) reduction += 10f;
            reduction += Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) / 100f * 5f;

            _threat[s.StringId] = MathF.Max(0f, GetThreat(s) - reduction);
            Notify($"You patrol the lands around {s.Name}. Bandit threat falls by {reduction:0}.", false);
        }

        private bool CanBuild(Settlement s, VillageProject p, out string reason)
        {
            reason = "";
            if (HasProject(s, p)) { reason = "Already built."; return false; }
            if (_buildProject.ContainsKey(s.StringId)) { reason = "Another work is already under construction here."; return false; }
            if (Hero.MainHero.Gold < Defs[p].Cost) { reason = $"You need {Defs[p].Cost} gold."; return false; }
            return true;
        }

        private void StartBuild(Settlement s, VillageProject p)
        {
            if (!CanBuild(s, p, out string reason)) { Notify(reason, true); return; }
            Hero.MainHero.ChangeHeroGold(-Defs[p].Cost);
            _buildProject[s.StringId] = Defs[p].Name;
            _buildDays[s.StringId] = Defs[p].Days;
            Notify($"Work begins on the {Defs[p].Name} in {s.Name}. It will take {Defs[p].Days} days.", false);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Menus ────────────────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Re-expose the vanilla village market. The economy itself is untouched —
            // this just guarantees a way to reach the buy/sell screen from the village.
            starter.AddGameMenuOption("village", "hindostan_village_trade", "{=!}Buy and sell goods",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Trade;
                    Settlement s = Settlement.CurrentSettlement;
                    if (s == null || !s.IsVillage) return false;
                    bool hostile = s.OwnerClan != null && Hero.MainHero?.MapFaction != null
                                   && s.OwnerClan.MapFaction != null
                                   && Hero.MainHero.MapFaction.IsAtWarWith(s.OwnerClan.MapFaction);
                    args.IsEnabled = !hostile;
                    if (hostile) args.Tooltip = new TextObject("{=!}You are at war with this village's lord.");
                    return true;
                },
                args =>
                {
                    if (!UI.TradeScreenHelper.OpenTradeWith(Settlement.CurrentSettlement))
                        Notify("The villagers have nothing to trade just now.", true);
                }, false, 1);

            starter.AddGameMenuOption("village", "hindostan_village_oversee", "{=!}Oversee your fief",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage;
                          return IsPlayerVillage(Settlement.CurrentSettlement); },
                args => GameMenu.SwitchToMenu("hindostan_village"), false, 3);

            starter.AddGameMenu("hindostan_village", "{=!}{HINDOSTAN_VILLAGE_TEXT}", VillageMenuInit);

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_patrol",
                "{=!}Patrol the village territory",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args => { Patrol(Settlement.CurrentSettlement); VillageMenuInit(args); });

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_projects",
                "{=!}Order construction works",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Trade; return true; },
                args => GameMenu.SwitchToMenu("hindostan_village_projects"));

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_appoint_zamindar",
                "{=!}Appoint or replace the village zamindar",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
                          return FeudalTitlesBehavior.Instance?.PlayerMayManage(Settlement.CurrentSettlement) ?? false; },
                args => { FeudalTitlesBehavior.Instance?.OpenManageZamindarDialog(Settlement.CurrentSettlement);
                          VillageMenuInit(args); });

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_dismiss_zamindar",
                "{=!}Dismiss the current zamindar",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
                          var ft = FeudalTitlesBehavior.Instance;
                          return ft != null && ft.PlayerMayManage(Settlement.CurrentSettlement)
                                 && ft.GetVillageLord(Settlement.CurrentSettlement) != null; },
                args => { FeudalTitlesBehavior.Instance?.DismissZamindar(Settlement.CurrentSettlement);
                          VillageMenuInit(args); });

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_leave", "{=!}Leave",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu("village"), true);

            // ── Construction sub-menu ──
            starter.AddGameMenu("hindostan_village_projects", "{=!}{HINDOSTAN_VILLAGE_PROJ_TEXT}", ProjectsMenuInit);

            foreach (VillageProject p in Enum.GetValues(typeof(VillageProject)))
            {
                VillageProject proj = p; // capture
                ProjectDef def = Defs[proj];
                starter.AddGameMenuOption("hindostan_village_projects", "hindostan_proj_" + proj,
                    "{=!}Build " + def.Name + " (" + def.Cost + "g, " + def.Days + " days) — " + def.Effect,
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Trade;
                              return CanBuild(Settlement.CurrentSettlement, proj, out _); },
                    args => { StartBuild(Settlement.CurrentSettlement, proj); GameMenu.SwitchToMenu("hindostan_village"); });
            }

            starter.AddGameMenuOption("hindostan_village_projects", "hindostan_proj_back", "{=!}Back",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu("hindostan_village"), true);
        }

        private void VillageMenuInit(MenuCallbackArgs args)
        {
            Settlement s = Settlement.CurrentSettlement;
            var sb = new StringBuilder();
            sb.AppendLine($"Your Fief: {s?.Name}");
            sb.AppendLine(" ");
            if (s?.Village != null)
            {
                sb.AppendLine($"Hearth: {(int)s.Village.Hearth}");
                sb.AppendLine($"Bandit threat: {GetThreat(s):0}/100  ({ThreatBand(GetThreat(s))})");
                Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
                sb.AppendLine($"Zamindar (village lord): {(z != null ? z.Name.ToString() : "vacant")}");
                int steward = z != null ? z.GetSkillValue(DefaultSkills.Steward) : 0;
                sb.AppendLine($"Militia: {(int)s.Militia}    Zamindar stewardship: {steward}");
                if (_reliefUntil.TryGetValue(s.StringId, out int until))
                    sb.AppendLine($"Under relief for {Math.Max(0, until - (int)CampaignTime.Now.ToDays)} more days.");
            }
            if (_buildProject.TryGetValue(s.StringId, out string pn))
                sb.AppendLine($"Under construction: {pn} ({(_buildDays.TryGetValue(s.StringId, out int bd) ? bd : 0)} days left)");
            string built = _completed.TryGetValue(s.StringId, out string c) ? c : "";
            sb.AppendLine($"Works built: {(string.IsNullOrEmpty(built) ? "none" : built.Replace(",", ", "))}");
            sb.AppendLine(" ");
            sb.AppendLine("What is your will?");
            MBTextManager.SetTextVariable("HINDOSTAN_VILLAGE_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }

        private void ProjectsMenuInit(MenuCallbackArgs args)
        {
            Settlement s = Settlement.CurrentSettlement;
            var sb = new StringBuilder();
            sb.AppendLine($"Construction works at {s?.Name}");
            sb.AppendLine($"Your gold: {Hero.MainHero.Gold}");
            sb.AppendLine(" ");
            sb.AppendLine("A village may hold one work under construction at a time.");
            MBTextManager.SetTextVariable("HINDOSTAN_VILLAGE_PROJ_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }

        private static string ThreatBand(float t)
            => t >= 90 ? "the village is being ruined" : t >= 80 ? "growth has halted"
             : t >= 60 ? "growth is slowed" : t >= 40 ? "watchful" : "calm";

        // ── Save / load ──────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var threatIds = _threat.Keys.ToList();
            var threatVals = _threat.Values.ToList();
            var compIds = _completed.Keys.ToList();
            var compVals = _completed.Values.ToList();
            var buildIds = _buildProject.Keys.ToList();
            var buildProj = _buildProject.Values.ToList();
            var buildDayIds = _buildDays.Keys.ToList();
            var buildDayVals = _buildDays.Values.ToList();

            dataStore.SyncData("hind_vil_threatIds", ref threatIds);
            dataStore.SyncData("hind_vil_threatVals", ref threatVals);
            dataStore.SyncData("hind_vil_compIds", ref compIds);
            dataStore.SyncData("hind_vil_compVals", ref compVals);
            dataStore.SyncData("hind_vil_buildIds", ref buildIds);
            dataStore.SyncData("hind_vil_buildProj", ref buildProj);
            dataStore.SyncData("hind_vil_buildDayIds", ref buildDayIds);
            dataStore.SyncData("hind_vil_buildDayVals", ref buildDayVals);

            var reliefIds = _reliefUntil.Keys.ToList();
            var reliefVals = _reliefUntil.Values.ToList();
            var reliefTIds = _reliefTroops.Keys.ToList();
            var reliefTVals = _reliefTroops.Values.ToList();
            dataStore.SyncData("hind_vil_reliefIds", ref reliefIds);
            dataStore.SyncData("hind_vil_reliefVals", ref reliefVals);
            dataStore.SyncData("hind_vil_reliefTIds", ref reliefTIds);
            dataStore.SyncData("hind_vil_reliefTVals", ref reliefTVals);

            if (!dataStore.IsSaving)
            {
                _threat = Zip(threatIds, threatVals);
                _completed = Zip(compIds, compVals);
                _buildProject = Zip(buildIds, buildProj);
                _buildDays = Zip(buildDayIds, buildDayVals);
                _lastPatrolDay = new Dictionary<string, int>();
                _reliefUntil = Zip(reliefIds, reliefVals);
                _reliefTroops = Zip(reliefTIds, reliefTVals);
                _lastOverwhelmDay = new Dictionary<string, int>();
            }
        }

        private static Dictionary<string, T> Zip<T>(List<string> keys, List<T> vals)
        {
            var d = new Dictionary<string, T>();
            for (int i = 0; i < keys.Count && i < vals.Count; i++) d[keys[i]] = vals[i];
            return d;
        }

        // ── Console (testing) ──────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("village_status", "hindostan")]
        public static string VillageStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            var villages = Settlement.All.Where(s => s.IsVillage && s.OwnerClan == Clan.PlayerClan).ToList();
            if (villages.Count == 0) return "Your clan holds no villages.";
            return string.Join("\n", villages.Select(s =>
                $"{s.Name}: hearth {(int)s.Village.Hearth}, threat {Instance.GetThreat(s):0}"));
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set_village_threat", "hindostan")]
        public static string SetThreat(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Settlement.CurrentSettlement == null || !Settlement.CurrentSettlement.IsVillage)
                return "Enter one of your villages first.";
            float v = 50f;
            if (args != null && args.Count > 0) float.TryParse(args[0], out v);
            Instance._threat[Settlement.CurrentSettlement.StringId] = MathF.Max(0f, MathF.Min(100f, v));
            return $"{Settlement.CurrentSettlement.Name} threat set to {v:0}.";
        }
    }
}
