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
using TheHindostanMod.UI;

namespace TheHindostanMod
{
    // The councils of Hindostan, at every landed rung of the feudal ladder. Each lord
    // who holds a town or castle — and the sovereign above them all — keeps a council
    // of three great offices, filled from his own vassals, kin and companions:
    //
    //   Diwan-i-Ala (Vizier)     — chief minister
    //   Mir Bakshi (Paymaster)   — head of the pay office
    //   Diwan-i-Kul (Treasurer)  — master of revenue
    //
    // The AI keeps its councils full automatically. The player appoints his own council
    // and may petition his liege for a seat in theirs — a coveted post that pays a
    // stipend and influence. Councils are viewed in the Council screen (the "Darbar").
    public class CouncilBehavior : CampaignBehaviorBase
    {
        public enum Post { Vizier = 0, MirBakshi = 1, Diwan = 2 }
        private static readonly Post[] AllPosts = { Post.Vizier, Post.MirBakshi, Post.Diwan };

        public static CouncilBehavior Instance { get; private set; }

        public const int PetitionInfluenceCost = 30;

        public static string PostTitle(Post p) => p == Post.Vizier ? "Diwan-i-Ala (Vizier)"
            : p == Post.MirBakshi ? "Mir Bakshi (Paymaster)" : "Diwan-i-Kul (Treasurer)";

        public static string PostPerk(Post p) => p == Post.Vizier
            ? "Stipend of influence; the lord's right hand."
            : p == Post.MirBakshi ? "Influence and a soldier's stipend."
            : "A treasurer's stipend of gold.";

        // councilHolderHeroId -> [viziderId, bakshiId, diwanId]
        private Dictionary<string, string[]> _council = new Dictionary<string, string[]>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        }

        // ── Who keeps a council ──────────────────────────────────────────────────────
        public static bool IsCouncilHolder(Hero h)
        {
            if (h == null || !h.IsAlive || h.Clan == null) return false;
            if (h.Clan.Kingdom != null && h.Clan.Kingdom.Leader == h) return true; // sovereign
            return h.Clan.Settlements.Any(s => s.IsTown || s.IsCastle);            // landed lord
        }

