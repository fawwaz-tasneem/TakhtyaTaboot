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

namespace TakhtyaTaboot
{
    // The court's instruments of favour (round 8) — three small mechanics on existing rails,
    // all in the Persianate idiom of the age:
    //   • KHIL'AT — the robe of honour, bestowed in dialogue on a lord of your realm: the
    //     khil'at-e-fakhira (a robe of royal silk, for service at court) or the char-aina
    //     breastplate (for feats of arms). Gold cost, relation + a Favor in the opinion
    //     ledger; one bestowal per lord per season and a half.
    //   • TITLES — the sovereign grants an honorific (Bahadur, Jang, ud-Daula, ul-Mulk) that
    //     is appended to the lord's name wherever the court writes it (NameWithHonorific).
    //     Influence cost; once per lord, permanent.
    //   • JHAROKHA DARSHAN — once a MONTH the sovereign shows himself at the jharokha of one
    //     of his realm's towns: a trickle of legitimacy and the town's loyalty. The custom
    //     Alamgir abolished, restored.
    public class CourtHonoursBehavior : CampaignBehaviorBase
    {
        public static CourtHonoursBehavior Instance { get; private set; }

        private const int RobeCost = 500;          // rupees, the khil'at of royal silk
        private const int BreastplateCost = 900;   // rupees, the char-aina for feats of arms
        private const int KhilatCooldownDays = 45;
        private const int TitleInfluenceCost = 60;
        private const int JharokhaIntervalDays = 30;

        private static readonly string[] TitleChoices = { "Bahadur", "Jang", "ud-Daula", "ul-Mulk" };

        private Dictionary<string, float> _khilatDay = new Dictionary<string, float>(); // heroId -> last bestowal day
        private Dictionary<string, string> _titles = new Dictionary<string, string>();  // heroId -> granted honorific
        private float _jharokhaDay = -999f;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            var kIds = _khilatDay.Keys.ToList();
            var kDays = _khilatDay.Values.ToList();
            var tIds = _titles.Keys.ToList();
            var tVals = _titles.Values.ToList();
            dataStore.SyncData("hind_honour_kids", ref kIds);
            dataStore.SyncData("hind_honour_kdays", ref kDays);
            dataStore.SyncData("hind_honour_tids", ref tIds);
            dataStore.SyncData("hind_honour_tvals", ref tVals);
            dataStore.SyncData("hind_honour_jday", ref _jharokhaDay);
            if (!dataStore.IsSaving)
            {
                _khilatDay = new Dictionary<string, float>();
                for (int i = 0; i < kIds.Count && i < kDays.Count; i++) _khilatDay[kIds[i]] = kDays[i];
                _titles = new Dictionary<string, string>();
                for (int i = 0; i < tIds.Count && i < tVals.Count; i++) _titles[tIds[i]] = tVals[i];
            }
        }

        // The granted honorific for a hero, or null — read by RoyalFarmaan.NameWithHonorific.
        public string TitleOf(Hero h)
            => h != null && _titles.TryGetValue(h.StringId, out string t) ? t : null;

        private static Hero Partner => Hero.OneToOneConversationHero;

        private static bool IsMyVassalLord(Hero p)
        {
            Kingdom mine = Hero.MainHero?.Clan?.Kingdom;
            return p != null && p != Hero.MainHero && mine != null && mine.Leader == Hero.MainHero
                   && p.Clan?.Kingdom == mine && p.Clan != Clan.PlayerClan && p.IsLord && !p.IsChild;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            AddKhilatFlow(starter);
            AddTitleFlow(starter);
            AddJharokhaMenu(starter);
        }

