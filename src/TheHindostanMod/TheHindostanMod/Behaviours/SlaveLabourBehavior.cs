using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Bonded labour (bandi/begar) on village fiefs — roadmap A.2, user-requested. Battle
    // captives in the player's prison train can be settled to forced labour in a village he
    // holds. The gang raises the village's yield (tax + prosperity to the bound town) but
    // breeds resentment (a daily unrest term added to bandit threat) and thins over time as
    // men escape — fugitives swelling the very banditry they feed. The whole trade-off is
    // deterministic and explained (SlaveLabourMath, tested).
    //
    // Ownership split: THIS behavior owns the gang counts, the settle/free UI, the daily
    // attrition, and save/load. VillageDevelopmentBehavior READS the yields (tax %, unrest,
    // prosperity) through the static queries below, so the village tax/threat pipeline stays
    // in one place. Surface per the UX charter: place-bound acts → the village menu.
    public class SlaveLabourBehavior : CampaignBehaviorBase
    {
        public static SlaveLabourBehavior Instance { get; private set; }

        private Dictionary<string, int> _labour = new Dictionary<string, int>(); // villageId -> gang size

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
        }

        // ── Queries the village simulation reads ─────────────────────────────────────
        public int LabourCount(Settlement s) => s != null && _labour.TryGetValue(s.StringId, out int n) ? n : 0;

        public float TaxBonusPct(Settlement s)
            => Config.Tune.SlaveLabourEnabled ? SlaveLabourMath.TaxBonusPct(LabourCount(s)) : 0f;

        public float DailyUnrest(Settlement s)
            => Config.Tune.SlaveLabourEnabled && s?.Village != null
               ? SlaveLabourMath.DailyUnrest(LabourCount(s), s.Village.Hearth) : 0f;

        public float BoundProsperityPerDay(Settlement s)
            => Config.Tune.SlaveLabourEnabled ? SlaveLabourMath.BoundProsperityPerDay(LabourCount(s)) : 0f;

        // ── Daily attrition & escape ─────────────────────────────────────────────────
        private void OnDailyTickSettlement(Settlement s)
        {
            if (s == null || !s.IsVillage || LabourCount(s) <= 0) return;
            UI.RoyalFarmaan.SuppressImmediate = true; // inside settlement iteration; enqueue only
            try { TYTLog.Guard("SlaveLabour.DailyTick:" + s.Name, () => DailyTick(s)); }
            finally { UI.RoyalFarmaan.SuppressImmediate = false; }
        }

        private void DailyTick(Settlement s)
        {
            int gang = LabourCount(s);
            float threat = VillageDevelopmentBehavior.Instance?.GetThreat(s) ?? 0f;
            int lost = SlaveLabourMath.DailyLoss(gang, threat, MBRandom.RandomFloat);
            if (lost <= 0) return;

            int remaining = Math.Max(0, gang - lost);
            if (remaining <= 0) _labour.Remove(s.StringId); else _labour[s.StringId] = remaining;

            // Fugitives flee to the hills and feed the banditry; the rest die at the work.
            int fled = SlaveLabourMath.Fugitives(lost);
            if (fled > 0)
                VillageDevelopmentBehavior.Instance?.AddThreat(s, fled * 2f);

            // Only the player's own villages get a notice; AI gangs thin silently.
            if (IsPlayerVillage(s))
                Notify($"At {s.Name}, {lost} bonded labourer(s) are lost" +
                       (fled > 0 ? $" — {fled} fled to the bandit country" : " to the work") + ".", true);
            TYTLog.Info($"SlaveLabour: {s.StringId} lost {lost} ({fled} fled), {remaining} remain.");
        }

        private static bool IsPlayerVillage(Settlement s)
            => s != null && s.IsVillage
               && (s.OwnerClan == Clan.PlayerClan
                   || FeudalTitlesBehavior.Instance?.GetVillageLord(s) == Hero.MainHero);

        // ── The village menu ─────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("hindostan_village", "hindostan_village_settle_labour",
                "{=!}Settle captive labourers here",
                SettleCondition, args => TYTLog.Guard("SlaveLabour.Settle", () => OpenSettle(args)), false, 5);

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_free_labour",
                "{=!}Free the bonded labourers",
                FreeCondition, args => TYTLog.Guard("SlaveLabour.Free", () => Free(args)), false, 6);
        }

        private bool SettleCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
            Settlement s = Settlement.CurrentSettlement;
            if (!Config.Tune.SlaveLabourEnabled || !IsPlayerVillage(s)) return false;

            int cap = SlaveLabourMath.LabourCap(s.Village?.Hearth ?? 0f);
            int room = cap - LabourCount(s);
            int captives = CaptiveCount(MobileParty.MainParty);
            args.IsEnabled = room > 0 && captives > 0;
            if (room <= 0) args.Tooltip = new TextObject("{=!}This village already holds all the bonded labour it can.");
            else if (captives <= 0) args.Tooltip = new TextObject("{=!}You have no captives in your train to settle.");
            return true;
        }

        private bool FreeCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
            Settlement s = Settlement.CurrentSettlement;
            if (!Config.Tune.SlaveLabourEnabled || !IsPlayerVillage(s) || LabourCount(s) <= 0) return false;
            return true;
        }

        private void OpenSettle(MenuCallbackArgs args)
        {
            Settlement s = Settlement.CurrentSettlement;
            if (s?.Village == null) return;
            int cap = SlaveLabourMath.LabourCap(s.Village.Hearth);
            int room = cap - LabourCount(s);
            int captives = CaptiveCount(MobileParty.MainParty);
            int settle = Math.Min(room, captives);
            if (settle <= 0) return;

            InformationManager.ShowInquiry(new InquiryData(
                "Bind Captives to Labour",
                $"Set {settle} of your captives to forced labour (begar) in {s.Name}?\n\n" +
                $"The gang would number {LabourCount(s) + settle} of a possible {cap}. Bonded hands raise the " +
                "village's tax and feed the bound town's market — but forced labour breeds unrest that no watchtower " +
                "can police, and the gang will thin as men escape to the bandit country or die at the work.",
                true, true, "Bind them", "Cancel",
                () => TYTLog.Guard("SlaveLabour.Bind", () => Settle(s, settle)), null), true);
        }

        private void Settle(Settlement s, int want)
        {
            MobileParty p = MobileParty.MainParty;
            if (s?.Village == null || p == null) return;
            int cap = SlaveLabourMath.LabourCap(s.Village.Hearth);
            int room = cap - LabourCount(s);
            int taken = RemoveCaptives(p, Math.Min(want, room));
            if (taken <= 0) { Notify("There were no captives to bind.", true); return; }

            _labour[s.StringId] = LabourCount(s) + taken;
            Notify($"{taken} captive(s) are bound to labour in {s.Name}. The gang now numbers {LabourCount(s)} of {cap}.", false);
            TYTLog.Info($"SlaveLabour: settled {taken} at {s.StringId}, gang {LabourCount(s)}.");
            RefreshMenu();
        }

        private void Free(MenuCallbackArgs args)
        {
            Settlement s = Settlement.CurrentSettlement;
            if (s == null) return;
            int freed = LabourCount(s);
            if (freed <= 0) return;
            _labour.Remove(s.StringId);
            // Manumission quiets the district and earns the local gentry's regard.
            VillageDevelopmentBehavior.Instance?.AddThreat(s, -MathF.Min(15f, (float)freed));
            foreach (Hero n in s.Notables?.Where(h => h != null && h.IsAlive) ?? Enumerable.Empty<Hero>())
                TaleWorlds.CampaignSystem.Actions.ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, +1);
            Notify($"You free the {freed} bonded labourer(s) of {s.Name}. The district breathes easier.", false);
            TYTLog.Info($"SlaveLabour: freed {freed} at {s.StringId}.");
            RefreshMenu();
        }

        private static void RefreshMenu()
        {
            // Re-render the village menu text so the labour line updates immediately.
            if (Campaign.Current?.CurrentMenuContext != null)
                GameMenu.SwitchToMenu("hindostan_village");
        }

        // ── Captive helpers ──────────────────────────────────────────────────────────
        // Only common (non-hero) prisoners are eligible for begar; captured lords are not.
        private static int CaptiveCount(MobileParty p)
        {
            if (p == null) return 0;
            int n = 0;
            TroopRoster roster = p.PrisonRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster.GetElementCopyAtIndex(i);
                if (e.Character != null && !e.Character.IsHero && e.Number > 0) n += e.Number;
            }
            return n;
        }

        private static int RemoveCaptives(MobileParty p, int want)
        {
            if (p == null || want <= 0) return 0;
            int removed = 0;
            TroopRoster roster = p.PrisonRoster;
            for (int i = roster.Count - 1; i >= 0 && removed < want; i--)
            {
                var e = roster.GetElementCopyAtIndex(i);
                if (e.Character == null || e.Character.IsHero || e.Number <= 0) continue;
                int take = Math.Min(e.Number, want - removed);
                if (take > 0) { roster.AddToCounts(e.Character, -take); removed += take; }
            }
            return removed;
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Save / load ──────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var ids = _labour.Keys.ToList();
            var counts = _labour.Values.ToList();
            dataStore.SyncData("hind_labour_ids", ref ids);
            dataStore.SyncData("hind_labour_counts", ref counts);
            if (!dataStore.IsSaving)
            {
                _labour = new Dictionary<string, int>();
                for (int i = 0; i < ids.Count && i < counts.Count; i++)
                    if (counts[i] > 0) _labour[ids[i]] = counts[i];
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("labour_status", "hindostan")]
        public static string LabourStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Instance._labour.Count == 0) return "No village holds bonded labour.";
            return string.Join("\n", Instance._labour.Select(kv =>
            {
                Settlement s = Settlement.Find(kv.Key);
                int cap = s?.Village != null ? SlaveLabourMath.LabourCap(s.Village.Hearth) : 0;
                return $"{s?.Name?.ToString() ?? kv.Key}: {kv.Value}/{cap} labourers " +
                       $"(+{SlaveLabourMath.TaxBonusPct(kv.Value):0.0}% tax, +{Instance.DailyUnrest(s):0.0} unrest/day)";
            }));
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("settle_labour", "hindostan")]
        public static string SettleLabour(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Settlement s = Settlement.CurrentSettlement;
            if (s == null || !s.IsVillage) return "Enter one of your villages first.";
            int n = 5;
            if (args != null && args.Count > 0) int.TryParse(args[0], out n);
            int cap = SlaveLabourMath.LabourCap(s.Village.Hearth);
            int add = Math.Max(0, Math.Min(n, cap - Instance.LabourCount(s)));
            Instance._labour[s.StringId] = Instance.LabourCount(s) + add;
            return $"{s.Name}: gang set to {Instance.LabourCount(s)}/{cap} (added {add}, no captives spent).";
        }
    }
}
