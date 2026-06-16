using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TheHindostanMod.UI;

namespace TheHindostanMod
{
    // The complete feudal ladder of Hindostan, as a layer over vanilla ownership.
    // Vanilla decides who holds the town or castle (the mansabdar); we add the rung
    // beneath it — every village has its own lord, a zamindar drawn from the village's
    // local notables. This is OUR hierarchy, not the engine's: it never transfers a
    // settlement or touches the economy, but it gives every village a real, clickable
    // lord with relations, a liege, and an encyclopedia entry.
    //
    //   Unlanded noble  <  Village zamindar  <  Castle lord  <  Town lord  <  Sovereign
    //
    // Every village is assigned a zamindar automatically (AI and player realms alike),
    // and the post is refilled at once whenever its holder dies.
    public class FeudalTitlesBehavior : CampaignBehaviorBase
    {
        public static FeudalTitlesBehavior Instance { get; private set; }

        public const int AppointInfluenceCost = 20;
        public const int AppointGoldCost = 500;
        public const int DismissInfluenceCost = 10;

        // villageId -> zamindar heroId
        private Dictionary<string, string> _zamindar = new Dictionary<string, string>();
        private int _lastSummonDay = -100;
        private const int SummonCooldownDays = 21;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        // ── Village levies: every zamindar commands a small body of men ─────────────
        // A village's levy scales with its hearth (its population/wealth), between 20
        // and 40 men. These are the troops the zamindar brings when called to war.
        public int GetLevySize(Settlement village)
        {
            if (village?.Village == null) return 0;
            float hearth = village.Village.Hearth;
            int size = 20 + (int)(hearth / 60f);   // ~hearth 1200 -> 40
            return Math.Max(20, Math.Min(40, size));
        }

        // ── Public queries ─────────────────────────────────────────────────────────
        public Hero GetVillageLord(Settlement village)
        {
            if (village == null || !village.IsVillage) return null;
            if (!_zamindar.TryGetValue(village.StringId, out string id) || string.IsNullOrEmpty(id)) return null;
            Hero h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == id);
            return h != null && h.IsAlive ? h : null;
        }

        public bool IsVillageZamindar(Hero hero)
            => hero != null && _zamindar.Values.Contains(hero.StringId);

        // Directly seat a hero (incl. the player) as a village's zamindar — used by the
        // career-progression "claim your first fief" path. Auto-fill won't displace a
        // living assigned lord, so a player zamindar persists.
        public void AssignZamindar(Settlement village, Hero hero)
        {
            if (village == null || !village.IsVillage || hero == null) return;
            _zamindar[village.StringId] = hero.StringId;
        }

        public List<Settlement> GetVillagesLordedBy(Hero hero)
        {
            var result = new List<Settlement>();
            if (hero == null) return result;
            foreach (var kv in _zamindar)
                if (kv.Value == hero.StringId)
                {
                    Settlement s = Settlement.All.FirstOrDefault(x => x.StringId == kv.Key);
                    if (s != null) result.Add(s);
                }
            return result;
        }

        // The five-rung ladder. Used by the hierarchy viewer and the encyclopedia.
        public string GetTier(Hero hero)
        {
            if (hero == null) return "";
            Kingdom k = hero.Clan?.Kingdom;
            if (k != null && k.Leader == hero) return "Sovereign (Padshah)";
            if (hero.Clan != null)
            {
                if (hero.Clan.Settlements.Any(s => s.IsTown)) return "Town Lord";
                if (hero.Clan.Settlements.Any(s => s.IsCastle)) return "Castle Lord";
            }
            if (IsVillageZamindar(hero)) return "Village Zamindar";
            if (hero.IsLord && k != null) return "Unlanded Noble";
            return "";
        }

        // Numeric rank for ordering (higher = greater). Unlanded 1 … Sovereign 5.
        public int GetTierRank(Hero hero)
        {
            switch (GetTier(hero))
            {
                case "Sovereign (Padshah)": return 5;
                case "Town Lord": return 4;
                case "Castle Lord": return 3;
                case "Village Zamindar": return 2;
                case "Unlanded Noble": return 1;
                default: return 0;
            }
        }

