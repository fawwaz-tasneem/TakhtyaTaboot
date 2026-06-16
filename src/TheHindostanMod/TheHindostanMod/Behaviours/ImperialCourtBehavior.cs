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
using TakhtyaTaboot.Config;
using TakhtyaTaboot.UI;

namespace TakhtyaTaboot
{
    // The seat of power and the formal council session. Every realm keeps a CAPITAL — a
    // fixed town held by its sovereign — and the imperial council is CONVENED there.
    // A sovereign is expected to convene it KingCouncilsPerYear times a year; a landed
    // lord his own council at least LordCouncilsPerYear. Neglect the cadence and the
    // realm murmurs (authority/legitimacy or influence bleed). Convening lets the holder
    // put matters to a weighted vote (war, peace, edicts), grant or revoke vassals'
    // mansabs, and appoint his offices. If the capital is taken, it moves of necessity;
    // a sovereign may also move it deliberately at great cost.
    public class ImperialCourtBehavior : CampaignBehaviorBase
    {
        public static ImperialCourtBehavior Instance { get; private set; }

        private Dictionary<string, string> _capital = new Dictionary<string, string>(); // kingdomId -> settlementId
        private int _cadenceDeadline = -1;  // day by which the player must next convene

        private enum PropType { War, Peace, Levies, Remission }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        private static Kingdom PK => Hero.MainHero?.Clan?.Kingdom;
        private static bool IsRuler => PK != null && PK.Leader == Hero.MainHero;

        // ── Capital ──────────────────────────────────────────────────────────────────
        public Settlement GetCapital(Kingdom k)
        {
            if (k == null) return null;
            if (_capital.TryGetValue(k.StringId, out string id))
            {
                Settlement s = Settlement.All.FirstOrDefault(x => x.StringId == id);
                if (s != null && s.IsTown && s.MapFaction == k) return s;
            }
            return AssignBestCapital(k);
        }

        private Settlement AssignBestCapital(Kingdom k)
        {
            if (k?.Leader == null) return null;
            Settlement best = k.Leader.Clan.Settlements.Where(s => s.IsTown)
                .OrderByDescending(s => s.Town?.Prosperity ?? 0f).FirstOrDefault();
            if (best == null)
                best = k.Settlements.Where(s => s.IsTown).OrderByDescending(s => s.Town?.Prosperity ?? 0f).FirstOrDefault();
            if (best != null) _capital[k.StringId] = best.StringId;
            return best;
        }

