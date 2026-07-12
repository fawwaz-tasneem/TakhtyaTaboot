using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.Util;
using static TakhtyaTaboot.Util.DarbarCourtMath;

namespace TakhtyaTaboot
{
    // The darbar petition court, held as DIALOGUE (playtest round 4: the inquiry screen felt
    // shallow — "the plaintiff should speak, the defendant should speak, my advisor should
    // speak"). From the sovereign's Darbar menu the sitting now runs as a chain of real
    // conversations before the throne:
    //   1. The PLAINTIFF states his case in his own words (and can be pressed for proof).
    //   2. The DEFENDANT stands forward and answers (and can be made to swear to it).
    //   3. The court ADVISOR (the wazir, or failing him the diwan) leans close with counsel —
    //      political counsel, weighted by whom HE favours, which the throne may freely defy.
    //   4. The sovereign renders judgment as a spoken line; the effects are the tested
    //      DarbarCourtMath (CourtRuling opinion records, influence, legitimacy) as before.
    // Cases are drawn from the LIVE realm: a boundary dispute between village zamindars, a
    // raided village's plea for justice, a market quarrel between notables.
    //
    // Engine shape: conversations are chained through a tick pump (opening a new map
    // conversation from inside a closing one's consequence is a re-entrancy risk — same
    // pattern as RoyalFarmaan.Pump). The sitting state is deliberately NOT serialized: a
    // conversation cannot span a save, so an interrupted sitting is simply abandoned.
    public class DarbarPetitionBehavior : CampaignBehaviorBase
    {
        public static DarbarPetitionBehavior Instance { get; private set; }

        private const int CooldownDays = 3; // the docket refills between sittings, not a grind
        private int _lastPetitionDay = -100;

        // ── The sitting (transient) ──────────────────────────────────────────────────
        private enum Stage { None, OpenPlaintiff, Plaintiff, OpenDefendant, Defendant, OpenAdvisor, Advisor }
        private Stage _stage = Stage.None;

        // Whether a court sitting is live — other conversation-openers (the qasid's audiences)
        // must wait for the court to rise rather than interleave its chain.
        public bool IsSitting => _stage != Stage.None;
        private Case _case;