        // Who a hero answers to in OUR feudal hierarchy.
        public Hero GetFeudalLiege(Hero hero)
        {
            if (hero == null) return null;
            Kingdom k = hero.Clan?.Kingdom ?? BoundKingdomOfZamindar(hero);
            if (k == null) return null;
            if (k.Leader == hero) return null;

            // A village zamindar answers to the lord who holds the bound town/castle.
            if (IsVillageZamindar(hero) && (hero.Clan == null || !HoldsTownOrCastle(hero.Clan)))
            {
                Settlement village = GetVillagesLordedBy(hero).FirstOrDefault();
                Hero boundLord = village?.Village?.Bound?.OwnerClan?.Leader;
                if (boundLord != null && boundLord != hero) return boundLord;
            }
            // Town/castle lords and unlanded nobles answer to the sovereign.
            return k.Leader;
        }

        // ── Auto-assignment ─────────────────────────────────────────────────────────
        private void OnNewGame(CampaignGameStarter starter) => AssignAllVillages();
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            AssignAllVillages();
            AddSummonMenus(starter);
        }
        private void OnWeeklyTick() => AssignAllVillages();

        // ── Call your vassals to war (from a town or castle you hold) ────────────────
        private void AddSummonMenus(CampaignGameStarter starter)
        {
            foreach (string root in new[] { "town", "castle" })
                starter.AddGameMenuOption(root, "hindostan_summon_levies_" + root,
                    "{=!}Summon your vassals' levies to war",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Recruit;
                              return CanSummonHere(Settlement.CurrentSettlement, out string reason, args); },
                    args => SummonLevies(Settlement.CurrentSettlement), false, 6);
        }

        private bool CanSummonHere(Settlement seat, out string reason, MenuCallbackArgs args)
        {
            reason = "";
            if (seat == null || !(seat.IsTown || seat.IsCastle) || seat.OwnerClan != Clan.PlayerClan)
                return false; // option simply absent when this isn't your seat

            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            bool atWar = k != null && Kingdom.All.Any(o => o != k && !o.IsEliminated && k.IsAtWarWith(o));
            int today = (int)CampaignTime.Now.ToDays;
            bool ready = today - _lastSummonDay >= SummonCooldownDays;

            args.IsEnabled = atWar && ready;
            if (!atWar) args.Tooltip = new TextObject("{=!}You may only call your levies when the realm is at war.");
            else if (!ready) args.Tooltip = new TextObject("{=!}Your levies were lately mustered; they need time to gather again.");
            return true;
        }

        private void SummonLevies(Settlement seat)
        {
            if (seat?.Town == null || MobileParty.MainParty == null) return;

            var villages = seat.Town.Villages?.Where(v => v?.Settlement != null).ToList()
                           ?? new List<Village>();
            if (villages.Count == 0) { Notify("No villages answer to this seat.", true); return; }

            int capacity = MobileParty.MainParty.Party.PartySizeLimit - MobileParty.MainParty.MemberRoster.TotalManCount;
            if (capacity <= 0) { Notify("Your host is already at full strength; there is no room for the levies.", true); return; }

            int summoned = 0;
            var lines = new List<string>();
            foreach (Village v in villages)
            {
                if (summoned >= capacity) break;
                Settlement vs = v.Settlement;
                Hero zamindar = GetVillageLord(vs);
                int levy = Math.Min(GetLevySize(vs), capacity - summoned);
                if (levy <= 0) continue;

                CharacterObject troop = (vs.Culture ?? seat.Culture)?.BasicTroop;
                if (troop == null) continue;
                MobileParty.MainParty.MemberRoster.AddToCounts(troop, levy);
                summoned += levy;
                lines.Add($"  {vs.Name}: {levy} men" + (zamindar != null ? $" under {zamindar.Name}" : ""));
            }

            if (summoned <= 0) { Notify("Your vassals could raise no men this season.", true); return; }
            _lastSummonDay = (int)CampaignTime.Now.ToDays;

            RoyalFarmaan.Issue("The Banners Are Called", $"By order of {Hero.MainHero.Name}",
                $"Your vassals answer the call to war and bring their levies to your banner at {seat.Name}:\n\n" +
                string.Join("\n", lines) + $"\n\n{summoned} men in all now ride with your host.",
                "Let us march");
        }

        private void AssignAllVillages()
        {
            foreach (Settlement s in Settlement.All)
            {
                if (!s.IsVillage) continue;
                if (GetVillageLord(s) != null) continue; // already held by a living lord
                Hero pick = ChooseZamindar(s, null);
                if (pick != null) _zamindar[s.StringId] = pick.StringId;
            }
        }

        // Pick the natural local lord: the headman first, then the most influential
        // notable. 'exclude' lets us choose a replacement different from the current.
        private Hero ChooseZamindar(Settlement village, Hero exclude)
        {
            var notables = village.Notables?
                .Where(n => n != null && n.IsAlive && !n.IsLord && n != exclude)
                .ToList() ?? new List<Hero>();
            if (notables.Count == 0) return null;
            return notables
                .OrderByDescending(n => n.IsHeadman ? 1 : 0)
                .ThenByDescending(n => n.Power)
                .First();
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool show)
        {
            if (victim == null || !IsVillageZamindar(victim)) return;
            foreach (Settlement village in GetVillagesLordedBy(victim).ToList())
            {
                Hero successor = ChooseZamindar(village, victim);
                if (successor != null)
                {
                    _zamindar[village.StringId] = successor.StringId;
                    if (village.Village?.Bound?.OwnerClan == Clan.PlayerClan)
                        Notify($"The zamindar of {village.Name} has died; {successor.Name} succeeds to the village.", false);
                }
                else _zamindar.Remove(village.StringId);
            }
        }

        // ── Player appointment / dismissal (with cost) ───────────────────────────────
        // The player manages the zamindars of villages within their own domain (the
        // villages bound to towns or castles their clan holds, or any village in a
        // realm they rule).
        public bool PlayerMayManage(Settlement village)
        {
            if (village == null || !village.IsVillage) return false;
            Clan boundOwner = village.Village?.Bound?.OwnerClan ?? village.OwnerClan;
            if (boundOwner == Clan.PlayerClan) return true;
            Kingdom k = village.MapFaction as Kingdom;
            return k != null && k.Leader == Hero.MainHero;
        }

        public void OpenManageZamindarDialog(Settlement village)
        {
            if (!PlayerMayManage(village))
            { Notify("This village does not lie within your domain.", true); return; }

            Hero current = GetVillageLord(village);
            var candidates = village.Notables?
                .Where(n => n != null && n.IsAlive && !n.IsLord && n != current)
                .ToList() ?? new List<Hero>();

            if (candidates.Count == 0)
            { Notify($"{village.Name} has no other notable fit to be made zamindar.", true); return; }

            var elements = new List<InquiryElement>();
            foreach (Hero n in candidates.OrderByDescending(n => n.IsHeadman ? 1 : 0).ThenByDescending(n => n.Power))
            {
                int rel = CharacterRelationManager.GetHeroRelation(Hero.MainHero, n);
                string hint = $"{NotableRole(n)}. Local influence {n.Power:0}. Relation with you {rel}. " +
                              $"Investiture costs {AppointInfluenceCost} influence and {AppointGoldCost} dinars.";
                elements.Add(new InquiryElement(n,
                    $"{n.Name} — {NotableRole(n)} (power {n.Power:0}, rel {rel})", null, true, hint));
            }

            string title = current != null ? $"Replace the Zamindar of {village.Name}" : $"Appoint the Zamindar of {village.Name}";
            string desc = (current != null
                    ? $"The village is presently held by {current.Name}. "
                    : "The village has no settled lord. ") +
                $"Invest a notable as its zamindar — its lord beneath you. Costs {AppointInfluenceCost} influence and {AppointGoldCost} dinars.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, desc, elements, true, 1, 1, "Invest as zamindar", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Hero hero) AppointZamindar(village, hero); },
                _ => { }, "", false), false, false);
        }

        private void AppointZamindar(Settlement village, Hero hero)
        {
            if (Clan.PlayerClan.Influence < AppointInfluenceCost)
            { Notify($"You need {AppointInfluenceCost} influence to invest a zamindar.", true); return; }
            if (Hero.MainHero.Gold < AppointGoldCost)
            { Notify($"You need {AppointGoldCost} dinars for the investiture.", true); return; }

            Hero previous = GetVillageLord(village);
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -AppointInfluenceCost);
            Hero.MainHero.ChangeHeroGold(-AppointGoldCost);
            _zamindar[village.StringId] = hero.StringId;

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, hero, 10);
            if (previous != null && previous != hero)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, previous, -8);

            RoyalFarmaan.Issue("Investiture of a Zamindar", $"By the hand of {Hero.MainHero.Name}",
                $"{hero.Name} is invested as zamindar of {village.Name}, to hold the village and its lands beneath " +
                "you and to answer for its peace and its revenues.", "Sealed by your authority");
        }

        public void DismissZamindar(Settlement village)
        {
            if (!PlayerMayManage(village)) { Notify("This village is not within your domain.", true); return; }
            Hero current = GetVillageLord(village);
            if (current == null) { Notify("There is no zamindar to dismiss.", true); return; }
            if (Clan.PlayerClan.Influence < DismissInfluenceCost)
            { Notify($"You need {DismissInfluenceCost} influence to dismiss a zamindar.", true); return; }

            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -DismissInfluenceCost);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, current, -10);

            Hero successor = ChooseZamindar(village, current);
            if (successor != null) _zamindar[village.StringId] = successor.StringId;
            else _zamindar.Remove(village.StringId);

            Notify($"{current.Name} is stripped of the zamindari of {village.Name}." +
                   (successor != null ? $" {successor.Name} is raised in his place." : ""), false);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────
        private static bool HoldsTownOrCastle(Clan clan)
            => clan != null && clan.Settlements.Any(s => s.IsTown || s.IsCastle);

        private static bool HoldsTownOrCastle(Hero h) => HoldsTownOrCastle(h?.Clan);

        private Kingdom BoundKingdomOfZamindar(Hero hero)
        {
            Settlement v = GetVillagesLordedBy(hero).FirstOrDefault();
            return v?.MapFaction as Kingdom ?? v?.Village?.Bound?.MapFaction as Kingdom;
        }

        public static string NotableRole(Hero h)
        {
            if (h == null) return "Notable";
            if (h.IsHeadman) return "Headman";
            if (h.IsRuralNotable) return "Rural notable";
            if (h.IsArtisan) return "Artisan";
            if (h.IsMerchant) return "Merchant";
            if (h.IsGangLeader) return "Gang leader";
            if (h.IsPreacher) return "Preacher";
            return "Notable";
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Save / load ────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var ids = _zamindar.Keys.ToList();
            var vals = _zamindar.Values.ToList();
            dataStore.SyncData("hind_zamindar_villages", ref ids);
            dataStore.SyncData("hind_zamindar_lords", ref vals);
            dataStore.SyncData("hind_zamindar_lastsummon", ref _lastSummonDay);
            if (!dataStore.IsSaving)
            {
                _zamindar = new Dictionary<string, string>();
                for (int i = 0; i < ids.Count && i < vals.Count; i++) _zamindar[ids[i]] = vals[i];
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("village_lords", "hindostan")]
        public static string ListVillageLords(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            var villages = Settlement.All.Where(s => s.IsVillage).Take(40).ToList();
            return string.Join("\n", villages.Select(v =>
            {
                Hero l = Instance.GetVillageLord(v);
                Hero liege = l != null ? Instance.GetFeudalLiege(l) : null;
                return $"{v.Name}: {(l != null ? l.Name.ToString() : "— vacant —")}" +
                       (liege != null ? $"  (vassal of {liege.Name})" : "");
            }));
        }
    }
}
