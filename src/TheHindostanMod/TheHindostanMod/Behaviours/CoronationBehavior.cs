using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Coronation ceremonies (roadmap B.1) — the darbar of accession. When a new sovereign
    // takes a throne the great houses are summoned to swear; who comes and who leaves an
    // empty place turns on what each house head thinks of him (his effective opinion). The
    // building blocks all pre-date this: SworeFealty/MissedCeremony opinion records, the
    // Ceremonial farmaan priority, the grievance dialogue that absentees become targets of.
    //
    // Two player-facing beats:
    //   • Player is the NEW sovereign — he holds court; AI house heads attend or snub by their
    //     regard for him. Attendees warm to him (SworeFealty), absentees are noted
    //     (MissedCeremony) and he may DEMAND a late oath — harder to win than attendance.
    //   • Player is a VASSAL of the new sovereign — he is summoned to swear: travel and bend
    //     the knee (SworeFealty, relation up) or deliberately stay away (MissedCeremony, the
    //     sovereign remembers).
    // AI-only accessions resolve silently — this is a player-facing ceremony, not court spam.
    // Attendance/late-oath odds are deterministic and tested (CoronationMath).
    public class CoronationBehavior : CampaignBehaviorBase
    {
        public static CoronationBehavior Instance { get; private set; }

        // kingdomId -> the ruler we last saw on that throne. Seeded at session launch so no
        // ceremony fires for the thrones already standing when the campaign loads.
        private Dictionary<string, string> _lastRuler = new Dictionary<string, string>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Coronation.DailyTick", OnDailyTick));
        }

        private void OnSessionLaunched(CampaignGameStarter starter) => Snapshot();

        private void Snapshot()
        {
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null))
                _lastRuler[k.StringId] = k.Leader.StringId;
        }

        // ── Accession detection ──────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (!Util.WorldGen.Ready) return;
            // The scripted 1707 cascade crowns and kills emperors in rapid succession; don't
            // stage a darbar on every beat of it. Keep the snapshot fresh and move on.
            if (Util.ScriptedSuccession.InProgress) { Snapshot(); return; }

            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null).ToList())
            {
                string now = k.Leader.StringId;
                if (!_lastRuler.TryGetValue(k.StringId, out string prev))
                {
                    _lastRuler[k.StringId] = now;
                    // A realm we have never seen: kingdoms standing at session launch were
                    // seeded by Snapshot(), so this is a kingdom FOUNDED mid-campaign — a
                    // genuine accession (the playtest gap: founding your own kingdom gave no
                    // coronation). The mod's temporary claim kingdoms (hind_rebel_*) are a war
                    // measure, not a throne — no darbar for them.
                    if (!k.StringId.StartsWith("hind_rebel") && k.Leader.IsAlive && !k.Leader.IsChild)
                        TYTLog.Guard("Coronation.Found:" + k.Name, () => HoldCoronation(k));
                    continue;
                }
                if (prev == now) continue;

                _lastRuler[k.StringId] = now;
                if (k.Leader.IsAlive && !k.Leader.IsChild)
                    TYTLog.Guard("Coronation.Hold:" + k.Name, () => HoldCoronation(k));
            }

            // Forget thrones that have fallen, so a re-formed kingdom with the same id crowns afresh.
            foreach (string id in _lastRuler.Keys.ToList())
                if (Kingdom.All.FirstOrDefault(x => x.StringId == id)?.IsEliminated ?? true)
                    _lastRuler.Remove(id);
        }

        private void HoldCoronation(Kingdom k)
        {
            if (k?.Leader == null) return;
            Hero sovereign = k.Leader;

            if (sovereign == Hero.MainHero) { PlayerSovereignDarbar(k); return; }

            // Player is a vassal of this realm (a member clan, not the ruling one): he is summoned.
            if (Hero.MainHero?.Clan?.Kingdom == k && Hero.MainHero.Clan != k.RulingClan)
                VassalSummons(k, sovereign);

            // Otherwise an AI accession elsewhere — resolved silently.
        }

        // ── The player holds his coronation darbar ───────────────────────────────────
        private void PlayerSovereignDarbar(Kingdom k)
        {
            var heads = k.Clans
                .Where(c => !c.IsEliminated && !c.IsMinorFaction && c.Leader != null
                            && c.Leader != Hero.MainHero && c.Leader.IsAlive && !c.Leader.IsChild)
                .Select(c => c.Leader).ToList();

            var attended = new List<Hero>();
            var absent = new List<Hero>();
            var op = OpinionBehavior.Instance;
            foreach (Hero head in heads)
            {
                float opinion = op?.EffectiveOpinion(head, Hero.MainHero) ?? 0f;
                if (CoronationMath.Attends(opinion, MBRandom.RandomFloat))
                {
                    attended.Add(head);
                    op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.SworeFealty);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, +2, false);
                }
                else
                {
                    absent.Add(head);
                    op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.MissedCeremony);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, -3, false);
                }
            }

            // A firm accession lends legitimacy; an empty hall costs it.
            LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero,
                heads.Count == 0 ? 0f : (attended.Count - absent.Count) * 1.5f, "the coronation darbar");

            string body =
                $"{RoyalFarmaan.NameWithHonorific(Hero.MainHero)} takes the throne of {k.Name}, and the great houses are " +
                $"summoned to the darbar to swear.\n \n{CoronationMath.LoyaltyVerdict(attended.Count, heads.Count)}\n \n" +
                (heads.Count == 0
                    ? "No vassal houses yet answer to your throne."
                    : $"Bent the knee ({attended.Count}): {NameList(attended)}.\n \n" +
                      (absent.Count == 0
                          ? "Not one house left an empty place."
                          : $"Left an empty place ({absent.Count}): {NameList(absent)}."));

            if (absent.Count > 0)
                RoyalFarmaan.Issue("Your Coronation Darbar", $"The court of {k.Name}", body,
                    seal: "Held this day, " + RoyalFarmaan.CurrentDate(),
                    primary: "Demand a late oath of the absent houses",
                    onPrimary: () => TYTLog.Guard("Coronation.LateOath", () => DemandLateOath(absent)),
                    secondary: "Let their absence stand — and be remembered",
                    onSecondary: () => Notify("You let the empty places stand. The court will not forget who was missing.", true),
                    priority: FarmaanPriority.Ceremonial);
            else
                RoyalFarmaan.Issue("Your Coronation Darbar", $"The court of {k.Name}", body,
                    seal: "Held this day, " + RoyalFarmaan.CurrentDate(),
                    primary: "Rise as sovereign", priority: FarmaanPriority.Ceremonial);

            TYTLog.Info($"Coronation: player crowned in {k.Name}; {attended.Count} attended, {absent.Count} absent.");
        }

        private void DemandLateOath(List<Hero> absent)
        {
            var op = OpinionBehavior.Instance;
            var bent = new List<Hero>();
            var defied = new List<Hero>();
            foreach (Hero head in absent.Where(h => h != null && h.IsAlive))
            {
                float opinion = op?.EffectiveOpinion(head, Hero.MainHero) ?? 0f;
                if (CoronationMath.AcceptsLateOath(opinion, MBRandom.RandomFloat))
                {
                    bent.Add(head);
                    op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.SworeFealty);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, +3, false);
                }
                else
                {
                    defied.Add(head);
                    op?.AddOpinion(Hero.MainHero, head, OpinionMath.OpinionType.Grudge);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, head, -5, false);
                }
            }

            string body =
                (bent.Count > 0 ? $"Bent the knee at your demand ({bent.Count}): {NameList(bent)}.\n \n" : "") +
                (defied.Count > 0
                    ? $"Defied you openly ({defied.Count}): {NameList(defied)}. The grudge is set down in the register; take it up with them at court if you would."
                    : "None dared defy the demand.");
            RoyalFarmaan.Issue("The Late Oath", "The court's demand answered", body,
                primary: "Noted", priority: FarmaanPriority.Ceremonial);
            TYTLog.Info($"Coronation: late oath — {bent.Count} bent, {defied.Count} defied.");
        }

        // ── The player is summoned to a new sovereign's coronation ───────────────────
        private void VassalSummons(Kingdom k, Hero sovereign)
        {
            RoyalFarmaan.FromRuler(k, "A Summons to the Coronation",
                $"{RoyalFarmaan.NameWithHonorific(sovereign)} accedes to the throne of {k.Name} and summons the vassals " +
                "of the realm to the darbar to renew their oath. Will you travel to court and swear, or send your regrets " +
                "and keep your distance?",
                primary: "Travel to the darbar and swear the oath",
                onPrimary: () => TYTLog.Guard("Coronation.Swear", () => SwearToSovereign(sovereign, true)),
                secondary: "Send regrets — stay away",
                onSecondary: () => TYTLog.Guard("Coronation.Snub", () => SwearToSovereign(sovereign, false)),
                priority: FarmaanPriority.Ceremonial);
        }

        private void SwearToSovereign(Hero sovereign, bool sworn)
        {
            var op = OpinionBehavior.Instance;
            if (sovereign == null || !sovereign.IsAlive) return;
            if (sworn)
            {
                op?.AddOpinion(sovereign, Hero.MainHero, OpinionMath.OpinionType.SworeFealty);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, sovereign, +5, false);
                Notify($"You bend the knee before {sovereign.Name}. Your oath is renewed, and the new sovereign marks your loyalty.", false);
            }
            else
            {
                op?.AddOpinion(sovereign, Hero.MainHero, OpinionMath.OpinionType.MissedCeremony);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, sovereign, -8, false);
                Notify($"You stay away from the coronation of {sovereign.Name}. He marks the empty place where you should have stood.", true);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────
        private static string NameList(List<Hero> heroes)
        {
            var names = heroes.Where(h => h != null).Select(h => h.Name.ToString()).ToList();
            if (names.Count <= 6) return string.Join(", ", names);
            return string.Join(", ", names.Take(6)) + $", and {names.Count - 6} more";
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        public override void SyncData(IDataStore dataStore)
        {
            var ids = _lastRuler.Keys.ToList();
            var rulers = _lastRuler.Values.ToList();
            dataStore.SyncData("hind_coron_kids", ref ids);
            dataStore.SyncData("hind_coron_rulers", ref rulers);
            if (!dataStore.IsSaving)
            {
                _lastRuler = new Dictionary<string, string>();
                for (int i = 0; i < ids.Count && i < rulers.Count; i++) _lastRuler[ids[i]] = rulers[i];
            }
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("coronation_test", "hindostan")]
        public static string CoronationTest(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k == null) return "You serve no realm.";
            Instance.HoldCoronation(k);
            return $"Coronation darbar staged for {k.Name} (as if {k.Leader?.Name} just acceded).";
        }
    }
}
