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

namespace TakhtyaTaboot
{
    // Padshahi Hukm — Imperial Authority (0-100) per kingdom: how far the emperor's
    // writ actually runs. High = taxes flow and lords obey; low = governors withhold
    // revenue, armies disperse, and the provinces drift toward independence.
    public class ImperialAuthorityBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, float> _authority = new Dictionary<string, float>(); // kingdomId -> 0..100

        public static ImperialAuthorityBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            // Seeding happens at session launch, NOT OnNewGameCreated: that event fires while the
            // engine is still creating kingdoms/clans on parallel threads (see Util/WorldGen.cs).
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("ImperialAuthority.WeeklyTick", OnWeeklyTick));
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public float GetAuthority(Kingdom kingdom)
            => kingdom != null && _authority.TryGetValue(kingdom.StringId, out float v) ? v : 75f;

        public void ModifyAuthority(Kingdom kingdom, float delta, string reason)
        {
            if (kingdom == null) return;
            float current = GetAuthority(kingdom);
            float next = Math.Max(0f, Math.Min(100f, current + delta));
            _authority[kingdom.StringId] = next;

            if (kingdom == Hero.MainHero?.Clan?.Kingdom)
            {
                float[] thresholds = { 75f, 50f, 25f, 10f };
                foreach (float t in thresholds)
                    if (current > t && next <= t) NotifyThreshold(kingdom, t, reason);
            }
        }

        private void NotifyThreshold(Kingdom kingdom, float t, string cause)
        {
            string msg = t switch
            {
                75f => $"Imperial authority in {kingdom.Name} is weakening. Lords test the emperor's resolve.",
                50f => $"The emperor's writ no longer runs freely. Governors withhold revenue. ({cause})",
                25f => $"Authority has collapsed in the provinces. Lords act as independent rulers.",
                10f => $"The empire exists in name only. Fragmentation is imminent.",
                _   => ""
            };
            if (!string.IsNullOrEmpty(msg))
                InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFCC2200)));
        }

        public string GetTier(Kingdom k)
        {
            float a = GetAuthority(k);
            return a >= 75 ? "Firm" : a >= 50 ? "Weakening" : a >= 25 ? "Faltering" : a >= 10 ? "Collapsing" : "In name only";
        }

        // ── Effect APIs ───────────────────────────────────────────────────────────
        public float GetTaxCollectionRate(Kingdom k)
            => GetAuthority(k) switch { >= 75 => 1.00f, >= 50 => 0.80f, >= 25 => 0.55f, >= 10 => 0.30f, _ => 0.10f };

        public float GetCallToArmsCompliance(Kingdom k)
            => GetAuthority(k) switch { >= 75 => 0.90f, >= 50 => 0.65f, >= 25 => 0.35f, >= 10 => 0.15f, _ => 0.05f };

        public bool CanIssueImperialDecree(Kingdom k) => GetAuthority(k) >= 40f;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        // Idempotent: only kingdoms without a stored meter are seeded, so loading a save
        // never clobbers persisted authority values.
        private void SeedMissing()
        {
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated))
                if (!_authority.ContainsKey(k.StringId))
                    _authority[k.StringId] = 75f;
        }

        private static int CountWars(Kingdom kingdom)
            => Kingdom.All.Count(other => other != kingdom && !other.IsEliminated
                                          && FactionManager.IsAtWarAgainstFaction(kingdom, other));

        private static int CountDisloyalLords(Kingdom kingdom)
            => kingdom.Leader == null ? 0
             : kingdom.Clans.Count(c => c.Leader != null && c.Leader != kingdom.Leader
                                        && CharacterRelationManager.GetHeroRelation(kingdom.Leader, c.Leader) < -30);

        private void OnWeeklyTick()
        {
            foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated))
            {
                int wars = CountWars(kingdom);
                if (wars >= 2) ModifyAuthority(kingdom, -2f, "simultaneous wars");

                int disloyal = CountDisloyalLords(kingdom);
                if (disloyal > 0) ModifyAuthority(kingdom, -0.5f * disloyal, "disloyal lords");

                float legit = LegitimacyBehavior.Instance?.GetLegitimacy(kingdom.Leader) ?? 50f;
                if (wars == 0 && legit > 60f) ModifyAuthority(kingdom, +1f, "peace and stable rule");
            }
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!Util.WorldGen.Ready) return; // skip the parallel world-gen distribution (see Util/WorldGen.cs)
            if (settlement == null || settlement.IsVillage) return;
            Kingdom lost = oldOwner?.Clan?.Kingdom;
            Kingdom gained = newOwner?.Clan?.Kingdom;
            if (lost != null && lost != gained)
                ModifyAuthority(lost, -5f, $"lost {settlement.Name}");
        }

        // ── Menu: State of the Empire ─────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SeedMissing();
            // Lives under the consolidated court menu (CourtMenuBehavior).
            starter.AddGameMenuOption(CourtMenuBehavior.MenuId, "hindostan_empire", "{=!}Survey the state of the empire",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                args => GameMenu.SwitchToMenu("hindostan_empire_state"), false, 5);

            starter.AddGameMenu("hindostan_empire_state", "{=!}{HINDOSTAN_EMPIRE_TEXT}", EmpireMenuInit);

            // The sovereign's two clear levers (playtest round 2: raising authority and
            // legitimacy "should be clear and possible"). Costs and cooldowns shown up front;
            // disabled options explain themselves via tooltip.
            starter.AddGameMenuOption("hindostan_empire_state", "hindostan_empire_darbar",
                "{=!}Hold a grand darbar (" + GrandDarbarCost + " rupees)",
                DarbarCondition, args => Util.TYTLog.Guard("ImperialAuthority.Darbar", HoldGrandDarbar), false, 1);
            starter.AddGameMenuOption("hindostan_empire_state", "hindostan_empire_charity",
                "{=!}Endow charitable works (" + CharityCost + " rupees)",
                CharityCondition, args => Util.TYTLog.Guard("ImperialAuthority.Charity", EndowCharity), false, 2);

            starter.AddGameMenuOption("hindostan_empire_state", "hindostan_empire_leave", "{=!}Back",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu(CourtMenuBehavior.MenuId),
                true);
        }

        // ── The sovereign's levers ────────────────────────────────────────────────
        private const int GrandDarbarCost = 20000;
        private const int CharityCost = 15000;
        private const int LeverCooldownDays = 60;
        private int _lastDarbarDay = -10000;
        private int _lastCharityDay = -10000;

        private static bool IsSovereign
            => Hero.MainHero?.Clan?.Kingdom != null && Hero.MainHero.Clan.Kingdom.Leader == Hero.MainHero;

        private bool LeverCondition(MenuCallbackArgs args, int lastDay, int cost, string act)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Continue;
            if (!IsSovereign)
            { args.IsEnabled = false; args.Tooltip = new TextObject("{=!}Only a reigning sovereign may " + act + "."); return true; }
            int wait = LeverCooldownDays - ((int)CampaignTime.Now.ToDays - lastDay);
            if (wait > 0)
            { args.IsEnabled = false; args.Tooltip = new TextObject($"{{=!}}The court still speaks of the last occasion — wait {wait} more day(s)."); return true; }
            if (Hero.MainHero.Gold < cost)
            { args.IsEnabled = false; args.Tooltip = new TextObject($"{{=!}}This demands {cost} rupees (you have {Hero.MainHero.Gold})."); return true; }
            return true;
        }

        private bool DarbarCondition(MenuCallbackArgs args)
            => LeverCondition(args, _lastDarbarDay, GrandDarbarCost, "hold a darbar");
        private bool CharityCondition(MenuCallbackArgs args)
            => LeverCondition(args, _lastCharityDay, CharityCost, "endow charitable works");

        // A grand darbar: the realm's great men are summoned, gifts pass both ways, and the
        // emperor is SEEN to reign. Authority first, a touch of legitimacy, warmer lords.
        private void HoldGrandDarbar()
        {
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k == null || !IsSovereign || Hero.MainHero.Gold < GrandDarbarCost) return;
            Hero.MainHero.ChangeHeroGold(-GrandDarbarCost);
            _lastDarbarDay = (int)CampaignTime.Now.ToDays;

            ModifyAuthority(k, +5f, "a grand darbar");
            LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, +2f, "the realm saw its sovereign enthroned");
            foreach (Clan c in k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != Hero.MainHero)
                                      .OrderByDescending(c => c.Influence).Take(8))
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Leader, +2, false);

            UI.RoyalFarmaan.FromRuler(k, "A Grand Darbar Is Held",
                "The great amirs and mansabdars are summoned to court; nazrana is presented, robes of honour " +
                "are bestowed, and every man leaves reminded of where the sun of Hindostan rises. " +
                "The emperor's writ runs a little firmer.", "So it was seen");
            GameMenu.SwitchToMenu("hindostan_empire_state"); // redraw the survey with new numbers
        }

        // Charity in the ruler's own faith: legitimacy first, a touch of authority.
        private void EndowCharity()
        {
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k == null || !IsSovereign || Hero.MainHero.Gold < CharityCost) return;
            Hero.MainHero.ChangeHeroGold(-CharityCost);
            _lastCharityDay = (int)CampaignTime.Now.ToDays;

            Religion faith = ReligionBehavior.Instance?.GetReligion(Hero.MainHero) ?? Religion.None;
            string works = faith == Religion.Islam ? "waqf endowments for mosques, wells and travellers' serais"
                         : faith == Religion.Hindu ? "annadana at the temples and ghats for pilgrims and the poor"
                         : faith == Religion.Sikh ? "langar kitchens that turn no soul away"
                         : "alms-houses and wells for the poor";

            LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, +5f, "charitable works in the ruler's name");
            ModifyAuthority(k, +1f, "charitable works");

            UI.RoyalFarmaan.FromRuler(k, "Charitable Works Are Endowed",
                $"From the sovereign's own purse, {works} are endowed across the realm. The qazis and pandits " +
                "alike speak the ruler's name with warmth; who feeds the people is fit to rule them.", "It is done");
            GameMenu.SwitchToMenu("hindostan_empire_state");
        }

        private void EmpireMenuInit(MenuCallbackArgs args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("State of the Empire");
            sb.AppendLine(" ");

            Kingdom kingdom = Hero.MainHero?.Clan?.Kingdom;
            if (kingdom == null)
            {
                sb.AppendLine("You serve no empire. Pledge to a kingdom to share in its fortunes.");
            }
            else
            {
                float auth = GetAuthority(kingdom);
                Hero ruler = kingdom.Leader;
                float legit = LegitimacyBehavior.Instance?.GetLegitimacy(ruler) ?? 60f;
                string legitTier = LegitimacyBehavior.Instance?.GetTier(ruler) ?? "Secure";

                sb.AppendLine($"Realm: {kingdom.Name}");
                sb.AppendLine($"Padshahi Hukm (Imperial Authority): {auth:0} / 100  [{GetTier(kingdom)}]");
                sb.AppendLine($"Legitimacy of {ruler?.Name}: {legit:0} / 100  [{legitTier}]");
                sb.AppendLine(" ");
                sb.AppendLine($"Tax actually collected: {GetTaxCollectionRate(kingdom) * 100f:0}%");
                sb.AppendLine($"Lords answering the call: {GetCallToArmsCompliance(kingdom) * 100f:0}%");
                sb.AppendLine(" ");
                sb.AppendLine("— What moves authority —");
                int wars = CountWars(kingdom);
                int disloyal = CountDisloyalLords(kingdom);
                sb.AppendLine($"   Simultaneous wars: {wars}" + (wars >= 2 ? "   (draining authority)" : ""));
                sb.AppendLine($"   Disloyal lords: {disloyal}" + (disloyal > 0 ? "   (draining authority)" : ""));
                sb.AppendLine(wars == 0 && legit > 60f ? "   At peace under a legitimate ruler (recovering)" : "   ");
                sb.AppendLine(" ");
                sb.AppendLine("— Raising authority & legitimacy —");
                sb.AppendLine("   Peace under a legitimate ruler recovers authority weekly");
                sb.AppendLine("   Hold a grand darbar (below): authority +5, legitimacy +2");
                sb.AppendLine("   Endow charitable works (below): legitimacy +5, authority +1");
                sb.AppendLine("   Crush a pretender or rebel: authority +8");
                sb.AppendLine("   Take a besieged city on honoured terms: legitimacy +4 (defying them costs dearly)");
                sb.AppendLine("   Mend quarrels with disloyal lords: each lord below -30 relation drains authority");
            }

            MBTextManager.SetTextVariable("HINDOSTAN_EMPIRE_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }

        public override void SyncData(IDataStore dataStore)
        {
            var ids = _authority.Keys.ToList();
            var vals = _authority.Values.ToList();
            dataStore.SyncData("hind_authority_ids", ref ids);
            dataStore.SyncData("hind_authority_vals", ref vals);
            dataStore.SyncData("hind_authority_darbar_day", ref _lastDarbarDay);
            dataStore.SyncData("hind_authority_charity_day", ref _lastCharityDay);
            if (!dataStore.IsSaving)
            {
                _authority.Clear();
                for (int i = 0; i < ids.Count; i++)
                    _authority[ids[i]] = i < vals.Count ? vals[i] : 75f;
            }
        }
    }
}
