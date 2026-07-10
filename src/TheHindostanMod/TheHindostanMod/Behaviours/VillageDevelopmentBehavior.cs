using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Village fief mechanics (design: wiki Chapter 15). EVERY village with a seated
    // zamindar runs the daily simulation — bandit threat, construction, hearth growth,
    // and tax accrual into a per-village coffer — so the world develops alongside the
    // player. The interactive layer (patrols, relief pleas, warnings, collection) is
    // the player's alone. AI zamindars fund and pick their own works weekly.
    // All effects are applied directly to hearth/prosperity each day — no model
    // overrides needed. Formulas live in Util.VillageFiefMath (unit-tested).
    public class VillageDevelopmentBehavior : CampaignBehaviorBase
    {
        // The first five entries predate the fief expansion; their NAMES are serialized
        // in the completed-works CSV, so they must never be renamed.
        public enum VillageProject
        {
            Granary, Watchtower, IrrigationCanal, MilitiaPost, TradePost,
            GranaryII, WatchtowerII, Stepwell, Bazaar, Shrine, Kotwali, Serai
        }

        private struct ProjectDef
        {
            public string Name; public int Cost; public int Days; public string Effect;
            public VillageProject? Requires;      // upgrade-tier prerequisite
            public float HearthPerDay;            // daily hearth growth
            public float ThreatFlat;              // daily flat threat reduction
            public float ThreatMult;              // multiplier on the day's remaining threat (1 = none)
            public float BoundProsperity;         // daily prosperity to the bound town
            public float TaxBonusPct;             // % added to the village's daily tax
            public float DefenceBonus;            // standing addition to DefenceStrength
            public int RelationOnComplete;        // one-time relation with the village's notables

            public ProjectDef(string n, int cost, int days, string effect, VillageProject? requires = null,
                              float hearth = 0f, float threatFlat = 0f, float threatMult = 1f,
                              float prosperity = 0f, float taxPct = 0f, float defence = 0f, int relation = 0)
            {
                Name = n; Cost = cost; Days = days; Effect = effect; Requires = requires;
                HearthPerDay = hearth; ThreatFlat = threatFlat; ThreatMult = threatMult;
                BoundProsperity = prosperity; TaxBonusPct = taxPct; DefenceBonus = defence;
                RelationOnComplete = relation;
            }
        }

        private static readonly Dictionary<VillageProject, ProjectDef> Defs = new Dictionary<VillageProject, ProjectDef>
        {
            // ── Food & hearth ──
            { VillageProject.Granary,        new ProjectDef("Granary",          500, 30, "+0.6 hearth growth per day",
                                                            hearth: 0.6f) },
            { VillageProject.GranaryII,      new ProjectDef("Great Granary",   1200, 40, "+0.6 more hearth per day, +5% tax",
                                                            requires: VillageProject.Granary, hearth: 0.6f, taxPct: 5f) },
            { VillageProject.IrrigationCanal,new ProjectDef("Irrigation Canal", 800, 45, "+1.0 hearth growth per day",
                                                            hearth: 1.0f) },
            { VillageProject.Stepwell,       new ProjectDef("Stepwell (Baoli)", 700, 35, "+0.4 hearth per day, +5% tax",
                                                            hearth: 0.4f, taxPct: 5f) },
            // ── Defence ──
            { VillageProject.Watchtower,     new ProjectDef("Watchtower",       300, 20, "bandit threat grows far slower",
                                                            threatMult: 0.6f) },
            { VillageProject.WatchtowerII,   new ProjectDef("Stone Watchtower", 900, 30, "threat falls further each day",
                                                            requires: VillageProject.Watchtower, threatFlat: 1.0f, threatMult: 0.85f) },
            { VillageProject.MilitiaPost,    new ProjectDef("Militia Post",     600, 25, "passively lowers bandit threat",
                                                            threatFlat: 1.0f, defence: 0.5f) },
            { VillageProject.Kotwali,        new ProjectDef("Kotwali",         1000, 35, "a police post: strong threat suppression",
                                                            requires: VillageProject.MilitiaPost, threatFlat: 1.5f, defence: 0.5f) },
            // ── Economy & faith ──
            { VillageProject.TradePost,      new ProjectDef("Trade Post",       400, 30, "raises the bound town's prosperity",
                                                            prosperity: 0.3f) },
            { VillageProject.Bazaar,         new ProjectDef("Bazaar",          1100, 40, "+10% tax, more prosperity to the bound town",
                                                            requires: VillageProject.TradePost, prosperity: 0.3f, taxPct: 10f) },
            { VillageProject.Shrine,         new ProjectDef("Shrine",           600, 30, "the faith kept: +0.2 hearth, the notables' goodwill",
                                                            hearth: 0.2f, relation: 3) },
            { VillageProject.Serai,          new ProjectDef("Serai",            800, 35, "a caravanserai: +5% tax, prosperity to the bound town",
                                                            prosperity: 0.2f, taxPct: 5f) },
        };

        // The Shrine takes the village faith's own house of worship as its display name.
        private static string ProjectDisplayName(VillageProject p, Settlement s)
        {
            if (p != VillageProject.Shrine) return Defs[p].Name;
            Religion r = ReligionBehavior.Instance?.GetCultureReligion(s?.Culture) ?? Religion.None;
            return r == Religion.Islam ? "Mosque (Masjid)"
                 : r == Religion.Hindu ? "Temple (Mandir)"
                 : r == Religion.Sikh ? "Gurdwara"
                 : Defs[p].Name;
        }

        // AI build chains by priority category (see VillageFiefMath.AiPriorityCategory).
        private static readonly VillageProject[] DefenceChain =
            { VillageProject.Watchtower, VillageProject.MilitiaPost, VillageProject.WatchtowerII, VillageProject.Kotwali };
        private static readonly VillageProject[] FoodChain =
            { VillageProject.Granary, VillageProject.Stepwell, VillageProject.IrrigationCanal, VillageProject.GranaryII };
        private static readonly VillageProject[] EconomyChain =
            { VillageProject.TradePost, VillageProject.Shrine, VillageProject.Serai, VillageProject.Bazaar };

        public static VillageDevelopmentBehavior Instance { get; private set; }

        // In-memory state; serialized via parallel lists in SyncData.
        private Dictionary<string, float> _threat = new Dictionary<string, float>();           // villageId -> 0..100
        private Dictionary<string, string> _completed = new Dictionary<string, string>();        // villageId -> "Granary,Watchtower"
        private Dictionary<string, string> _buildProject = new Dictionary<string, string>();     // villageId -> project name
        private Dictionary<string, int> _buildDays = new Dictionary<string, int>();              // villageId -> days remaining (legacy int mirror)
        private Dictionary<string, float> _buildProgress = new Dictionary<string, float>();      // villageId -> days remaining (fractional)
        private Dictionary<string, float> _treasury = new Dictionary<string, float>();           // villageId -> coffer (dinars)
        private Dictionary<string, string> _queued = new Dictionary<string, string>();           // villageId -> queued project name
        private Dictionary<string, int> _lastCollectDay = new Dictionary<string, int>();         // villageId -> day taxes last collected
        private Dictionary<string, int> _lastPatrolDay = new Dictionary<string, int>();          // villageId -> day index
        private Dictionary<string, int> _reliefUntil = new Dictionary<string, int>();            // villageId -> day relief ends
        private Dictionary<string, int> _reliefTroops = new Dictionary<string, int>();           // villageId -> men to return
        private Dictionary<string, int> _lastOverwhelmDay = new Dictionary<string, int>();       // villageId -> day a plea was last sent
        private const int ReliefDays = 6;
        private const int OverwhelmCooldown = 12;
        private const int CollectCooldownDays = 7;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("VillageDev.WeeklyTick", OnWeeklyTick));
        }

        // ── Queries ────────────────────────────────────────────────────────────────
        public float GetThreat(Settlement s) => s != null && _threat.TryGetValue(s.StringId, out float v) ? v : 0f;
        public float GetTreasury(Settlement s) => s != null && _treasury.TryGetValue(s.StringId, out float v) ? v : 0f;

        // The player may oversee a village he holds outright (engine owner) OR one he holds in
        // zamindari through our feudal layer — the latter covers a vassal granted a village
        // beneath an AI lord, where the engine owner is still that lord's clan.
        private bool IsPlayerVillage(Settlement s)
            => s != null && s.IsVillage
               && (s.OwnerClan == Clan.PlayerClan
                   || FeudalTitlesBehavior.Instance?.GetVillageLord(s) == Hero.MainHero);

        private bool HasProject(Settlement s, VillageProject p)
            => _completed.TryGetValue(s.StringId, out string list)
               && list.Split(',').Contains(Defs[p].Name);

        // Sum an effect over the village's completed works. The daily RATES (hearth,
        // prosperity) scale with the MCM development pace; structural properties
        // (threat multiplier, tax %, defence) do not.
        private float SumBuilt(Settlement s, Func<ProjectDef, float> pick)
        {
            if (!_completed.TryGetValue(s.StringId, out string list) || string.IsNullOrEmpty(list)) return 0f;
            float sum = 0f;
            var names = list.Split(',');
            foreach (var kv in Defs)
                if (names.Contains(kv.Value.Name)) sum += pick(kv.Value);
            return sum;
        }

        private float BuiltThreatMultiplier(Settlement s)
        {
            float mult = 1f;
            if (_completed.TryGetValue(s.StringId, out string list) && !string.IsNullOrEmpty(list))
            {
                var names = list.Split(',');
                foreach (var kv in Defs)
                    if (names.Contains(kv.Value.Name)) mult *= kv.Value.ThreatMult;
            }
            return mult;
        }

        // ── Daily simulation ─────────────────────────────────────────────────────────
        private void OnDailyTickSettlement(Settlement s)
        {
            // We're inside the engine's settlement iteration: any farmaan this tick raises (a
            // bandit overwhelm plea) must be enqueued, not shown now. RoyalFarmaan.Pump() shows it
            // later from the campaign TickEvent, outside the iteration. See FarmaanScreen.
            UI.RoyalFarmaan.SuppressImmediate = true;
            try { TYTLog.Guard("VillageDev.DailyTick:" + (s?.Name?.ToString() ?? "?"), () => DailyTickSettlement(s)); }
            finally { UI.RoyalFarmaan.SuppressImmediate = false; }
        }

        private void DailyTickSettlement(Settlement s)
        {
            if (s == null || !s.IsVillage || s.Village == null) return;

            // The full pipeline runs for EVERY village with a seated zamindar (this is what
            // makes the AI world develop); the interactive steps are player-only.
            bool player = IsPlayerVillage(s);
            if (!player && FeudalTitlesBehavior.Instance?.GetVillageLord(s) == null) return;

            // Per-step breadcrumbs: a NATIVE crash bypasses the outer Guard's try/catch and leaves
            // no managed report, so the heartbeat's last crumb is the only witness. Naming each step
            // turns "died somewhere in Khanna's tick" into "died in Khanna › MaybeOverwhelm".
            TYTLog.Crumb("UpdateThreat");        UpdateThreat(s, player);
            TYTLog.Crumb("AdvanceConstruction");  AdvanceConstruction(s, player);
            TYTLog.Crumb("ApplyHearthProsperity"); ApplyHearthAndProsperity(s);
            TYTLog.Crumb("AccrueTax");            AccrueTax(s);
            if (!player) return;
            TYTLog.Crumb("HandleReliefReturn");   HandleReliefReturn(s);
            TYTLog.Crumb("MaybeOverwhelm");       MaybeOverwhelm(s);
            TYTLog.Crumb("NotifyThreat");         NotifyThreat(s);
        }

        private void UpdateThreat(Settlement s, bool player)
        {
            bool reliefActive = player && _reliefUntil.TryGetValue(s.StringId, out int until)
                                && (int)CampaignTime.Now.ToDays < until;

            Kingdom owner = s.OwnerClan?.Kingdom;
            bool atWar = owner != null && Kingdom.All.Any(o => o != owner && !o.IsEliminated && owner.IsAtWarWith(o));
            bool lordPresent = player && MobileParty.MainParty != null && MobileParty.MainParty.CurrentSettlement == s;

            _threat[s.StringId] = VillageFiefMath.ThreatStep(
                GetThreat(s), reliefActive, atWar,
                flatReduction: SumBuilt(s, d => d.ThreatFlat),
                watchMultiplier: BuiltThreatMultiplier(s),
                defence: DefenceStrength(s),
                lordPresent: lordPresent);
        }

        // Daily suppression from the village's own defenders: its militia, the built
        // defence works, and the zamindar's stewardship/charm (the governor effect).
        // Scaled by the Militia & zamindar defence weight (MCM).
        private float DefenceStrength(Settlement s)
        {
            float w = Config.Tune.MilitiaDefenceWeight;
            if (w <= 0f) return 0f;
            float militia = s.Militia;
            Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
            int steward = z != null ? z.GetSkillValue(DefaultSkills.Steward) : 0;
            int charm = z != null ? z.GetSkillValue(DefaultSkills.Charm) : 0;
            return w * (MathF.Min(2f, militia / 25f)
                        + VillageFiefMath.ThreatDecayBonus(charm, steward)
                        + SumBuilt(s, d => d.DefenceBonus));
        }

        // ── Taxes: the village coffer ────────────────────────────────────────────────
        private void AccrueTax(Settlement s)
        {
            float rate = Config.Tune.VillageTaxPerHearth;
            if (rate <= 0f) return;
            Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
            float authorityRate = 1f;
            if (s.MapFaction is Kingdom k && ImperialAuthorityBehavior.Instance != null)
                authorityRate = ImperialAuthorityBehavior.Instance.GetTaxCollectionRate(k);
            _treasury[s.StringId] = GetTreasury(s) + VillageFiefMath.DailyTax(
                s.Village.Hearth, SumBuilt(s, d => d.TaxBonusPct), GetThreat(s),
                authorityRate, z?.GetSkillValue(DefaultSkills.Steward) ?? 0, rate);
        }

        private void CollectTaxes(Settlement s)
        {
            float coffer = GetTreasury(s);
            if (coffer < 1f) { Notify("The village coffer is empty.", true); return; }

            int today = (int)CampaignTime.Now.ToDays;
            if (_lastCollectDay.TryGetValue(s.StringId, out int last) && today - last < CollectCooldownDays)
            {
                // Squeezing the village more than once a week sours the local gentry.
                int penalty = Config.Tune.VillageTaxCollectRelationPenalty;
                if (penalty > 0)
                    foreach (Hero n in s.Notables?.Where(n => n != null && n.IsAlive) ?? Enumerable.Empty<Hero>())
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, -penalty);
                Notify("You squeeze the district again so soon — the notables take it ill.", true);
            }
            _lastCollectDay[s.StringId] = today;

            int amount = (int)coffer;
            _treasury[s.StringId] = coffer - amount;
            Hero.MainHero.ChangeHeroGold(amount);
            Notify($"You collect {amount} dinars from the coffer of {s.Name}.", false);
        }

        // ── Bandit raids overwhelm the militia; the zamindar pleads for relief ──────────
        private void MaybeOverwhelm(Settlement s)
        {
            if (_reliefUntil.ContainsKey(s.StringId)) return;     // already being relieved
            int today = (int)CampaignTime.Now.ToDays;
            if (_lastOverwhelmDay.TryGetValue(s.StringId, out int last) && today - last < OverwhelmCooldown) return;

            float t = GetThreat(s);
            if (t < 50f) return;
            // The weaker the village's own defence relative to the threat, the likelier it breaks.
            float exposure = MathF.Max(0.2f, 1f - DefenceStrength(s) / 6f);
            float chance = Config.Tune.PatrolOverwhelmChance * (t / 100f) * exposure;
            if (MBRandom.RandomFloat >= chance) return;
            TriggerOverwhelm(s);
        }

        private void TriggerOverwhelm(Settlement s)
        {
            _lastOverwhelmDay[s.StringId] = (int)CampaignTime.Now.ToDays;
            _threat[s.StringId] = MathF.Min(100f, GetThreat(s) + 25f); // the militia is broken
            if (s.Village != null) s.Village.Hearth = MathF.Max(0f, s.Village.Hearth - 10f);

            Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
            string sender = (z != null && z != Hero.MainHero) ? $"{z.Name}, your zamindar of {s.Name}," : $"The headman of {s.Name}";
            int men = Config.Tune.ReliefDetachmentSize;

            RoyalFarmaan.Issue("A Plea from the Village", $"{s.Name} is beset",
                $"{sender} sends a desperate letter: bandits have broken the militia and ravage the district. He begs for relief — " +
                $"will you send a commander with {men} men to drive them off, or ride to patrol the lands yourself?",
                seal: "Sealed in haste",
                primary: $"Send a commander with {men} men", onPrimary: () => DispatchRelief(s),
                secondary: "I will see to it myself", onSecondary: () => SelfRelief(s),
                dedupeKey: "plea:" + s.StringId, cooldownDays: OverwhelmCooldown);
        }

        private void DispatchRelief(Settlement s)
        {
            MobileParty party = MobileParty.MainParty;
            if (party == null) return;
            int took = RemoveTroops(party, Config.Tune.ReliefDetachmentSize);
            if (took <= 0) { Notify($"You have no men to spare for the relief of {s.Name}.", true); return; }

            int today = (int)CampaignTime.Now.ToDays;
            _reliefUntil[s.StringId] = today + ReliefDays;
            _reliefTroops[s.StringId] = took;
            _threat[s.StringId] = MathF.Max(0f, GetThreat(s) - 40f);

            Hero comp = Clan.PlayerClan?.Companions?.FirstOrDefault(h => h != null && h.IsAlive);
            string who = comp != null ? comp.Name.ToString() : "your commander";
            Notify($"{who} rides to relieve {s.Name} with {took} men. They will return in {ReliefDays} days.", false);
        }

        private void SelfRelief(Settlement s)
            => Notify($"You resolve to see to {s.Name} yourself. Ride there and patrol the district to drive off the bandits.", false);

        private void HandleReliefReturn(Settlement s)
        {
            if (!_reliefUntil.TryGetValue(s.StringId, out int until)) return;
            if ((int)CampaignTime.Now.ToDays < until) return;
            int count = _reliefTroops.TryGetValue(s.StringId, out int c) ? c : 0;
            _reliefUntil.Remove(s.StringId);
            _reliefTroops.Remove(s.StringId);
            if (count > 0 && MobileParty.MainParty != null)
            {
                CharacterObject troop = (s.Culture ?? s.OwnerClan?.Culture)?.BasicTroop;
                if (troop != null) MobileParty.MainParty.MemberRoster.AddToCounts(troop, count);
                Notify($"Your {count} men return from the relief of {s.Name}.", false);
            }
        }

        // Remove up to 'want' regular (non-hero) troops from the party, returning how many left.
        private static int RemoveTroops(MobileParty p, int want)
        {
            int removed = 0;
            var roster = p.MemberRoster;
            for (int i = roster.Count - 1; i >= 0 && removed < want; i--)
            {
                var e = roster.GetElementCopyAtIndex(i);
                if (e.Character == null || e.Character.IsHero) continue;
                int take = Math.Min(e.Number, want - removed);
                if (take > 0) { roster.AddToCounts(e.Character, -take); removed += take; }
            }
            return removed;
        }

        private void AdvanceConstruction(Settlement s, bool player)
        {
            if (!_buildProject.TryGetValue(s.StringId, out string projName)) return;

            // Fractional progress: an able zamindar (Engineering) builds faster.
            float remaining = _buildProgress.TryGetValue(s.StringId, out float p) ? p
                : _buildDays.TryGetValue(s.StringId, out int legacy) ? legacy : 0f; // migrate old saves
            Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
            remaining -= VillageFiefMath.BuildSpeedFactor(z?.GetSkillValue(DefaultSkills.Engineering) ?? 0);
            if (remaining > 0f)
            {
                _buildProgress[s.StringId] = remaining;
                _buildDays[s.StringId] = (int)Math.Ceiling(remaining); // legacy mirror for downgrade tolerance
                return;
            }

            // Completed.
            string list = _completed.TryGetValue(s.StringId, out string c) && !string.IsNullOrEmpty(c)
                ? c + "," + projName : projName;
            _completed[s.StringId] = list;
            _buildProject.Remove(s.StringId);
            _buildDays.Remove(s.StringId);
            _buildProgress.Remove(s.StringId);

            // A finished shrine (etc.) earns the goodwill of the local gentry — for whoever built it.
            var def = Defs.Values.FirstOrDefault(d => d.Name == projName);
            if (def.RelationOnComplete > 0 && z != null)
                foreach (Hero n in s.Notables?.Where(n => n != null && n.IsAlive && n != z) ?? Enumerable.Empty<Hero>())
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(z, n, def.RelationOnComplete);

            if (player)
            {
                VillageProject? done = ByName(projName);
                string shown = done.HasValue ? ProjectDisplayName(done.Value, s) : projName;
                Notify($"Construction complete in {s.Name}: the {shown} now stands.", false);
            }

            // A queued work begins at once — if its patron can still pay.
            if (player && _queued.TryGetValue(s.StringId, out string nextName))
            {
                _queued.Remove(s.StringId);
                VillageProject? next = ByName(nextName);
                if (next.HasValue)
                {
                    if (Hero.MainHero.Gold >= Defs[next.Value].Cost && CanBuild(s, next.Value, out _))
                    {
                        Hero.MainHero.ChangeHeroGold(-Defs[next.Value].Cost);
                        BeginConstruction(s, next.Value);
                        Notify($"The queued work begins in {s.Name}: the {Defs[next.Value].Name}.", false);
                    }
                    else Notify($"The queued {nextName} in {s.Name} is cancelled — the coffers cannot bear it.", true);
                }
            }
        }

        private static VillageProject? ByName(string name)
        {
            foreach (var kv in Defs)
                if (kv.Value.Name == name) return kv.Key;
            return null;
        }

        private void BeginConstruction(Settlement s, VillageProject p)
        {
            _buildProject[s.StringId] = Defs[p].Name;
            _buildProgress[s.StringId] = Defs[p].Days;
            _buildDays[s.StringId] = Defs[p].Days;
        }

        private void ApplyHearthAndProsperity(Settlement s)
        {
            float pace = Config.Tune.VillageDevelopmentPace;
            float hearthDelta = SumBuilt(s, d => d.HearthPerDay) * pace;

            float growthFactor = VillageFiefMath.HearthGrowthFactor(GetThreat(s));
            hearthDelta = growthFactor < 0f ? -1f : hearthDelta * growthFactor;

            if (hearthDelta != 0f)
                s.Village.Hearth = MathF.Max(0f, s.Village.Hearth + hearthDelta);

            float prosperity = SumBuilt(s, d => d.BoundProsperity) * pace;
            if (prosperity > 0f)
            {
                Town boundTown = s.Village.Bound?.Town;
                if (boundTown != null) boundTown.Prosperity += prosperity;
            }
        }

        private void NotifyThreat(Settlement s)
        {
            if (s.OwnerClan != Clan.PlayerClan) return;
            float t = GetThreat(s);
            // Only warn on crossing into a worse band (cheap state: compare to stored band).
            // Simple daily nudge at high threat.
            if (t >= 80f)
                Notify($"Bandits overrun the country around {s.Name}. Patrol soon or the village will wither.", true);
            else if (t >= 60f && MBRandom.RandomFloat < 0.25f)
                Notify($"Banditry is rising around {s.Name}. Consider a patrol.", true);
        }

        // A word with the local gentry steadies the district (the dialogue pack's hook).
        public void ReassureVillage(Settlement s)
        {
            if (s == null || !s.IsVillage) return;
            _threat[s.StringId] = MathF.Max(0f, GetThreat(s) - 5f);
        }

        // ── Player actions ───────────────────────────────────────────────────────────
        private void Patrol(Settlement s)
        {
            int today = (int)CampaignTime.Now.ToDays;
            if (_lastPatrolDay.TryGetValue(s.StringId, out int last) && last == today)
            { Notify("Your men have already swept the district today.", true); return; }
            _lastPatrolDay[s.StringId] = today;

            float reduction = 20f;
            if (MobileParty.MainParty != null && MobileParty.MainParty.MemberRoster.TotalManCount > 100) reduction += 10f;
            reduction += Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) / 100f * 5f;

            _threat[s.StringId] = MathF.Max(0f, GetThreat(s) - reduction);
            Notify($"You patrol the lands around {s.Name}. Bandit threat falls by {reduction:0}.", false);
        }

        // Eligibility WITHOUT the "another work active" restriction — used both for a
        // direct start and for queueing behind the active work.
        private bool IsEligible(Settlement s, VillageProject p, out string reason)
        {
            reason = "";
            if (HasProject(s, p)) { reason = "Already built."; return false; }
            if (Defs[p].Requires.HasValue && !HasProject(s, Defs[p].Requires.Value))
            { reason = $"Requires the {Defs[Defs[p].Requires.Value].Name} first."; return false; }
            if (_buildProject.TryGetValue(s.StringId, out string active) && active == Defs[p].Name)
            { reason = "Already under construction."; return false; }
            if (_queued.TryGetValue(s.StringId, out string queued) && queued == Defs[p].Name)
            { reason = "Already queued."; return false; }
            return true;
        }

        private bool CanBuild(Settlement s, VillageProject p, out string reason)
        {
            if (!IsEligible(s, p, out reason)) return false;
            if (_buildProject.ContainsKey(s.StringId)) { reason = "Another work is already under construction here."; return false; }
            if (Hero.MainHero.Gold < Defs[p].Cost) { reason = $"You need {Defs[p].Cost} gold."; return false; }
            return true;
        }

        // One work may be queued behind the active one; it charges when it starts.
        private bool CanQueue(Settlement s, VillageProject p, out string reason)
        {
            if (!IsEligible(s, p, out reason)) return false;
            if (!_buildProject.ContainsKey(s.StringId)) { reason = "Nothing is being built; start it directly."; return false; }
            if (_queued.ContainsKey(s.StringId)) { reason = "A work is already queued here."; return false; }
            return true;
        }

        private void StartBuild(Settlement s, VillageProject p)
        {
            if (CanBuild(s, p, out _))
            {
                Hero.MainHero.ChangeHeroGold(-Defs[p].Cost);
                BeginConstruction(s, p);
                Notify($"Work begins on the {Defs[p].Name} in {s.Name}. It will take about {Defs[p].Days} days.", false);
                return;
            }
            if (CanQueue(s, p, out string reason))
            {
                _queued[s.StringId] = Defs[p].Name;
                Notify($"The {Defs[p].Name} is queued in {s.Name}; its {Defs[p].Cost} dinars are charged when work begins.", false);
                return;
            }
            Notify(reason, true);
        }

        // ── AI development (weekly) ──────────────────────────────────────────────────
        // AI zamindars draw most of their coffer as income, keep the rest as a build
        // budget, and start works by need: danger -> defence, hunger -> food, else economy.
        private void OnWeeklyTick()
        {
            if (!Config.Tune.AiVillageDevelopment) return;
            var ft = FeudalTitlesBehavior.Instance;
            if (ft == null) return;

            int startsLeft = Config.Tune.AiVillageBuildsPerWeek;
            foreach (Settlement s in Settlement.All)
            {
                if (!s.IsVillage || s.Village == null) continue;
                if (IsPlayerVillage(s)) continue; // the player runs his own works
                Hero z = ft.GetVillageLord(s);
                if (z == null || !z.IsAlive) continue;

                // Income draw: most of the coffer to the zamindar, the rest retained.
                float coffer = GetTreasury(s);
                if (coffer >= 10f)
                {
                    int draw = (int)(coffer * VillageFiefMath.AiCofferDrawShare);
                    if (draw > 0) { _treasury[s.StringId] = coffer - draw; z.ChangeHeroGold(draw); }
                }

                if (startsLeft <= 0) continue;
                if (_buildProject.ContainsKey(s.StringId)) continue;

                VillageProject? pick = PickAiProject(s);
                if (!pick.HasValue) continue;
                int cost = Defs[pick.Value].Cost;

                // Budget: retained coffer first, then the zamindar's purse above his floor.
                float retained = GetTreasury(s);
                int fromCoffer = (int)Math.Min(retained, cost);
                int fromPurse = cost - fromCoffer;
                if (fromPurse > 0 && z.Gold - fromPurse < VillageFiefMath.AiGoldFloor(z.IsLord)) continue; // cannot afford; no debt

                _treasury[s.StringId] = retained - fromCoffer;
                if (fromPurse > 0) z.ChangeHeroGold(-fromPurse);
                BeginConstruction(s, pick.Value);
                startsLeft--;
                TYTLog.Info($"AI village work: {z.Name} begins the {Defs[pick.Value].Name} in {s.Name}.");
            }
        }

        private VillageProject? PickAiProject(Settlement s)
        {
            int category = VillageFiefMath.AiPriorityCategory(GetThreat(s), s.Village.Hearth);
            VillageProject[][] order = category == VillageFiefMath.PriorityDefence
                ? new[] { DefenceChain, FoodChain, EconomyChain }
                : category == VillageFiefMath.PriorityFood
                    ? new[] { FoodChain, DefenceChain, EconomyChain }
                    : new[] { EconomyChain, FoodChain, DefenceChain };
            foreach (var chain in order)
                foreach (VillageProject p in chain)
                    if (IsEligible(s, p, out _)) return p;
            return null;
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Menus ────────────────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // NOTE: no mod trade option here. The current game version's village menu already has
            // the vanilla "Buy products" entry; the old "Buy and sell goods" re-exposure duplicated
            // it and its reflection opener no longer matched the engine's InventoryManager.

            starter.AddGameMenuOption("village", "hindostan_village_oversee", "{=!}Oversee your fief",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage;
                          return IsPlayerVillage(Settlement.CurrentSettlement); },
                args => GameMenu.SwitchToMenu("hindostan_village"), false, 3);

            starter.AddGameMenu("hindostan_village", "{=!}{HINDOSTAN_VILLAGE_TEXT}", VillageMenuInit);

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_patrol",
                "{=!}Patrol the village territory",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Continue; return true; },
                args => { Patrol(Settlement.CurrentSettlement); VillageMenuInit(args); });

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_collect",
                "{=!}Collect the village taxes",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Trade;
                    Settlement s = Settlement.CurrentSettlement;
                    args.IsEnabled = GetTreasury(s) >= 1f;
                    if (!args.IsEnabled) args.Tooltip = new TextObject("{=!}The village coffer is empty.");
                    return true;
                },
                args => { CollectTaxes(Settlement.CurrentSettlement); VillageMenuInit(args); });

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_projects",
                "{=!}Open the works ledger",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Manage; return true; },
                args => UI.VillageWorksScreen.Open(Settlement.CurrentSettlement));

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_appoint_zamindar",
                "{=!}Appoint or replace the village zamindar",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
                          return FeudalTitlesBehavior.Instance?.PlayerMayManage(Settlement.CurrentSettlement) ?? false; },
                args => { FeudalTitlesBehavior.Instance?.OpenManageZamindarDialog(Settlement.CurrentSettlement);
                          VillageMenuInit(args); });

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_dismiss_zamindar",
                "{=!}Dismiss the current zamindar",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
                          var ft = FeudalTitlesBehavior.Instance;
                          return ft != null && ft.PlayerMayManage(Settlement.CurrentSettlement)
                                 && ft.GetVillageLord(Settlement.CurrentSettlement) != null; },
                args => { FeudalTitlesBehavior.Instance?.DismissZamindar(Settlement.CurrentSettlement);
                          VillageMenuInit(args); });

            starter.AddGameMenuOption("hindostan_village", "hindostan_village_leave", "{=!}Leave",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu("village"), true);

            // The old text construction sub-menu is gone: the works ledger is a proper Gauntlet
            // screen now (UI/VillageWorksScreen — project list, progress bar, coffer, tax estimate).
        }

        private void VillageMenuInit(MenuCallbackArgs args)
        {
            Settlement s = Settlement.CurrentSettlement;
            var sb = new StringBuilder();
            sb.AppendLine($"Your Fief: {s?.Name}");
            sb.AppendLine(" ");
            if (s?.Village != null)
            {
                sb.AppendLine($"Hearth: {(int)s.Village.Hearth}");
                sb.AppendLine($"Bandit threat: {GetThreat(s):0}/100  ({ThreatBand(GetThreat(s))})");
                Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
                sb.AppendLine($"Zamindar (village lord): {(z != null ? z.Name.ToString() : "vacant")}");
                int steward = z != null ? z.GetSkillValue(DefaultSkills.Steward) : 0;
                sb.AppendLine($"Militia: {(int)s.Militia}    Zamindar stewardship: {steward}");

                float authorityRate = s.MapFaction is Kingdom k && ImperialAuthorityBehavior.Instance != null
                    ? ImperialAuthorityBehavior.Instance.GetTaxCollectionRate(k) : 1f;
                float perDay = VillageFiefMath.DailyTax(s.Village.Hearth, SumBuilt(s, d => d.TaxBonusPct),
                    GetThreat(s), authorityRate, steward, Config.Tune.VillageTaxPerHearth);
                sb.AppendLine($"Coffer: {(int)GetTreasury(s)} dinars  (~{perDay:0.0}/day)");

                if (_reliefUntil.TryGetValue(s.StringId, out int until))
                    sb.AppendLine($"Under relief for {Math.Max(0, until - (int)CampaignTime.Now.ToDays)} more days.");
            }
            if (_buildProject.TryGetValue(s.StringId, out string pn))
            {
                float left = _buildProgress.TryGetValue(s.StringId, out float bp) ? bp
                    : _buildDays.TryGetValue(s.StringId, out int bd) ? bd : 0f;
                VillageProject? cur = ByName(pn);
                float total = cur.HasValue ? Defs[cur.Value].Days : left;
                int pct = total > 0f ? (int)(100f * (1f - left / total)) : 0;
                sb.AppendLine($"Under construction: {pn} — {pct}% ({Math.Ceiling(left):0} days left)");
            }
            if (_queued.TryGetValue(s.StringId, out string qn))
                sb.AppendLine($"Queued next: {qn}");
            string built = _completed.TryGetValue(s.StringId, out string c) ? c : "";
            if (!string.IsNullOrEmpty(built))
            {
                var shown = built.Split(',').Select(n =>
                { VillageProject? bp2 = ByName(n); return bp2.HasValue ? ProjectDisplayName(bp2.Value, s) : n; });
                sb.AppendLine("Works built: " + string.Join(", ", shown));
            }
            else sb.AppendLine("Works built: none");
            sb.AppendLine(" ");
            sb.AppendLine("What is your will?");
            MBTextManager.SetTextVariable("HINDOSTAN_VILLAGE_TEXT", sb.ToString().Replace("\r\n", "\n"), false);
        }

        private static string ThreatBand(float t)
            => t >= 90 ? "the village is being ruined" : t >= 80 ? "growth has halted"
             : t >= 60 ? "growth is slowed" : t >= 40 ? "watchful" : "calm";

        // ── UI façade (the Gauntlet works screen reads and acts ONLY through these) ──
        // One row of the works ledger, flattened for the view model: no engine types leak up.
        public sealed class WorkItem
        {
            public string Id;              // enum name, stable
            public string Name;            // display (Shrine is faith-flavored)
            public string Effect;
            public string PrereqName;      // "" if none
            public int Cost, Days;
            public bool IsBuilt, IsActive, IsQueued;
            public bool CanAct;            // start now, or queue behind the active work
            public bool WillQueue;         // acting queues rather than starts
            public string DisabledReason;  // why CanAct is false
        }

        public List<WorkItem> DescribeWorks(Settlement s)
        {
            var list = new List<WorkItem>();
            if (s == null || !s.IsVillage) return list;
            string activeName = _buildProject.TryGetValue(s.StringId, out string an) ? an : null;
            string queuedName = _queued.TryGetValue(s.StringId, out string qn) ? qn : null;
            foreach (var kv in Defs)
            {
                bool eligible = IsEligible(s, kv.Key, out string why);
                bool canStart = eligible && CanBuild(s, kv.Key, out why);
                bool canQueue = eligible && !canStart && CanQueue(s, kv.Key, out why);
                list.Add(new WorkItem
                {
                    Id = kv.Key.ToString(),
                    Name = ProjectDisplayName(kv.Key, s),
                    Effect = kv.Value.Effect,
                    PrereqName = kv.Value.Requires.HasValue ? Defs[kv.Value.Requires.Value].Name : "",
                    Cost = kv.Value.Cost,
                    Days = kv.Value.Days,
                    IsBuilt = HasProject(s, kv.Key),
                    IsActive = activeName == kv.Value.Name,
                    IsQueued = queuedName == kv.Value.Name,
                    CanAct = canStart || canQueue,
                    WillQueue = canQueue,
                    DisabledReason = why ?? "",
                });
            }
            return list;
        }

        // The work under way: name + fraction done (0..1) + whole days left; false if idle.
        public bool TryGetActiveWork(Settlement s, out string name, out float doneFraction, out int daysLeft)
        {
            name = ""; doneFraction = 0f; daysLeft = 0;
            if (s == null || !_buildProject.TryGetValue(s.StringId, out name)) return false;
            float left = _buildProgress.TryGetValue(s.StringId, out float bp) ? bp
                : _buildDays.TryGetValue(s.StringId, out int bd) ? bd : 0f;
            VillageProject? cur = ByName(name);
            float total = cur.HasValue ? Defs[cur.Value].Days : left;
            doneFraction = total > 0f ? 1f - left / total : 0f;
            daysLeft = (int)Math.Ceiling(left);
            return true;
        }

        public string GetQueuedWorkName(Settlement s)
            => s != null && _queued.TryGetValue(s.StringId, out string qn) ? qn : "";

        public float TaxPerDayEstimate(Settlement s)
        {
            if (s?.Village == null) return 0f;
            Hero z = FeudalTitlesBehavior.Instance?.GetVillageLord(s);
            float authorityRate = s.MapFaction is Kingdom k && ImperialAuthorityBehavior.Instance != null
                ? ImperialAuthorityBehavior.Instance.GetTaxCollectionRate(k) : 1f;
            return VillageFiefMath.DailyTax(s.Village.Hearth, SumBuilt(s, d => d.TaxBonusPct),
                GetThreat(s), authorityRate, z?.GetSkillValue(DefaultSkills.Steward) ?? 0,
                Config.Tune.VillageTaxPerHearth);
        }

        public string GetThreatBand(Settlement s) => ThreatBand(GetThreat(s));

        public void UiStartWork(Settlement s, string workId)
        {
            if (s == null || !Enum.TryParse(workId, out VillageProject p)) return;
            StartBuild(s, p);
        }

        public void UiCollectTaxes(Settlement s) { if (s != null) CollectTaxes(s); }
        public void UiPatrol(Settlement s) { if (s != null) Patrol(s); }

        // ── Save / load ──────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            var threatIds = _threat.Keys.ToList();
            var threatVals = _threat.Values.ToList();
            var compIds = _completed.Keys.ToList();
            var compVals = _completed.Values.ToList();
            var buildIds = _buildProject.Keys.ToList();
            var buildProj = _buildProject.Values.ToList();
            var buildDayIds = _buildDays.Keys.ToList();
            var buildDayVals = _buildDays.Values.ToList();

            dataStore.SyncData("hind_vil_threatIds", ref threatIds);
            dataStore.SyncData("hind_vil_threatVals", ref threatVals);
            dataStore.SyncData("hind_vil_compIds", ref compIds);
            dataStore.SyncData("hind_vil_compVals", ref compVals);
            dataStore.SyncData("hind_vil_buildIds", ref buildIds);
            dataStore.SyncData("hind_vil_buildProj", ref buildProj);
            dataStore.SyncData("hind_vil_buildDayIds", ref buildDayIds);
            dataStore.SyncData("hind_vil_buildDayVals", ref buildDayVals);

            var reliefIds = _reliefUntil.Keys.ToList();
            var reliefVals = _reliefUntil.Values.ToList();
            var reliefTIds = _reliefTroops.Keys.ToList();
            var reliefTVals = _reliefTroops.Values.ToList();
            dataStore.SyncData("hind_vil_reliefIds", ref reliefIds);
            dataStore.SyncData("hind_vil_reliefVals", ref reliefVals);
            dataStore.SyncData("hind_vil_reliefTIds", ref reliefTIds);
            dataStore.SyncData("hind_vil_reliefTVals", ref reliefTVals);

            // Fief-expansion keys (append-only: absent on old saves -> empty lists -> defaults).
            var treasIds = _treasury.Keys.ToList();
            var treasVals = _treasury.Values.ToList();
            var queueIds = _queued.Keys.ToList();
            var queueVals = _queued.Values.ToList();
            var progIds = _buildProgress.Keys.ToList();
            var progVals = _buildProgress.Values.ToList();
            var collectIds = _lastCollectDay.Keys.ToList();
            var collectVals = _lastCollectDay.Values.ToList();
            dataStore.SyncData("hind_vil_treasIds", ref treasIds);
            dataStore.SyncData("hind_vil_treasVals", ref treasVals);
            dataStore.SyncData("hind_vil_queueIds", ref queueIds);
            dataStore.SyncData("hind_vil_queueVals", ref queueVals);
            dataStore.SyncData("hind_vil_progIds", ref progIds);
            dataStore.SyncData("hind_vil_progVals", ref progVals);
            dataStore.SyncData("hind_vil_collectIds", ref collectIds);
            dataStore.SyncData("hind_vil_collectVals", ref collectVals);

            if (!dataStore.IsSaving)
            {
                _threat = Zip(threatIds, threatVals);
                _completed = Zip(compIds, compVals);
                _buildProject = Zip(buildIds, buildProj);
                _buildDays = Zip(buildDayIds, buildDayVals);
                _lastPatrolDay = new Dictionary<string, int>();
                _reliefUntil = Zip(reliefIds, reliefVals);
                _reliefTroops = Zip(reliefTIds, reliefTVals);
                _lastOverwhelmDay = new Dictionary<string, int>();
                _treasury = Zip(treasIds, treasVals);
                _queued = Zip(queueIds, queueVals);
                _buildProgress = Zip(progIds, progVals);
                _lastCollectDay = Zip(collectIds, collectVals);

                // Migration: an old save has integer _buildDays but no fractional progress.
                foreach (var kv in _buildDays)
                    if (!_buildProgress.ContainsKey(kv.Key)) _buildProgress[kv.Key] = kv.Value;
            }
        }

        private static Dictionary<string, T> Zip<T>(List<string> keys, List<T> vals)
        {
            var d = new Dictionary<string, T>();
            for (int i = 0; i < keys.Count && i < vals.Count; i++) d[keys[i]] = vals[i];
            return d;
        }

        // ── Console (testing) ──────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("village_status", "hindostan")]
        public static string VillageStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            var villages = Settlement.All.Where(s => s.IsVillage && Instance.IsPlayerVillage(s)).ToList();
            if (villages.Count == 0) return "Your clan holds no villages.";
            return string.Join("\n", villages.Select(s =>
                $"{s.Name}: hearth {(int)s.Village.Hearth}, threat {Instance.GetThreat(s):0}, " +
                $"coffer {(int)Instance.GetTreasury(s)}" +
                (Instance._buildProject.TryGetValue(s.StringId, out string bp) ? $", building {bp}" : "")));
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set_village_threat", "hindostan")]
        public static string SetThreat(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Settlement.CurrentSettlement == null || !Settlement.CurrentSettlement.IsVillage)
                return "Enter one of your villages first.";
            float v = 50f;
            if (args != null && args.Count > 0) float.TryParse(args[0], out v);
            Instance._threat[Settlement.CurrentSettlement.StringId] = MathF.Max(0f, MathF.Min(100f, v));
            return $"{Settlement.CurrentSettlement.Name} threat set to {v:0}.";
        }
    }
}