        private class Case
        {
            public bool IsPlea;                  // one-party plea vs two-party dispute
            public Hero Plaintiff, Defendant, Advisor;
            public string Title;
            public string PlaintiffSay, PlaintiffProof;   // his case; his answer when pressed
            public string DefendantSay, DefendantSwear;   // his answer; his oath when pressed
            public CourtStance AdvisorLeaning;            // what the advisor would rule
            public string ForPlaintiffLine, ForDefendantLine, CompromiseLine, DismissLine; // the player's spoken judgments
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, _ => PumpConversations());
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnConversationEnded);
        }

        public override void SyncData(IDataStore dataStore)
            => dataStore.SyncData("hind_darbar_lastPetition", ref _lastPetitionDay);

        private static bool IsPlayerRuler()
            => Hero.MainHero?.Clan?.Kingdom != null && Hero.MainHero.Clan.Kingdom.Leader == Hero.MainHero;

        // ── Menu entry (the sovereign's Darbar) ──────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("hindostan_darbar", "darbar_petitions",
                "{=!}Hear a petition and render judgment",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Mission;
                    if (!IsPlayerRuler()) return false;
                    int since = (int)CampaignTime.Now.ToDays - _lastPetitionDay;
                    if (since < CooldownDays)
                    { args.IsEnabled = false; args.Tooltip = new TextObject($"{{=!}}The docket is thin; petitioners will gather again in {CooldownDays - since} day(s)."); }
                    return true;
                },
                args => TYTLog.Guard("Darbar.Petition", HoldPetitionSitting));

            RegisterCourtDialogs(starter);
        }

        public void HoldPetitionSitting()
        {
            if (!IsPlayerRuler()) { Notify("Only a sovereign holds the darbar.", true); return; }
            if (_stage != Stage.None) return; // a sitting is already in motion
            Case c = BuildCase(Hero.MainHero.Clan.Kingdom);
            if (c == null) { Notify("No petitioner brings a case worth the crown's time today.", true); return; }
            _lastPetitionDay = (int)CampaignTime.Now.ToDays;
            _case = c;
            _stage = Stage.OpenPlaintiff; // the tick pump ushers him before the throne
        }

        // ── The conversation chain (tick pump + abandonment) ─────────────────────────
        private void PumpConversations()
        {
            if (_stage != Stage.OpenPlaintiff && _stage != Stage.OpenDefendant && _stage != Stage.OpenAdvisor) return;
            try
            {
                if (Campaign.Current?.ConversationManager?.IsConversationInProgress == true) return; // last one still tearing down

                Hero next = _stage == Stage.OpenPlaintiff ? _case?.Plaintiff
                          : _stage == Stage.OpenDefendant ? _case?.Defendant
                          : _case?.Advisor;
                if (next == null || !next.IsAlive || next.CharacterObject == null)
                {
                    // The party cannot stand before the throne after all — the sitting collapses.
                    TYTLog.Warn("Darbar: a party to the case is gone; the sitting is adjourned.");
                    _stage = Stage.None; _case = null;
                    return;
                }

                _stage = _stage == Stage.OpenPlaintiff ? Stage.Plaintiff
                       : _stage == Stage.OpenDefendant ? Stage.Defendant
                       : Stage.Advisor;
                CampaignMapConversation.OpenConversation(
                    new ConversationCharacterData(CharacterObject.PlayerCharacter),
                    new ConversationCharacterData(next.CharacterObject, null, noHorse: true, noWeapon: true, noBodyguards: true));
            }
            catch (Exception e)
            {
                TYTLog.Error("Darbar: could not usher the next party before the throne", e);
                _stage = Stage.None; _case = null;
            }
        }

        // ESC / an unexpected close mid-testimony abandons the sitting; a close we caused
        // ourselves (stage already moved to Open*) lets the chain continue.
        private void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            if (_stage == Stage.Plaintiff || _stage == Stage.Defendant || _stage == Stage.Advisor)
            {
                _stage = Stage.None; _case = null;
                Notify("The sitting is adjourned without judgment.", true);
            }
        }

        // Is the current conversation partner the case-party this stage expects?
        private bool Standing(Stage stage, Hero who)
            => _stage == stage && _case != null && who != null && Hero.OneToOneConversationHero == who;

        // ── The court dialogs ────────────────────────────────────────────────────────
        private void RegisterCourtDialogs(CampaignGameStarter starter)
        {
            // ═ Act I — the plaintiff states his case ═
            starter.AddDialogLine("hind_court_pl_say", "start", "hind_court_pl_opts", "{=!}{HIND_COURT_LINE}",
                () =>
                {
                    if (!Standing(Stage.Plaintiff, _case?.Plaintiff)) return false;
                    MBTextManager.SetTextVariable("HIND_COURT_LINE", _case.PlaintiffSay, false);
                    return true;
                }, null, 200);

            starter.AddPlayerLine("hind_court_pl_press", "hind_court_pl_opts", "hind_court_pl_proof",
                "{=!}Speak plainly — what proof do you bring before the throne?",
                () => Standing(Stage.Plaintiff, _case?.Plaintiff) && !string.IsNullOrEmpty(_case.PlaintiffProof), null, 200);

            starter.AddDialogLine("hind_court_pl_proof", "hind_court_pl_proof", "hind_court_pl_opts", "{=!}{HIND_COURT_LINE}",
                () =>
                {
                    if (!Standing(Stage.Plaintiff, _case?.Plaintiff)) return false;
                    MBTextManager.SetTextVariable("HIND_COURT_LINE", _case.PlaintiffProof, false);
                    _case.PlaintiffProof = null; // pressed once; asking again would loop the same words
                    return true;
                }, null, 200);

            // Dispute: call the accused. Plea: go straight to counsel/judgment.
            starter.AddPlayerLine("hind_court_pl_next", "hind_court_pl_opts", "close_window",
                "{=!}Enough. Let the accused stand forward and answer.",
                () => Standing(Stage.Plaintiff, _case?.Plaintiff) && !_case.IsPlea,
                () => _stage = Stage.OpenDefendant, 200);

            starter.AddPlayerLine("hind_court_pl_counsel", "hind_court_pl_opts", "close_window",
                "{=!}The throne will take counsel. Stand aside.",
                () => Standing(Stage.Plaintiff, _case?.Plaintiff) && _case.IsPlea && _case.Advisor != null,
                () => _stage = Stage.OpenAdvisor, 200);

            // Plea with no advisor seated: judge from the plaintiff's own hearing.
            AddJudgmentLines(starter, "hind_court_pl_opts", pleaOnly: true, requireNoAdvisor: true);

            starter.AddPlayerLine("hind_court_pl_adjourn", "hind_court_pl_opts", "close_window",
                "{=!}The darbar will consider. (adjourn the sitting)",
                () => Standing(Stage.Plaintiff, _case?.Plaintiff),
                () => { _stage = Stage.None; _case = null; }, 199);

            // ═ Act II — the defendant answers ═
            starter.AddDialogLine("hind_court_def_say", "start", "hind_court_def_opts", "{=!}{HIND_COURT_LINE}",
                () =>
                {
                    if (!Standing(Stage.Defendant, _case?.Defendant)) return false;
                    MBTextManager.SetTextVariable("HIND_COURT_LINE", _case.DefendantSay, false);
                    return true;
                }, null, 200);

            starter.AddPlayerLine("hind_court_def_press", "hind_court_def_opts", "hind_court_def_swear",
                "{=!}And you will swear to this, before the throne and before God?",
                () => Standing(Stage.Defendant, _case?.Defendant) && !string.IsNullOrEmpty(_case.DefendantSwear), null, 200);

            starter.AddDialogLine("hind_court_def_swear", "hind_court_def_swear", "hind_court_def_opts", "{=!}{HIND_COURT_LINE}",
                () =>
                {
                    if (!Standing(Stage.Defendant, _case?.Defendant)) return false;
                    MBTextManager.SetTextVariable("HIND_COURT_LINE", _case.DefendantSwear, false);
                    _case.DefendantSwear = null;
                    return true;
                }, null, 200);

            starter.AddPlayerLine("hind_court_def_counsel", "hind_court_def_opts", "close_window",
                "{=!}The throne will take counsel before it speaks. Stand aside.",
                () => Standing(Stage.Defendant, _case?.Defendant) && _case.Advisor != null,
                () => _stage = Stage.OpenAdvisor, 200);

            // Judge directly from the defendant's hearing (with or without an advisor seated).
            AddJudgmentLines(starter, "hind_court_def_opts", pleaOnly: false, requireNoAdvisor: false);

            starter.AddPlayerLine("hind_court_def_adjourn", "hind_court_def_opts", "close_window",
                "{=!}The darbar will consider. (adjourn the sitting)",
                () => Standing(Stage.Defendant, _case?.Defendant),
                () => { _stage = Stage.None; _case = null; }, 198);

            // ═ Act III — the advisor's counsel, and judgment ═
            starter.AddDialogLine("hind_court_adv_say", "start", "hind_court_adv_opts", "{=!}{HIND_COURT_LINE}",
                () =>
                {
                    if (!Standing(Stage.Advisor, _case?.Advisor)) return false;
                    MBTextManager.SetTextVariable("HIND_COURT_LINE", AdvisorCounsel(_case), false);
                    return true;
                }, null, 200);

            AddJudgmentLines(starter, "hind_court_adv_opts", pleaOnly: false, requireNoAdvisor: false, advisorStage: true);

            starter.AddPlayerLine("hind_court_adv_adjourn", "hind_court_adv_opts", "close_window",
                "{=!}The darbar will consider. (adjourn the sitting)",
                () => Standing(Stage.Advisor, _case?.Advisor),
                () => { _stage = Stage.None; _case = null; }, 198);
        }

        // The sovereign's spoken judgments, attachable to any hearing state. Each closes the
        // conversation and applies the tested outcome.
        private void AddJudgmentLines(CampaignGameStarter starter, string fromToken, bool pleaOnly, bool requireNoAdvisor, bool advisorStage = false)
        {
            Func<bool> here = () =>
            {
                if (_case == null) return false;
                bool standing = advisorStage ? Standing(Stage.Advisor, _case.Advisor)
                    : fromToken.StartsWith("hind_court_pl") ? Standing(Stage.Plaintiff, _case.Plaintiff)
                    : Standing(Stage.Defendant, _case.Defendant);
                if (!standing) return false;
                if (pleaOnly && !_case.IsPlea) return false;
                if (requireNoAdvisor && _case.Advisor != null) return false;
                return true;
            };
            string suffix = fromToken.Replace("hind_court_", "");

            starter.AddPlayerLine("hind_court_rule_pl_" + suffix, fromToken, "close_window", "{=!}{HIND_RULE_PL}",
                () =>
                {
                    if (!here()) return false;
                    MBTextManager.SetTextVariable("HIND_RULE_PL", _case.ForPlaintiffLine, false);
                    return true;
                },
                () => TYTLog.Guard("Darbar.Rule", () => Rule(CourtStance.ForPlaintiff)), 197);

            starter.AddPlayerLine("hind_court_rule_def_" + suffix, fromToken, "close_window", "{=!}{HIND_RULE_DEF}",
                () =>
                {
                    if (!here() || _case.IsPlea) return false;
                    MBTextManager.SetTextVariable("HIND_RULE_DEF", _case.ForDefendantLine, false);
                    return true;
                },
                () => TYTLog.Guard("Darbar.Rule", () => Rule(CourtStance.ForDefendant)), 197);

            starter.AddPlayerLine("hind_court_rule_mid_" + suffix, fromToken, "close_window", "{=!}{HIND_RULE_MID}",
                () =>
                {
                    if (!here()) return false;
                    MBTextManager.SetTextVariable("HIND_RULE_MID", _case.CompromiseLine, false);
                    return true;
                },
                () => TYTLog.Guard("Darbar.Rule", () => Rule(CourtStance.Compromise)), 197);

            starter.AddPlayerLine("hind_court_rule_dis_" + suffix, fromToken, "close_window", "{=!}{HIND_RULE_DIS}",
                () =>
                {
                    if (!here()) return false;
                    MBTextManager.SetTextVariable("HIND_RULE_DIS", _case.DismissLine, false);
                    return true;
                },
                () => TYTLog.Guard("Darbar.Rule", () => Rule(CourtStance.Dismiss)), 197);
        }

        // The advisor leans the way his own friendships lean — political counsel, not justice.
        private string AdvisorCounsel(Case c)
        {
            if (c.IsPlea)
                return c.AdvisorLeaning == CourtStance.ForPlaintiff
                    ? "Huzoor, grain is cheaper than rebellion. Grant the relief — a district that blesses the throne pays its taxes; one that buries its children does not."
                    : "Huzoor, the crown cannot feed every village the rains fail. Refer it to the local lord whose duty it is — the throne's mercy must not become the throne's habit.";
            string rec = c.AdvisorLeaning == CourtStance.ForPlaintiff ? c.Plaintiff?.Name?.ToString() : c.Defendant?.Name?.ToString();
            string other = c.AdvisorLeaning == CourtStance.ForPlaintiff ? c.Defendant?.Name?.ToString() : c.Plaintiff?.Name?.ToString();
            return c.AdvisorLeaning == CourtStance.Compromise
                ? "Huzoor, neither man's cause is clean and neither man is worth an enemy. Split the matter down the middle and let them both grumble in equal measure."
                : $"Huzoor, if you would have my counsel plainly: find for {rec}. His cause is the better witnessed — and {other} has fewer friends the throne need fear. But the seal is yours, not mine.";
        }

        // ── Building a grounded case from the live realm ─────────────────────────────
        private Case BuildCase(Kingdom k)
        {
            if (k == null) return null;
            var ft = FeudalTitlesBehavior.Instance;

            Hero advisor = CouncilBehavior.Instance?.GetCouncillor(k, CouncilBehavior.Post.PrimeMinister)
                        ?? CouncilBehavior.Instance?.GetCouncillor(k, CouncilBehavior.Post.Treasurer);
            if (advisor != null && (!advisor.IsAlive || advisor == Hero.MainHero)) advisor = null;

            var zamindars = ft == null ? new List<Hero>() :
                Settlement.All.Where(s => s.IsVillage && s.MapFaction == k)
                    .Select(s => ft.GetVillageLord(s))
                    .Where(h => h != null && h.IsAlive && h != Hero.MainHero && h.Clan != Clan.PlayerClan)
                    .Distinct().ToList();

            Settlement raided = Settlement.All
                .Where(s => s.IsVillage && s.MapFaction == k && (VillageDevelopmentBehavior.Instance?.GetThreat(s) ?? 0f) >= 40f)
                .OrderByDescending(s => VillageDevelopmentBehavior.Instance?.GetThreat(s) ?? 0f)
                .FirstOrDefault();

            var notables = Settlement.All.Where(s => s.MapFaction == k && (s.IsTown || s.IsVillage))
                .SelectMany(s => s.Notables ?? Enumerable.Empty<Hero>())
                .Where(h => h != null && h.IsAlive && h != Hero.MainHero)
                .Distinct().ToList();

            // 1) Two zamindars disputing a boundary.
            if (zamindars.Count >= 2)
            {
                Hero a = zamindars[MBRandom.RandomInt(zamindars.Count)];
                Hero b = zamindars.First(x => x != a);
                return new Case
                {
                    Plaintiff = a, Defendant = b, Advisor = advisor,
                    AdvisorLeaning = Lean(advisor, a, b),
                    Title = "A Dispute of Boundaries",
                    PlaintiffSay = $"Huzoor, I come for justice. {b.Name} has moved the boundary stones in the night, and his men plough land my fathers tilled before his were born. I ask only for what is mine.",
                    PlaintiffProof = "The old men of the village will swear to where the stones truly stood, huzoor — and the qazi's register bears my grandfather's seal upon that land.",
                    DefendantSay = $"The stones stand where they have always stood, huzoor. {a.Name}'s claim is a story that grows with each telling. The land is mine by use and by right, and I have fed my people from it in good years and bad.",
                    DefendantSwear = "On my head and my eyes, huzoor. Bring the registers — I do not fear them.",
                    ForPlaintiffLine = $"The throne has heard. The stones return to their old places — the land is {a.Name}'s. Let it be entered in the registers.",
                    ForDefendantLine = $"The throne has heard. Use and possession weigh heavier than old seals — the land stays with {b.Name}. Let it be entered.",
                    CompromiseLine = "The throne has heard. The disputed land is split between you, and the boundary drawn anew by the qazi. Let neither man test it again.",
                    DismissLine = "The darbar has greater cares than two men's furrows. Away with this — settle it between yourselves.",
                };
            }

            // 2) A raided village pleads for justice.
            if (raided != null)
            {
                Hero pleader = (raided.Notables ?? Enumerable.Empty<Hero>()).FirstOrDefault(h => h != null && h.IsAlive)
                               ?? ft?.GetVillageLord(raided);
                if (pleader != null && pleader != Hero.MainHero)
                    return new Case
                    {
                        IsPlea = true, Plaintiff = pleader, Advisor = advisor,
                        AdvisorLeaning = advisor != null && CharacterRelationManager.GetHeroRelation(advisor, pleader) >= 0
                            ? CourtStance.ForPlaintiff : CourtStance.Compromise,
                        Title = "A Plea for Justice",
                        PlaintiffSay = $"Huzoor, I come from {raided.Name}, and I come with ashes in my hands. Bandits have burned our ricks and driven off the cattle, and the lord's men never came. We are your children — give us justice, or by winter we bury ours.",
                        PlaintiffProof = $"Come and see the ash where the granary stood, huzoor. The whole of {raided.Name} will speak with one voice — those the road has not already taken.",
                        ForPlaintiffLine = "The throne has heard, and the throne will act. The crown takes this district under its own hand until order is restored. Go and tell them.",
                        CompromiseLine = "The throne has heard. Your own lord will answer for his district — the court will see that he does. Go to him with the crown's word behind you.",
                        DismissLine = "The crown cannot chase every dacoit from every hill. Look to your own lord and your own walls. Away.",
                    };
            }

            // 3) Two notables quarrelling over a bargain.
            if (notables.Count >= 2)
            {
                Hero a = notables[MBRandom.RandomInt(notables.Count)];
                Hero b = notables.First(x => x != a);
                return new Case
                {
                    Plaintiff = a, Defendant = b, Advisor = advisor,
                    AdvisorLeaning = Lean(advisor, a, b),
                    Title = "A Quarrel of the Markets",
                    PlaintiffSay = $"Huzoor, {b.Name} took my grain at the rains' price and paid me at the harvest's — a cheat dressed as a bargain. I ask restitution, and I ask it before the throne because the bazaar dares not give it.",
                    PlaintiffProof = "My ledger, huzoor, entry by entry — and the broker who witnessed the weighing will speak, if he is not too afraid of the defendant's friends.",
                    DefendantSay = $"A bargain is a bargain, huzoor. {a.Name} was glad enough of my coin when the rains failed and no other hand was open. Now the harvest is in, he cries cheat. The court should not indulge a seller's regret.",
                    DefendantSwear = "I swear it, huzoor — the coin was counted out in the sight of three men, and not one of them will say otherwise.",
                    ForPlaintiffLine = $"The throne has heard. The bargain was made under the knife of hunger and the court will not bless it. {b.Name} pays the difference. Entered.",
                    ForDefendantLine = $"The throne has heard. A bargain freely struck is a bargain kept — the court finds for {b.Name}. Let the bazaar take note.",
                    CompromiseLine = "The throne has heard. The difference is split between you, and both of you will drink tea together in the sight of the bazaar. Entered.",
                    DismissLine = "The darbar is not a grain exchange. Away with this squabble — the court's time is not for sale at any price.",
                };
            }

            return null;
        }

        // Whom would the advisor favour? The friend — or the middle way when he loves and
        // fears them equally.
        private static CourtStance Lean(Hero advisor, Hero plaintiff, Hero defendant)
        {
            if (advisor == null) return CourtStance.Compromise;
            int rp = CharacterRelationManager.GetHeroRelation(advisor, plaintiff);
            int rd = CharacterRelationManager.GetHeroRelation(advisor, defendant);
            return rp > rd ? CourtStance.ForPlaintiff : rd > rp ? CourtStance.ForDefendant : CourtStance.Compromise;
        }

        // ── Rendering judgment (the tested outcome, as before) ──────────────────────
        private void Rule(CourtStance stance)
        {
            Case c = _case;
            _stage = Stage.None; _case = null;
            if (c == null) return;

            CourtOutcome o = c.IsPlea ? JudgePlea(stance) : Judge(stance);
            var op = OpinionBehavior.Instance;

            if (c.Plaintiff != null && o.PlaintiffOpinion != 0f)
                op?.AddOpinion(c.Plaintiff, Hero.MainHero, OpinionMath.OpinionType.CourtRuling, o.PlaintiffOpinion);
            if (!c.IsPlea && c.Defendant != null && o.DefendantOpinion != 0f)
                op?.AddOpinion(c.Defendant, Hero.MainHero, OpinionMath.OpinionType.CourtRuling, o.DefendantOpinion);

            if (o.Influence != 0) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, o.Influence);
            if (o.Legitimacy != 0f) LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, o.Legitimacy, "a judgment at the darbar");

            if (c.Plaintiff != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Plaintiff, Sign(o.PlaintiffOpinion), false);
            if (!c.IsPlea && c.Defendant != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Defendant, Sign(o.DefendantOpinion), false);

            string infl = o.Influence != 0 ? $"influence {o.Influence:+0;-0}" : "";
            string legit = o.Legitimacy != 0f ? $"legitimacy {o.Legitimacy:+0.0;-0.0}" : "";
            string both = string.Join(", ", new[] { infl, legit }.Where(x => x != ""));
            Notify($"Judgment is rendered in '{c.Title}'{(both == "" ? "" : " (" + both + ")")}. The registers remember.", o.Legitimacy < 0f);
            TYTLog.Info($"Darbar: '{c.Title}' ruled {stance} (plaint {o.PlaintiffOpinion:+0;-0}, def {o.DefendantOpinion:+0;-0}, inf {o.Influence:+0;-0}, legit {o.Legitimacy:+0.0;-0.0}).");
        }

        private static int Sign(float v) => v > 0f ? 2 : v < 0f ? -2 : 0;

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("darbar_petition", "hindostan")]
        public static string DarbarPetition(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (!IsPlayerRuler()) return "You must be a sovereign to hold the darbar.";
            Instance._lastPetitionDay = -100; // bypass the cooldown for testing
            Instance.HoldPetitionSitting();
            return Instance._stage != Stage.None
                ? "The petitioner is ushered before the throne (conversation opens)."
                : "No grounded case available (need zamindars, a raided village, or notables in your realm).";
        }
    }
}
