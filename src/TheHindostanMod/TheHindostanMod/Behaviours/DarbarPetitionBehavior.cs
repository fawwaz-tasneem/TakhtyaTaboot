using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.Util;
using static TakhtyaTaboot.Util.DarbarCourtMath;

namespace TakhtyaTaboot
{
    // The darbar petition court (roadmap B.1) — the sitting where the sovereign hears grounded
    // cases and renders judgment. Unlike the decree levers (which the darbar already had), these
    // petitions are drawn from the LIVE realm: a boundary dispute between two village zamindars, a
    // raided village pleading for justice, a quarrel between two notables. The judgment writes a
    // signed CourtRuling opinion record into each party's regard for the sovereign (the hook the
    // opinion ledger defined but nothing yet used), and moves the crown's influence and legitimacy.
    // Effects are the tested DarbarCourtMath; this class gathers live parties and applies them.
    public class DarbarPetitionBehavior : CampaignBehaviorBase
    {
        public static DarbarPetitionBehavior Instance { get; private set; }

        private const int CooldownDays = 3; // the docket refills between sittings, not a grind
        private int _lastPetitionDay = -100;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        private static bool IsPlayerRuler()
            => Hero.MainHero?.Clan?.Kingdom != null && Hero.MainHero.Clan.Kingdom.Leader == Hero.MainHero;

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Added to the sovereign's Darbar (created by CouncilBehavior — registered before this).
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
        }

        // ── Case model ───────────────────────────────────────────────────────────────
        private class Case
        {
            public bool IsPlea;         // one-party plea (no defendant) vs two-party dispute
            public Hero Plaintiff, Defendant;
            public string Title, Body;
            public string ForPlaintiff, ForDefendant, Middle, Dismiss; // option labels
        }

