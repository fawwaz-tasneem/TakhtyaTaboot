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
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Command your parties on the campaign map. You may direct the parties of your own
    // clan (companions and kin) directly — their order is re-asserted each hour so it
    // holds — and you may ORDER your vassals, which is a relation-gated request they may
    // refuse (and which is applied once, since they keep their own AI). Orders:
    //   Follow me · Find & attack an enemy · Support a lord · Reinforce a holding ·
    //   Defend a village · Stand down.
    public class PartyOrdersBehavior : CampaignBehaviorBase
    {
        public static PartyOrdersBehavior Instance { get; private set; }

        private const int Follow = 0, Attack = 1, Support = 2, Reinforce = 3, Defend = 4, StandDown = 5;
        private const int ClanOrderDays = 30, VassalOrderDays = 20;

        private class Order { public int Type; public string TargetId; public bool Vassal; public int Expiry; }
        private Dictionary<string, Order> _orders = new Dictionary<string, Order>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, OnHourlyTickParty);
        }

        // ── Which parties you may command ────────────────────────────────────────────
        private static IEnumerable<MobileParty> ClanParties()
        {
            if (Clan.PlayerClan == null) yield break;
            foreach (var wpc in Clan.PlayerClan.WarPartyComponents)
            {
                MobileParty mp = wpc.MobileParty;
                if (mp != null && mp.IsActive && mp != MobileParty.MainParty && mp.LeaderHero != null) yield return mp;
            }
        }

        private static IEnumerable<MobileParty> VassalParties()
        {
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k == null) yield break;
            bool king = k.Leader == Hero.MainHero;
            foreach (Clan c in k.Clans)
            {
                if (c == Clan.PlayerClan || c.IsEliminated || c.Leader == null) continue;
                bool vassal = king || FeudalTitlesBehavior.Instance?.GetFeudalLiege(c.Leader) == Hero.MainHero;
                if (!vassal) continue;
                MobileParty mp = c.Leader.PartyBelongedTo;
                if (mp != null && mp.IsActive) yield return mp;
            }
        }

        private static List<MobileParty> CommandableParties()
            => ClanParties().Concat(VassalParties()).Distinct().ToList();

        private static bool IsVassalParty(MobileParty p) => p?.LeaderHero?.Clan != Clan.PlayerClan;

        // ── Menu + flow ──────────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            foreach (string root in new[] { "town", "castle", "village" })
                starter.AddGameMenuOption(root, "hindostan_command_" + root, "{=!}Command your parties",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return CommandableParties().Count > 0; },
                    args => OpenCommand(), false, 11);
        }

        private void OpenCommand()
        {
            var parties = CommandableParties();
            if (parties.Count == 0) { Notify("You have no companions or vassals to command.", true); return; }
            var elements = parties.Select(p => new InquiryElement(p, PartyLabel(p), null, true, PartyHint(p))).ToList();
            Pick("Command Your Parties", "Choose a party to give orders to.", elements,
                id => { if (id is MobileParty p) ChooseOrder(p); });
        }

        private static string PartyLabel(MobileParty p)
            => $"{p.LeaderHero?.Name} — {(IsVassalParty(p) ? "vassal" : "your clan")} ({p.MemberRoster.TotalManCount} men)";

        private static string PartyHint(MobileParty p)
        {
            string cur = Instance != null && Instance._orders.TryGetValue(p.StringId, out Order o) ? OrderName(o.Type) : "none";
            return $"Current order: {cur}. " + (IsVassalParty(p) ? "A vassal may refuse." : "A clan party obeys.");
        }

        private void ChooseOrder(MobileParty p)
        {
            bool vassal = IsVassalParty(p);
            var elements = new List<InquiryElement>
            {
                new InquiryElement(Follow, "Follow me", null, true, "Escort your party."),
                new InquiryElement(Attack, "Find and attack an enemy", null, true, "Hunt and engage an enemy lord."),
                new InquiryElement(Support, "Support a lord", null, true, "Escort and fight alongside a friendly lord."),
                new InquiryElement(Reinforce, "Reinforce a holding", null, true, "March to one of your towns or castles."),
                new InquiryElement(Defend, "Defend a village", null, true, "Guard one of your villages against raids."),
                new InquiryElement(StandDown, "Stand down", null, true, "Cancel the current order."),
            };
            Pick($"Orders for {p.LeaderHero?.Name}",
                vassal ? "This lord is your vassal; he may refuse the order." : "Your clan party will obey.",
                elements, id => OnOrderChosen(p, Convert.ToInt32(id)));
        }

        private void OnOrderChosen(MobileParty p, int type)
        {
            switch (type)
            {
                case Follow: Issue(p, Follow, null); break;
                case Attack: PickParty(p, Attack, enemy: true); break;
                case Support: PickParty(p, Support, enemy: false); break;
                case Reinforce: PickSettlement(p, Reinforce, villages: false); break;
                case Defend: PickSettlement(p, Defend, villages: true); break;
                case StandDown:
                    _orders.Remove(p.StringId);
                    try { p.SetMoveModeHold(); } catch { }
                    Notify($"{p.LeaderHero?.Name} stands down.", false);
                    break;
            }
        }

        private void PickParty(MobileParty p, int type, bool enemy)
        {
            IFaction mine = Hero.MainHero?.MapFaction;
            var targets = MobileParty.All.Where(m => m != null && m.IsActive && m.LeaderHero != null && m != p
                    && m.MapFaction != null && mine != null
                    && (enemy ? mine.IsAtWarWith(m.MapFaction) : m.MapFaction == mine && m != MobileParty.MainParty))
                .Take(30).ToList();
            if (targets.Count == 0) { Notify(enemy ? "No enemy in the field to hunt." : "No friendly lord to support.", true); return; }
            var elements = targets.Select(m => new InquiryElement(m,
                $"{m.LeaderHero.Name} ({m.MapFaction?.Name})", null, true, $"{m.MemberRoster.TotalManCount} men")).ToList();
            Pick(enemy ? "Attack which lord?" : "Support which lord?", "Choose a target.", elements,
                id => { if (id is MobileParty t) Issue(p, type, t.StringId); });
        }

        private void PickSettlement(MobileParty p, int type, bool villages)
        {
            var list = (Clan.PlayerClan?.Settlements ?? Enumerable.Empty<Settlement>())
                .Where(s => villages ? s.IsVillage : (s.IsTown || s.IsCastle)).ToList();
            if (list.Count == 0) { Notify(villages ? "You hold no villages." : "You hold no towns or castles.", true); return; }
            var elements = list.Select(s => new InquiryElement(s, s.Name.ToString(), null, true,
                villages ? "Defend this village." : "Reinforce this holding.")).ToList();
            Pick(villages ? "Defend which village?" : "Reinforce which holding?", "Choose a destination.", elements,
                id => { if (id is Settlement s) Issue(p, type, s.StringId); });
        }

        // ── Issuing & enforcing orders ───────────────────────────────────────────────
        private void Issue(MobileParty p, int type, string targetId)
        {
            int today = (int)CampaignTime.Now.ToDays;
            if (IsVassalParty(p))
            {
                Hero leader = p.LeaderHero;
                // HIS opinion of you, not just clan-book relation: oaths, favours and
                // grudges in the personal ledger sway whether he rides at your word.
                float rel = OpinionBehavior.Instance?.EffectiveOpinion(leader, Hero.MainHero)
                            ?? CharacterRelationManager.GetHeroRelation(Hero.MainHero, leader);
                float auth = Hero.MainHero.Clan?.Kingdom?.Leader == Hero.MainHero
                    ? (ImperialAuthorityBehavior.Instance?.GetAuthority(Hero.MainHero.Clan.Kingdom) ?? 50f) : 0f;
                bool accept = rel + auth / 2f + MBRandom.RandomInt(-20, 20) >= 20f;
                if (!accept)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leader, -2);
                    Notify($"{leader.Name} refuses your order. He answers to no captain but the field.", true);
                    return;
                }
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leader, 1);
                var ov = new Order { Type = type, TargetId = targetId, Vassal = true, Expiry = today + VassalOrderDays };
                _orders[p.StringId] = ov;
                ApplyOrderMovement(p, ov);
                Notify($"{leader.Name} agrees to your order: {OrderName(type)}.", false);
                return;
            }

            var o = new Order { Type = type, TargetId = targetId, Vassal = false, Expiry = today + ClanOrderDays };
            _orders[p.StringId] = o;
            ApplyOrderMovement(p, o);
            Notify($"{p.LeaderHero?.Name} will carry out your order: {OrderName(type)}.", false);
        }

        private void OnHourlyTickParty(MobileParty party)
            => TYTLog.GuardQuiet("PartyOrders.HourlyTick", () => HourlyTick(party));

        private void HourlyTick(MobileParty party)
        {
            if (!TYTLog.Valid(party) || !_orders.TryGetValue(party.StringId, out Order o)) return;
            if ((int)CampaignTime.Now.ToDays >= o.Expiry) { _orders.Remove(party.StringId); return; }
            if (!party.IsActive || party.LeaderHero == null) { _orders.Remove(party.StringId); return; }

            // An army's movement is controlled by its commander; don't fight it (unless it's the player's).
            if (party.Army != null && party.Army.LeaderParty != MobileParty.MainParty) return;

            // A clan party that has left the clan is no longer ours to command. A vassal who ACCEPTED an
            // order is now held to it for its duration (re-asserted each hour) — previously it was a single
            // nudge the vassal's AI overrode at once, so "follow me" appeared to do nothing.
            if (!o.Vassal && party.LeaderHero.Clan != Clan.PlayerClan) { _orders.Remove(party.StringId); return; }
            ApplyOrderMovement(party, o);
        }

        private void ApplyOrderMovement(MobileParty p, Order o)
        {
            var nav = MobileParty.NavigationType.Default;
            try
            {
                switch (o.Type)
                {
                    case Follow:
                        if (MobileParty.MainParty != null) p.SetMoveEscortParty(MobileParty.MainParty, nav, false);
                        break;
                    case Attack:
                        { MobileParty t = FindParty(o.TargetId); if (t != null && t.IsActive) p.SetMoveEngageParty(t, nav); else _orders.Remove(p.StringId); }
                        break;
                    case Support:
                        { MobileParty t = FindParty(o.TargetId); if (t != null && t.IsActive) p.SetMoveEscortParty(t, nav, false); else _orders.Remove(p.StringId); }
                        break;
                    case Reinforce:
                        { Settlement s = FindSettlement(o.TargetId); if (s != null) p.SetMoveGoToSettlement(s, nav, false); }
                        break;
                    case Defend:
                        { Settlement s = FindSettlement(o.TargetId); if (s != null) p.SetMoveDefendSettlement(s, false, nav); }
                        break;
                }
            }
            catch { /* AI may reject an order mid-army; leave it for next tick */ }
        }

        private static MobileParty FindParty(string id)
            => string.IsNullOrEmpty(id) ? null : MobileParty.All.FirstOrDefault(m => m.StringId == id);

        private static Settlement FindSettlement(string id)
            => string.IsNullOrEmpty(id) ? null : Settlement.All.FirstOrDefault(s => s.StringId == id);

        private static string OrderName(int t) => t == Follow ? "follow you" : t == Attack ? "attack an enemy"
            : t == Support ? "support a lord" : t == Reinforce ? "reinforce a holding"
            : t == Defend ? "defend a village" : "stand down";

        // ── Helpers ──────────────────────────────────────────────────────────────────
        private void Pick(string title, string desc, List<InquiryElement> elements, Action<object> onPick)
        {
            if (elements.Count == 0) { Notify("There is nothing to choose.", true); return; }
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, desc, elements, true, 1, 1, "Confirm", "Cancel",
                sel => { if (sel != null && sel.Count > 0) onPick(sel[0].Identifier); },
                _ => { }, "", false), false, false);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Save / load ──────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var pids = new List<string>(); var types = new List<int>(); var tgts = new List<string>();
            var vass = new List<int>(); var exps = new List<int>();
            foreach (var kv in _orders)
            {
                pids.Add(kv.Key); types.Add(kv.Value.Type); tgts.Add(kv.Value.TargetId ?? "");
                vass.Add(kv.Value.Vassal ? 1 : 0); exps.Add(kv.Value.Expiry);
            }
            dataStore.SyncData("hind_orders_pids", ref pids);
            dataStore.SyncData("hind_orders_types", ref types);
            dataStore.SyncData("hind_orders_tgts", ref tgts);
            dataStore.SyncData("hind_orders_vass", ref vass);
            dataStore.SyncData("hind_orders_exps", ref exps);

            if (!dataStore.IsSaving)
            {
                _orders = new Dictionary<string, Order>();
                for (int i = 0; i < pids.Count; i++)
                    _orders[pids[i]] = new Order
                    {
                        Type = i < types.Count ? types[i] : 0,
                        TargetId = i < tgts.Count ? tgts[i] : "",
                        Vassal = i < vass.Count && vass[i] != 0,
                        Expiry = i < exps.Count ? exps[i] : 0,
                    };
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("orders", "hindostan")]
        public static string ListOrders(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Instance._orders.Count == 0) return "No parties are under orders.";
            return string.Join("\n", Instance._orders.Select(kv =>
            {
                MobileParty p = FindParty(kv.Key);
                return $"{(p?.LeaderHero?.Name?.ToString() ?? kv.Key)}: {OrderName(kv.Value.Type)}" +
                       (kv.Value.Vassal ? " (vassal)" : "");
            }));
        }
    }
}
