using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // The dynasty layer the engine lacks. Three things live here:
    //   1. A persistent registry — which clans belong to which dynasty, and which clan
    //      is a CADET branch of which parent house (replacing the old StringId-prefix
    //      hack as the source of truth).
    //   2. The roll of sovereigns — every hero who has ever held each throne — so the
    //      children of a reigning OR fallen line carry their culture's royal style:
    //      Shahzada/Shahzadi (Muslim realms), Yuvraj/Rajkumari (Hindu), Kanwar/Bibi
    //      (Sikh), Mirza for grandsons of the Timurid line.
    //   3. Cadet-house founding: the player charters an adult kinsman as head of his
    //      own house (gold + influence + the sovereign's consent, judged by his
    //      PERSONAL opinion), and rich AI houses rarely do the same — so the world's
    //      family tree branches the way the period's did.
    public class DynastyBehavior : CampaignBehaviorBase
    {
        public static DynastyBehavior Instance { get; private set; }

        private const int FoundInfluenceCost = 100;

        private Dictionary<string, string> _clanDynasty = new Dictionary<string, string>();   // clanId -> dynastyId
        private Dictionary<string, string> _dynastyName = new Dictionary<string, string>();   // dynastyId -> display name
        private Dictionary<string, string> _cadetParent = new Dictionary<string, string>();   // childClanId -> parentClanId
        private Dictionary<string, string> _pastSovereigns = new Dictionary<string, string>(); // kingdomId -> csv heroIds
        private Dictionary<string, string> _accessionDay = new Dictionary<string, string>();   // kingdomId -> day the CURRENT ruler acceded
        private int _cadetCount;
        private int _lastAiFoundDay = -1;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Dynasty.WeeklyTick", OnWeeklyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            SyncDict(dataStore, "hind_dyn_clans", ref _clanDynasty);
            SyncDict(dataStore, "hind_dyn_names", ref _dynastyName);
            SyncDict(dataStore, "hind_dyn_cadets", ref _cadetParent);
            SyncDict(dataStore, "hind_dyn_sovs", ref _pastSovereigns);
            SyncDict(dataStore, "hind_dyn_accday", ref _accessionDay);
            dataStore.SyncData("hind_dyn_cadetCount", ref _cadetCount);
            dataStore.SyncData("hind_dyn_lastAiFound", ref _lastAiFoundDay);
        }

        private static void SyncDict(IDataStore ds, string key, ref Dictionary<string, string> dict)
        {
            var keys = dict.Keys.ToList();
            var vals = dict.Values.ToList();
            ds.SyncData(key + "_k", ref keys);
            ds.SyncData(key + "_v", ref vals);
            if (!ds.IsSaving)
            {
                dict = new Dictionary<string, string>();
                for (int i = 0; i < keys.Count && i < vals.Count; i++) dict[keys[i]] = vals[i];
            }
        }

        // ── Registry queries ─────────────────────────────────────────────────────────
        public string DynastyOf(Clan clan)
            => clan != null && _clanDynasty.TryGetValue(clan.StringId, out string d) ? d : null;

        public string DynastyName(string dynastyId)
            => dynastyId != null && _dynastyName.TryGetValue(dynastyId, out string n) ? n : null;

        public Clan CadetParentOf(Clan clan)
        {
            if (clan == null || !_cadetParent.TryGetValue(clan.StringId, out string pid)) return null;
            return Clan.All.FirstOrDefault(c => c.StringId == pid);
        }

        public bool SameDynasty(Clan a, Clan b)
        {
            string da = DynastyOf(a), db = DynastyOf(b);
            return da != null && da == db;
        }

        public int CadetHousesFounded => _cadetCount;

        public void RegisterCadet(Clan cadet, Clan parent)
        {
            if (cadet == null || parent == null) return;
            _cadetParent[cadet.StringId] = parent.StringId;
            string dynasty = DynastyOf(parent) ?? EnsureDynasty(parent);
            _clanDynasty[cadet.StringId] = dynasty;
            _cadetCount++;
        }

        private string EnsureDynasty(Clan clan)
        {
            string existing = DynastyOf(clan);
            if (existing != null) return existing;
            string id = "dyn_" + clan.StringId;
            _clanDynasty[clan.StringId] = id;
            _dynastyName[id] = "House of " + (clan.Name?.ToString() ?? clan.StringId);
            return id;
        }

        // ── Royal styles ─────────────────────────────────────────────────────────────
        // The style of a prince/princess of a line that holds or has held a throne.
        public string RoyalStyle(Hero hero)
        {
            if (hero == null) return null;

            Kingdom viaParent = ThroneOfAncestor(hero.Father) ?? ThroneOfAncestor(hero.Mother);
            if (viaParent != null) return StyleFor(viaParent, hero.IsFemale, grandchild: false);

            Kingdom viaGrandparent = ThroneOfAncestor(hero.Father?.Father) ?? ThroneOfAncestor(hero.Father?.Mother)
                ?? ThroneOfAncestor(hero.Mother?.Father) ?? ThroneOfAncestor(hero.Mother?.Mother);
            if (viaGrandparent != null) return StyleFor(viaGrandparent, hero.IsFemale, grandchild: true);
            return null;
        }

        // The kingdom whose roll of sovereigns contains this hero, or null.
        private Kingdom ThroneOfAncestor(Hero ancestor)
        {
            if (ancestor == null) return null;
            foreach (var kv in _pastSovereigns)
                if (Csv(kv.Value).Contains(ancestor.StringId))
                    return Kingdom.All.FirstOrDefault(k => k.StringId == kv.Key);
            return null;
        }

        private static string StyleFor(Kingdom realm, bool female, bool grandchild)
        {
            Religion r = ReligionBehavior.Instance?.GetCultureReligion(realm?.Culture) ?? Religion.None;
            if (grandchild)
                return r == Religion.Islam && !female ? "Mirza" : null; // only the Timurid custom survives a generation
            switch (r)
            {
                case Religion.Islam: return female ? "Shahzadi" : "Shahzada";
                case Religion.Hindu: return female ? "Rajkumari" : "Yuvraj";
                case Religion.Sikh: return female ? "Bibi" : "Kanwar";
                default: return female ? "Princess" : "Prince";
            }
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Idempotent seed: every ruling clan is a dynasty; every reigning sovereign
            // goes on the roll; legacy claimant/exile clans link to their origin dynasty.
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && !UnifiedEmpireBehavior.IsDormant(x)))
            {
                if (k.RulingClan != null) EnsureDynasty(k.RulingClan);
                RecordSovereign(k);
            }
            foreach (Clan c in Clan.All)
                if (c?.StringId != null && !_cadetParent.ContainsKey(c.StringId)
                    && (c.StringId.StartsWith("tyt_claim_") || c.StringId.StartsWith("tyt_exile_")))
                {
                    // Back-compat: an old-save cadet-ish clan without a registry entry —
                    // link it under its kingdom's ruling house if any, purely for grouping.
                    Clan parent = c.Kingdom?.RulingClan;
                    if (parent != null && parent != c) { _cadetParent[c.StringId] = parent.StringId; _clanDynasty[c.StringId] = EnsureDynasty(parent); }
                }

            AddMenus(starter);
        }

        private void OnWeeklyTick()
        {
            // Keep the roll of sovereigns current (a new ruler joins it the week he accedes).
            // A dormant shell's nominal leader is an empire vassal, not a reigning sovereign.
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null
                                                         && !UnifiedEmpireBehavior.IsDormant(x)))
            {
                if (k.RulingClan != null) EnsureDynasty(k.RulingClan);
                RecordSovereign(k);
            }

            // AI cadet founding: rare, yearly, capped — a slow branching of the great houses.
            int today = (int)CampaignTime.Now.ToDays;
            if (_lastAiFoundDay >= 0 && today - _lastAiFoundDay < 360) return;
            _lastAiFoundDay = today;
            if (_cadetCount >= Config.Tune.CadetMaxHouses) return;

            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated))
            {
                if (_cadetCount >= Config.Tune.CadetMaxHouses) break;
                foreach (Clan c in k.Clans.Where(c => !c.IsEliminated && c != Clan.PlayerClan
                                                      && !c.IsUnderMercenaryService && c.Leader != null))
                {
                    if (c.Renown < Config.Tune.CadetAiRenownFloor) continue;
                    if (c.Leader.Gold < Config.Tune.CadetGoldCost * 2) continue;
                    var spareMales = AdultKin(c).Where(h => !h.IsFemale && h != c.Leader).ToList();
                    if (spareMales.Count < 2) continue; // needs an heir AND a spare
                    if (MBRandom.RandomFloat >= 0.10f) continue;

                    Hero founder = spareMales.Skip(1).First(); // the second son rides out
                    c.Leader.ChangeHeroGold(-Config.Tune.CadetGoldCost);
                    Clan cadet = CadetHouse.Found(founder, c, k);
                    if (cadet != null && k == Hero.MainHero?.Clan?.Kingdom)
                        RoyalFarmaan.FromRuler(k, "A New House Is Chartered",
                            $"With the blessing of the throne, {founder.Name} of {c.Name} departs his father's roof to found " +
                            $"his own house. Let the registers record {cadet.Name} among the houses of the realm.",
                            "So it is recorded", priority: FarmaanPriority.Ceremonial);
                    break; // one per realm per year at most
                }
            }
        }

        private void RecordSovereign(Kingdom k)
        {
            if (k?.Leader == null) return;
            string csv = _pastSovereigns.TryGetValue(k.StringId, out string v) ? v : "";
            var ids = Csv(csv);
            if (!ids.Contains(k.Leader.StringId))
            {
                // A roll that already has entries means this is a real accession happening in
                // play — date it today. A FIRST entry is the campaign-start ruler, who has
                // plainly reigned a while already: seed a standing tenure so buying out an
                // established king costs what it should (Alamgir's 49 years are the point of
                // the whole exercise — see SuccessionLawMath.IncumbentPrice).
                int today = (int)CampaignTime.Now.ToDays;
                int seedYears = k.Leader.StringId == "tyt_aurangzeb" ? 49 : ids.Count == 0 ? 15 : 0;
                _accessionDay[k.StringId] = (today - seedYears * CampaignTime.DaysInYear).ToString();

                ids.Add(k.Leader.StringId);
                _pastSovereigns[k.StringId] = string.Join(",", ids);
            }
        }

        // Years the CURRENT ruler has sat this throne (0 if unknown). Read by the succession
        // crisis economy: an incumbent's abdication price grows with his tenure.
        public float GetReignYears(Kingdom k)
        {
            if (k?.StringId == null || !_accessionDay.TryGetValue(k.StringId, out string s)
                || !int.TryParse(s, out int day)) return 0f;
            return Math.Max(0f, ((float)CampaignTime.Now.ToDays - day) / CampaignTime.DaysInYear);
        }

        private static List<string> Csv(string s)
            => string.IsNullOrEmpty(s) ? new List<string>() : s.Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();

        private static IEnumerable<Hero> AdultKin(Clan c)
            => c.Heroes.Where(h => h != null && h.IsAlive && !h.IsChild && h.IsLord);

        // ── Player founding (menu under the court submenu, see CourtMenuBehavior) ─────
        private void AddMenus(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("hindostan_court", "hindostan_charter_cadet",
                "{=!}Charter a cadet house (a kinsman founds his own house)",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Manage;
                    if (Clan.PlayerClan?.Leader != Hero.MainHero) return false;
                    args.IsEnabled = EligibleFounders().Any();
                    if (!args.IsEnabled) args.Tooltip = new TaleWorlds.Localization.TextObject(
                        "{=!}No adult kinsman of your house (beyond your heir) stands ready.");
                    return true;
                },
                args => OpenCharterDialog(), false, 8);
        }

        private IEnumerable<Hero> EligibleFounders()
        {
            Clan pc = Clan.PlayerClan;
            if (pc == null) yield break;
            Hero heir = pc.Heroes.Where(h => h != null && h.IsAlive && !h.IsChild && !h.IsFemale && h != Hero.MainHero)
                .OrderByDescending(h => Hero.MainHero.Children.Contains(h) ? 1 : 0)
                .ThenByDescending(h => h.Age).FirstOrDefault(); // the presumptive heir stays home
            foreach (Hero h in pc.Heroes)
            {
                if (h == null || !h.IsAlive || h.IsChild || h == Hero.MainHero || h == heir) continue;
                if (h.IsFemale && !Config.Tune.CadetAllowFemale) continue;
                if (!h.IsLord) continue;
                yield return h;
            }
        }

        private void OpenCharterDialog()
        {
            int cost = Config.Tune.CadetGoldCost;
            if (Hero.MainHero.Gold < cost) { Notify($"Chartering a house demands {cost} rupees.", true); return; }
            if (Clan.PlayerClan.Influence < FoundInfluenceCost) { Notify($"You need {FoundInfluenceCost} influence.", true); return; }

            // The sovereign's consent is a PERSONAL judgment of you.
            Kingdom k = Clan.PlayerClan.Kingdom;
            if (k?.Leader != null && k.Leader != Hero.MainHero)
            {
                float opinion = OpinionBehavior.Instance?.EffectiveOpinion(k.Leader, Hero.MainHero)
                                ?? CharacterRelationManager.GetHeroRelation(k.Leader, Hero.MainHero);
                if (opinion < 0f)
                {
                    RoyalFarmaan.FromRuler(k, "The Charter Is Refused",
                        "The throne sees no cause to multiply the houses of a lord it does not trust. " +
                        "Mend your standing with the sovereign, and petition again.", "I hear it");
                    return;
                }
            }

            var elements = EligibleFounders()
                .Select(h => new InquiryElement(h, $"{h.Name} ({(int)h.Age}, {(h.IsFemale ? "daughter" : "kinsman")} of your house)",
                    null, true, $"{h.Name} departs with spouse and children to found a house of your dynasty."))
                .ToList();
            if (elements.Count == 0) return;

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Charter a Cadet House",
                $"Choose the kinsman who will found a new house of your dynasty. It costs {cost} rupees and {FoundInfluenceCost} influence.",
                elements, true, 1, 1, "Charter it", "Not now",
                sel =>
                {
                    if (sel == null || sel.Count == 0 || !(sel[0].Identifier is Hero founder)) return;
                    Hero.MainHero.ChangeHeroGold(-cost);
                    TaleWorlds.CampaignSystem.Actions.ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -FoundInfluenceCost);
                    Clan cadet = CadetHouse.Found(founder, Clan.PlayerClan, Clan.PlayerClan.Kingdom);
                    if (cadet == null) { Notify("The charter could not be sealed; your silver is returned.", true); Hero.MainHero.ChangeHeroGold(cost); return; }
                    OpinionBehavior.Instance?.AddOpinion(founder, Hero.MainHero, OpinionMath.OpinionType.Favor, 12f);
                    RoyalFarmaan.Issue("A House of Your Blood", $"Sealed by the hand of {Hero.MainHero.Name}",
                        $"{founder.Name} departs your roof with your blessing to found {cadet.Name} — a cadet house of your " +
                        "dynasty, sworn to the same realm. May your line branch and flourish.",
                        seal: "Entered among the houses of the realm", primary: "Go with my blessing",
                        priority: FarmaanPriority.Ceremonial);
                },
                _ => { }, "", false), false, false);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));
    }
}
