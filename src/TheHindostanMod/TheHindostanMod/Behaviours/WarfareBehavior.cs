using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;

namespace TakhtyaTaboot
{
    // Makes wars MEAN something. Every war the player's realm fights carries a war goal
    // and a running war score (battles won, forts taken). Peace is no blank handshake —
    // the victor dictates terms: a province ceded, a nazrana indemnity, or tributary
    // submission. War wearies the realm over time. And the fighting is personal: victories
    // earn mansab, captured cities may be sacked or spared, captured lords ransomed or held,
    // and the sovereign may call the realm's banners to the field.
    public class WarfareBehavior : CampaignBehaviorBase
    {
        public enum WarGoal { Conquest = 0, Tribute = 1, Humble = 2, Defense = 3 }
        private enum Term { WhitePeace, DemandNazrana, DemandProvince, MakeTributary, OfferNazrana }

        public static WarfareBehavior Instance { get; private set; }

        private const int DecisiveScore = 30;
        private const int BannerCooldownDays = 14;

        // Per-war state, keyed by the OTHER kingdom's id (the player's realm is implicit).
        private Dictionary<string, int> _goal = new Dictionary<string, int>();
        private Dictionary<string, int> _score = new Dictionary<string, int>();
        private Dictionary<string, float> _weary = new Dictionary<string, float>();
        // Tributaries the player's realm has imposed: tributaryKingdomId -> day tribute ends.
        private Dictionary<string, int> _tributaryUntil = new Dictionary<string, int>();

