using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // The Hindostan dialogue pack: person-to-person court business, done face to face
    // (the UX charter's rule — personal acts belong in CONVERSATION, not menus).
    // Every flow follows the proven mansab-petition template (MansabdariBehavior):
    // AddPlayerLine hooked on "hero_main_options", side-effect-free conditions that may
    // set text variables, consequences that do the work, ending at close_window or back
    // at hero_main_options. Six flows:
    //   1. Swear fealty to your liege in person        (writes a SworeFealty opinion)
    //   2. Present the nazrana with your own hands      (bridges NazranaBehavior)
    //   3. Air a grievance / mend or press a quarrel    (reads the opinion ledger)
    //   4. A word with your village's notable           (steadies the district)
    //   5. Address a royal prince by his style          (reads the dynasty layer)
    //   6. Invite a lord to your council                (seeds CouncilBehavior)
    public class HindostanDialogsBehavior : CampaignBehaviorBase
    {
        public static HindostanDialogsBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore) { }

        private static Hero Partner => Hero.OneToOneConversationHero;

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            AddFealtyFlow(starter);
            AddNazranaFlow(starter);
            AddGrievanceFlow(starter);
            AddNotableFlow(starter);
            AddPrinceFlow(starter);
            AddCouncilInviteFlow(starter);
            AddFollowFlow(starter);
            AddAnecdoteFlow(starter);
        }

        // ── 8. The news of the roads (round 8: the world talks about its own history) ─
        // Any lord will pass on what the serais are saying — real events of the age retold
        // as hearsay (Util.HistoricalAnecdotes, tested). The same man keeps his tale for a
        // week, then the wheel turns.
        private void AddAnecdoteFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_dlg_news", "hero_main_options", "hind_dlg_news_reply",
                "{=!}Tell me — what word do the roads carry these days?",
                () =>
                {
                    Hero p = Partner;
                    if (p == null || p == Hero.MainHero) return false;
                    int week = (int)((float)CampaignTime.Now.ToDays / 7f);
                    MBTextManager.SetTextVariable("HIND_NEWS",
                        HistoricalAnecdotes.Tale(CoronationOaths.SeedOf(p.StringId), week), false);
                    return true;
                }, null, 101);

            starter.AddDialogLine("hind_dlg_news_reply", "hind_dlg_news_reply", "hero_main_options",
                "{=!}{HIND_NEWS}", () => true, null);
        }

        // ── 1. Swear fealty in person ────────────────────────────────────────────────
        private void AddFealtyFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_dlg_fealty", "hero_main_options", "hind_dlg_fealty_reply",
                "{=!}Huzoor, I come to renew my oath — my sword and my salt are yours.",
                () =>
                {
                    Hero p = Partner;
                    if (p == null || p == Hero.MainHero) return false;
                    if (FiefHierarchyBehavior.Instance?.GetLiege(Hero.MainHero) != p) return false;
                    return OpinionBehavior.Instance?.HasLive(p, Hero.MainHero, OpinionMath.OpinionType.SworeFealty) != true;
                }, null, 108);

            starter.AddDialogLine("hind_dlg_fealty_reply", "hind_dlg_fealty_reply", "close_window",
                "{=!}Rise. The court sees who keeps faith — and who does not. Your oath is entered in the registers.",
                () => true,
                () =>
                {
                    Hero p = Partner;
                    if (p == null) return;
                    OpinionBehavior.Instance?.AddOpinion(p, Hero.MainHero, OpinionMath.OpinionType.SworeFealty);
                    ChangeClanInfluenceAction.Apply(Clan.PlayerClan, 5f);
                    Notify($"Your oath to {p.Name} is renewed; the court takes note.", false);
                });
        }

        // ── 2. Present the nazrana in person ─────────────────────────────────────────
        private void AddNazranaFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_dlg_nazrana", "hero_main_options", "hind_dlg_nazrana_reply",
                "{=!}Jahanpanah, I bring my nazrana to lay before the throne with my own hands.",
                () =>
                {
                    Hero p = Partner;
                    Kingdom k = Clan.PlayerClan?.Kingdom;
                    return p != null && k != null && p == k.Leader && p != Hero.MainHero
                           && (NazranaBehavior.Instance?.HasPendingCall ?? false);
                }, null, 109);

            starter.AddDialogLine("hind_dlg_nazrana_reply", "hind_dlg_nazrana_reply", "close_window",
                "{=!}Brought with your own hands? That is the old courtesy, and the throne remembers it. Present your gift.",
                () => true,
                () => NazranaBehavior.Instance?.PresentInPerson());
        }

        // ── 3. Air a grievance ───────────────────────────────────────────────────────
        private void AddGrievanceFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_dlg_grudge", "hero_main_options", "hind_dlg_grudge_reply",
                "{=!}There is a shadow between us, {GRUDGE_NAME}. Speak plainly — what wrong lies between us?",
                () =>
                {
                    Hero p = Partner;
                    var op = OpinionBehavior.Instance;
                    if (p == null || p == Hero.MainHero || op == null) return false;
                    if (!op.TryGetGrievance(Hero.MainHero, p, out _, out _, out _)) return false;
                    MBTextManager.SetTextVariable("GRUDGE_NAME", p.Name?.ToString() ?? "my lord", false);
                    return true;
                }, null, 107);

            starter.AddDialogLine("hind_dlg_grudge_reply", "hind_dlg_grudge_reply", "hind_dlg_grudge_choice",
                "{=!}{GRUDGE_TEXT}",
                () =>
                {
                    Hero p = Partner;
                    var op = OpinionBehavior.Instance;
                    if (p == null || op == null) return false;
                    op.TryGetGrievance(Hero.MainHero, p, out var type, out _, out bool mineAgainstHim);
                    string what = OpinionMath.Describe(type);
                    MBTextManager.SetTextVariable("GRUDGE_TEXT", mineAgainstHim
                        ? $"You know well enough: {what}. If you would have it mended, mend it."
                        : $"You ask me? It is {what} that stands between us — and it was not of my making.", false);
                    return true;
                }, null);

            starter.AddPlayerLine("hind_dlg_grudge_mend", "hind_dlg_grudge_choice", "hind_dlg_grudge_mended",
                "{=!}Then let it be mended. Take this purse of 500 rupees, and let there be salt between us again.",
                () => Hero.MainHero.Gold >= 500,
                () =>
                {
                    Hero p = Partner;
                    var op = OpinionBehavior.Instance;
                    if (p == null || op == null) return;
                    if (op.TryGetGrievance(Hero.MainHero, p, out var type, out _, out _))
                    {
                        op.ClearOpinion(Hero.MainHero, p, type);
                        op.ClearOpinion(p, Hero.MainHero, type);
                    }
                    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, p, 500, true);
                    op.AddOpinion(p, Hero.MainHero, OpinionMath.OpinionType.Favor);
                    Notify($"The quarrel with {p.Name} is laid to rest.", false);
                });

            starter.AddDialogLine("hind_dlg_grudge_mended", "hind_dlg_grudge_mended", "close_window",
                "{=!}...So be it. The matter is closed, and I will say so at court.", () => true, null);

            starter.AddPlayerLine("hind_dlg_grudge_press", "hind_dlg_grudge_choice", "hind_dlg_grudge_pressed",
                "{=!}The quarrel stands. See that you remember it.",
                () => true,
                () =>
                {
                    Hero p = Partner;
                    var op = OpinionBehavior.Instance;
                    if (p == null || op == null) return;
                    op.AddOpinion(p, Hero.MainHero, OpinionMath.OpinionType.Grudge, -6f);
                    op.AddOpinion(Hero.MainHero, p, OpinionMath.OpinionType.Grudge, -6f);
                });

            starter.AddDialogLine("hind_dlg_grudge_pressed", "hind_dlg_grudge_pressed", "close_window",
                "{=!}Oh, I shall. Count on it.", () => true, null);

            starter.AddPlayerLine("hind_dlg_grudge_leave", "hind_dlg_grudge_choice", "hero_main_options",
                "{=!}Another day, then.", () => true, null);
        }

        // ── 4. A word with your village's notable ────────────────────────────────────
        private void AddNotableFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_dlg_notable", "hero_main_options", "hind_dlg_notable_reply",
                "{=!}As the lord of this village, I would hear how the district fares.",
                () =>
                {
                    Hero p = Partner;
                    Settlement v = p?.CurrentSettlement;
                    if (p == null || !p.IsNotable || v == null || !v.IsVillage) return false;
                    return FeudalTitlesBehavior.Instance?.GetVillageLord(v) == Hero.MainHero
                           || v.OwnerClan == Clan.PlayerClan;
                }, null, 106);

            starter.AddDialogLine("hind_dlg_notable_reply", "hind_dlg_notable_reply", "hind_dlg_notable_choice",
                "{=!}Huzoor honours us. The fields are as they are — and the roads are not what they were. What is your will?",
                () => true, null);

            starter.AddPlayerLine("hind_dlg_notable_reassure", "hind_dlg_notable_choice", "hind_dlg_notable_done",
                "{=!}Tell the people their lord watches over them. No dacoit will make his nest here while I hold this land.",
                () => true,
                () =>
                {
                    Hero p = Partner;
                    Settlement v = p?.CurrentSettlement;
                    if (p == null || v == null) return;
                    VillageDevelopmentBehavior.Instance?.ReassureVillage(v);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, p, 2);
                    OpinionBehavior.Instance?.AddOpinion(p, Hero.MainHero, OpinionMath.OpinionType.Favor, 4f);
                });

            starter.AddDialogLine("hind_dlg_notable_done", "hind_dlg_notable_done", "close_window",
                "{=!}The village will sleep the easier for it, Huzoor. May your shadow never grow less.",
                () => true, null);

            starter.AddPlayerLine("hind_dlg_notable_leave", "hind_dlg_notable_choice", "hero_main_options",
                "{=!}That is all for now.", () => true, null);
        }

        // ── 5. Address a royal prince ────────────────────────────────────────────────
        private void AddPrinceFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_dlg_prince", "hero_main_options", "hind_dlg_prince_reply",
                "{=!}Adaab, {PRINCE_STYLE}. May I ask how you see the days ahead for your house?",
                () =>
                {
                    Hero p = Partner;
                    string style = p != null && p != Hero.MainHero ? DynastyBehavior.Instance?.RoyalStyle(p) : null;
                    if (string.IsNullOrEmpty(style)) return false;
                    MBTextManager.SetTextVariable("PRINCE_STYLE", style, false);
                    return true;
                }, null, 105);

            starter.AddDialogLine("hind_dlg_prince_reply", "hind_dlg_prince_reply", "close_window",
                "{=!}{PRINCE_ANSWER}",
                () =>
                {
                    Hero p = Partner;
                    if (p == null) return false;
                    Kingdom k = p.Clan?.Kingdom;
                    Hero heir = k != null ? SuccessionLawBehavior.Instance?.LawfulHeir(k) : null;
                    string answer;
                    if (heir == p)
                        answer = "The days ahead? They are mine, if God wills it. The throne knows its heir — and so should you.";
                    else if (heir != null
                             && (OpinionBehavior.Instance?.EffectiveOpinion(p, heir) ?? 0f) < 0f)
                        answer = "You ask a dangerous question. My brother wears the future like a robe cut for another man. " +
                                 "We shall see what the seasons bring.";
                    else
                        answer = "The house stands, and I stand with it. Whatever comes, there is salt between us all — for now.";
                    MBTextManager.SetTextVariable("PRINCE_ANSWER", answer, false);
                    return true;
                }, null);
        }

        // ── 6. Invite a lord to your council ─────────────────────────────────────────
        private void AddCouncilInviteFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_dlg_invite", "hero_main_options", "hind_dlg_invite_reply",
                "{=!}My court has need of men of your quality. Would you sit at my council?",
                () =>
                {
                    Hero p = Partner;
                    if (p == null || p == Hero.MainHero || !p.IsLord || p.Clan == null) return false;
                    if (!CouncilBehavior.IsCouncilHolder(Hero.MainHero)) return false;
                    if (p.Clan.Kingdom == null || p.Clan.Kingdom != Clan.PlayerClan?.Kingdom) return false;
                    if (CouncilBehavior.Instance?.GetPostOf(p) != null) return false;
                    return Clan.PlayerClan.Influence >= 20f;
                }, null, 104);

            starter.AddDialogLine("hind_dlg_invite_reply", "hind_dlg_invite_reply", "close_window",
                "{=!}You do my house honour. When a seat stands open at your council, my name is yours to call on.",
                () => true,
                () =>
                {
                    Hero p = Partner;
                    if (p == null) return;
                    ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -20f);
                    // The Favor raises his EffectiveOpinion of you, which is exactly what
                    // CouncilBehavior's candidate scoring now reads — he rises to the top
                    // of the next appointment naturally.
                    OpinionBehavior.Instance?.AddOpinion(p, Hero.MainHero, OpinionMath.OpinionType.Favor);
                    Notify($"{p.Name} is minded to serve at your council; appoint him when a seat opens.", false);
                });
        }

        // ── 7. Command a party face to face (round 6: the map menu's orders, spoken) ─
        // "Follow my banner" / "resume your course" said to the lord himself. Rides on
        // PartyOrdersBehavior — same acceptance ledger, same hourly re-assertion — but an
        // order asked in person carries extra weight with a vassal.
        private static bool _followAccepted;

        private void AddFollowFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_dlg_follow", "hero_main_options", "hind_dlg_follow_reply",
                "{=!}Ride with me — keep your banner at my side.",
                () =>
                {
                    Hero p = Partner;
                    var po = PartyOrdersBehavior.Instance;
                    return p != null && po != null
                           && po.CanCommandInDialogue(p, out MobileParty mp, out _)
                           && !po.IsUnderMyOrder(mp);
                },
                () =>
                {
                    Hero p = Partner;
                    var po = PartyOrdersBehavior.Instance;
                    _followAccepted = p != null && po != null
                                      && po.CanCommandInDialogue(p, out MobileParty mp, out _)
                                      && po.TryIssueFollowInPerson(mp);
                }, 106);

            starter.AddDialogLine("hind_dlg_follow_yes", "hind_dlg_follow_reply", "close_window",
                "{=!}My banner rides at yours. Lead, and we follow.",
                () => _followAccepted, null);
            starter.AddDialogLine("hind_dlg_follow_no", "hind_dlg_follow_reply", "close_window",
                "{=!}No. I answer to no captain but the field — ask again when the ledger between us reads better.",
                () => !_followAccepted, null);

            starter.AddPlayerLine("hind_dlg_release", "hero_main_options", "hind_dlg_release_reply",
                "{=!}You may resume your own course.",
                () =>
                {
                    Hero p = Partner;
                    var po = PartyOrdersBehavior.Instance;
                    return p?.PartyBelongedTo != null && p.PartyBelongedTo.LeaderHero == p
                           && po != null && po.IsUnderMyOrder(p.PartyBelongedTo);
                },
                () =>
                {
                    Hero p = Partner;
                    if (p?.PartyBelongedTo != null) PartyOrdersBehavior.Instance?.StandDownInPerson(p.PartyBelongedTo);
                }, 106);
            starter.AddDialogLine("hind_dlg_release_r", "hind_dlg_release_reply", "close_window",
                "{=!}As you say. My banner keeps its own road again.", () => true, null);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));
    }
}