        private void EnsureCapitals()
        {
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null))
                GetCapital(k);
        }

        public bool IsCapital(Settlement s) => s != null && s.MapFaction is Kingdom k && GetCapital(k) == s;

        private void OnOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturer,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (s == null || !s.IsTown) return;
            // A capital that has left its realm's hands must be re-seated.
            foreach (string kid in _capital.Keys.ToList())
            {
                if (_capital[kid] != s.StringId) continue;
                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == kid);
                if (k == null || k.IsEliminated) { _capital.Remove(kid); continue; }
                if (s.MapFaction == k) continue; // still ours
                Settlement moved = AssignBestCapital(k);
                if (k == PK && moved != null)
                    RoyalFarmaan.FromRuler(k, "The Capital Is Moved of Necessity",
                        $"{s.Name} is lost, and with it the old seat of empire. The court removes to {moved.Name}, " +
                        "which is proclaimed the new capital.", "So it must be");
            }
        }

        // ── Deliberate capital move (ruler, from another of his towns) ────────────────
        private void MoveCapitalHere(Settlement s)
        {
            Kingdom k = PK;
            if (k == null || !IsRuler) { Notify("Only the sovereign may move the capital.", true); return; }
            if (s == null || !s.IsTown || s.OwnerClan != Clan.PlayerClan) { Notify("You may only seat the capital in a town you hold.", true); return; }
            if (GetCapital(k) == s) { Notify($"{s.Name} is already your capital.", true); return; }
            int cost = Tune.MoveCapitalCost;
            if (Hero.MainHero.Gold < cost) { Notify($"Moving the capital demands {cost} dinars (you have {Hero.MainHero.Gold}).", true); return; }

            Hero.MainHero.ChangeHeroGold(-cost);
            _capital[k.StringId] = s.StringId;
            // The upheaval of moving the court strains the new seat for a season.
            if (s.Town != null)
            {
                s.Town.Loyalty = MathF.Max(0f, s.Town.Loyalty - 30f);
                s.Town.Prosperity = MathF.Max(0f, s.Town.Prosperity - 1000f);
            }
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -4f, "the upheaval of moving the capital");
            RoyalFarmaan.FromRuler(k, "A New Seat of Empire",
                $"By your command the capital is removed to {s.Name}. The cost is heavy and the city is unsettled by the influx " +
                "of the court, but henceforth the Darbar sits here.", "Let it be so");
        }

        // ── Convening ────────────────────────────────────────────────────────────────
        private int ConveneIntervalDays()
        {
            int quota = IsRuler ? Tune.KingCouncilsPerYear : Tune.LordCouncilsPerYear;
            return Math.Max(30, (int)Math.Round(365f / Math.Max(1, quota)));
        }

        private void Convene()
        {
            _cadenceDeadline = (int)CampaignTime.Now.ToDays + ConveneIntervalDays();
            GameMenu.SwitchToMenu("hindostan_convene");
        }

        private bool PlayerHoldsCouncil() => CouncilBehavior.IsCouncilHolder(Hero.MainHero);

        // The player may convene the imperial council only while present in his capital.
        private bool CanConveneImperial(Settlement s)
            => IsRuler && s != null && s.IsTown && GetCapital(PK) == s;

        // A landed lord (not the sovereign) convenes his own council at a seat he holds.
        private bool CanConveneLordly(Settlement s)
            => !IsRuler && PlayerHoldsCouncil() && s != null && (s.IsTown || s.IsCastle) && s.OwnerClan == Clan.PlayerClan;

        // ── Cadence enforcement ──────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (!PlayerHoldsCouncil()) { _cadenceDeadline = -1; return; }
            int today = (int)CampaignTime.Now.ToDays;
            if (_cadenceDeadline < 0) { _cadenceDeadline = today + ConveneIntervalDays(); return; }
            if (today >= _cadenceDeadline)
            {
                ApplyCadencePenalty();
                _cadenceDeadline = today + ConveneIntervalDays();
            }
        }

        private void ApplyCadencePenalty()
        {
            Kingdom k = PK;
            if (IsRuler && k != null)
            {
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -5f, "the Darbar left unconvened");
                LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, -3f, "neglecting the council");
                RoyalFarmaan.FromRuler(k, "The Darbar Sits Empty",
                    "Too long has the imperial council gone unconvened. The nobles murmur that you neglect the rites of rule, " +
                    "and your authority and legitimacy suffer for it. Convene the Darbar at your capital.", "I shall summon them");
            }
            else if (Clan.PlayerClan != null)
            {
                ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -10f);
                Notify("Your vassals grumble that you have not held your council; your standing slips. Convene them at your seat.", true);
            }
        }

        // ── Votes ────────────────────────────────────────────────────────────────────
        private void OpenProposals()
        {
            Kingdom k = PK;
            if (k == null) { Notify("You serve no realm.", true); return; }

            var props = BuildProposals(k);
            if (props.Count == 0) { Notify("There is no great matter to put before the council just now.", false); return; }

            var elements = props.Select(p => new InquiryElement(p, p.Label, null, true, p.Hint)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "A Matter for the Council",
                "Put a single matter before the assembled lords. They will vote, and their voice carries weight.",
                elements, true, 1, 1, "Put it to the vote", "Withdraw",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Proposal pr) RunVote(k, pr); },
                _ => { }, "", false), false, false);
        }

        private class Proposal
        {
            public PropType Type; public Kingdom Target; public string Label; public string Hint; public int Bias;
        }

        private List<Proposal> BuildProposals(Kingdom k)
        {
            var list = new List<Proposal>();

            foreach (Kingdom e in Kingdom.All.Where(x => x != k && !x.IsEliminated && k.IsAtWarWith(x)).Take(2))
                list.Add(new Proposal { Type = PropType.Peace, Target = e, Bias = 10,
                    Label = $"Make peace with {e.Name}", Hint = "The council weighs an end to this war." });

            foreach (Kingdom e in Kingdom.All.Where(x => x != k && !x.IsEliminated && !k.IsAtWarWith(x)).Take(2))
                list.Add(new Proposal { Type = PropType.War, Target = e, Bias = -12,
                    Label = $"Declare war on {e.Name}", Hint = "The council weighs war. Many lords are wary of it." });

            list.Add(new Proposal { Type = PropType.Levies, Bias = -5,
                Label = "Decree extraordinary levies (+authority, +treasury)", Hint = "A heavy demand the lords resent." });
            list.Add(new Proposal { Type = PropType.Remission, Bias = 8,
                Label = "Decree a remission of taxes (+legitimacy, -authority)", Hint = "A popular measure the lords favour." });

            return list;
        }

        private void RunVote(Kingdom k, Proposal pr)
        {
            int yes = 2, no = 0; // the holder's own bloc
            foreach (Clan c in k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != Hero.MainHero))
            {
                int w = (int)c.Tier + 1;
                int rel = CharacterRelationManager.GetHeroRelation(Hero.MainHero, c.Leader);
                int score = pr.Bias + rel + MBRandom.RandomInt(-25, 25);
                if (score >= 0) yes += w; else no += w;
            }
            bool passed = yes > no;

            string outcome = passed ? "carries" : "is voted down";
            var sb = new StringBuilder();
            sb.AppendLine($"The council {outcome}: \"{pr.Label}\".");
            sb.AppendLine($"Ayes {yes}, Noes {no}.");

            if (passed) sb.AppendLine(Enact(k, pr));
            else if (IsRuler) ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -2f, "a measure voted down");

            RoyalFarmaan.Issue("The Council Has Spoken", $"In the Darbar of {k.Name}", sb.ToString().Replace("\r\n", "\n"),
                "So the council wills");
        }

        private string Enact(Kingdom k, Proposal pr)
        {
            try
            {
                switch (pr.Type)
                {
                    case PropType.Peace:
                        if (pr.Target != null && k.IsAtWarWith(pr.Target)) MakePeaceAction.Apply(k, pr.Target);
                        return $"Peace is made with {pr.Target?.Name}.";
                    case PropType.War:
                        if (pr.Target != null && !k.IsAtWarWith(pr.Target)) DeclareWarAction.ApplyByDefault(k, pr.Target);
                        return $"War is declared upon {pr.Target?.Name}.";
                    case PropType.Levies:
                        ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, 6f, "extraordinary levies");
                        Hero.MainHero.ChangeHeroGold(3000);
                        return "The levies are decreed; the treasury swells and your writ runs harder.";
                    case PropType.Remission:
                        LegitimacyBehavior.Instance?.ModifyLegitimacy(k.Leader, 6f, "a remission of taxes");
                        ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -4f, "remitting taxes");
                        return "The remission is proclaimed; the people bless your name.";
                }
            }
            catch { return "The measure could not be carried into effect."; }
            return "";
        }

        // A landed lord's council yields counsel rather than affairs of state: a modest
        // gain in standing and goodwill among his own people.
        private void HoldLordlyCounsel()
        {
            if (Clan.PlayerClan != null) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, 5f);
            var cb = CouncilBehavior.Instance;
            if (cb != null)
                foreach (CouncilBehavior.Post p in CouncilBehavior.AllPosts)
                {
                    Hero c = cb.GetCouncillor(Hero.MainHero, p);
                    if (c != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c, 1);
                }
            Notify("You take counsel with your vassals and councillors. Your standing among them grows.", false);
        }

        // ── Grant / revoke a vassal's mansab (ruler) ─────────────────────────────────
        private void OpenGrantMansab(bool grant)
        {
            Kingdom k = PK;
            if (k == null || !IsRuler) { Notify("Only the sovereign grants and revokes mansabs.", true); return; }
            var m = MansabdariBehavior.Instance;
            if (m == null) return;

            var clans = k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != Hero.MainHero).ToList();
            if (clans.Count == 0) { Notify("You have no vassals to honour.", false); return; }

            var elements = clans.Select(c => new InquiryElement(c,
                $"{c.Leader.Name} — {m.GetTitle(c)}",
                null, true, $"Relation {CharacterRelationManager.GetHeroRelation(Hero.MainHero, c.Leader)}. " +
                            (grant ? "Raise him one mansab." : "Reduce him one mansab."))).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                grant ? "Grant a Mansab" : "Revoke a Mansab",
                grant ? "Raise a vassal one step in the mansabdari." : "Reduce a vassal one step in the mansabdari.",
                elements, true, 1, 1, grant ? "Grant" : "Revoke", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Clan c) DoGrant(k, c, grant); },
                _ => { }, "", false), false, false);
        }

        private void DoGrant(Kingdom k, Clan vassal, bool grant)
        {
            string title = MansabdariBehavior.Instance?.AdjustRank(vassal, grant ? +1 : -1);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, vassal.Leader, grant ? 6 : -8);
            RoyalFarmaan.FromRuler(k, grant ? "A Mansab Bestowed" : "A Mansab Withdrawn",
                grant
                    ? $"By favour of the throne, {vassal.Leader.Name} is raised to the mansab of {title}."
                    : $"By judgement of the throne, {vassal.Leader.Name} is reduced to the mansab of {title}.",
                "The court's will be done");
        }

        // ── Menus ────────────────────────────────────────────────────────────────────
        private void OnNewGame(CampaignGameStarter starter) => EnsureCapitals();

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureCapitals();
            AddMenus(starter);
        }

        private void AddMenus(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town", "hindostan_convene_imperial", "{=!}Convene the imperial council (Darbar)",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return CanConveneImperial(Settlement.CurrentSettlement); },
                args => Convene(), false, 9);

            foreach (string root in new[] { "town", "castle" })
                starter.AddGameMenuOption(root, "hindostan_convene_lordly_" + root, "{=!}Convene your council",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return CanConveneLordly(Settlement.CurrentSettlement); },
                    args => Convene(), false, 9);

            starter.AddGameMenuOption("town", "hindostan_move_capital", "{=!}Seat the imperial capital here",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage;
                          Settlement s = Settlement.CurrentSettlement;
                          if (!IsRuler || s == null || !s.IsTown || s.OwnerClan != Clan.PlayerClan || GetCapital(PK) == s) return false;
                          args.Tooltip = new TextObject($"{{=!}}Move the capital here for {Tune.MoveCapitalCost} dinars.");
                          return true; },
                args => MoveCapitalHere(Settlement.CurrentSettlement), false, 10);

            // The council session itself.
            starter.AddGameMenu("hindostan_convene", "{=!}{HINDOSTAN_CONVENE_TEXT}", ConveneInit);

            starter.AddGameMenuOption("hindostan_convene", "convene_vote", "{=!}Put a matter to the council (vote)",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Mission; return IsRuler; },
                args => { OpenProposals(); });

            starter.AddGameMenuOption("hindostan_convene", "convene_counsel", "{=!}Take counsel with your vassals",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Wait; return !IsRuler; },
                args => { HoldLordlyCounsel(); GameMenu.SwitchToMenu("hindostan_convene"); });

            starter.AddGameMenuOption("hindostan_convene", "convene_grant", "{=!}Grant a vassal a mansab",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage; return IsRuler; },
                args => { OpenGrantMansab(true); });

            starter.AddGameMenuOption("hindostan_convene", "convene_revoke", "{=!}Revoke a vassal's mansab",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Bribe; return IsRuler; },
                args => { OpenGrantMansab(false); });

            starter.AddGameMenuOption("hindostan_convene", "convene_council", "{=!}Appoint your council offices",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage; return true; },
                args => CouncilScreen.Open());

            starter.AddGameMenuOption("hindostan_convene", "convene_leave", "{=!}Conclude the council",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu(Settlement.CurrentSettlement != null && Settlement.CurrentSettlement.IsTown ? "town" : "castle"), true);
        }

        private void ConveneInit(MenuCallbackArgs args)
        {
            Kingdom k = PK;
            var sb = new StringBuilder();
            sb.AppendLine(IsRuler ? $"The Imperial Darbar of {k?.Name}" : "Your Council");
            sb.AppendLine(" ");
            if (k != null && IsRuler)
            {
                Settlement cap = GetCapital(k);
                float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(k) ?? 0f;
                float legit = LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 0f;
                sb.AppendLine($"Capital: {(cap != null ? cap.Name.ToString() : "—")}    Authority {auth:0}    Legitimacy {legit:0}");
            }
            sb.AppendLine($"Next council expected within {ConveneIntervalDays()} days of the last.");
            sb.AppendLine(" ");
            sb.AppendLine("The lords are assembled. How will you proceed?");
            MBTextManager.SetTextVariable("HINDOSTAN_CONVENE_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Save / load ──────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var ids = _capital.Keys.ToList();
            var vals = _capital.Values.ToList();
            dataStore.SyncData("hind_capital_kingdoms", ref ids);
            dataStore.SyncData("hind_capital_settlements", ref vals);
            dataStore.SyncData("hind_court_cadenceDeadline", ref _cadenceDeadline);
            if (!dataStore.IsSaving)
            {
                _capital = new Dictionary<string, string>();
                for (int i = 0; i < ids.Count && i < vals.Count; i++) _capital[ids[i]] = vals[i];
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("capital", "hindostan")]
        public static string CapitalCmd(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            var sb = new StringBuilder();
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null).Take(12))
            {
                Settlement cap = Instance.GetCapital(k);
                sb.AppendLine($"{k.Name}: {(cap != null ? cap.Name.ToString() : "—")}");
            }
            return sb.ToString();
        }
    }
}
