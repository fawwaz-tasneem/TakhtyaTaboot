using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace TakhtyaTaboot
{
    // The single court doorway. The mod's town/castle options used to pile up in the
    // settlement root menu; they now live one level down, behind ONE entry per root:
    // "Attend to matters of court and realm" -> the hindostan_court menu. Behaviors add
    // their options to "hindostan_court" instead of "town"/"castle".
    //
    // MUST be registered (and thus session-launched) BEFORE every behavior that adds
    // options to hindostan_court — the menu has to exist first.
    public class CourtMenuBehavior : CampaignBehaviorBase
    {
        public const string MenuId = "hindostan_court";

        public override void RegisterEvents()
            => CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);

        public override void SyncData(IDataStore dataStore) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            foreach (string root in new[] { "town", "castle" })
                starter.AddGameMenuOption(root, "hindostan_court_entry_" + root,
                    "{=!}Attend to matters of court and realm",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                    args => GameMenu.SwitchToMenu(MenuId), false, 4);

            starter.AddGameMenu(MenuId, "{=!}{HINDOSTAN_COURT_TEXT}", CourtMenuInit);

            // The Back option sorts LAST (index 99) so behaviors' options sit above it.
            starter.AddGameMenuOption(MenuId, "hindostan_court_back", "{=!}Back",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu(Settlement.CurrentSettlement != null && Settlement.CurrentSettlement.IsTown ? "town" : "castle"),
                true, 99);
        }

        private void CourtMenuInit(MenuCallbackArgs args)
        {
            var sb = new StringBuilder();
            Settlement s = Settlement.CurrentSettlement;
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            sb.AppendLine($"The court at {s?.Name}");
            sb.AppendLine(" ");
            if (k != null)
            {
                sb.AppendLine($"Realm: {k.Name}    Sovereign: {k.Leader?.Name}");
                if (ImperialAuthorityBehavior.Instance != null)
                    sb.AppendLine($"Imperial authority: {ImperialAuthorityBehavior.Instance.GetTier(k)}");
            }
            else sb.AppendLine("You serve no realm.");
            sb.AppendLine(" ");
            sb.AppendLine("What business brings you to court?");
            MBTextManager.SetTextVariable("HINDOSTAN_COURT_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }
    }
}