        private int _lastBannerDay = -100;
        private bool _ready;
        private bool _applyingTerms;
        private readonly HashSet<string> _peaceUrged = new HashSet<string>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.OnPlayerBattleEndEvent.AddNonSerializedListener(this, OnPlayerBattleEnd);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnPrisonerTaken);
        }

        private static Kingdom PK => Hero.MainHero?.Clan?.Kingdom;
        private static Kingdom Find(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);
        private static bool IsRuler => PK != null && PK.Leader == Hero.MainHero;

        // ── War goals ────────────────────────────────────────────────────────────────
        private void OnWarDeclared(IFaction f1, IFaction f2, DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom pk = PK;
            if (pk == null) return;
            if (f1 != pk && f2 != pk) return;
            IFaction other = f1 == pk ? f2 : f1;
            if (!(other is Kingdom ok)) return;

            _score[ok.StringId] = 0;
            _weary[ok.StringId] = 0f;
            _peaceUrged.Remove(ok.StringId);

            if (!_ready) { _goal[ok.StringId] = (int)WarGoal.Defense; return; }

            if (IsRuler) OfferGoalChoice(ok);
            else { _goal[ok.StringId] = (int)WarGoal.Defense; AnnounceGoal(ok, WarGoal.Defense); }
        }

        private void OfferGoalChoice(Kingdom ok)
        {
            var elements = new List<InquiryElement>
            {
                new InquiryElement(WarGoal.Conquest, "Conquest — seize their provinces", null, true, "Aim to take and keep enemy lands."),
                new InquiryElement(WarGoal.Tribute, "Tribute — bleed them for gold", null, true, "Aim for an indemnity or tributary submission."),
                new InquiryElement(WarGoal.Humble, "Chastisement — humble their pride", null, true, "A punitive war for standing, not land."),
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"War Aim against {ok.Name}",
                $"Your realm goes to war with {ok.Name}. What is your aim? It will shape the terms you seek at its end.",
                elements, true, 1, 1, "Set the aim", "Decide later",
                sel => { WarGoal g = sel != null && sel.Count > 0 && sel[0].Identifier is WarGoal wg ? wg : WarGoal.Conquest;
                         _goal[ok.StringId] = (int)g; AnnounceGoal(ok, g); },
                _ => { _goal[ok.StringId] = (int)WarGoal.Conquest; }, "", false), false, false);
        }

        private void AnnounceGoal(Kingdom ok, WarGoal g)
        {
            string aim = g == WarGoal.Conquest ? "to seize their provinces"
                : g == WarGoal.Tribute ? "to wring tribute from them"
                : g == WarGoal.Humble ? "to humble their pride"
                : "to defend the realm";
            RoyalFarmaan.FromRuler(PK, "The Aim of the War",
                $"The war with {ok.Name} is prosecuted {aim}. Press the enemy in battle and at their walls; " +
                "the more decisive your victories, the harsher the peace you may dictate.", "It shall be done");
        }

        private WarGoal GoalOf(string id) => _goal.TryGetValue(id, out int g) ? (WarGoal)g : WarGoal.Conquest;
        private int ScoreOf(string id) => _score.TryGetValue(id, out int s) ? s : 0;
        private void AddScore(string id, int delta) { if (id != null) _score[id] = ScoreOf(id) + delta; }

        // ── War score ────────────────────────────────────────────────────────────────
        private void OnPlayerBattleEnd(MapEvent mapEvent)
        {
            if (mapEvent == null) return;
            Kingdom pk = PK;
            IFaction self = (IFaction)pk ?? Clan.PlayerClan;
            IFaction att = mapEvent.AttackerSide?.MapFaction;
            IFaction def = mapEvent.DefenderSide?.MapFaction;
            IFaction opp = att == self ? def : (def == self ? att : null);

            bool win = mapEvent.HasWinner && mapEvent.WinningSide == mapEvent.PlayerSide;
            bool siege = mapEvent.MapEventSettlement != null;

            // War score for the realm's war.
            if (pk != null && opp is Kingdom ok && _score.ContainsKey(ok.StringId))
                AddScore(ok.StringId, (win ? 1 : -1) * (siege ? 12 : 6));

            // Battlefield valour toward the next mansab — earned against real foes, not brigands.
            // Capturing or routing the enemy sovereign on the field is worth a great deal.
            if (win && pk != null && opp is Kingdom enemy)
            {
                float gain = Config.Tune.ValourPerWin * (siege ? Config.Tune.ValourSiegeMultiplier : 1f);
                if (RoutedOrCapturedKing(mapEvent, enemy))
                {
                    gain += Config.Tune.ValourKingCapture;
                    Notify($"You have broken {enemy.Leader?.Name} on the field — a deed that will be sung of. Your valour soars.", false);
                }
                MansabdariBehavior.Instance?.AddValour(Clan.PlayerClan, gain);
            }
        }

        // True if the enemy realm's sovereign was on the losing side of this battle
        // (captured or routed), used to award the great valour bonus.
        private static bool RoutedOrCapturedKing(MapEvent e, Kingdom enemy)
        {
            if (e == null || enemy?.Leader == null) return false;
            BattleSideEnum losing = e.WinningSide == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker;
            MapEventSide side = e.GetMapEventSide(losing);
            if (side?.Parties == null) return false;
            foreach (MapEventParty p in side.Parties)
                if (p?.Party?.LeaderHero == enemy.Leader) return true;
            return false;
        }

        private void OnOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturer,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (s == null || !(s.IsTown || s.IsCastle)) return;
            Kingdom pk = PK;
            Kingdom newK = newOwner?.Clan?.Kingdom;
            Kingdom oldK = oldOwner?.Clan?.Kingdom;

            // War score from provinces changing hands between the warring realms.
            if (pk != null)
            {
                if (newK == pk && oldK != null && _score.ContainsKey(oldK.StringId)) AddScore(oldK.StringId, 20);
                else if (oldK == pk && newK != null && _score.ContainsKey(newK.StringId)) AddScore(newK.StringId, -20);
            }

            // Spoils of war — the player takes a city by storm.
            if (newOwner == Hero.MainHero && detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
                OfferSpoils(s);
        }

        // ── Spoils: sack or show mercy, then judge the notables ──────────────────────
        private enum Fate { Pardon, Penalize, Banish, Execute }

        private void OfferSpoils(Settlement s)
        {
            RoyalFarmaan.Issue("The City Is Taken", $"{s.Name} falls to your arms",
                $"{s.Name} is yours by storm. Your soldiers look to you: do you give the city over to plunder, " +
                "or show the mercy that wins a people's hearts?",
                seal: "By right of conquest",
                primary: "Sack the city", onPrimary: () => { Sack(s); JudgeNotables(s); },
                secondary: "Show mercy", onSecondary: () => { Mercy(s); JudgeNotables(s); });
        }

        // Sit in judgement over the conquered city's notables, one by one. Each fate is a
        // trade-off between the spoils/standing you gain and the unrest and enmity you sow.
        private void JudgeNotables(Settlement s)
        {
            if (s?.Notables == null) return;
            var queue = new Queue<Hero>(s.Notables.Where(n => n != null && n.IsAlive)
                .OrderByDescending(n => n.Power).Take(6));
            PromptNextNotable(s, queue);
        }

        private void PromptNextNotable(Settlement s, Queue<Hero> queue)
        {
            while (queue.Count > 0)
            {
                Hero n = queue.Dequeue();
                if (n == null || !n.IsAlive) continue;
                int rel = CharacterRelationManager.GetHeroRelation(Hero.MainHero, n);
                string role = FeudalTitlesBehavior.NotableRole(n);
                var elements = new List<InquiryElement>
                {
                    new InquiryElement(Fate.Pardon, "Pardon him", null, true, "Win his goodwill; the city settles. Relation rises."),
                    new InquiryElement(Fate.Penalize, "Fine him", null, true, "Seize his gold; he resents it. Relation falls, slight unrest."),
                    new InquiryElement(Fate.Banish, "Banish him", null, true, "Strip his standing and cast him out. Real unrest and lasting enmity."),
                    new InquiryElement(Fate.Execute, "Execute him", null, true, "Put him to death. Severe unrest; his kin and peers will not forget."),
                };
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"The Fate of {n.Name}",
                    $"{n.Name}, {role} of {s.Name} (your relation {rel}). As conqueror, decree his fate.",
                    elements, true, 1, 1, "Decree it", "Pardon the rest",
                    sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Fate f) ApplyFate(s, n, f);
                             PromptNextNotable(s, queue); },
                    _ => { while (queue.Count > 0) { Hero r = queue.Dequeue(); if (r != null && r.IsAlive) ApplyFate(s, r, Fate.Pardon); }
                           ApplyFate(s, n, Fate.Pardon); },
                    "", false), false, false);
                return; // the rest continue from the callback
            }
        }

        private void ApplyFate(Settlement s, Hero n, Fate f)
        {
            Town town = s.Town;
            switch (f)
            {
                case Fate.Pardon:
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, 10);
                    if (town != null) town.Loyalty = MathF.Min(100f, town.Loyalty + 3f);
                    break;

                case Fate.Penalize:
                    int fine = 500 + (int)n.Power * 2;
                    int take = Math.Min(fine, Math.Max(0, n.Gold));
                    if (take > 0) GiveGoldAction.ApplyBetweenCharacters(n, Hero.MainHero, take, true);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, -10);
                    if (town != null) town.Loyalty = MathF.Max(0f, town.Loyalty - 3f);
                    AddPressure(s, 5f);
                    break;

                case Fate.Banish:
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, -20);
                    try { n.AddPower(-n.Power * 0.85f); } catch { }
                    if (town != null) { town.Loyalty = MathF.Max(0f, town.Loyalty - 8f); town.Prosperity = MathF.Max(0f, town.Prosperity - 300f); }
                    AddPressure(s, 12f);
                    AffectKinAndPeers(s, n, -12, -4);
                    Notify($"{n.Name} is stripped of standing and cast out of {s.Name}.", false);
                    break;

                case Fate.Execute:
                    if (town != null) { town.Loyalty = MathF.Max(0f, town.Loyalty - 15f); town.Prosperity = MathF.Max(0f, town.Prosperity - 600f); }
                    AddPressure(s, 25f);
                    AffectKinAndPeers(s, n, -30, -8);
                    if (IsRuler) LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, -4f, "an execution after conquest");
                    try { KillCharacterAction.ApplyByMurder(n, Hero.MainHero); } catch { }
                    Notify($"{n.Name} is put to death. {s.Name} seethes.", false);
                    break;
            }
        }

        private static void AddPressure(Settlement s, float delta)
        {
            var rc = RevoltCascadeBehavior.Instance;
            if (rc != null) rc.SetPressure(s, MathF.Min(100f, rc.GetPressure(s) + delta));
        }

        private static void AffectKinAndPeers(Settlement s, Hero n, int kinDelta, int peerDelta)
        {
            if (n.Clan?.Leader != null && n.Clan.Leader != n)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n.Clan.Leader, kinDelta);
            if (s.Notables != null)
                foreach (Hero other in s.Notables.Where(o => o != null && o.IsAlive && o != n))
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, other, peerDelta);
        }

        private void Sack(Settlement s)
        {
            int loot = 2000;
            if (s.Town != null) loot += (int)s.Town.Prosperity;
            Hero.MainHero.ChangeHeroGold(loot);
            if (s.Town != null) s.Town.Prosperity = MathF.Max(0f, s.Town.Prosperity - 1500f);
            RevoltCascadeBehavior.Instance?.SetPressure(s, Math.Min(100f, (RevoltCascadeBehavior.Instance.GetPressure(s)) + 30f));
            if (IsRuler) LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, -5f, "the sack of a city");
            Notify($"Your men sack {s.Name}. You take {loot} dinars, but the city seethes with hatred.", false);
        }

        private void Mercy(Settlement s)
        {
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, 15f);
            RevoltCascadeBehavior.Instance?.SetPressure(s, MathF.Max(0f, (RevoltCascadeBehavior.Instance?.GetPressure(s) ?? 0f) - 15f));
            if (IsRuler) LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, 6f, "clemency to a conquered city");
            if (s.Notables != null)
                foreach (Hero n in s.Notables.Where(n => n != null && n.IsAlive))
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, 5);
            Notify($"You spare {s.Name}. The people bless your name, and your standing grows.", false);
        }

        // ── Captured lords: ransom or hostage ────────────────────────────────────────
        private void OnPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            if (prisoner == null || !prisoner.IsLord || prisoner.Clan == Clan.PlayerClan) return;
            bool mine = captor != null && (captor.LeaderHero == Hero.MainHero || captor == MobileParty.MainParty?.Party);
            if (!mine) return;
            Kingdom pk = PK;
            Kingdom pkOf = prisoner.Clan?.Kingdom;
            if (pk == null || pkOf == null || !pk.IsAtWarWith(pkOf)) return;

            int ransom = 1000 + (prisoner.Clan != null ? (int)prisoner.Clan.Tier * 1500 : 1500);
            RoyalFarmaan.Issue("A Noble Captive", $"{prisoner.Name} is your prisoner",
                $"{prisoner.Name} of {prisoner.Clan?.Name} has fallen into your hands. Will you ransom him for gold, " +
                "or hold him hostage as leverage over his house?",
                seal: "The fortunes of war",
                primary: $"Ransom for {ransom} dinars", onPrimary: () => Ransom(prisoner, ransom),
                secondary: "Hold as hostage", onSecondary: () => Notify($"You hold {prisoner.Name} hostage. His house will not soon forget it.", false));
        }

        private void Ransom(Hero prisoner, int ransom)
        {
            try
            {
                Hero.MainHero.ChangeHeroGold(ransom);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, prisoner, 5);
                EndCaptivityAction.ApplyByRansom(prisoner, Hero.MainHero);
                Notify($"You ransom {prisoner.Name} for {ransom} dinars. His gratitude is noted.", false);
            }
            catch { Notify("The ransom could not be arranged.", true); }
        }

        // ── War-weariness ────────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter) => AddMenus(starter);

        private void OnDailyTick()
        {
            _ready = true;
            Kingdom pk = PK;
            if (pk == null) return;
            int today = (int)CampaignTime.Now.ToDays;

            foreach (string id in _score.Keys.ToList())
            {
                Kingdom ok = Find(id);
                if (ok == null || !pk.IsAtWarWith(ok)) continue;
                float w = (_weary.TryGetValue(id, out float v) ? v : 0f) + 0.6f + (ScoreOf(id) < 0 ? 0.6f : 0f);
                _weary[id] = w;
                if (w >= 60f && IsRuler && !_peaceUrged.Contains(id))
                {
                    _peaceUrged.Add(id);
                    RoyalFarmaan.Issue("The Realm Wearies of War", $"From the Imperial Council of {pk.Name}",
                        $"The war with {ok.Name} drags on and the realm grows weary. The council urges you to seek terms — " +
                        "press for what advantage your war score allows, or grant peace. Direct the war effort from any town or castle.",
                        seal: null, primary: "I shall consider it");
                }
            }

            // Tributaries pay their nazrana; their bond keeps the peace.
            foreach (string id in _tributaryUntil.Keys.ToList())
            {
                Kingdom trib = Find(id);
                if (trib == null || today >= _tributaryUntil[id]) { _tributaryUntil.Remove(id); continue; }
                if (pk.IsAtWarWith(trib)) MakePeaceAction.Apply(pk, trib);
                if (today % 7 == 0 && trib.Leader != null && pk.Leader != null && trib.Leader.Gold > 500)
                    GiveGoldAction.ApplyBetweenCharacters(trib.Leader, pk.Leader, 500, true);
            }
        }

        // ── Peace negotiation (ruler dictates terms) ─────────────────────────────────
        private void OnMakePeace(IFaction f1, IFaction f2, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom pk = PK;
            if (pk == null) return;
            IFaction other = f1 == pk ? f2 : (f2 == pk ? f1 : null);
            if (!(other is Kingdom ok) || !_score.ContainsKey(ok.StringId)) return;

            // If WE dictated the terms, they are already applied. Otherwise apply a
            // score-based indemnity so even an AI peace settles accounts.
            if (!_applyingTerms)
            {
                int sc = ScoreOf(ok.StringId);
                if (Math.Abs(sc) >= DecisiveScore && pk.Leader != null && ok.Leader != null)
                {
                    Hero winner = sc > 0 ? pk.Leader : ok.Leader;
                    Hero loser = sc > 0 ? ok.Leader : pk.Leader;
                    int amount = Math.Min(Math.Abs(sc) * 80, loser.Gold);
                    if (amount > 0) GiveGoldAction.ApplyBetweenCharacters(loser, winner, amount, true);
                }
            }
            _score.Remove(ok.StringId); _goal.Remove(ok.StringId); _weary.Remove(ok.StringId); _peaceUrged.Remove(ok.StringId);
        }

        private void OpenDirectWar()
        {
            Kingdom pk = PK;
            if (pk == null || !IsRuler) { Notify("Only the sovereign may direct the realm's wars.", true); return; }
            var wars = _score.Keys.Select(Find).Where(k => k != null && pk.IsAtWarWith(k)).ToList();
            if (wars.Count == 0) { Notify("The realm is at peace.", false); return; }

            var elements = wars.Select(k => new InquiryElement(k,
                $"{k.Name} — {GoalOf(k.StringId)}, war score {ScoreOf(k.StringId)}",
                null, true, $"Score {ScoreOf(k.StringId)} ({Standing(ScoreOf(k.StringId))}). Choose to dictate terms.")).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Direct the War Effort", "Choose a war to bring to terms.",
                elements, true, 1, 1, "Negotiate", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Kingdom k) OfferTerms(k); },
                _ => { }, "", false), false, false);
        }

        private void OfferTerms(Kingdom ok)
        {
            int sc = ScoreOf(ok.StringId);
            var elements = new List<InquiryElement> { new InquiryElement(Term.WhitePeace, "White peace (no terms)", null, true, "End the war as it stands.") };
            if (sc >= 8) elements.Add(new InquiryElement(Term.DemandNazrana, "Demand a nazrana indemnity", null, true, "Take gold for peace."));
            if (sc >= DecisiveScore)
            {
                elements.Add(new InquiryElement(Term.DemandProvince, "Demand a province", null, true, "Annex one of their towns or castles."));
                elements.Add(new InquiryElement(Term.MakeTributary, "Make them a tributary", null, true, "They pay weekly nazrana and keep the peace."));
            }
            if (sc <= -8) elements.Add(new InquiryElement(Term.OfferNazrana, "Offer a nazrana for peace", null, true, "Buy your way out of a losing war."));

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Terms with {ok.Name}",
                $"Your war score stands at {sc} ({Standing(sc)}). Dictate the peace:",
                elements, true, 1, 1, "Seal it", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Term t) ApplyTerms(ok, t); },
                _ => { }, "", false), false, false);
        }

        private void ApplyTerms(Kingdom ok, Term t)
        {
            Kingdom pk = PK;
            if (pk == null) return;
            _applyingTerms = true;
            try
            {
                switch (t)
                {
                    case Term.DemandNazrana:
                        TransferGold(ok.Leader, pk.Leader, Math.Min(ScoreOf(ok.StringId) * 100, ok.Leader?.Gold ?? 0));
                        break;
                    case Term.OfferNazrana:
                        TransferGold(pk.Leader, ok.Leader, Math.Min(Math.Abs(ScoreOf(ok.StringId)) * 100, pk.Leader?.Gold ?? 0));
                        break;
                    case Term.DemandProvince:
                        Settlement prize = ok.Settlements.Where(s => s.IsTown).Concat(ok.Settlements.Where(s => s.IsCastle)).FirstOrDefault();
                        if (prize != null) ChangeOwnerOfSettlementAction.ApplyByGift(prize, Hero.MainHero);
                        break;
                    case Term.MakeTributary:
                        _tributaryUntil[ok.StringId] = (int)CampaignTime.Now.ToDays + 365 * 3;
                        break;
                }
                MakePeaceAction.Apply(pk, ok);
                RoyalFarmaan.FromRuler(pk, "Peace Is Dictated",
                    $"Peace is sealed with {ok.Name} on your terms ({Describe(t)}). The war is ended.", "It is done");
            }
            catch { Notify("The terms could not be enforced.", true); }
            finally { _applyingTerms = false; }
            _score.Remove(ok.StringId); _goal.Remove(ok.StringId); _weary.Remove(ok.StringId);
        }

        private static void TransferGold(Hero from, Hero to, int amount)
        {
            if (from != null && to != null && amount > 0) GiveGoldAction.ApplyBetweenCharacters(from, to, amount, true);
        }

        private static string Describe(Term t) => t == Term.DemandNazrana ? "a nazrana indemnity"
            : t == Term.DemandProvince ? "a province ceded" : t == Term.MakeTributary ? "their tributary submission"
            : t == Term.OfferNazrana ? "a nazrana paid for peace" : "white peace";

        private static string Standing(int sc) => sc >= DecisiveScore ? "you are winning decisively"
            : sc >= 8 ? "you hold the advantage" : sc <= -DecisiveScore ? "you are losing badly"
            : sc <= -8 ? "the enemy holds the advantage" : "the war hangs in the balance";

        // ── Call the realm's banners (ruler muster) ──────────────────────────────────
        private void CallBanners()
        {
            Kingdom pk = PK;
            if (pk == null || !IsRuler) { Notify("Only the sovereign calls the realm's banners.", true); return; }
            int today = (int)CampaignTime.Now.ToDays;
            if (today - _lastBannerDay < BannerCooldownDays) { Notify("The banners were lately called; the lords need time to gather.", true); return; }
            if (MobileParty.MainParty == null) return;
            _lastBannerDay = today;

            float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(pk) ?? 60f;
            int capacity = MobileParty.MainParty.Party.PartySizeLimit - MobileParty.MainParty.MemberRoster.TotalManCount;
            int mustered = 0; int loyal = 0, defiant = 0;

            foreach (Clan c in pk.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != Hero.MainHero))
            {
                int rel = CharacterRelationManager.GetHeroRelation(Hero.MainHero, c.Leader);
                bool answers = (rel + auth / 2f + MBRandom.RandomInt(0, 30)) >= 40f;
                if (answers)
                {
                    loyal++;
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Leader, 2);
                    int levy = Math.Min(15 + (int)c.Tier * 5, Math.Max(0, capacity - mustered));
                    CharacterObject troop = (c.Culture ?? pk.Culture)?.BasicTroop;
                    if (levy > 0 && troop != null) { MobileParty.MainParty.MemberRoster.AddToCounts(troop, levy); mustered += levy; }
                }
                else
                {
                    defiant++;
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Leader, -4);
                }
            }

            if (defiant > 0) ImperialAuthorityBehavior.Instance?.ModifyAuthority(pk, -2f, "lords defied the muster");
            RoyalFarmaan.FromRuler(pk, "The Banners Are Called",
                $"You summon the realm to war. {loyal} houses answer and bring {mustered} men to your host; " +
                $"{defiant} hold back, and their defiance is noted against them.", "Let us march");
        }

        // ── Menus ────────────────────────────────────────────────────────────────────
        private void AddMenus(CampaignGameStarter starter)
        {
            foreach (string root in new[] { "town", "castle" })
            {
                starter.AddGameMenuOption(root, "hindostan_directwar_" + root, "{=!}Direct the war effort",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                              return IsRuler && AtWar(); },
                    args => OpenDirectWar(), false, 7);
                starter.AddGameMenuOption(root, "hindostan_callbanners_" + root, "{=!}Call the realm's banners",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Recruit;
                              return IsRuler && AtWar(); },
                    args => CallBanners(), false, 8);
            }
        }

        private static bool AtWar()
        {
            Kingdom pk = PK;
            return pk != null && Kingdom.All.Any(o => o != pk && !o.IsEliminated && pk.IsAtWarWith(o));
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Save / load ──────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var gIds = _goal.Keys.ToList(); var gVals = _goal.Values.ToList();
            var sIds = _score.Keys.ToList(); var sVals = _score.Values.ToList();
            var wIds = _weary.Keys.ToList(); var wVals = _weary.Values.ToList();
            var tIds = _tributaryUntil.Keys.ToList(); var tVals = _tributaryUntil.Values.ToList();
            dataStore.SyncData("hind_war_gIds", ref gIds); dataStore.SyncData("hind_war_gVals", ref gVals);
            dataStore.SyncData("hind_war_sIds", ref sIds); dataStore.SyncData("hind_war_sVals", ref sVals);
            dataStore.SyncData("hind_war_wIds", ref wIds); dataStore.SyncData("hind_war_wVals", ref wVals);
            dataStore.SyncData("hind_war_tIds", ref tIds); dataStore.SyncData("hind_war_tVals", ref tVals);
            dataStore.SyncData("hind_war_lastbanner", ref _lastBannerDay);
            if (!dataStore.IsSaving)
            {
                _goal = Zip(gIds, gVals); _score = Zip(sIds, sVals); _weary = Zip(wIds, wVals); _tributaryUntil = Zip(tIds, tVals);
            }
        }

        private static Dictionary<string, T> Zip<T>(List<string> k, List<T> v)
        {
            var d = new Dictionary<string, T>();
            for (int i = 0; i < k.Count && i < v.Count; i++) d[k[i]] = v[i];
            return d;
        }

        // ── Console ────────────────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("war_status", "hindostan")]
        public static string WarStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom pk = PK;
            if (pk == null) return "You serve no realm.";
            var wars = Instance._score.Keys.Select(Find).Where(k => k != null && pk.IsAtWarWith(k)).ToList();
            float valour = MansabdariBehavior.Instance?.GetValour(Clan.PlayerClan) ?? 0f;
            if (wars.Count == 0) return $"Valour toward next mansab: {valour:0}. The realm is at peace.";
            return $"Valour toward next mansab: {valour:0}\n" + string.Join("\n", wars.Select(k =>
                $"{k.Name}: goal {Instance.GoalOf(k.StringId)}, score {Instance.ScoreOf(k.StringId)} ({Standing(Instance.ScoreOf(k.StringId))})"));
        }
    }
}
