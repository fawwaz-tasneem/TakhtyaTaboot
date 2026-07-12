using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // The qasid — messengers, native (no-Diplomacy mandate, ROADMAP block E). Where the akhbaar
    // scout brings word BACK, the qasid carries YOUR word OUT: dispatch him after any lord from
    // the lord's encyclopedia page, and when the rider reaches him a conversation opens AS IF
    // YOU STOOD THERE YOURSELF — the full dialogue tree, every option you would have in person
    // (fealty, grievances, invitations, war talk). Price and road time are deterministic and
    // tested (MessengerMath): cheaper and faster than a scout, foreign courts cost half again.
    //
    // The audience is opened by a tick pump (the darbar court's proven pattern): it waits for
    // the campaign MAP (never interrupts a battle or a town walk), for any conversation to end,
    // and for the darbar court to rise, then ushers the lord in. Arrived-but-unheard audiences
    // are serialized, so a save between arrival and audience loses nothing.
    public class MessengerBehavior : CampaignBehaviorBase
    {
        public static MessengerBehavior Instance { get; private set; }

        private class Qasid
        {
            public string TargetId;
            public string TargetName; // kept for the report if the lord dies on the road
            public float ArriveDay;
        }

        private List<Qasid> _riders = new List<Qasid>();
        private List<string> _audiences = new List<string>(); // arrived: heroes awaiting the word

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Qasid.DailyTick", OnDailyTick));
            CampaignEvents.TickEvent.AddNonSerializedListener(this, _ => PumpAudiences());
        }

        public override void SyncData(IDataStore dataStore)
        {
            var ids = _riders.Select(r => r.TargetId).ToList();
            var names = _riders.Select(r => r.TargetName).ToList();
            var days = _riders.Select(r => r.ArriveDay).ToList();
            dataStore.SyncData("hind_qasid_ids", ref ids);
            dataStore.SyncData("hind_qasid_names", ref names);
            dataStore.SyncData("hind_qasid_days", ref days);
            dataStore.SyncData("hind_qasid_audiences", ref _audiences);
            if (!dataStore.IsSaving)
            {
                _riders = new List<Qasid>();
                for (int i = 0; i < ids.Count && i < names.Count && i < days.Count; i++)
                    _riders.Add(new Qasid { TargetId = ids[i], TargetName = names[i], ArriveDay = days[i] });
                if (_audiences == null) _audiences = new List<string>();
            }
        }

        // ── Dispatch (the encyclopedia surface) ──────────────────────────────────────
        public bool IsEnRoute(Hero h)
            => h != null && (_riders.Any(r => r.TargetId == h.StringId) || _audiences.Contains(h.StringId));

        public bool CanDispatchFor(Hero h, out string label, out string reason)
        {
            label = ""; reason = "";
            if (h == null || !h.IsAlive || h.IsChild || !h.IsLord
                || h.Clan == null || h.Clan == Clan.PlayerClan)
            { reason = "no lord to carry word to"; return false; }
            if (IsEnRoute(h))
            {
                Qasid r = _riders.FirstOrDefault(x => x.TargetId == h.StringId);
                reason = r != null
                    ? $"A qasid is on the road — audience in ~{Math.Max(0f, r.ArriveDay - (float)CampaignTime.Now.ToDays):0} day(s)."
                    : "Your qasid has reached him — the audience is at hand.";
                return false;
            }
            int cost = CostFor(h);
            label = $"Send a qasid ({cost} rupees)";
            if (Hero.MainHero.Gold < cost) { reason = $"You cannot pay the {cost}-rupee fee."; return false; }
            return true;
        }

        private static int CostFor(Hero h)
            => MessengerMath.DispatchCost(h.Clan?.Kingdom != null && h.Clan.Kingdom != Clan.PlayerClan?.Kingdom);

        public void DispatchFromEncyclopedia(Hero h)
        {
            if (!CanDispatchFor(h, out _, out string reason)) { if (!string.IsNullOrEmpty(reason)) Notify(reason, true); return; }
            int cost = CostFor(h);
            InformationManager.ShowInquiry(new InquiryData(
                "Send a Qasid",
                $"Send a messenger to {UI.RoyalFarmaan.NameWithHonorific(h)} for {cost} rupees? When the rider reaches him " +
                "you will speak through your qasid as if you stood before the lord yourself — every word available in " +
                "person is available by messenger.",
                true, true, "Send him", "Not now",
                () => TYTLog.Guard("Qasid.Dispatch", () => Dispatch(h)), null), true);
        }

        private void Dispatch(Hero h)
        {
            if (h == null || IsEnRoute(h)) return;
            int cost = CostFor(h);
            if (Hero.MainHero.Gold < cost) return;

            Vec2 from = Settlement.CurrentSettlement?.GetPosition2D ?? MobileParty.MainParty?.GetPosition2D ?? Vec2.Zero;
            float distance = from != Vec2.Zero ? from.Distance(TargetPosition(h)) : 0f;
            float days = MessengerMath.DaysToReach(distance);

            Hero.MainHero.ChangeHeroGold(-cost);
            _riders.Add(new Qasid
            {
                TargetId = h.StringId,
                TargetName = h.Name.ToString(),
                ArriveDay = (float)CampaignTime.Now.ToDays + days,
            });
            Notify($"Your qasid rides for {h.Name} ({cost} rupees). Expect the audience in some {(int)Math.Ceiling(days)} day(s).", false);
            TYTLog.Info($"Qasid: dispatched to {h.StringId}, {cost} rupees, ~{days:0.0} days.");
        }

        private static Vec2 TargetPosition(Hero h)
        {
            if (h.PartyBelongedTo != null) return h.PartyBelongedTo.GetPosition2D;
            if (h.CurrentSettlement != null) return h.CurrentSettlement.GetPosition2D;
            return h.HomeSettlement?.GetPosition2D ?? Vec2.Zero;
        }

        // ── The road, and the audience ───────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (_riders.Count == 0) return;
            float now = (float)CampaignTime.Now.ToDays;
            foreach (Qasid r in _riders.Where(r => now >= r.ArriveDay).ToList())
            {
                _riders.Remove(r);
                Hero h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == r.TargetId);
                if (h == null)
                {
                    UI.RoyalFarmaan.Issue("The Qasid Returns Unheard", "By the hand of your messenger",
                        $"Your qasid reached the end of the road with the letter undelivered: {r.TargetName} is dead. " +
                        "Whatever business you had with him passes to his heirs.",
                        primary: "So be it", dedupeKey: "qasid_dead_" + r.TargetId);
                    continue;
                }
                _audiences.Add(r.TargetId);
                TYTLog.Info($"Qasid: reached {r.TargetId}; audience queued.");
            }
        }

        // Usher the audience in when the moment allows: on the map, no conversation live, and
        // the darbar court not sitting (its chain must never be interleaved).
        private void PumpAudiences()
        {
            if (_audiences.Count == 0) return;
            TYTLog.GuardQuiet("Qasid.Pump", () =>
            {
                if (!(GameStateManager.Current?.ActiveState is MapState)) return;
                if (Campaign.Current?.ConversationManager?.IsConversationInProgress == true) return;
                if (DarbarPetitionBehavior.Instance?.IsSitting == true) return;

                string id = _audiences[0];
                Hero h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == id);
                _audiences.RemoveAt(0);
                if (h == null || h.CharacterObject == null) return; // died between arrival and audience

                Notify($"Your qasid stands before {h.Name}. Speak — the lord receives your words as if you were there.", false);
                CampaignMapConversation.OpenConversation(
                    new ConversationCharacterData(CharacterObject.PlayerCharacter),
                    new ConversationCharacterData(h.CharacterObject, null, noHorse: true, noWeapon: true, noBodyguards: true));
            });
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("qasid_status", "hindostan")]
        public static string QasidStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Instance._riders.Count == 0 && Instance._audiences.Count == 0) return "No qasid on the road.";
            float now = (float)CampaignTime.Now.ToDays;
            var lines = Instance._riders.Select(r =>
                $"{r.TargetName} — audience in {Math.Max(0f, r.ArriveDay - now):0.0} day(s)").ToList();
            lines.AddRange(Instance._audiences.Select(id => $"{id} — ARRIVED, audience pending"));
            return string.Join("\n", lines);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("qasid_arrive", "hindostan")]
        public static string QasidArrive(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Instance._riders.Count == 0) return "No qasid on the road.";
            foreach (Qasid r in Instance._riders) r.ArriveDay = 0f;
            Instance.OnDailyTick();
            return "All riders forced to arrive; audiences open on the map.";
        }
    }
}
