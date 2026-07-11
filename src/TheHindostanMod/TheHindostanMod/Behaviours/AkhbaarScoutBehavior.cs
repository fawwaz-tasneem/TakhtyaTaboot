using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Akhbaar scouts (roadmap A.1) — the seed of the akhbarat espionage layer (wiki ch.17).
    // From any court the player pays to dispatch a harkara after a named lord; when the
    // runner comes off the road an AKHBAAR (newsletter) arrives in the farmaan layer:
    // where the lord is, what he is about, and his strength in hearsay terms — a rounded
    // count and a worded composition, never an exact roster. The report is a snapshot
    // taken the day it ARRIVES (the runner rode back with fresh word, not stale).
    //   • Price and delay are quoted up front and deterministic (AkhbaarMath, tested):
    //     famous houses are cheaper to trace, foreign realms cost half again, and the
    //     road takes 2–12 days by real map distance.
    //   • One scout per target at a time; the runner always returns with SOMETHING —
    //     a camp, a garrison town, a dungeon, or word of the lord's death.
    // Surface per the UX charter: dispatch is place-bound → the hindostan_court menu;
    // the report is an event → a farmaan.
    public class AkhbaarScoutBehavior : CampaignBehaviorBase
    {
        public static AkhbaarScoutBehavior Instance { get; private set; }

        private class Scout
        {
            public string TargetId;   // hero StringId
            public string TargetName; // kept for the report if the lord dies on the road
            public float ArriveDay;   // CampaignTime.Now.ToDays when the akhbaar lands
            public string Origin;     // settlement the scout was hired at (flavour)
        }

        private List<Scout> _scouts = new List<Scout>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Akhbaar.DailyTick", OnDailyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            var ids = _scouts.Select(s => s.TargetId).ToList();
            var names = _scouts.Select(s => s.TargetName).ToList();
            var days = _scouts.Select(s => s.ArriveDay).ToList();
            var origins = _scouts.Select(s => s.Origin ?? "").ToList();
            dataStore.SyncData("hind_akhbaar_ids", ref ids);
            dataStore.SyncData("hind_akhbaar_names", ref names);
            dataStore.SyncData("hind_akhbaar_days", ref days);
            dataStore.SyncData("hind_akhbaar_origins", ref origins);
            if (!dataStore.IsSaving)
            {
                _scouts = new List<Scout>();
                for (int i = 0; i < ids.Count && i < names.Count && i < days.Count; i++)
                    _scouts.Add(new Scout
                    {
                        TargetId = ids[i], TargetName = names[i], ArriveDay = days[i],
                        Origin = i < origins.Count ? origins[i] : "",
                    });
            }
        }

        // ── Dispatch (the court menu) ────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption(CourtMenuBehavior.MenuId, "hindostan_akhbaar_dispatch",
                "{=!}Dispatch an akhbaar scout after a lord",
                DispatchCondition, args => TYTLog.Guard("Akhbaar.Open", OpenDispatch), false, 8);
        }

        private bool DispatchCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Default;
            if (_scouts.Count > 0)
                args.Tooltip = new TaleWorlds.Localization.TextObject(
                    "{=!}" + _scouts.Count + " scout(s) already on the road.");
            return true;
        }

        private void OpenDispatch()
        {
            var candidates = Candidates();
            if (candidates.Count == 0)
            { Notify("There is no lord worth a scout's fee.", true); return; }

            var elements = candidates.Select(h =>
            {
                int cost = CostFor(h);
                bool tracked = IsTracked(h);
                string hint = $"{h.Clan?.Name} of {h.Clan?.Kingdom?.Name}. Fee: {cost} dinars."
                    + (tracked ? " A scout is already on his trail."
                       : Hero.MainHero.Gold < cost ? " You cannot pay the fee." : "");
                return new InquiryElement(h, $"{UI.RoyalFarmaan.NameWithHonorific(h)} — {h.Clan?.Name}",
                    null, !tracked && Hero.MainHero.Gold >= cost, hint);
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Dispatch an Akhbaar Scout",
                "Name the lord, and a harkara takes the road after him. His akhbaar arrives when the runner returns — famous houses are traced quickly, foreign lords cost half again.",
                elements, true, 1, 1, "Dispatch", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Hero h) TYTLog.Guard("Akhbaar.Dispatch", () => Dispatch(h)); },
                _ => { }, "", false), false, false);
        }

        // Lords worth scouting: heads of houses and lords in the field, of any realm but the
        // player's own clan. Own-realm lords first (the cheap, useful case), then by realm.
        private static List<Hero> Candidates()
        {
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            return Hero.AllAliveHeroes
                .Where(h => h != Hero.MainHero && !h.IsChild
                            && h.Clan != null && h.Clan != Clan.PlayerClan && h.Clan.Kingdom != null
                            && (h.Clan.Leader == h || h.PartyBelongedTo?.LeaderHero == h))
                .OrderByDescending(h => h.Clan.Kingdom == mine)
                .ThenBy(h => h.Clan.Kingdom.Name.ToString())
                .ThenBy(h => h.Name.ToString())
                .ToList();
        }

        private static int CostFor(Hero h)
            => AkhbaarMath.DispatchCost(h.Clan?.Tier ?? 0, h.Clan?.Kingdom == Clan.PlayerClan?.Kingdom);

        private bool IsTracked(Hero h) => _scouts.Any(s => s.TargetId == h.StringId);

        private void Dispatch(Hero h)
        {
            if (h == null || IsTracked(h)) return;
            int cost = CostFor(h);
            if (Hero.MainHero.Gold < cost) return;

            Settlement here = Settlement.CurrentSettlement;
            float distance = here != null ? here.GetPosition2D.Distance(TargetPosition(h)) : 0f;
            float days = AkhbaarMath.DaysToLocate(distance);

            Hero.MainHero.ChangeHeroGold(-cost);
            _scouts.Add(new Scout
            {
                TargetId = h.StringId,
                TargetName = h.Name.ToString(),
                ArriveDay = (float)CampaignTime.Now.ToDays + days,
                Origin = here?.Name.ToString() ?? "",
            });
            Notify($"Your harkara slips out after {h.Name} ({cost} dinars). Expect his akhbaar in some {(int)Math.Ceiling(days)} days.", false);
            TYTLog.Info($"Akhbaar: scout dispatched after {h.StringId} from {here?.StringId}, {cost} dinars, ~{days:0.0} days.");
        }

        private static Vec2 TargetPosition(Hero h)
        {
            if (h.PartyBelongedTo != null) return h.PartyBelongedTo.GetPosition2D;
            if (h.CurrentSettlement != null) return h.CurrentSettlement.GetPosition2D;
            return h.HomeSettlement?.GetPosition2D ?? Vec2.Zero;
        }

        // ── The road home ────────────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (_scouts.Count == 0) return;
            float now = (float)CampaignTime.Now.ToDays;
            foreach (Scout s in _scouts.Where(s => now >= s.ArriveDay).ToList())
            {
                _scouts.Remove(s);
                DeliverReport(s);
            }
        }

        private static void DeliverReport(Scout s)
        {
            Hero h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == s.TargetId);
            string body = h == null
                ? $"Your scout returns with word already on every road: {s.TargetName} is dead. Whatever business you had with him passes to his heirs."
                : BuildReport(h);

            UI.RoyalFarmaan.Issue("Akhbaar: " + s.TargetName,
                "By the hand of your harkara, come off the road" + (string.IsNullOrEmpty(s.Origin) ? "" : " to " + s.Origin),
                body,
                seal: "Set down in the akhbaar registers, " + UI.RoyalFarmaan.CurrentDate(),
                primary: "Noted",
                dedupeKey: "akhbaar_" + s.TargetId);
            TYTLog.Info($"Akhbaar: report delivered for {s.TargetId}.");
        }

        private static string BuildReport(Hero h)
        {
            string name = UI.RoyalFarmaan.NameWithHonorific(h);

            if (h.IsPrisoner)
            {
                string where = h.CurrentSettlement != null
                    ? $"in the dungeons of {h.CurrentSettlement.Name}"
                    : h.PartyBelongedToAsPrisoner?.LeaderHero != null
                        ? $"in the train of {h.PartyBelongedToAsPrisoner.LeaderHero.Name}"
                        : "in unknown hands";
                return $"{name} is a CAPTIVE, held {where}. He commands nothing while he sits in chains.";
            }

            MobileParty party = h.PartyBelongedTo;
            if (party == null || party.LeaderHero != h)
            {
                return h.CurrentSettlement != null
                    ? $"{name} keeps to {h.CurrentSettlement.Name}, with no war band under his banner. If you would find him, find him at court."
                    : $"{name} has gone to ground; no war band rides under his banner. The last rumour places him near {h.HomeSettlement?.Name.ToString() ?? "his home estates"}.";
            }

            // Count and composition, in hearsay terms.
            int foot = 0, bows = 0, horse = 0;
            var roster = party.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                var e = roster.GetElementCopyAtIndex(i);
                if (e.Character == null || e.Number <= 0) continue;
                if (e.Character.IsMounted) horse += e.Number;
                else if (e.Character.IsRanged) bows += e.Number;
                else foot += e.Number;
            }
            int total = roster.TotalManCount;
            string strength = total >= 10
                ? $"{AkhbaarMath.StrengthWord(total)} — some {AkhbaarMath.RoughCount(total)} men, {AkhbaarMath.CompositionLine(foot, bows, horse)}"
                : $"{AkhbaarMath.StrengthWord(total)} of {total} men";

            string doing = party.SiegeEvent?.BesiegedSettlement != null
                ? $"encamped before the walls of {party.SiegeEvent.BesiegedSettlement.Name}, pressing a siege"
                : party.CurrentSettlement != null
                    ? $"quartered within the walls of {party.CurrentSettlement.Name}"
                    : party.MapEvent != null
                        ? $"under arms in a battle near {NearestHolding(party)}"
                        : party.Army != null
                            ? $"on the march near {NearestHolding(party)}, riding with the army of {party.Army.LeaderParty?.LeaderHero?.Name}"
                            : $"on the march near {NearestHolding(party)}";

            return $"Your scout found {name} {doing}.\n \nHe rides with {strength}.";
        }

        private static string NearestHolding(MobileParty party)
        {
            Vec2 pos = party.GetPosition2D;
            Settlement best = null; float bestD = float.MaxValue;
            foreach (Settlement s in Settlement.All)
            {
                if (!s.IsTown && !s.IsCastle) continue;
                float d = pos.Distance(s.GetPosition2D);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best?.Name.ToString() ?? "no place your scout could name";
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Console-cheat surface (AkhbaarCheats) ────────────────────────────────────
        public string DescribeStatus()
        {
            if (_scouts.Count == 0) return "No scouts on the road.";
            float now = (float)CampaignTime.Now.ToDays;
            return string.Join("\n", _scouts.Select(s =>
                $"{s.TargetName} ({s.TargetId}) — akhbaar in {Math.Max(0f, s.ArriveDay - now):0.0} days (from {s.Origin})"));
        }

        public string ForceArrive()
        {
            if (_scouts.Count == 0) return "No scouts on the road.";
            var due = _scouts.ToList();
            _scouts.Clear();
            foreach (Scout s in due) DeliverReport(s);
            return $"{due.Count} akhbaar(s) forced to arrive.";
        }

        public string DebugDispatch(string namePart)
        {
            Hero h = Candidates().FirstOrDefault(x =>
                x.Name.ToString().IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0);
            if (h == null) return $"No scoutable lord matches '{namePart}'.";
            if (IsTracked(h)) return $"A scout is already after {h.Name}.";
            _scouts.Add(new Scout
            {
                TargetId = h.StringId, TargetName = h.Name.ToString(),
                ArriveDay = (float)CampaignTime.Now.ToDays, Origin = "the console",
            });
            return $"Free scout dispatched after {h.Name}; akhbaar arrives on the next daily tick.";
        }
    }
}