        // ── The khil'at ──────────────────────────────────────────────────────────────
        private void AddKhilatFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_khilat", "hero_main_options", "hind_khilat_reply",
                "{=!}Approach. The court would bestow a khil'at upon you.",
                () =>
                {
                    Hero p = Partner;
                    if (!IsMyVassalLord(p)) return false;
                    float last = _khilatDay.TryGetValue(p.StringId, out float d) ? d : -999f;
                    return (float)CampaignTime.Now.ToDays - last >= KhilatCooldownDays
                           && Hero.MainHero.Gold >= RobeCost;
                }, null, 103);

            starter.AddDialogLine("hind_khilat_reply", "hind_khilat_reply", "hind_khilat_choice",
                "{=!}Jahanpanah, you do my house honour. I stand at your pleasure.", () => true, null);

            starter.AddPlayerLine("hind_khilat_robe", "hind_khilat_choice", "hind_khilat_done_robe",
                "{=!}Receive the khil'at-e-fakhira — a robe of royal silk, for your service to the court. (" + RobeCost + " rupees)",
                () => Hero.MainHero.Gold >= RobeCost,
                () => TYTLog.Guard("Honours.Robe", () => BestowKhilat(false)));
            starter.AddPlayerLine("hind_khilat_plate", "hind_khilat_choice", "hind_khilat_done_plate",
                "{=!}Receive the char-aina — a breastplate from the royal armoury, for your feats of arms. (" + BreastplateCost + " rupees)",
                () => Hero.MainHero.Gold >= BreastplateCost,
                () => TYTLog.Guard("Honours.Plate", () => BestowKhilat(true)));
            starter.AddPlayerLine("hind_khilat_never", "hind_khilat_choice", "close_window",
                "{=!}Another day. You may withdraw.", () => true, null);

            starter.AddDialogLine("hind_khilat_done_robe", "hind_khilat_done_robe", "close_window",
                "{=!}I wear it upon my shoulders and my honour both, Jahanpanah. The court will see whose favour clothes me.",
                () => true, null);
            starter.AddDialogLine("hind_khilat_done_plate", "hind_khilat_done_plate", "close_window",
                "{=!}Steel from the Padishah's own armoury — there is no higher wage for a soldier. My sword remembers this.",
                () => true, null);
        }

        private void BestowKhilat(bool martial)
        {
            Hero p = Partner;
            if (p == null) return;
            int cost = martial ? BreastplateCost : RobeCost;
            if (Hero.MainHero.Gold < cost) return;
            Hero.MainHero.ChangeHeroGold(-cost);
            _khilatDay[p.StringId] = (float)CampaignTime.Now.ToDays;
            OpinionBehavior.Instance?.AddOpinion(p, Hero.MainHero, OpinionMath.OpinionType.Favor);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, p, martial ? 7 : 5, false);
            Notify(martial
                ? $"{p.Name} receives the char-aina before the court. The soldiers mark whose feats the throne rewards."
                : $"{p.Name} is robed in the khil'at-e-fakhira. The court marks whose service the throne clothes.", false);
            TYTLog.Info($"Honours: khil'at ({(martial ? "char-aina" : "robe")}) bestowed on {p.StringId}.");
        }

        // ── The granted title ────────────────────────────────────────────────────────
        private void AddTitleFlow(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("hind_title", "hero_main_options", "hind_title_reply",
                "{=!}Kneel. The court would join a title of honour to your name.",
                () =>
                {
                    Hero p = Partner;
                    return IsMyVassalLord(p) && TitleOf(p) == null
                           && Clan.PlayerClan.Influence >= TitleInfluenceCost;
                }, null, 102);

            starter.AddDialogLine("hind_title_reply", "hind_title_reply", "close_window",
                "{=!}Jahanpanah... my house will carry it, and what it carries, it keeps.",
                () => true, () => TYTLog.Guard("Honours.Title", OpenTitlePicker));
        }

        private void OpenTitlePicker()
        {
            Hero p = Partner;
            if (p == null) return;
            var elements = new List<InquiryElement>
            {
                new InquiryElement("Bahadur", $"{p.Name} Bahadur", null, true, "'The Brave' — the soldier's honorific."),
                new InquiryElement("Jang", $"{p.Name} Jang", null, true, "'Of the War' — for command in the field."),
                new InquiryElement("ud-Daula", $"{p.Name} ud-Daula", null, true, "'Pillar of the State' — for service to the realm."),
                new InquiryElement("ul-Mulk", $"{p.Name} ul-Mulk", null, true, "'Of the Kingdom' — the great administrator's style."),
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Grant of Title",
                $"Choose the honorific the court joins to the name of {p.Name}. It costs {TitleInfluenceCost} influence, and the registers do not un-write it.",
                elements, true, 1, 1, "Bestow", "Not now",
                sel =>
                {
                    if (sel == null || sel.Count == 0 || !(sel[0].Identifier is string title)) return;
                    TYTLog.Guard("Honours.TitleGrant", () => GrantTitle(p, title));
                }, _ => { }, "", false), false, false);
        }

        private void GrantTitle(Hero p, string title)
        {
            if (p == null || TitleOf(p) != null || Clan.PlayerClan.Influence < TitleInfluenceCost) return;
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -TitleInfluenceCost);
            _titles[p.StringId] = title;
            OpinionBehavior.Instance?.AddOpinion(p, Hero.MainHero, OpinionMath.OpinionType.Favor);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, p, 6, false);
            Notify($"By farmaan of the court, {p.Name} is henceforth styled {p.Name} {title}.", false);
            TYTLog.Info($"Honours: title '{title}' granted to {p.StringId}.");
        }

        // ── The jharokha darshan ─────────────────────────────────────────────────────
        private void AddJharokhaMenu(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town", "hindostan_jharokha", "{=!}Show yourself at the jharokha",
                JharokhaCondition, _ => TYTLog.Guard("Honours.Jharokha", DoJharokha), false, 10);
        }

        private bool JharokhaCondition(MenuCallbackArgs args)
        {
            Kingdom mine = Hero.MainHero?.Clan?.Kingdom;
            Settlement s = Settlement.CurrentSettlement;
            if (mine == null || mine.Leader != Hero.MainHero || s == null || !s.IsTown || s.MapFaction != mine)
                return false;
            args.optionLeaveType = GameMenuOption.LeaveType.Continue;
            float since = (float)CampaignTime.Now.ToDays - _jharokhaDay;
            if (since < JharokhaIntervalDays)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject($"{{=!}}The custom is a monthly one — again in {JharokhaIntervalDays - (int)since} day(s).");
            }
            return true;
        }

        private void DoJharokha()
        {
            Settlement s = Settlement.CurrentSettlement;
            if (s?.Town == null) return;
            _jharokhaDay = (float)CampaignTime.Now.ToDays;
            LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, 1.5f, "the jharokha darshan");
            s.Town.Loyalty = Math.Min(100f, s.Town.Loyalty + 2f);
            Notify($"At first light you appear at the jharokha of {s.Name}, and the crowd below sees with its own eyes " +
                   "that the sovereign lives, reigns, and looks upon them. The custom the old Padishah abolished is kept again.", false);
            TYTLog.Info($"Honours: jharokha darshan at {s.StringId}.");
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));
    }
}
