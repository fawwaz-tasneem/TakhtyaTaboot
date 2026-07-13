using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // War exhaustion, native (no-Diplomacy mandate). Every side of every kingdom-vs-kingdom war
    // carries an exhaustion score fed by casualties, fiefs lost, villages raided, and the grind
    // of time (WarExhaustionMath, tested; smaller realms feel each loss harder). When a side is
    // SPENT (100):
    //   • an AI-led realm sues for peace with that enemy (plain MakePeaceAction — which the
    //     NoThroneWarPeacePatch still guards, so a throne war can never slip through);
    //   • the PLAYER-ruled realm is never forced — the sovereign dictates his own peace from the
    //     war menu — but the realm's authority bleeds daily while he ignores a spent people.
    // INTEGRATION RULES (why this is native): wars involving live claim kingdoms (hind_rebel_*:
    // accession wars, AI civil wars, secession wars) are NEVER tracked — those are binary and
    // settle by their own deadlines. Exhaustion lingers after peace (decaying), so back-to-back
    // wars begin already weary. WarfareBehavior reads Exhaustion() for its council advisory and
    // war-menu display instead of its old time-only weariness.
    public class WarExhaustionBehavior : CampaignBehaviorBase
    {
        public static WarExhaustionBehavior Instance { get; private set; }

        // What wore a realm down. Exhaustion accrued from named sources and then threw the provenance
        // away into one anonymous float — so the number could never explain itself. It is now itemized
        // alongside the total (ch.30 §3a: if it moves a bar, it must be nameable in the tooltip).
        public enum Source { Casualties = 0, FiefsLost = 1, VillagesRaided = 2, TheGrindOfTime = 3 }

        public static string Describe(Source s)
        {
            switch (s)
            {
                case Source.Casualties:     return "Casualties";
                case Source.FiefsLost:      return "Fiefs lost";
                case Source.VillagesRaided: return "Villages harried";
                default:                    return "The grind of time";
            }
        }

        // "myKingdomId|enemyKingdomId" -> my side's exhaustion in that war (0..100).
        private Dictionary<string, float> _exhaustion = new Dictionary<string, float>();
        // "myKingdomId|enemyKingdomId|source" -> how much of that total came from THIS source.
        private Dictionary<string, float> _components = new Dictionary<string, float>();
        private int _lastPeaceDay = -1;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("WarExhaustion.Daily", OnDailyTick));
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, e => TYTLog.GuardQuiet("WarExhaustion.MapEvent", () => OnMapEventEnded(e)));
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
        }

        // ── Reads ────────────────────────────────────────────────────────────────────
        public float Exhaustion(Kingdom mine, Kingdom enemy)
            => mine != null && enemy != null && _exhaustion.TryGetValue(Key(mine, enemy), out float v) ? v : 0f;

        private static string Key(Kingdom mine, Kingdom enemy) => mine.StringId + "|" + enemy.StringId;

        // A war this system tracks: two real kingdoms, neither a live claim kingdom.
        private static bool Tracked(Kingdom a, Kingdom b)
            => a != null && b != null && a != b && !a.IsEliminated && !b.IsEliminated
               && !ThroneWar.IsRebelKingdom(a) && !ThroneWar.IsRebelKingdom(b);

        private static float RealmStrength(Kingdom k)
            => k?.Clans?.Where(c => c != null && !c.IsEliminated).Sum(c => c.CurrentTotalStrength) ?? 1f;

        private void Add(Kingdom mine, Kingdom enemy, float points, Source source)
        {
            if (!Tracked(mine, enemy)) return;
            float scale = WarExhaustionMath.StrengthScale(RealmStrength(mine));

            float before = Exhaustion(mine, enemy);
            float after = WarExhaustionMath.Accrue(before, points, scale);
            _exhaustion[Key(mine, enemy)] = after;

            // Credit the source with what it ACTUALLY added (the cap may have swallowed some of it), so
            // the components always sum to the total the player is shown.
            string ck = Key(mine, enemy) + "|" + (int)source;
            _components[ck] = (_components.TryGetValue(ck, out float c) ? c : 0f) + (after - before);
        }

        // The itemized reason this realm is tired. Empty on a save that predates the itemization — the
        // total is still right, only its provenance was never recorded.
        public List<(string label, float value)> BreakdownOf(Kingdom mine, Kingdom enemy)
        {
            var rows = new List<(string, float)>();
            if (mine == null || enemy == null) return rows;
            string prefix = Key(mine, enemy) + "|";
            foreach (Source s in System.Enum.GetValues(typeof(Source)))
                if (_components.TryGetValue(prefix + (int)s, out float v) && v >= 0.5f)
                    rows.Add((Describe(s), v));
            return rows.OrderByDescending(r => r.Item2).ToList();
        }

        // ── Accrual ──────────────────────────────────────────────────────────────────
        private void OnMapEventEnded(MapEvent e)
        {
            if (e == null || !WorldGen.Ready) return;
            Kingdom att = e.AttackerSide?.MapFaction as Kingdom;
            Kingdom def = e.DefenderSide?.MapFaction as Kingdom;
            if (!Tracked(att, def)) return;

            if (e.IsRaid)
            {
                Add(def, att, WarExhaustionMath.PerVillageRaided, Source.VillagesRaided);
                return;
            }
            // Blood on both sides, each weighed against its own realm's size.
            Add(att, def, e.AttackerSide.TroopCasualties * WarExhaustionMath.PerCasualty, Source.Casualties);
            Add(def, att, e.DefenderSide.TroopCasualties * WarExhaustionMath.PerCasualty, Source.Casualties);
        }

        private void OnOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!WorldGen.Ready || s == null || (!s.IsTown && !s.IsCastle)) return;
            if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege) return;
            Kingdom loser = oldOwner?.Clan?.Kingdom;
            Kingdom winner = newOwner?.Clan?.Kingdom;
            Add(loser, winner, WarExhaustionMath.PerFiefLost, Source.FiefsLost);
        }

        private void OnDailyTick()
        {
            if (!WorldGen.Ready) return;
            int today = (int)CampaignTime.Now.ToDays;
            if (_lastPeaceDay == today) return;
            _lastPeaceDay = today;

            // One pass over the ledger: creep while at war, decay in peace, drop dead pairs.
            foreach (string key in _exhaustion.Keys.ToList())
            {
                var (mine, enemy) = Split(key);
                if (mine == null || enemy == null || mine.IsEliminated || enemy.IsEliminated)
                { _exhaustion.Remove(key); continue; }

                if (mine.IsAtWarWith(enemy))
                    Add(mine, enemy, WarExhaustionMath.DailyCreep, Source.TheGrindOfTime);
                else
                {
                    float decayed = WarExhaustionMath.DecayInPeace(_exhaustion[key]);
                    if (decayed <= 0f)
                    {
                        _exhaustion.Remove(key);
                        foreach (Source s in System.Enum.GetValues(typeof(Source)))
                            _components.Remove(key + "|" + (int)s);
                    }
                    else
                    {
                        // Fade the components in step with the total, so the breakdown never claims more
                        // than the realm actually feels.
                        float ratio = _exhaustion[key] <= 0f ? 0f : decayed / _exhaustion[key];
                        _exhaustion[key] = decayed;
                        foreach (Source s in System.Enum.GetValues(typeof(Source)))
                        {
                            string ck = key + "|" + (int)s;
                            if (_components.TryGetValue(ck, out float v)) _components[ck] = v * ratio;
                        }
                    }
                }
            }

            // Ensure every live tracked war has entries (wars that predate the behavior, joins).
            foreach (Kingdom a in Kingdom.All.Where(k => !k.IsEliminated && !ThroneWar.IsRebelKingdom(k)))
                foreach (Kingdom b in Kingdom.All.Where(k => k != a && !k.IsEliminated && !ThroneWar.IsRebelKingdom(k) && a.IsAtWarWith(k)))
                    if (!_exhaustion.ContainsKey(Key(a, b))) _exhaustion[Key(a, b)] = 0f;

            // The spent sue for peace — or, ruled by the player, bleed while he refuses to.
            foreach (string key in _exhaustion.Keys.ToList())
            {
                if (!WarExhaustionMath.SuesForPeace(_exhaustion[key])) continue;
                var (mine, enemy) = Split(key);
                if (mine == null || enemy == null || !mine.IsAtWarWith(enemy)) continue;

                if (mine.Leader == Hero.MainHero)
                {
                    // The sovereign is told once, then pays for every day of deafness.
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(mine, -0.3f, "a spent realm kept at war");
                    RoyalFarmaan.Issue("The Realm Is Spent", $"From the Imperial Council of {mine.Name}",
                        $"The war with {enemy.Name} has bled the realm white — the treasury groans, the villages empty, " +
                        "the lords mutter. The council implores you: make peace from the war menu, or the throne's " +
                        "authority will bleed for every further day of it.",
                        seal: null, primary: "I hear the realm's groaning",
                        dedupeKey: "spent:" + enemy.StringId, cooldownDays: 15);
                    continue;
                }

                // An AI-led realm concludes peace. Plain MakePeaceAction: the throne-war patch
                // still stands guard behind it, so nothing binary can slip through.
                try
                {
                    MakePeaceAction.Apply(mine, enemy);
                    if (Hero.MainHero?.Clan?.Kingdom == mine || Hero.MainHero?.Clan?.Kingdom == enemy)
                        Notify($"{mine.Name} is spent by the war and has sued for peace with {enemy.Name}.", false);
                    TYTLog.Info($"WarExhaustion: {mine.Name} (spent) made peace with {enemy.Name}.");
                }
                catch (Exception e2) { TYTLog.Error("WarExhaustion: peace failed", e2); }
            }
        }

        private static (Kingdom, Kingdom) Split(string key)
        {
            int i = key.IndexOf('|');
            if (i <= 0) return (null, null);
            string a = key.Substring(0, i), b = key.Substring(i + 1);
            return (Kingdom.All.FirstOrDefault(k => k.StringId == a), Kingdom.All.FirstOrDefault(k => k.StringId == b));
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        public override void SyncData(IDataStore dataStore)
        {
            var keys = _exhaustion.Keys.ToList();
            var vals = _exhaustion.Values.ToList();
            var ckeys = _components.Keys.ToList();
            var cvals = _components.Values.ToList();
            dataStore.SyncData("hind_wex_keys", ref keys);
            dataStore.SyncData("hind_wex_vals", ref vals);
            dataStore.SyncData("hind_wex_ckeys", ref ckeys);
            dataStore.SyncData("hind_wex_cvals", ref cvals);
            if (!dataStore.IsSaving)
            {
                _exhaustion = new Dictionary<string, float>();
                for (int i = 0; i < keys.Count && i < vals.Count; i++) _exhaustion[keys[i]] = vals[i];

                // A save from before the itemization simply has no components: the totals stay right and
                // the breakdown fills in from here on.
                _components = new Dictionary<string, float>();
                if (ckeys != null && cvals != null)
                    for (int i = 0; i < ckeys.Count && i < cvals.Count; i++) _components[ckeys[i]] = cvals[i];
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("exhaustion_status", "hindostan")]
        public static string ExhaustionStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Instance._exhaustion.Count == 0) return "No war exhaustion tracked (no eligible wars).";
            return string.Join("\n", Instance._exhaustion.OrderByDescending(kv => kv.Value).Take(20).Select(kv =>
            {
                var (m, e) = Split(kv.Key);
                bool atWar = m != null && e != null && m.IsAtWarWith(e);
                return $"{m?.Name} vs {e?.Name}: {kv.Value:0} ({WarExhaustionMath.Tier(kv.Value)}){(atWar ? "" : " [at peace, decaying]")}";
            }));
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set_exhaustion", "hindostan")]
        public static string SetExhaustion(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (args == null || args.Count < 1) return "Usage: hindostan.set_exhaustion <value> [enemy kingdom name part] — sets YOUR realm's exhaustion vs the enemy (or all its wars).";
            if (!float.TryParse(args[0], out float v)) return "First argument must be a number.";
            Kingdom mine = Hero.MainHero?.Clan?.Kingdom;
            if (mine == null) return "You serve no realm.";
            string filter = args.Count > 1 ? string.Join(" ", args.Skip(1)).ToLowerInvariant() : null;
            int set = 0;
            foreach (Kingdom e in Kingdom.All.Where(k => k != mine && !k.IsEliminated && mine.IsAtWarWith(k)))
            {
                if (filter != null && !e.Name.ToString().ToLowerInvariant().Contains(filter)) continue;
                Instance._exhaustion[Key(mine, e)] = MathF.Max(0f, MathF.Min(100f, v));
                set++;
            }
            return set > 0 ? $"Exhaustion set to {v:0} for {set} war(s) of {mine.Name}." : "No matching war.";
        }
    }
}