        public void HoldPetitionSitting()
        {
            if (!IsPlayerRuler()) { Notify("Only a sovereign holds the darbar.", true); return; }
            Case c = BuildCase(Hero.MainHero.Clan.Kingdom);
            if (c == null) { Notify("No petitioner brings a case worth the crown's time today.", true); return; }
            _lastPetitionDay = (int)CampaignTime.Now.ToDays;

            var elements = new List<InquiryElement>();
            if (c.IsPlea)
            {
                elements.Add(new InquiryElement(CourtStance.ForPlaintiff, c.ForPlaintiff, null, true, "Grant the petitioner's plea."));
                elements.Add(new InquiryElement(CourtStance.Compromise, c.Middle, null, true, "Refer the matter to the local authority."));
                elements.Add(new InquiryElement(CourtStance.Dismiss, c.Dismiss, null, true, "Turn the petitioner away."));
            }
            else
            {
                elements.Add(new InquiryElement(CourtStance.ForPlaintiff, c.ForPlaintiff, null, true, "Rule for the plaintiff."));
                elements.Add(new InquiryElement(CourtStance.ForDefendant, c.ForDefendant, null, true, "Rule for the defendant."));
                elements.Add(new InquiryElement(CourtStance.Compromise, c.Middle, null, true, "Impose a compromise (spends influence)."));
                elements.Add(new InquiryElement(CourtStance.Dismiss, c.Dismiss, null, true, "Dismiss the case unheard (costs legitimacy)."));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                c.Title, c.Body, elements, true, 1, 1, "Render judgment", "Adjourn",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is CourtStance st) TYTLog.Guard("Darbar.Rule", () => Rule(c, st)); },
                _ => { }, "", false), false, false);
        }

        // ── Building a grounded case from the live realm ─────────────────────────────
        private Case BuildCase(Kingdom k)
        {
            if (k == null) return null;
            var ft = FeudalTitlesBehavior.Instance;

            // Village zamindar-lords in the realm (not the player), for a land dispute.
            var zamindars = ft == null ? new List<Hero>() :
                Settlement.All.Where(s => s.IsVillage && s.MapFaction == k)
                    .Select(s => ft.GetVillageLord(s))
                    .Where(h => h != null && h.IsAlive && h.IsLord && h != Hero.MainHero && h.Clan != Clan.PlayerClan)
                    .Distinct().ToList();

            // A raided village (high threat) with a notable to plead, for a justice plea.
            Settlement raided = Settlement.All
                .Where(s => s.IsVillage && s.MapFaction == k && (VillageDevelopmentBehavior.Instance?.GetThreat(s) ?? 0f) >= 40f)
                .OrderByDescending(s => VillageDevelopmentBehavior.Instance?.GetThreat(s) ?? 0f)
                .FirstOrDefault();

            // Notables of the realm, for a market/tax quarrel.
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
                    Plaintiff = a, Defendant = b,
                    Title = "A Dispute of Boundaries",
                    Body = $"{a.Name}, zamindar of his village, comes before your darbar claiming that {b.Name} has moved the boundary " +
                           "stones and ploughed land that is rightfully his. Both are your vassals; both look to you for justice.",
                    ForPlaintiff = $"Find for {a.Name} — restore the old boundary",
                    ForDefendant = $"Find for {b.Name} — the new line stands",
                    Middle = "Split the disputed land between them",
                    Dismiss = "Send them both away to settle it themselves",
                };
            }

            // 2) A raided village pleads for justice.
            if (raided != null)
            {
                Hero pleader = (raided.Notables ?? Enumerable.Empty<Hero>()).FirstOrDefault(h => h != null && h.IsAlive)
                               ?? ft?.GetVillageLord(raided);
                if (pleader != null)
                    return new Case
                    {
                        IsPlea = true, Plaintiff = pleader,
                        Title = "A Plea for Justice",
                        Body = $"{pleader.Name} comes from {raided.Name}, which bandits have harried without mercy. He kneels before the " +
                               "darbar and begs the crown for justice and relief, for the local lord has not answered his cries.",
                        ForPlaintiff = "Grant relief — the crown will see the district protected",
                        Middle = "Refer him to his zamindar to handle",
                        Dismiss = "Turn him away — the crown has larger cares",
                    };
            }

            // 3) Two notables quarrelling.
            if (notables.Count >= 2)
            {
                Hero a = notables[MBRandom.RandomInt(notables.Count)];
                Hero b = notables.First(x => x != a);
                return new Case
                {
                    Plaintiff = a, Defendant = b,
                    Title = "A Quarrel of the Markets",
                    Body = $"{a.Name} accuses {b.Name} of cheating him in a matter of grain and gold. The two notables have brought " +
                           "their quarrel to your darbar rather than come to blows in the bazaar.",
                    ForPlaintiff = $"Find for {a.Name}",
                    ForDefendant = $"Find for {b.Name}",
                    Middle = "Order restitution split between them",
                    Dismiss = "Dismiss the squabble unheard",
                };
            }

            return null;
        }

        // ── Rendering judgment ───────────────────────────────────────────────────────
        private void Rule(Case c, CourtStance stance)
        {
            CourtOutcome o = c.IsPlea ? JudgePlea(stance) : Judge(stance);
            var op = OpinionBehavior.Instance;

            if (c.Plaintiff != null && o.PlaintiffOpinion != 0f)
                op?.AddOpinion(c.Plaintiff, Hero.MainHero, OpinionMath.OpinionType.CourtRuling, o.PlaintiffOpinion);
            if (!c.IsPlea && c.Defendant != null && o.DefendantOpinion != 0f)
                op?.AddOpinion(c.Defendant, Hero.MainHero, OpinionMath.OpinionType.CourtRuling, o.DefendantOpinion);

            if (o.Influence != 0) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, o.Influence);
            if (o.Legitimacy != 0f) LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, o.Legitimacy, "a judgment at the darbar");

            // Also nudge raw relation a touch, so the ruling reads in vanilla terms too.
            if (c.Plaintiff != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Plaintiff, Sign(o.PlaintiffOpinion), false);
            if (!c.IsPlea && c.Defendant != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Defendant, Sign(o.DefendantOpinion), false);

            Notify(Verdict(c, stance, o), o.Legitimacy < 0f);
            TYTLog.Info($"Darbar: '{c.Title}' ruled {stance} (plaint {o.PlaintiffOpinion:+0;-0}, def {o.DefendantOpinion:+0;-0}, inf {o.Influence:+0;-0}, legit {o.Legitimacy:+0.0;-0.0}).");
        }

        private static int Sign(float v) => v > 0f ? 2 : v < 0f ? -2 : 0;

        private static string Verdict(Case c, CourtStance stance, CourtOutcome o)
        {
            string infl = o.Influence != 0 ? $" (influence {o.Influence:+0;-0})" : "";
            string legit = o.Legitimacy != 0f ? $", legitimacy {o.Legitimacy:+0.0;-0.0}" : "";
            switch (stance)
            {
                case CourtStance.ForPlaintiff:
                    return c.IsPlea
                        ? $"You grant {c.Plaintiff?.Name}'s plea. The petitioner blesses your name{infl}{legit}."
                        : $"You rule for {c.Plaintiff?.Name}. He is grateful; {c.Defendant?.Name} nurses the wound{infl}{legit}.";
                case CourtStance.ForDefendant:
                    return $"You rule for {c.Defendant?.Name}. He is grateful; {c.Plaintiff?.Name} nurses the wound{infl}{legit}.";
                case CourtStance.Compromise:
                    return c.IsPlea
                        ? $"You refer {c.Plaintiff?.Name} to his local lord. A measured answer{infl}{legit}."
                        : $"You impose a compromise. Neither party leaves aggrieved, though the court's patience is spent{infl}{legit}.";
                default:
                    return c.IsPlea
                        ? $"You turn {c.Plaintiff?.Name} away. He leaves bitter, and the court's mercy is questioned{infl}{legit}."
                        : $"You dismiss the case unheard. Both parties leave aggrieved{infl}{legit}.";
            }
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        public override void SyncData(IDataStore dataStore)
            => dataStore.SyncData("hind_darbar_lastPetition", ref _lastPetitionDay);

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("darbar_petition", "hindostan")]
        public static string DarbarPetition(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (!IsPlayerRuler()) return "You must be a sovereign to hold the darbar.";
            Instance._lastPetitionDay = -100; // bypass the cooldown for testing
            Instance.HoldPetitionSitting();
            return "Held a petition sitting (if a grounded case was available).";
        }
    }
}
