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
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // THE WAKIL — how a house manufactures a claim on a town it has never held (wiki ch.30 §1).
    //
    // You leave a companion behind in a town as your agent. He does not fight; he sits in the bazaar and
    // works the merchant houses, week after week, buying goodwill in your name. His pace is his CHARM and
    // his wit (ClaimMath.AgentWeeklyRelationGain) — a dull agent still gets there, slowly; a brilliant
    // courtier turns a town in a season.
    //
    // When two-thirds of the town's merchants stand at +40 to your house, the town is *yours to claim*:
    // the ledger grants an EXTERNAL claim — shallower than a lifetime of governance (3 years), and
    // PERISHABLE. Act on it within two years — march for it yourself, or petition the crown to take it up
    // as the realm's casus belli — or it lapses and the years of patient bribery are wasted.
    //
    // The AI plays the same game abstractly (AI lords have no companion rosters to spare): a landed house
    // may quietly cultivate a neighbouring town and, in time, earn the same claim — so an AI war of
    // conquest can be backed by a claim it actually built.
    public class WakilBehavior : CampaignBehaviorBase
    {
        public static WakilBehavior Instance { get; private set; }

        private const float AiWeeklyDispatchChance = 0.04f;  // a house begins cultivating a neighbour
        private const float AiWeeklyProgressGain   = 1.5f;   // ...and works at a plain, unhurried pace

        // The player's agents: hero id -> settlement id, with the fractional relation they have banked.
        private List<string> _agentHero    = new List<string>();
        private List<string> _agentSettle  = new List<string>();
        private List<float>  _agentBanked  = new List<float>();   // fractional relation not yet applied
        private List<int>    _agentSince   = new List<int>();

        // The AI's abstract cultivation: clan id -> settlement id, 0..100 toward the same threshold.
        private List<string> _aiClan     = new List<string>();
        private List<string> _aiSettle   = new List<string>();
        private List<float>  _aiProgress = new List<float>();

        private bool _ready;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => { if (_ready) TYTLog.Guard("Wakil.Weekly", OnWeeklyTick); });
        }

        private static int Today => (int)CampaignTime.Now.ToDays;
        private static bool Claimable(Settlement s) => s != null && s.IsTown;   // merchants live in towns

        private static List<Hero> Merchants(Settlement s)
            => s?.Notables?.Where(n => n != null && n.IsAlive && n.IsMerchant).ToList() ?? new List<Hero>();

        // ── Menus ────────────────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _ready = true;

            starter.AddGameMenuOption("town", "hindostan_wakil_leave", "{=!}Leave a companion as our wakil",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Trade;
                    Settlement s = Settlement.CurrentSettlement;
                    if (!Claimable(s) || s.OwnerClan == Clan.PlayerClan) return false;
                    if (AgentIn(s) != null) { args.IsEnabled = false; args.Tooltip = new TaleWorlds.Localization.TextObject("Your wakil is already at work here."); return true; }
                    if (!Companions().Any()) { args.IsEnabled = false; args.Tooltip = new TaleWorlds.Localization.TextObject("You have no companion to spare."); return true; }
                    return true;
                },
                args => OpenWakilChoice(Settlement.CurrentSettlement), false, 6);

            starter.AddGameMenuOption("town", "hindostan_wakil_recall", "{=!}Recall your wakil",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return AgentIn(Settlement.CurrentSettlement) != null;
                },
                args => Recall(Settlement.CurrentSettlement), false, 7);
        }

        private static IEnumerable<Hero> Companions()
            => MobileParty.MainParty?.MemberRoster?.GetTroopRoster()
                   .Where(e => e.Character != null && e.Character.IsHero
                               && e.Character.HeroObject != null
                               && e.Character.HeroObject != Hero.MainHero
                               && e.Character.HeroObject.IsAlive)
                   .Select(e => e.Character.HeroObject)
               ?? Enumerable.Empty<Hero>();

        private void OpenWakilChoice(Settlement s)
        {
            var merchants = Merchants(s);
            if (merchants.Count == 0)
            {
                Notify($"{s.Name} has no merchant houses worth the courting.", true);
                return;
            }
            int needed = ClaimMath.MerchantsNeeded(merchants.Count);

            var elements = Companions().Select(h =>
            {
                int charm = h.GetSkillValue(DefaultSkills.Charm);
                int wit = h.GetAttributeValue(DefaultCharacterAttributes.Intelligence);
                float pace = ClaimMath.AgentWeeklyRelationGain(charm, wit);
                return new InquiryElement(h, $"{h.Name} — charm {charm}, wit {wit} ({pace:0.0}/week)", null, true,
                    $"{h.Name} will remain in {s.Name} and work the merchant houses at about {pace:0.0} points of " +
                    $"goodwill a week. He must bring {needed} of the {merchants.Count} merchants to +{ClaimMath.MerchantThreshold}.");
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"A Wakil in {s.Name}",
                $"Leave a companion behind to court the merchant houses of {s.Name}. When {needed} of its " +
                $"{merchants.Count} merchants stand at +{ClaimMath.MerchantThreshold} to you, your house will hold a " +
                "claim upon the town — good for two years, and no longer.\n\nHe will not march with you while he serves.",
                elements, true, 1, 1, "Leave him here", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Hero h) Station(h, s); },
                _ => { }, "", false), false, false);
        }

        // ── Stationing and recalling (the risky engine surface — every step guarded) ──
        private void Station(Hero agent, Settlement s)
        {
            if (agent == null || !Claimable(s)) return;
            TYTLog.Guard("Wakil.Station", () =>
            {
                MobileParty party = MobileParty.MainParty;
                if (party == null) return;

                // Take him off the roster, then set him down inside the town. If either half fails we must
                // not leave a hero in limbo — belonging to no party and standing nowhere.
                try
                {
                    party.MemberRoster.RemoveTroop(agent.CharacterObject, 1);
                    EnterSettlementAction.ApplyForCharacterOnly(agent, s);
                }
                catch (Exception e)
                {
                    TYTLog.Error("Wakil: could not station the agent; returning him to the party", e);
                    try { if (agent.PartyBelongedTo == null) AddHeroToPartyAction.Apply(agent, party, true); } catch { }
                    Notify("The wakil could not be settled in the town.", true);
                    return;
                }

                _agentHero.Add(agent.StringId); _agentSettle.Add(s.StringId);
                _agentBanked.Add(0f); _agentSince.Add(Today);

                int needed = ClaimMath.MerchantsNeeded(Merchants(s).Count);
                RoyalFarmaan.Issue("A Wakil Is Left Behind", $"In the bazaar of {s.Name}",
                    $"{agent.Name} remains in {s.Name} as your wakil. He will court its merchant houses in your name, " +
                    $"week upon week, until {needed} of them stand at +{ClaimMath.MerchantThreshold} to you — and then " +
                    "the town is yours to claim.\n\nHe does not ride with you while he serves. Recall him from the town menu.",
                    seal: "Patience, and silver", primary: "Let him work");
                TYTLog.Info($"Wakil: {agent.Name} stationed in {s.Name}.");
            });
        }

        private void Recall(Settlement s)
        {
            Hero agent = AgentIn(s);
            if (agent == null) return;
            TYTLog.Guard("Wakil.Recall", () =>
            {
                try
                {
                    if (agent.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(agent);
                    if (MobileParty.MainParty != null && agent.PartyBelongedTo == null)
                        AddHeroToPartyAction.Apply(agent, MobileParty.MainParty, true);
                }
                catch (Exception e) { TYTLog.Error("Wakil: recall failed", e); }

                Forget(agent.StringId);
                Notify($"{agent.Name} leaves the bazaar of {s.Name} and rejoins your host.", false);
            });
        }

        private Hero AgentIn(Settlement s)
        {
            if (s == null) return null;
            for (int i = 0; i < _agentSettle.Count; i++)
                if (_agentSettle[i] == s.StringId)
                {
                    Hero h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == _agentHero[i]);
                    if (h != null) return h;
                }
            return null;
        }

        // ── The weekly courting ──────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            WorkThePlayersAgents();
            WorkTheAiAgents();
        }

        private void WorkThePlayersAgents()
        {
            for (int i = _agentHero.Count - 1; i >= 0; i--)
            {
                Hero agent = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _agentHero[i]);
                Settlement s = Settlement.All.FirstOrDefault(x => x.StringId == _agentSettle[i]);

                // A dead agent, a lost town, a town we have since taken — the posting is over.
                if (agent == null || s == null || !Claimable(s))
                { RemoveAgentAt(i); continue; }
                if (s.OwnerClan == Clan.PlayerClan)
                {
                    Notify($"{s.Name} is ours — {agent.Name}'s work in its bazaar is done.", false);
                    Recall(s);
                    continue;
                }

                var merchants = Merchants(s);
                if (merchants.Count == 0) { RemoveAgentAt(i); continue; }

                // Bank the week's fractional goodwill; spend it in whole points, on the merchants who are
                // furthest from being won (a wakil works the room, not his favourites).
                float gain = ClaimMath.AgentWeeklyRelationGain(
                    agent.GetSkillValue(DefaultSkills.Charm),
                    agent.GetAttributeValue(DefaultCharacterAttributes.Intelligence));
                _agentBanked[i] += gain;

                int points = (int)_agentBanked[i];
                if (points > 0)
                {
                    _agentBanked[i] -= points;
                    foreach (Hero m in merchants
                        .Where(m => CharacterRelationManager.GetHeroRelation(Hero.MainHero, m) < ClaimMath.MerchantThreshold)
                        .OrderBy(m => CharacterRelationManager.GetHeroRelation(Hero.MainHero, m))
                        .Take(Math.Max(1, points)))
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, m, 1, false);
                }

                // Is the town turned?
                int won = merchants.Count(m => CharacterRelationManager.GetHeroRelation(Hero.MainHero, m) >= ClaimMath.MerchantThreshold);
                if (ClaimMath.ClaimEarned(won, merchants.Count))
                {
                    ClaimsBehavior.Instance?.GrantExternalClaim(Clan.PlayerClan, s);
                    RoyalFarmaan.Issue("The Bazaar Is Ours", $"From {agent.Name}, your wakil in {s.Name}",
                        $"{won} of the {merchants.Count} merchant houses of {s.Name} now name you friend. The town is " +
                        "yours to claim.\n\nPress the claim within two years — march for it, or petition the crown to " +
                        "take it up as the realm's cause — or the years of patient silver are wasted.",
                        seal: "Silver buys what steel cannot", primary: "The claim is entered");
                    Recall(s);
                }
            }
        }

        // The AI plays the same game, abstractly: a landed house quietly cultivates a neighbouring town it
        // does not hold, and in time earns the same perishable claim. This is what lets an AI war of
        // conquest be backed by a claim the AI actually built, rather than one it merely inherited.
        private void WorkTheAiAgents()
        {
            // Existing cultivations progress.
            for (int i = _aiClan.Count - 1; i >= 0; i--)
            {
                Clan c = Clan.All.FirstOrDefault(x => x.StringId == _aiClan[i]);
                Settlement s = Settlement.All.FirstOrDefault(x => x.StringId == _aiSettle[i]);
                if (c == null || c.IsEliminated || c.Leader == null || s == null || !Claimable(s)
                    || s.OwnerClan == c || s.OwnerClan?.Kingdom == c.Kingdom)
                { RemoveAiAt(i); continue; }

                _aiProgress[i] += AiWeeklyProgressGain
                                  * ClaimMath.AgentWeeklyRelationGain(c.Leader.GetSkillValue(DefaultSkills.Charm),
                                                                     c.Leader.GetAttributeValue(DefaultCharacterAttributes.Intelligence));
                if (_aiProgress[i] >= 100f)
                {
                    ClaimsBehavior.Instance?.GrantExternalClaim(c, s);
                    TYTLog.Info($"Wakil (AI): {c.Name} has cultivated {s.Name} and holds a claim on it.");
                    RemoveAiAt(i);
                }
            }

            // ...and new ones begin, now and then.
            if (MBRandom.RandomFloat > AiWeeklyDispatchChance) return;

            var houses = Clan.All.Where(c => c != null && !c.IsEliminated && c.Leader != null
                                             && c != Clan.PlayerClan && c.Kingdom != null
                                             && !ThroneWar.IsRebelKingdom(c.Kingdom)
                                             && c.Settlements.Any()
                                             && !_aiClan.Contains(c.StringId)).ToList();
            if (houses.Count == 0) return;
            Clan house = houses[MBRandom.RandomInt(houses.Count)];

            Settlement seat = house.Settlements.FirstOrDefault();
            if (seat == null) return;

            // A neighbouring town of another realm — the nearer, the likelier.
            Settlement mark = Settlement.All
                .Where(s => Claimable(s) && s.OwnerClan?.Kingdom != null && s.OwnerClan.Kingdom != house.Kingdom
                            && !ThroneWar.IsRebelKingdom(s.OwnerClan.Kingdom))
                .OrderBy(s => seat.GetPosition2D.Distance(s.GetPosition2D))
                .Take(4)
                .OrderBy(_ => MBRandom.RandomFloat)
                .FirstOrDefault();
            if (mark == null) return;

            _aiClan.Add(house.StringId); _aiSettle.Add(mark.StringId); _aiProgress.Add(0f);
        }

        // ── State ────────────────────────────────────────────────────────────────────
        private void Forget(string heroId)
        {
            int i = _agentHero.IndexOf(heroId);
            if (i >= 0) RemoveAgentAt(i);
        }

        private void RemoveAgentAt(int i)
        {
            _agentHero.RemoveAt(i); _agentSettle.RemoveAt(i);
            _agentBanked.RemoveAt(i); _agentSince.RemoveAt(i);
        }

        private void RemoveAiAt(int i)
        {
            _aiClan.RemoveAt(i); _aiSettle.RemoveAt(i); _aiProgress.RemoveAt(i);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_wakil_hero",   ref _agentHero);
            dataStore.SyncData("hind_wakil_settle", ref _agentSettle);
            dataStore.SyncData("hind_wakil_banked", ref _agentBanked);
            dataStore.SyncData("hind_wakil_since",  ref _agentSince);
            dataStore.SyncData("hind_wakil_aiclan", ref _aiClan);
            dataStore.SyncData("hind_wakil_aisettle", ref _aiSettle);
            dataStore.SyncData("hind_wakil_aiprog", ref _aiProgress);

            if (!dataStore.IsSaving)
            {
                _agentHero ??= new List<string>(); _agentSettle ??= new List<string>();
                _agentBanked ??= new List<float>(); _agentSince ??= new List<int>();
                _aiClan ??= new List<string>(); _aiSettle ??= new List<string>(); _aiProgress ??= new List<float>();
            }
        }

        // ── Console ──────────────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("wakils", "hindostan")]
        public static string Wakils(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            var sb = new System.Text.StringBuilder();

            if (Instance._agentHero.Count == 0) sb.AppendLine("You have no wakil at work.");
            for (int i = 0; i < Instance._agentHero.Count; i++)
            {
                Hero h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == Instance._agentHero[i]);
                Settlement s = Settlement.All.FirstOrDefault(x => x.StringId == Instance._agentSettle[i]);
                if (h == null || s == null) continue;
                var m = Merchants(s);
                int won = m.Count(x => CharacterRelationManager.GetHeroRelation(Hero.MainHero, x) >= ClaimMath.MerchantThreshold);
                sb.AppendLine($"{h.Name} in {s.Name}: {won}/{m.Count} merchants won " +
                              $"(needs {ClaimMath.MerchantsNeeded(m.Count)}), {Today - Instance._agentSince[i]}d at work.");
            }

            sb.AppendLine($"\nAI cultivations in progress: {Instance._aiClan.Count}");
            for (int i = 0; i < Instance._aiClan.Count; i++)
                sb.AppendLine($"  {Clan.All.FirstOrDefault(c => c.StringId == Instance._aiClan[i])?.Name} -> " +
                              $"{Settlement.All.FirstOrDefault(s => s.StringId == Instance._aiSettle[i])?.Name}: {Instance._aiProgress[i]:0}%");
            return sb.ToString();
        }
    }
}