        private IEnumerable<Hero> AllHolders()
        {
            var set = new HashSet<Hero>();
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated))
                if (k.Leader != null) set.Add(k.Leader);
            foreach (Clan c in Clan.All.Where(c => !c.IsEliminated && c.Leader != null))
                if (c.Settlements.Any(s => s.IsTown || s.IsCastle)) set.Add(c.Leader);
            return set;
        }

        // ── Queries ────────────────────────────────────────────────────────────────
        public Hero GetCouncillor(Hero holder, Post p)
        {
            if (holder == null) return null;
            if (!_council.TryGetValue(holder.StringId, out string[] ids) || ids == null) return null;
            string id = ids[(int)p];
            Hero h = string.IsNullOrEmpty(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == id);
            return h != null && h.IsAlive ? h : null;
        }

        // Back-compat: a kingdom's council is its sovereign's council.
        public Hero GetCouncillor(Kingdom k, Post p) => GetCouncillor(k?.Leader, p);

        // The office a hero holds, with whose council, or null.
        public string GetPostOf(Hero hero)
        {
            if (hero == null) return null;
            foreach (var kv in _council)
            {
                for (int i = 0; i < 3; i++)
                    if (kv.Value[i] == hero.StringId)
                    {
                        Hero holder = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == kv.Key);
                        string who = holder == Hero.MainHero ? "you"
                            : holder != null ? holder.Name.ToString() : "a lord";
                        return $"{PostTitle((Post)i)} to {who}";
                    }
            }
            return null;
        }

        public string DescribeCouncil(Hero holder)
        {
            var sb = new StringBuilder();
            foreach (Post p in AllPosts)
            {
                Hero h = GetCouncillor(holder, p);
                sb.AppendLine($"  {PostTitle(p)}: {(h != null ? h.Name.ToString() : "— vacant —")}");
            }
            return sb.ToString().Replace("\r\n", "\n");
        }
        public string DescribeCouncil(Kingdom k) => DescribeCouncil(k?.Leader);

        private void Set(Hero holder, Post p, Hero councillor)
        {
            if (holder == null) return;
            if (!_council.TryGetValue(holder.StringId, out string[] ids) || ids == null)
            { ids = new[] { "", "", "" }; _council[holder.StringId] = ids; }
            ids[(int)p] = councillor?.StringId ?? "";
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        private void OnNewGame(CampaignGameStarter starter) => FillAll();
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            FillAll();
            AddMenus(starter);
        }
        private void OnWeeklyTick()
        {
            FillAll();
            foreach (Hero holder in AllHolders()) ApplyCouncilEffects(holder);
        }

        private void FillAll()
        {
            foreach (Hero holder in AllHolders())
            {
                bool playerHolder = holder == Hero.MainHero;
                foreach (Post p in AllPosts)
                {
                    Hero current = GetCouncillor(holder, p);
                    if (current != null) continue;
                    if (playerHolder) continue; // the player fills his own council
                    Hero pick = ChooseBest(holder, p);
                    if (pick != null) Set(holder, p, pick);
                }
            }
        }

        // Candidates = the holder's vassals, kin and companions.
        public List<Hero> GetCandidates(Hero holder, Post p)
        {
            var set = new HashSet<Hero>();
            if (holder?.Clan == null) return new List<Hero>();

            // Kin & companions of the holder's own house.
            foreach (Hero h in holder.Clan.Heroes)
                if (h != null && h.IsAlive && !h.IsChild && h != holder) set.Add(h);

            // Vassals among the realm's clan leaders.
            Kingdom k = holder.Clan.Kingdom;
            if (k != null)
                foreach (Clan c in k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != holder))
                    if (FeudalTitlesBehavior.Instance?.GetFeudalLiege(c.Leader) == holder) set.Add(c.Leader);

            // Zamindars of the villages beneath the holder's seats.
            var ft = FeudalTitlesBehavior.Instance;
            if (ft != null)
                foreach (Settlement seat in holder.Clan.Settlements.Where(s => s.IsTown || s.IsCastle))
                    foreach (Village v in seat.Town?.Villages ?? Enumerable.Empty<Village>())
                    {
                        Hero z = ft.GetVillageLord(v.Settlement);
                        if (z != null && z != holder) set.Add(z);
                    }

            // Exclude those already seated on this holder's council.
            return set.Where(h => !HoldsOtherPost(holder, p, h)).ToList();
        }

        private Hero ChooseBest(Hero holder, Post p)
        {
            var cands = GetCandidates(holder, p);
            return cands.Count == 0 ? null : cands.OrderByDescending(h => Score(h, p, holder)).First();
        }

        private bool HoldsOtherPost(Hero holder, Post p, Hero h)
        {
            foreach (Post other in AllPosts)
                if (other != p && GetCouncillor(holder, other) == h) return true;
            return false;
        }

        private static float Score(Hero h, Post p, Hero holder)
        {
            float skill = p == Post.Vizier ? h.GetSkillValue(DefaultSkills.Steward) + h.GetSkillValue(DefaultSkills.Charm)
                : p == Post.MirBakshi ? h.GetSkillValue(DefaultSkills.Leadership) + h.GetSkillValue(DefaultSkills.Tactics)
                : h.GetSkillValue(DefaultSkills.Steward) + h.GetSkillValue(DefaultSkills.Trade);
            float rel = holder != null ? CharacterRelationManager.GetHeroRelation(h, holder) : 0;
            return skill + rel * 2f;
        }

        private void ApplyCouncilEffects(Hero holder)
        {
            bool sovereign = holder.Clan?.Kingdom != null && holder.Clan.Kingdom.Leader == holder;
            Hero vizier = GetCouncillor(holder, Post.Vizier);
            Hero bakshi = GetCouncillor(holder, Post.MirBakshi);
            Hero diwan = GetCouncillor(holder, Post.Diwan);

            if (sovereign)
            {
                Kingdom k = holder.Clan.Kingdom;
                if (vizier != null) ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 1.5f, "the Vizier's administration");
                if (bakshi != null && holder.Clan != null) ChangeClanInfluenceAction.Apply(holder.Clan, 2f);
                if (diwan != null) holder.ChangeHeroGold(400);
            }
            else
            {
                if (vizier != null && holder.Clan != null) ChangeClanInfluenceAction.Apply(holder.Clan, 1.5f);
                if (bakshi != null && holder.Clan != null) ChangeClanInfluenceAction.Apply(holder.Clan, 1f);
                if (diwan != null) holder.ChangeHeroGold(300);
            }

            // Councillors' perks.
            foreach (Post p in AllPosts)
            {
                Hero c = GetCouncillor(holder, p);
                if (c?.Clan == null) continue;
                ChangeClanInfluenceAction.Apply(c.Clan, 1f);
                if (p == Post.Diwan) c.ChangeHeroGold(200);
            }
        }

        // ── Player: appoint your own council ─────────────────────────────────────────
        public void OpenAppointDialog(Post p)
        {
            Hero holder = Hero.MainHero;
            if (!IsCouncilHolder(holder)) { Notify("You hold no town or castle, so you keep no council.", true); return; }

            var cands = GetCandidates(holder, p);
            if (cands.Count == 0) { Notify("No vassal, kinsman or companion stands ready for this office.", true); return; }

            var elements = new List<InquiryElement>();
            foreach (Hero h in cands.OrderByDescending(c => Score(c, p, holder)))
            {
                int rel = CharacterRelationManager.GetHeroRelation(holder, h);
                string hint = $"Relation {rel}. Steward {h.GetSkillValue(DefaultSkills.Steward)}, " +
                              $"Leadership {h.GetSkillValue(DefaultSkills.Leadership)}, Tactics {h.GetSkillValue(DefaultSkills.Tactics)}.";
                elements.Add(new InquiryElement(h, $"{h.Name} — rel {rel}", null, true, hint));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Appoint your {PostTitle(p)}",
                $"Name a vassal, kinsman or companion to the office of {PostTitle(p)} in your council.",
                elements, true, 1, 1, "Appoint", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Hero hero) AppointOwn(p, hero); },
                _ => { }, "", false), false, false);
        }

        private void AppointOwn(Post p, Hero hero)
        {
            Hero holder = Hero.MainHero;
            Hero previous = GetCouncillor(holder, p);
            Set(holder, p, hero);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(holder, hero, 5);
            if (previous != null && previous != hero)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(holder, previous, -3);
            RoyalFarmaan.Issue("Appointment to your Council", $"By order of {holder.Name}",
                $"{hero.Name} is raised to the office of {PostTitle(p)} in your council, to serve you with counsel and diligence.",
                "Sealed by your hand");
        }

        // ── Player: petition your liege for a seat ───────────────────────────────────
        public Hero PlayerLiege()
        {
            Hero liege = FeudalTitlesBehavior.Instance?.GetFeudalLiege(Hero.MainHero);
            return IsCouncilHolder(liege) ? liege : null;
        }

        public void OpenPetitionDialog()
        {
            Hero liege = PlayerLiege();
            if (liege == null) { Notify("You answer to no liege who keeps a council.", true); return; }

            var elements = new List<InquiryElement>();
            foreach (Post p in AllPosts)
            {
                Hero seated = GetCouncillor(liege, p);
                string status = seated == null ? "vacant" : (seated == Hero.MainHero ? "held by you" : $"held by {seated.Name}");
                string hint = $"{PostPerk(p)} Presently {status}. Costs {PetitionInfluenceCost} influence to petition.";
                bool enabled = seated != Hero.MainHero;
                elements.Add(new InquiryElement(p, $"{PostTitle(p)} — {status}", null, enabled, hint));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Petition {liege.Name} for a Council Seat",
                $"Petition your liege for an office in their council. A seat pays a stipend and lends you influence at their court. " +
                $"Each petition costs {PetitionInfluenceCost} influence.",
                elements, true, 1, 1, "Petition", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Post p) PetitionForSeat(liege, p); },
                _ => { }, "", false), false, false);
        }

        private void PetitionForSeat(Hero liege, Post p)
        {
            if (Clan.PlayerClan.Influence < PetitionInfluenceCost)
            { Notify($"You need {PetitionInfluenceCost} influence to petition.", true); return; }

            Hero seated = GetCouncillor(liege, p);
            int rel = CharacterRelationManager.GetHeroRelation(Hero.MainHero, liege);
            int myTier = FeudalTitlesBehavior.Instance?.GetTierRank(Hero.MainHero) ?? 0;
            int seatedTier = seated != null ? (FeudalTitlesBehavior.Instance?.GetTierRank(seated) ?? 0) : 0;
            int req = 15 + (int)p * 5;

            // Granted if there is a vacancy you merit, or you outrank and out-favour the
            // present holder.
            bool granted = rel >= req && (seated == null || myTier >= seatedTier);

            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, granted ? -PetitionInfluenceCost : -PetitionInfluenceCost / 2);

            if (!granted)
            {
                RoyalFarmaan.FromLiege(liege, "Petition Denied",
                    $"Your petition for the office of {PostTitle(p)} is declined. " +
                    (rel < req ? "You have not yet earned my trust for so high a charge." : "The office is held by one I will not displace.") +
                    " Serve me better, and ask again.",
                    "As you will, my lord");
                return;
            }

            if (seated != null && seated != Hero.MainHero)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(seated, Hero.MainHero, -10);
            Set(liege, p, Hero.MainHero);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, liege, 5);
            RoyalFarmaan.FromLiege(liege, "Petition Granted",
                $"I name you my {PostTitle(p)}. Serve my council faithfully, and the office's stipend and honours are yours. " +
                PostPerk(p),
                "I am honoured to serve");
        }

        // ── Royal Farmaans of state (sovereign only) ─────────────────────────────────
        private void IssueDecreeTaxRemission()
        {
            Kingdom k = Hero.MainHero.Clan.Kingdom;
            LegitimacyBehavior.Instance?.ModifyLegitimacy(k.Leader, 6f, "a remission of taxes");
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -4f, "remitting taxes");
            foreach (Clan c in k.Clans.Where(c => c.Leader != null && c.Leader != Hero.MainHero))
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Leader, 2);
            ConfirmDecree("Remission of Taxes",
                "You proclaim a remission of the year's hardest taxes. The people bless your name and the nobles are content — " +
                "but the treasury's reach into the provinces slackens.");
        }

        private void IssueDecreeLevies()
        {
            Kingdom k = Hero.MainHero.Clan.Kingdom;
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 6f, "extraordinary levies");
            Hero.MainHero.ChangeHeroGold(3000);
            foreach (Clan c in k.Clans.Where(c => c.Leader != null && c.Leader != Hero.MainHero))
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Leader, -3);
            ConfirmDecree("Demand for Extraordinary Levies",
                "You demand extraordinary levies across the realm. The treasury swells and your writ runs harder — " +
                "but the nobles grumble at the imposition.");
        }

        private void IssueDecreeFestival()
        {
            Kingdom k = Hero.MainHero.Clan.Kingdom;
            if (Hero.MainHero.Gold < 5000) { Notify("A grand Darbar befitting the throne costs 5000 dinars.", true); return; }
            Hero.MainHero.ChangeHeroGold(-5000);
            LegitimacyBehavior.Instance?.ModifyLegitimacy(k.Leader, 8f, "a grand Darbar");
            foreach (Clan c in k.Clans.Where(c => c.Leader != null && c.Leader != Hero.MainHero))
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Leader, 1);
            ConfirmDecree("A Grand Darbar",
                "You hold a grand Darbar of state — elephants, music, and largesse. The splendour of your court is spoken of " +
                "across Hindostan, and your legitimacy rises.");
        }

        private void ConfirmDecree(string title, string body)
            => RoyalFarmaan.Issue(title, $"Proclaimed by {Hero.MainHero.Name}", body, "Let it be proclaimed");

        // ── Action routed from the Council screen ────────────────────────────────────
        public void ScreenAction(Hero holder, Post post)
        {
            if (holder == null) return;
            if (holder == Hero.MainHero) OpenAppointDialog(post);
            else if (holder == PlayerLiege()) OpenPetitionDialog();
            else Notify($"You may only observe the council of {holder.Name}.", false);
        }

        // ── Menus ────────────────────────────────────────────────────────────────────
        private static bool IsPlayerRuler() => Hero.MainHero?.Clan?.Kingdom?.Leader == Hero.MainHero;

        private void AddMenus(CampaignGameStarter starter)
        {
            // View / manage the council — available to anyone in a town or castle.
            foreach (string root in new[] { "town", "castle" })
                starter.AddGameMenuOption(root, "hindostan_council_" + root, "{=!}The Council (Darbar)",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                    args => CouncilScreen.Open(), false, 5);

            // The sovereign's Darbar of state — issue Royal Farmaans.
            foreach (string root in new[] { "town", "castle" })
                starter.AddGameMenuOption(root, "hindostan_darbar_" + root, "{=!}Hold court and issue decrees",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return IsPlayerRuler(); },
                    args => GameMenu.SwitchToMenu("hindostan_darbar"), false, 6);

            starter.AddGameMenu("hindostan_darbar", "{=!}{HINDOSTAN_DARBAR_TEXT}", DarbarInit);
            starter.AddGameMenuOption("hindostan_darbar", "darbar_decree", "{=!}Issue a Royal Farmaan of state",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Mission;
                          bool ok = ImperialAuthorityBehavior.Instance?.CanIssueImperialDecree(Hero.MainHero.Clan.Kingdom) ?? true;
                          if (!ok) args.Tooltip = new TextObject("{=!}Your authority is too weak to issue decrees (needs 40).");
                          args.IsEnabled = ok; return true; },
                args => GameMenu.SwitchToMenu("hindostan_darbar_decrees"));
            starter.AddGameMenuOption("hindostan_darbar", "darbar_council", "{=!}Review and appoint your council",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage; return true; },
                args => CouncilScreen.Open());
            starter.AddGameMenuOption("hindostan_darbar", "darbar_leave", "{=!}Leave the Darbar",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu(Settlement.CurrentSettlement != null && Settlement.CurrentSettlement.IsTown ? "town" : "castle"), true);

            starter.AddGameMenu("hindostan_darbar_decrees", "{=!}{HINDOSTAN_DECREE_TEXT}", DecreeInit);
            starter.AddGameMenuOption("hindostan_darbar_decrees", "decree_remit", "{=!}Proclaim a remission of taxes  (+legitimacy, -authority)",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args => { IssueDecreeTaxRemission(); GameMenu.SwitchToMenu("hindostan_darbar"); });
            starter.AddGameMenuOption("hindostan_darbar_decrees", "decree_levy", "{=!}Demand extraordinary levies  (+authority +gold, -nobles)",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args => { IssueDecreeLevies(); GameMenu.SwitchToMenu("hindostan_darbar"); });
            starter.AddGameMenuOption("hindostan_darbar_decrees", "decree_festival", "{=!}Proclaim a grand Darbar  (5000g, +legitimacy)",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args => { IssueDecreeFestival(); GameMenu.SwitchToMenu("hindostan_darbar"); });
            starter.AddGameMenuOption("hindostan_darbar_decrees", "decree_back", "{=!}Back",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu("hindostan_darbar"), true);
        }

        private void DarbarInit(MenuCallbackArgs args)
        {
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            var sb = new StringBuilder();
            sb.AppendLine($"The Darbar of {(k != null ? k.Name.ToString() : "your realm")}");
            sb.AppendLine(" ");
            if (k != null)
            {
                float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(k) ?? 0f;
                float legit = LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 0f;
                sb.AppendLine($"Imperial authority: {auth:0}    Legitimacy: {legit:0}");
                sb.AppendLine(" ");
                sb.AppendLine("Your imperial council:");
                sb.Append(DescribeCouncil(k.Leader));
            }
            sb.AppendLine(" ");
            sb.AppendLine("How will you rule?");
            MBTextManager.SetTextVariable("HINDOSTAN_DARBAR_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }

        private void DecreeInit(MenuCallbackArgs args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("A Royal Farmaan, once sealed, reshapes the realm. Choose your decree:");
            sb.AppendLine(" ");
            sb.AppendLine($"Your gold: {Hero.MainHero.Gold}");
            MBTextManager.SetTextVariable("HINDOSTAN_DECREE_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Save / load ────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var ids = _council.Keys.ToList();
            var v = new List<string>(); var b = new List<string>(); var d = new List<string>();
            foreach (string id in ids) { var a = _council[id]; v.Add(a[0]); b.Add(a[1]); d.Add(a[2]); }

            dataStore.SyncData("hind_council_holders", ref ids);
            dataStore.SyncData("hind_council_vizier", ref v);
            dataStore.SyncData("hind_council_bakshi", ref b);
            dataStore.SyncData("hind_council_diwan", ref d);

            if (!dataStore.IsSaving)
            {
                _council = new Dictionary<string, string[]>();
                for (int i = 0; i < ids.Count; i++)
                    _council[ids[i]] = new[]
                    {
                        i < v.Count ? v[i] : "", i < b.Count ? b[i] : "", i < d.Count ? d[i] : "",
                    };
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("council", "hindostan")]
        public static string ShowCouncil(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Hero holder = Hero.MainHero;
            if (!IsCouncilHolder(holder))
            {
                Hero liege = Instance.PlayerLiege();
                if (liege == null) return "You hold no council and answer to no council-keeping liege.";
                return $"Your liege {liege.Name}'s council:\n" + Instance.DescribeCouncil(liege);
            }
            return $"Your council:\n" + Instance.DescribeCouncil(holder);
        }
    }
}
