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
using TakhtyaTaboot.UI;

namespace TakhtyaTaboot
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
        // heroId -> liege heroId: an explicit feudal bond (e.g. a new vassal placed under a
        // castle lord on joining a realm), overriding the default bound-lord/sovereign rule.
        private Dictionary<string, string> _liegeOverride = new Dictionary<string, string>();
        private int _lastSummonDay = -100;
        private const int SummonCooldownDays = 21;
        private const int LiegeChainDepthCap = 16; // override chains are short; anything deeper is a bug

        // ── Derived state (never serialized; rebuilt from _zamindar on load/launch) ──
        // Reverse index heroId -> villageIds and a settlement lookup, because the naive
        // forms (Values.Contains, Settlement.All.FirstOrDefault per village) are O(n)/O(n·m)
        // and sit inside the hierarchy screen's recursion and the weekly assignment pass.
        private readonly Dictionary<string, List<string>> _villagesOf = new Dictionary<string, List<string>>();
        private Dictionary<string, Settlement> _settlementById;

        private Settlement SettlementById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_settlementById == null)
            {
                _settlementById = new Dictionary<string, Settlement>();
                foreach (Settlement s in Settlement.All)
                    _settlementById[s.StringId] = s;
            }
            return _settlementById.TryGetValue(id, out Settlement found) ? found : null;
        }

        private void RebuildIndexes()
        {
            _villagesOf.Clear();
            foreach (var kv in _zamindar)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (!_villagesOf.TryGetValue(kv.Value, out List<string> list))
                    _villagesOf[kv.Value] = list = new List<string>();
                list.Add(kv.Key);
            }
        }

        // ALL writes to _zamindar go through these two, so the reverse index never drifts.
        private void SetZamindarEntry(string villageId, string heroId)
        {
            if (string.IsNullOrEmpty(villageId) || string.IsNullOrEmpty(heroId)) return;
            RemoveZamindarEntry(villageId);
            _zamindar[villageId] = heroId;
            if (!_villagesOf.TryGetValue(heroId, out List<string> list))
                _villagesOf[heroId] = list = new List<string>();
            if (!list.Contains(villageId)) list.Add(villageId);
        }

        private void RemoveZamindarEntry(string villageId)
        {
            if (string.IsNullOrEmpty(villageId)) return;
            if (!_zamindar.TryGetValue(villageId, out string prev)) return;
            _zamindar.Remove(villageId);
            if (!string.IsNullOrEmpty(prev) && _villagesOf.TryGetValue(prev, out List<string> list))
            {
                list.Remove(villageId);
                if (list.Count == 0) _villagesOf.Remove(prev);
            }
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            // No OnNewGameCreated assignment: AssignAllVillages walks Settlement.All and notables
            // while the engine is still creating them on parallel threads (see Util/WorldGen.cs);
            // OnSessionLaunched runs it safely a moment later.
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("FeudalTitles.WeeklyTick", OnWeeklyTick));
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            // Pruning hooks: without these, _zamindar/_liegeOverride entries for destroyed
            // clans/kingdoms and conquered seats accumulated forever (and were re-serialized
            // into every save), while reads papered over them lazily.
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
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
            => hero != null && _villagesOf.ContainsKey(hero.StringId);

        // Directly seat a hero (incl. the player) as a village's zamindar — used by the
        // career-progression "claim your first fief" path. Auto-fill won't displace a
        // living assigned lord, so a player zamindar persists.
        //
        // Returns TRUE only if the seat actually took. Callers that charge influence or
        // announce a grant MUST check this — the old void version silently no-oped on the
        // mercenary guard while ClaimFief still spent influence and proclaimed success.
        public bool AssignZamindar(Settlement village, Hero hero)
        {
            if (village == null || !village.IsVillage || hero == null) return false;
            // Mercenaries hold no land — they serve for pay, not for fiefs.
            if (hero == Hero.MainHero && (Clan.PlayerClan?.IsUnderMercenaryService ?? false)) return false;
            // A LORD must hold the rank for a village (Mansabdar-e-Sad or better); local
            // notables sit outside the mansab ladder and are exempt.
            if (hero.IsLord && hero.Clan != null
                && MansabdariBehavior.Instance?.CanHold(hero.Clan, village) == false) return false;

            SetZamindarEntry(village.StringId, hero.StringId);

            // The PLAYER's zamindari is a real village fief: it then shows among the clan's
            // holdings (clan management screen), names the player as the village's owner in
            // the encyclopedia, and can be administered and developed through the fief menu.
            // AI zamindars remain a stored layer by default (engine ownership for AI distorts
            // fief votes/income and is reverted by conquest of the bound seat); the
            // AiZamindarEngineOwnership tunable exists to experiment with the alternative.
            bool wantsEngineOwnership = hero == Hero.MainHero
                || (Config.Tune.AiZamindarEngineOwnership && hero.IsLord && hero.Clan != null);
            if (wantsEngineOwnership && village.OwnerClan != hero.Clan)
            {
                try
                {
                    ChangeOwnerOfSettlementAction.ApplyByGift(village, hero);
                    if (village.OwnerClan == hero.Clan)
                        Util.TYTLog.Info($"AssignZamindar: {village.Name} is now a real village fief of {hero.Name}.");
                    else
                        Util.TYTLog.Warn($"AssignZamindar: gift of {village.Name} did not transfer ownership " +
                                         $"(still {village.OwnerClan?.Name?.ToString() ?? "unowned"}). " +
                                         "The holder can still oversee it via the feudal layer.");
                }
                catch (Exception e) { Util.TYTLog.Error($"AssignZamindar: ApplyByGift({village.Name}) threw", e); }
            }
            return true;
        }

        // Ordered by StringId so every "which village comes first" decision (liege seat,
        // bound realm) is deterministic rather than at the mercy of dictionary order.
        public List<Settlement> GetVillagesLordedBy(Hero hero)
        {
            var result = new List<Settlement>();
            if (hero == null || !_villagesOf.TryGetValue(hero.StringId, out List<string> ids)) return result;
            foreach (string id in ids.OrderBy(x => x, StringComparer.Ordinal))
            {
                Settlement s = SettlementById(id);
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
                // Engine ownership counts too: a clan that really owns a village (cheat /
                // GrantFief path) is a zamindar even if no dict entry was ever written —
                // otherwise the display said "Unlanded Noble" while the tax code charged
                // for the village.
                if (hero == hero.Clan.Leader && hero.Clan.Settlements.Any(s => s.IsVillage)) return "Village Zamindar";
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

        // An explicit feudal bond placed on a hero (a new vassal under a castle lord),
        // or null. Validated: the liege must be alive, landed, and of the same realm.
        public Hero GetLiegeOverride(Hero hero)
        {
            if (hero == null || !_liegeOverride.TryGetValue(hero.StringId, out string id) || string.IsNullOrEmpty(id))
                return null;
            Hero liege = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
            if (liege == null || !liege.IsAlive || liege == hero) return null;
            if (liege.Clan?.Kingdom == null || liege.Clan.Kingdom != hero.Clan?.Kingdom) return null;
            if (!HoldsTownOrCastle(liege)) return null;
            return liege;
        }

        // Returns false when the bond would close a cycle (A answers to B answers to A):
        // a cycled pair is unreachable from the sovereign in the hierarchy tree's top-down
        // walk, so both nobles simply VANISHED from the hierarchy screen.
        public bool SetLiege(Hero hero, Hero liege)
        {
            if (hero == null) return false;
            if (liege == null) { _liegeOverride.Remove(hero.StringId); return true; }
            if (liege == hero) return false;
            if (ChainReaches(liege, hero))
            {
                Util.TYTLog.Warn($"SetLiege: refused {hero.Name} -> {liege.Name}; it would close a liege cycle.");
                return false;
            }
            _liegeOverride[hero.StringId] = liege.StringId;
            return true;
        }

        // Walk the OVERRIDE chain upward from 'from'; true if it reaches 'target'.
        // Raw storage on purpose (not GetLiegeOverride): a half-invalid stored entry must
        // still count for cycle detection or the reconcile pass could resurrect a loop.
        private bool ChainReaches(Hero from, Hero target)
        {
            var visited = new HashSet<string>();
            string cur = from?.StringId;
            for (int depth = 0; depth < LiegeChainDepthCap && !string.IsNullOrEmpty(cur); depth++)
            {
                if (cur == target?.StringId) return true;
                if (!visited.Add(cur)) return false; // pre-existing loop not involving target
                if (!_liegeOverride.TryGetValue(cur, out cur)) return false;
            }
            return false;
        }

        // Who a hero answers to in OUR feudal hierarchy.
        public Hero GetFeudalLiege(Hero hero)
        {
            if (hero == null) return null;
            Kingdom k = hero.Clan?.Kingdom ?? BoundKingdomOfZamindar(hero);
            if (k == null) return null;
            if (k.Leader == hero) return null;

            // An explicit bond (e.g. a new vassal placed under a castle lord) takes precedence.
            Hero forced = GetLiegeOverride(hero);
            if (forced != null) return forced;

            // A village zamindar answers to the lord who holds the bound town/castle.
            if (IsVillageZamindar(hero) && (hero.Clan == null || !HoldsTownOrCastle(hero.Clan)))
            {
                Settlement village = GetVillagesLordedBy(hero).FirstOrDefault();
                Hero boundLord = village?.Village?.Bound?.OwnerClan?.Leader;
                if (boundLord != null && boundLord != hero) return boundLord;
            }

            // A CASTLE lord (no town of his own) answers to the NEAREST town lord of his realm
            // (playtest round 3): the qiladar of a fort is a rung below the subahdar of the city
            // whose country he guards, not the emperor's direct man. Town lords, and castle lords
            // in a realm with no other town lord, still answer to the sovereign.
            Clan clan = hero.Clan;
            if (clan != null && clan.Leader == hero
                && !clan.Settlements.Any(s => s.IsTown) && clan.Settlements.Any(s => s.IsCastle))
            {
                Settlement castle = clan.Settlements.First(s => s.IsCastle);
                Hero townLord = null;
                float best = float.MaxValue;
                foreach (Clan c in k.Clans)
                {
                    if (c == null || c.IsEliminated || c == clan || c.Leader == null || c.Leader == k.Leader) continue;
                    foreach (Settlement t in c.Settlements)
                    {
                        if (!t.IsTown) continue;
                        float d = castle.GetPosition2D.Distance(t.GetPosition2D);
                        if (d < best) { best = d; townLord = c.Leader; }
                    }
                }
                if (townLord != null) return townLord;
            }

            // Town lords and unlanded nobles answer to the sovereign.
            return k.Leader;
        }

        // ── Auto-assignment ─────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _settlementById = null; // new session, new settlement set
            RebuildIndexes();
            Reconcile();
            AssignAllVillages();
            AddSummonMenus(starter);
        }
        private void OnWeeklyTick() { Reconcile(); GrantVillagesToUnlandedLords(); AssignAllVillages(); EnsurePlayerPlacement(); }

        // The court seats unlanded nobles of sufficient mansab in villages of their realm
        // (over a mere notable, never over another lord) — the AI half of "villages are
        // fiefs": AI lords climb the same first rung the player does. One grant per realm
        // per week keeps the transition gentle.
        private void GrantVillagesToUnlandedLords()
        {
            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated))
            {
                Hero candidate = k.Clans
                    .Where(c => !c.IsEliminated && !c.IsUnderMercenaryService && c.Leader != null
                                && c.Leader != Hero.MainHero && c.Leader != k.Leader
                                && !c.Settlements.Any()
                                && (MansabdariBehavior.Instance?.GetRankIndex(c) ?? 0) >= 1
                                && GetVillagesLordedBy(c.Leader).Count == 0)
                    .OrderByDescending(c => MansabdariBehavior.Instance?.GetRankIndex(c) ?? 0)
                    .ThenByDescending(c => c.Renown)
                    .Select(c => c.Leader)
                    .FirstOrDefault();
                if (candidate == null) continue;

                Settlement seat = Settlement.All
                    .Where(s => s.IsVillage && s.MapFaction == k)
                    .Where(s => { Hero cur = GetVillageLord(s); return cur == null || !cur.IsLord; })
                    .OrderBy(s => s.StringId, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (seat == null) continue;

                if (GrantVillageToLord(seat, candidate))
                    Util.TYTLog.Info($"Court grant: {candidate.Name} seated as zamindar of {seat.Name} ({k.Name}).");
            }
        }

        // ── Weekly reconciliation ────────────────────────────────────────────────────
        // Validates every stored entry so the layer can never drift from the live world:
        // dead/vanished zamindars, lords whose village left their realm, overrides whose
        // liege died/defected/lost his seat, and liege cycles are all scrubbed here.
        private void Reconcile()
        {
            // 1. Zamindar seats.
            foreach (string villageId in _zamindar.Keys.ToList())
            {
                Settlement village = SettlementById(villageId);
                if (village == null || !village.IsVillage) { RemoveZamindarEntry(villageId); continue; }

                string heroId = _zamindar[villageId];
                Hero holder = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
                if (holder == null) { RemoveZamindarEntry(villageId); continue; } // refilled by AssignAllVillages

                // A LORD zamindar whose village now sits in a foreign realm has lost it;
                // local notables persist under conquest (they are of the village, not the realm).
                if (holder.IsLord)
                {
                    Kingdom villageRealm = village.MapFaction as Kingdom;
                    Kingdom holderRealm = holder.Clan?.Kingdom;
                    if (villageRealm != null && villageRealm != holderRealm)
                    {
                        RemoveZamindarEntry(villageId);
                        Hero replacement = ChooseZamindar(village, holder);
                        if (replacement != null) SetZamindarEntry(villageId, replacement.StringId);
                        if (holder == Hero.MainHero)
                            Notify($"{village.Name} has passed out of your realm; your zamindari there is forfeit.", true);
                    }
                }
            }

            // 2. Liege overrides: hero and liege alive, same realm, liege still landed.
            //    (GetLiegeOverride validates lazily on read; this prunes the STORAGE so a
            //    stale bond cannot silently reactivate when conditions realign.)
            foreach (string heroId in _liegeOverride.Keys.ToList())
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
                Hero liege = hero == null ? null
                    : Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _liegeOverride[heroId]);
                if (hero == null || liege == null || liege == hero
                    || liege.Clan?.Kingdom == null || liege.Clan.Kingdom != hero.Clan?.Kingdom
                    || !HoldsTownOrCastle(liege)
                    || HoldsTownOrCastle(hero)) // a lord with his own seat answers to the sovereign, not an old bond
                { _liegeOverride.Remove(heroId); continue; }
            }

            // 3. Cycle scrub (pre-existing loops from old saves; SetLiege refuses new ones).
            foreach (string heroId in _liegeOverride.Keys.ToList())
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
                Hero liege = hero == null ? null
                    : Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _liegeOverride[heroId]);
                if (hero != null && liege != null && ChainReaches(liege, hero))
                {
                    Util.TYTLog.Warn($"Reconcile: broke a liege cycle at {hero.Name} -> {liege.Name}.");
                    _liegeOverride.Remove(heroId);
                }
            }
        }

        private void OnClanDestroyed(Clan clan)
        {
            if (clan == null) return;
            var memberIds = new HashSet<string>(clan.Heroes.Where(h => h != null).Select(h => h.StringId));
            foreach (string villageId in _zamindar.Keys.ToList())
                if (memberIds.Contains(_zamindar[villageId]))
                {
                    RemoveZamindarEntry(villageId);
                    Settlement village = SettlementById(villageId);
                    Hero replacement = village != null ? ChooseZamindar(village, null) : null;
                    if (replacement != null) SetZamindarEntry(villageId, replacement.StringId);
                }
            foreach (string heroId in _liegeOverride.Keys.ToList())
                if (memberIds.Contains(heroId) || memberIds.Contains(_liegeOverride[heroId]))
                    _liegeOverride.Remove(heroId);
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            // Realm gone: every bond anchored inside it is void. Zamindar seats survive
            // (villages and their notables outlive the realm); Reconcile reseats lord-zamindars.
            if (kingdom == null) return;
            foreach (string heroId in _liegeOverride.Keys.ToList())
            {
                Hero liege = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _liegeOverride[heroId]);
                if (liege == null || liege.Clan?.Kingdom == null || liege.Clan.Kingdom == kingdom)
                    _liegeOverride.Remove(heroId);
            }
        }

        // A town/castle changing hands across realms reseats the LORD zamindars of its bound
        // villages (a foreign lord cannot keep lording a village inside the conqueror's realm);
        // notable zamindars stay — the local gentry bends to whoever holds the seat.
        private void OnOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturer,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!Util.WorldGen.Ready) return; // skip the parallel world-gen distribution (see Util/WorldGen.cs)
            if (s == null) return;

            if (s.IsVillage)
            {
                // The village itself was granted/taken through the ENGINE (cheat, GrantFief,
                // conquest side-effect): keep the feudal layer in step with reality.
                Hero engineLord = newOwner?.Clan?.Leader ?? newOwner;
                Hero current = GetVillageLord(s);
                if (engineLord != null && engineLord.IsLord && current != engineLord
                    && newOwner?.Clan != null && s.OwnerClan == newOwner.Clan)
                    SetZamindarEntry(s.StringId, engineLord.StringId);
                return;
            }

            if (!s.IsTown && !s.IsCastle) return;
            Kingdom newRealm = newOwner?.Clan?.Kingdom;
            Kingdom oldRealm = oldOwner?.Clan?.Kingdom;
            if (newRealm == oldRealm || s.Town?.Villages == null) return;

            foreach (Village v in s.Town.Villages)
            {
                Settlement vs = v?.Settlement;
                Hero holder = vs != null ? GetVillageLord(vs) : null;
                if (holder == null || !holder.IsLord) continue;
                if (holder.Clan?.Kingdom == newRealm) continue; // his realm took the seat — he keeps it
                RemoveZamindarEntry(vs.StringId);
                Hero replacement = ChooseZamindar(vs, holder);
                if (replacement != null) SetZamindarEntry(vs.StringId, replacement.StringId);
                if (holder == Hero.MainHero)
                    Notify($"{s.Name} has fallen, and with it your zamindari of {vs.Name}.", true);
            }
        }

        // ── Call your vassals to war (from a town or castle you hold) ────────────────
        private void AddSummonMenus(CampaignGameStarter starter)
        {
            // Lives under the consolidated court menu (CourtMenuBehavior).
            starter.AddGameMenuOption(CourtMenuBehavior.MenuId, "hindostan_summon_levies",
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
                if (pick != null) SetZamindarEntry(s.StringId, pick.StringId);
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
                    SetZamindarEntry(village.StringId, successor.StringId);
                    if (village.Village?.Bound?.OwnerClan == Clan.PlayerClan)
                        Notify($"The zamindar of {village.Name} has died; {successor.Name} succeeds to the village.", false);
                }
                else RemoveZamindarEntry(village.StringId);
            }
        }

        // ── New vassal placement: a liege and a village on joining a realm ───────────
        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (!Util.WorldGen.Ready) return; // skip the parallel world-gen distribution (see Util/WorldGen.cs)
            if (clan == null) return;

            // ANY clan changing realm (not just the player, as before): bonds anchored in the
            // old realm dissolve, and its lords lose village seats left behind in it.
            var memberIds = new HashSet<string>(clan.Heroes.Where(h => h != null).Select(h => h.StringId));
            foreach (string heroId in _liegeOverride.Keys.ToList())
            {
                bool heroMoved = memberIds.Contains(heroId);
                bool liegeMoved = memberIds.Contains(_liegeOverride[heroId]);
                if (heroMoved || liegeMoved) _liegeOverride.Remove(heroId);
            }
            foreach (Hero lord in clan.Heroes.Where(h => h != null && h.IsLord))
                foreach (Settlement village in GetVillagesLordedBy(lord).ToList())
                    if (village.MapFaction as Kingdom != newKingdom)
                    {
                        RemoveZamindarEntry(village.StringId);
                        Hero replacement = ChooseZamindar(village, lord);
                        if (replacement != null) SetZamindarEntry(village.StringId, replacement.StringId);
                    }

            if (clan == Clan.PlayerClan) EnsurePlayerPlacement();
        }

        // Place a landless player-vassal beneath a castle lord and grant him a village.
        // Mercenaries are skipped (they hold no land) until they swear as full vassals — the
        // weekly tick catches that transition, since it does not raise a kingdom-change event.
        private void EnsurePlayerPlacement()
        {
            Clan pc = Clan.PlayerClan;
            Kingdom k = pc?.Kingdom;
            if (k == null || pc.IsUnderMercenaryService) return;
            if (k.Leader == Hero.MainHero) return;   // the sovereign answers to none
            if (HoldsTownOrCastle(pc)) return;       // a great lord in his own right

            Hero existing = GetLiegeOverride(Hero.MainHero);
            Hero liege = existing ?? ChooseLiege(k);
            if (liege == null || liege == Hero.MainHero) return;
            if (existing == null && !SetLiege(Hero.MainHero, liege)) return; // refused (would cycle)

            bool hasVillage = GetVillagesLordedBy(Hero.MainHero).Count > 0 || pc.Settlements.Any(s => s.IsVillage);
            Settlement granted = hasVillage ? null : GrantVillageUnder(liege);

            if (granted != null)
                RoyalFarmaan.FromRuler(k, "A Place in the Realm",
                    $"You enter the service of {k.Name}. You are placed beneath {liege.Name}, who is your liege, and granted " +
                    $"the zamindari of {granted.Name}. Serve him well — petition for a seat on his council, and rise.",
                    "I am honoured");
            else if (existing == null)
                RoyalFarmaan.FromRuler(k, "A Place in the Realm",
                    $"You enter the service of {k.Name}, beneath {liege.Name}, who is your liege. Serve him well and rise.",
                    "I am honoured");
        }

        // A castle lord by preference (the rung directly above a new vassal), else a town
        // lord, else the sovereign.
        private Hero ChooseLiege(Kingdom k)
        {
            var castleLords = k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != Hero.MainHero
                && c.Settlements.Any(s => s.IsCastle)).ToList();
            if (castleLords.Count > 0)
                return castleLords.OrderByDescending(c => CharacterRelationManager.GetHeroRelation(Hero.MainHero, c.Leader))
                    .ThenByDescending(c => c.Settlements.Count(s => s.IsCastle)).First().Leader;

            var townLords = k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != Hero.MainHero
                && c.Settlements.Any(s => s.IsTown)).ToList();
            if (townLords.Count > 0)
                return townLords.OrderByDescending(c => CharacterRelationManager.GetHeroRelation(Hero.MainHero, c.Leader)).First().Leader;

            return k.Leader != Hero.MainHero ? k.Leader : null;
        }

        // Make the player zamindar of a village beneath the liege's seat.
        private Settlement GrantVillageUnder(Hero liege)
        {
            if (liege?.Clan == null) return null;
            foreach (Settlement seat in liege.Clan.Settlements.Where(s => s.IsCastle || s.IsTown))
                foreach (Village v in seat.Town?.Villages ?? Enumerable.Empty<Village>())
                {
                    Settlement vs = v?.Settlement;
                    if (vs == null) continue;
                    if (GetVillageLord(vs) == Hero.MainHero) return vs; // already ours
                    // Only report a village the seat actually TOOK — the old version returned
                    // vs unconditionally, so a refused assignment was still announced as granted.
                    if (AssignZamindar(vs, Hero.MainHero)) return vs;
                }
            return null;
        }

        // Seat an AI LORD as zamindar of a village in his realm (the AI half of "villages are
        // fiefs": unlanded nobles get real village seats). Returns true if the seat took.
        public bool GrantVillageToLord(Settlement village, Hero lord)
        {
            if (village == null || !village.IsVillage || lord == null || !lord.IsLord) return false;
            if (village.MapFaction as Kingdom != lord.Clan?.Kingdom) return false;
            Hero current = GetVillageLord(village);
            if (current != null && current.IsLord) return false; // never displace a seated lord
            return AssignZamindar(village, lord);
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
                              $"Investiture costs {AppointInfluenceCost} influence and {AppointGoldCost} rupees.";
                elements.Add(new InquiryElement(n,
                    $"{n.Name} — {NotableRole(n)} (power {n.Power:0}, rel {rel})", null, true, hint));
            }

            string title = current != null ? $"Replace the Zamindar of {village.Name}" : $"Appoint the Zamindar of {village.Name}";
            string desc = (current != null
                    ? $"The village is presently held by {current.Name}. "
                    : "The village has no settled lord. ") +
                $"Invest a notable as its zamindar — its lord beneath you. Costs {AppointInfluenceCost} influence and {AppointGoldCost} rupees.";

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
            { Notify($"You need {AppointGoldCost} rupees for the investiture.", true); return; }

            Hero previous = GetVillageLord(village);
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -AppointInfluenceCost);
            Hero.MainHero.ChangeHeroGold(-AppointGoldCost);
            SetZamindarEntry(village.StringId, hero.StringId);

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
            if (successor != null) SetZamindarEntry(village.StringId, successor.StringId);
            else RemoveZamindarEntry(village.StringId);

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

            var lIds = _liegeOverride.Keys.ToList();
            var lVals = _liegeOverride.Values.ToList();
            dataStore.SyncData("hind_liege_heroes", ref lIds);
            dataStore.SyncData("hind_liege_lieges", ref lVals);

            if (!dataStore.IsSaving)
            {
                _zamindar = new Dictionary<string, string>();
                for (int i = 0; i < ids.Count && i < vals.Count; i++) _zamindar[ids[i]] = vals[i];

                _liegeOverride = new Dictionary<string, string>();
                for (int i = 0; i < lIds.Count && i < lVals.Count; i++) _liegeOverride[lIds[i]] = lVals[i];

                _settlementById = null;
                RebuildIndexes();
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
