using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Disaffection conspiracies (no-Diplomacy mandate): the Diplomacy mod's secession and
    // abdication factions rebuilt natively on this mod's own systems. Lords whose PERSONAL
    // regard for their sovereign has curdled (the opinion ledger, not raw relation) band
    // together; once the cabal has heads, muscle and time (DisaffectionMath, tested), it
    // serves an ULTIMATUM:
    //   • ABDICATION — the realm has a lawful heir and the ruler's legitimacy is low: the
    //     quarrel is with the man. Yield, and the heir accedes (CoronationBehavior stages the
    //     darbar on its own). Refuse, and the cabal's strongest house raises a leadership
    //     civil war through the EXISTING CivilWarBehavior machinery.
    //   • SECESSION — no heir worth raising: the quarrel is with the realm. Yield, and the
    //     malcontents depart in peace as a new kingdom. Refuse, and they tear free by war.
    // A seceded state that survives GRADUATES (ThroneWar.Graduate): its hind_rebel_* id stops
    // meaning "claim kingdom", it makes peace like any realm, and it holds a founding darbar.
    //
    // Player experience v1: as RULER you get the spymaster's warning (if one is seated) and
    // the ultimatum choice; as a VASSAL you get the side-choice when a secession war breaks
    // (abdication wars already ask via CivilWarBehavior). The player's clan never joins an AI
    // cabal in v1 — joining/instigating conspiracies is a follow-up.
    public class DisaffectionBehavior : CampaignBehaviorBase
    {
        public static DisaffectionBehavior Instance { get; private set; }

        // One conspiracy per kingdom: kingdomId -> (formedDay, member clan ids).
        private Dictionary<string, int> _formedDay = new Dictionary<string, int>();
        private Dictionary<string, List<string>> _members = new Dictionary<string, List<string>>();

        // At most one secession WAR at a time (like the AI civil war: a defining convulsion).
        private bool _warActive;
        private string _warOriginId = "", _warRebelId = "", _warLeadHeroId = "";
        private int _warDeadline = -1;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Disaffection.Weekly", OnWeeklyTick));
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Disaffection.Daily", OnDailyTick));
        }

        public override void SyncData(IDataStore ds)
        {
            var kIds = _formedDay.Keys.ToList();
            var kDays = _formedDay.Values.ToList();
            var kMembers = kIds.Select(id => string.Join(",", _members.TryGetValue(id, out var m) ? m : new List<string>())).ToList();
            ds.SyncData("hind_disaff_kIds", ref kIds);
            ds.SyncData("hind_disaff_kDays", ref kDays);
            ds.SyncData("hind_disaff_kMembers", ref kMembers);

            ds.SyncData("hind_disaff_warActive", ref _warActive);
            ds.SyncData("hind_disaff_warOrigin", ref _warOriginId);
            ds.SyncData("hind_disaff_warRebel", ref _warRebelId);
            ds.SyncData("hind_disaff_warLead", ref _warLeadHeroId);
            ds.SyncData("hind_disaff_warDeadline", ref _warDeadline);

            // The graduation register lives in the static ThroneWar; this behavior persists it.
            var grads = ThroneWar.GraduatedIds;
            ds.SyncData("hind_disaff_graduated", ref grads);

            if (!ds.IsSaving)
            {
                _formedDay = new Dictionary<string, int>();
                _members = new Dictionary<string, List<string>>();
                for (int i = 0; i < kIds.Count && i < kDays.Count; i++)
                {
                    _formedDay[kIds[i]] = kDays[i];
                    _members[kIds[i]] = i < kMembers.Count && !string.IsNullOrEmpty(kMembers[i])
                        ? kMembers[i].Split(',').ToList() : new List<string>();
                }
                ThroneWar.LoadGraduated(grads);
            }
        }

        // The safety net asks whose breakaway a rebel kingdom is (fold-home on destruction).
        public Kingdom OriginRealmOf(string rebelKingdomId)
            => _warActive && rebelKingdomId == _warRebelId
                ? Kingdom.All.FirstOrDefault(k => k.StringId == _warOriginId) : null;

        // ── The weekly conspiracy scan ───────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            if (!WorldGen.Ready) return;

            foreach (Kingdom k in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null && !ThroneWar.IsRebelKingdom(x)).ToList())
            {
                if (SuccessionBehavior.Instance?.GetCrisisState(k) != CrisisState.None) continue;
                // A realm already fighting a breakaway is convulsed enough (same rule as CivilWar).
                if (Kingdom.All.Any(r => ThroneWar.IsRebelKingdom(r) && !r.IsEliminated && r.IsAtWarWith(k))) continue;

                if (_members.ContainsKey(k.StringId)) TYTLog.Guard("Disaffection.Tend:" + k.Name, () => TendConspiracy(k));
                else TYTLog.Guard("Disaffection.Seed:" + k.Name, () => MaybeFormConspiracy(k));
            }

            // Realms that vanished take their cabals with them.
            foreach (string id in _members.Keys.ToList())
                if (Kingdom.All.FirstOrDefault(x => x.StringId == id)?.IsEliminated ?? true)
                { _members.Remove(id); _formedDay.Remove(id); }
        }

        private static float OpinionOfRuler(Clan c, Kingdom k)
            => OpinionBehavior.Instance?.EffectiveOpinion(c.Leader, k.Leader)
               ?? CharacterRelationManager.GetHeroRelation(c.Leader, k.Leader);

        private static bool Eligible(Clan c, Kingdom k)
            => c != null && !c.IsEliminated && !c.IsUnderMercenaryService && !c.IsMinorFaction
               && c.Leader != null && c.Leader.IsAlive && c != k.RulingClan && c != Clan.PlayerClan;

        private void MaybeFormConspiracy(Kingdom k)
        {
            var disaffected = k.Clans.Where(c => Eligible(c, k) && DisaffectionMath.IsDisaffected(OpinionOfRuler(c, k))).ToList();
            if (disaffected.Count == 0) return;
            float plot = disaffected.Sum(c => c.CurrentTotalStrength);
            float loyal = Math.Max(1f, k.Clans.Where(c => !c.IsEliminated && !disaffected.Contains(c)).Sum(c => c.CurrentTotalStrength));
            if (!DisaffectionMath.ConspiracyForms(disaffected.Count, plot, loyal)) return;

            _formedDay[k.StringId] = (int)CampaignTime.Now.ToDays;
            _members[k.StringId] = disaffected.Select(c => c.StringId).ToList();
            TYTLog.Info($"Disaffection: a cabal of {disaffected.Count} house(s) forms in {k.Name}.");

            // A seated spymaster earns his keep: the player-ruler hears the whispers early.
            if (k.Leader == Hero.MainHero
                && CouncilBehavior.Instance?.GetCouncillor(k, CouncilBehavior.Post.Spymaster) != null)
                Notify("Your spymaster leans close: there are whispers of a cabal among the disaffected houses. Mend their grievances — or ready the host.", true);
        }

        private void TendConspiracy(Kingdom k)
        {
            var ids = _members[k.StringId];
            var clans = ids.Select(id => Clan.All.FirstOrDefault(c => c.StringId == id))
                .Where(c => Eligible(c, k) && c.Kingdom == k && DisaffectionMath.IsDisaffected(OpinionOfRuler(c, k)))
                .ToList();
            _members[k.StringId] = clans.Select(c => c.StringId).ToList();

            // Mended grievances dissolve the plot quietly.
            if (clans.Count < DisaffectionMath.MinClans)
            {
                _members.Remove(k.StringId); _formedDay.Remove(k.StringId);
                if (k.Leader == Hero.MainHero) Notify("The whispers of a cabal fade — the disaffected houses have drifted apart.", false);
                return;
            }

            float plot = clans.Sum(c => c.CurrentTotalStrength);
            float loyal = Math.Max(1f, k.Clans.Where(c => !c.IsEliminated && !clans.Contains(c)).Sum(c => c.CurrentTotalStrength));
            int simmered = (int)CampaignTime.Now.ToDays - _formedDay[k.StringId];
            if (!DisaffectionMath.UltimatumReady(simmered, plot, loyal)) return;

            // Ultimatum time — but the war machinery is a shared bottleneck: if a convulsion is
            // already raging anywhere, the cabal bides its time another week.
            if (_warActive || CivilWarBehavior.Instance?.IsActive == true || AccessionWarBehavior.Instance?.IsActive == true) return;

            ServeUltimatum(k, clans, plot, loyal);
        }

        // ── The ultimatum ────────────────────────────────────────────────────────────
        private void ServeUltimatum(Kingdom k, List<Clan> cabal, float plot, float loyal)
        {
            Hero heir = SuccessionLawBehavior.Instance?.LawfulHeir(k);
            if (heir != null && (!heir.IsAlive || heir == k.Leader)) heir = null;
            float legit = LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 60f;
            bool abdication = DisaffectionMath.DemandsAbdication(heir != null, legit);
            Clan lead = cabal.OrderByDescending(c => c.CurrentTotalStrength).First();

            // The plot is spent the moment it speaks, whatever the answer.
            _members.Remove(k.StringId); _formedDay.Remove(k.StringId);

            TYTLog.Info($"Disaffection: ultimatum in {k.Name} — {(abdication ? "abdication" : "secession")}, led by {lead.Name}.");

            if (k.Leader == Hero.MainHero) { PlayerUltimatum(k, cabal, lead, heir, abdication); return; }

            // An AI ruler weighs the odds.
            if (DisaffectionMath.AiRulerYields(plot, loyal, legit, MBRandom.RandomFloat))
            {
                if (abdication) Abdicate(k, heir);
                else GrantIndependence(k, cabal, lead);
            }
            else
            {
                if (abdication) RefusedAbdicationWar(k, lead);
                else StartSecessionWar(k, cabal, lead);
            }
        }

        private void PlayerUltimatum(Kingdom k, List<Clan> cabal, Clan lead, Hero heir, bool abdication)
        {
            string names = string.Join(", ", cabal.Take(5).Select(c => c.Name.ToString()))
                           + (cabal.Count > 5 ? $" and {cabal.Count - 5} more" : "");
            if (abdication && heir != null)
            {
                RoyalFarmaan.Issue("An Ultimatum from the Disaffected Houses",
                    $"Sealed by {lead.Name} and the confederate houses",
                    $"The houses of {names} declare, with one seal, that they no longer suffer your rule. They demand you " +
                    $"abdicate the throne of {k.Name} in favour of {heir.Name}, the lawful heir — or they will put the " +
                    "matter to the sword.",
                    seal: "Delivered by a herald who did not wait for an answer",
                    primary: $"Abdicate in favour of {heir.Name}",
                    onPrimary: () => TYTLog.Guard("Disaffection.PlayerAbdicate", () => Abdicate(k, heir)),
                    secondary: "Refuse — let them come",
                    onSecondary: () => TYTLog.Guard("Disaffection.PlayerRefuseAbd", () => RefusedAbdicationWar(k, lead)),
                    priority: FarmaanPriority.Ceremonial);
            }
            else
            {
                RoyalFarmaan.Issue("An Ultimatum from the Disaffected Houses",
                    $"Sealed by {lead.Name} and the confederate houses",
                    $"The houses of {names} declare, with one seal, that they will no longer be ruled from your throne. " +
                    $"They demand leave to depart {k.Name} and govern themselves — or they will tear themselves free by war.",
                    seal: "Delivered by a herald who did not wait for an answer",
                    primary: "Let them go — grant independence",
                    onPrimary: () => TYTLog.Guard("Disaffection.PlayerGrant", () => GrantIndependence(k, cabal, lead)),
                    secondary: "Refuse — no realm sunders itself while I reign",
                    onSecondary: () => TYTLog.Guard("Disaffection.PlayerRefuseSec", () => StartSecessionWar(k, cabal, lead)),
                    priority: FarmaanPriority.Ceremonial);
            }
        }

        // ── The four endings ─────────────────────────────────────────────────────────
        private void Abdicate(Kingdom k, Hero heir)
        {
            if (k == null || heir == null || !heir.IsAlive || k.IsEliminated) return;
            Hero old = k.Leader;
            try
            {
                if (heir.Clan != null && heir.Clan != k.RulingClan) ChangeRulingClanAction.Apply(k, heir.Clan);
                if (heir.Clan != null && heir.Clan.Leader != heir)
                    ChangeClanLeaderAction.ApplyWithSelectedNewLeader(heir.Clan, heir);
            }
            catch (Exception e) { TYTLog.Error("Disaffection.Abdicate failed", e); return; }

            LegitimacyBehavior.Instance?.SetLegitimacy(heir, 50f); // a throne yielded under duress
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -8f, "a sovereign abdicated under an ultimatum");
            AnnounceToRealm(k, "The Throne Is Yielded",
                $"{old?.Name} has abdicated the throne of {k.Name} under the seal of the disaffected houses. " +
                $"{heir.Name} accedes. The realm is spared the sword — but every court in Hindostan marks how this crown changed heads.");
            TYTLog.Info($"Disaffection: {old?.Name} abdicated {k.Name} to {heir.Name}.");
            // CoronationBehavior sees the leader change and stages the darbar on its own.
        }

        private void GrantIndependence(Kingdom k, List<Clan> cabal, Clan lead)
        {
            Settlement seat = lead.Settlements.FirstOrDefault(s => s.IsTown)
                ?? lead.Settlements.FirstOrDefault(s => s.IsCastle) ?? lead.Settlements.FirstOrDefault();
            if (seat == null) { AnnounceToRealm(k, "The Cabal Falters", "The malcontents hold no seat from which to govern; their demand dies of its own poverty."); return; }

            Kingdom rebel = RevoltCascadeBehavior.Instance?.CreateRebelKingdom(lead, seat, $"Dominion of {lead.Name}");
            if (rebel == null) return;
            foreach (Clan c in cabal.Where(c => c != lead && !c.IsEliminated && c.Kingdom == k))
                try { ChangeKingdomAction.ApplyByJoinToKingdom(c, rebel, default(CampaignTime), false); } catch { }

            // Leaving by rebellion declares war; a GRANTED independence settles it at once.
            if (rebel.IsAtWarWith(k))
                try { ThroneWar.WithInternalPeace(() => MakePeaceAction.Apply(rebel, k)); } catch { }

            Graduate(rebel, k, peacefully: true);
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -6f, "provinces released from the realm");
            LegitimacyBehavior.Instance?.ModifyLegitimacy(k.Leader, +2f, "a sundering settled without blood");
            AnnounceToRealm(k, "A Realm Parted in Peace",
                $"By the sovereign's own grant, the houses of the disaffected depart {k.Name} to govern themselves as the " +
                $"{rebel.Name}. No blood was let — but the realm is smaller this day.");
        }

        private void RefusedAbdicationWar(Kingdom k, Clan lead)
        {
            bool started = CivilWarBehavior.Instance?.StartChallenge(k, lead) ?? false;
            if (!started)
            {
                // The machinery was busy or the challenge fizzled; the realm hears of the plot anyway.
                AnnounceToRealm(k, "A Cabal Exposed",
                    $"The disaffected houses' demand is refused and their nerve breaks — the cabal scatters, {lead.Name} disgraced but unbowed.");
                if (k.Leader != null && lead.Leader != null)
                    OpinionBehavior.Instance?.AddOpinion(lead.Leader, k.Leader, OpinionMath.OpinionType.Grudge, -18f);
            }
            // If it DID start, CivilWarBehavior announces and runs it: the refused ultimatum has
            // outgrown its cause — the cabal's lead house now fights for the throne itself.
        }

        private void StartSecessionWar(Kingdom k, List<Clan> cabal, Clan lead)
        {
            Settlement seat = lead.Settlements.FirstOrDefault(s => s.IsTown)
                ?? lead.Settlements.FirstOrDefault(s => s.IsCastle) ?? lead.Settlements.FirstOrDefault();
            if (seat == null) { RefusedAbdicationWar(k, lead); return; } // no seat: collapses like a broken cabal

            Kingdom rebel = RevoltCascadeBehavior.Instance?.CreateRebelKingdom(lead, seat, $"Dominion of {lead.Name}");
            if (rebel == null) return;
            foreach (Clan c in cabal.Where(c => c != lead && !c.IsEliminated && c.Kingdom == k))
                try { ChangeKingdomAction.ApplyByJoinToKingdom(c, rebel, default(CampaignTime), false); } catch { }
            RevoltCascadeBehavior.Instance?.EnsureAtWar(rebel, k);

            _warActive = true;
            _warOriginId = k.StringId;
            _warRebelId = rebel.StringId;
            _warLeadHeroId = lead.Leader?.StringId ?? "";
            _warDeadline = (int)CampaignTime.Now.ToDays + CivilWarMath.WarDeadlineDays;
            TYTLog.Info($"Disaffection: secession war — {rebel.Name} tears at {k.Name}.");

            AnnounceToRealm(k, "The Realm Tears Itself Apart",
                $"The demand of the disaffected is refused, and they answer with the sword: the houses of {lead.Name} " +
                $"and his confederates rise as the {rebel.Name}. They fight not for the throne, but to be free of it.");

            // A player-vassal of the sundering realm chooses a side (abdication wars ask via CivilWarBehavior).
            if (Clan.PlayerClan?.Kingdom == k && k.Leader != Hero.MainHero)
            {
                Hero ruler = k.Leader;
                RoyalFarmaan.Issue("The Realm Sunders — Where Do You Stand?",
                    $"Proclaimed at {seat.Name}",
                    $"The {rebel.Name} has torn free of {k.Name} and the two hosts gather. Your banner must fly somewhere.",
                    seal: "The realm holds its breath",
                    primary: "I hold to the throne",
                    onPrimary: () =>
                    {
                        if (ruler != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler, +5);
                    },
                    secondary: "I ride with the seceders",
                    onSecondary: () =>
                    {
                        Kingdom r = Kingdom.All.FirstOrDefault(x => x.StringId == _warRebelId);
                        if (r != null && !r.IsEliminated)
                            try { ChangeKingdomAction.ApplyByJoinToKingdom(Clan.PlayerClan, r, default(CampaignTime), false); } catch { }
                        if (ruler != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler, -20);
                    });
            }
        }

        // ── The secession war's course (daily) ───────────────────────────────────────
        private void OnDailyTick()
        {
            if (!WorldGen.Ready || !_warActive) return;
            Kingdom origin = Kingdom.All.FirstOrDefault(x => x.StringId == _warOriginId);
            Kingdom rebel = Kingdom.All.FirstOrDefault(x => x.StringId == _warRebelId);

            if (origin == null || origin.IsEliminated) { EndWar(); return; }
            if (rebel == null || rebel.IsEliminated || !rebel.Settlements.Any())
            { ResolveSecession(origin, rebel, independent: false, "the secession was broken and its houses scattered"); return; }

            if ((int)CampaignTime.Now.ToDays >= _warDeadline)
            {
                float rebelStr = rebel.Clans.Where(c => !c.IsEliminated).Sum(c => c.CurrentTotalStrength);
                float loyalStr = origin.Clans.Where(c => !c.IsEliminated).Sum(c => c.CurrentTotalStrength);
                bool free = CivilWarMath.RebelWins(rebelStr, loyalStr, MBRandom.RandomFloat, MBRandom.RandomFloat);
                ResolveSecession(origin, rebel, free,
                    free ? "a season of war could not bring the seceders to heel" : "the imperial host ground the secession down");
            }
        }

        private void ResolveSecession(Kingdom origin, Kingdom rebel, bool independent, string how)
        {
            EndWar();
            try
            {
                if (rebel != null && origin != null && rebel.IsAtWarWith(origin))
                    ThroneWar.WithInternalPeace(() => MakePeaceAction.Apply(rebel, origin));

                if (independent && rebel != null && !rebel.IsEliminated)
                {
                    Graduate(rebel, origin, peacefully: false);
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(origin, -12f, "provinces torn away by war");
                    if (origin.Leader != null) LegitimacyBehavior.Instance?.ModifyLegitimacy(origin.Leader, -6f, "a realm sundered by force");
                    AnnounceToRealm(origin, "A Realm Torn in Two",
                        $"{how}: the {rebel.Name} stands free of {origin.Name}, a realm in its own right. The map of Hindostan is redrawn.");
                }
                else if (rebel != null)
                {
                    foreach (Clan c in rebel.Clans.ToList())
                        try { ChangeKingdomAction.ApplyByJoinToKingdom(c, origin, default(CampaignTime), false); } catch { }
                    if (!rebel.IsEliminated && !rebel.Settlements.Any())
                        try { DestroyKingdomAction.Apply(rebel); } catch { }
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(origin, +6f, "a secession crushed");
                    AnnounceToRealm(origin, "The Secession Crushed",
                        $"{how}: the breakaway houses are brought back beneath the banner of {origin.Name}, their standing in ruins.");
                    Hero lead = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _warLeadHeroId);
                    if (lead != null && origin.Leader != null)
                    {
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(lead, origin.Leader, -30);
                        OpinionBehavior.Instance?.AddOpinion(lead, origin.Leader, OpinionMath.OpinionType.Grudge, -20f);
                    }
                }
            }
            catch (Exception e) { TYTLog.Error("Disaffection.ResolveSecession failed", e); }
        }

        // A rebel kingdom becomes a REAL kingdom: peace rules apply, the safety net stands
        // down, and the new sovereign holds his founding darbar.
        private void Graduate(Kingdom rebel, Kingdom origin, bool peacefully)
        {
            ThroneWar.Graduate(rebel.StringId);
            if (rebel.Leader != null) LegitimacyBehavior.Instance?.SetLegitimacy(rebel.Leader, peacefully ? 55f : 48f);
            CoronationBehavior.Instance?.HoldFoundingDarbar(rebel);
            TYTLog.Info($"Disaffection: {rebel.Name} ({rebel.StringId}) graduated to a real kingdom ({(peacefully ? "granted" : "won by war")}).");
        }

        private void EndWar()
        {
            _warActive = false;
            _warOriginId = ""; _warRebelId = ""; _warLeadHeroId = "";
            _warDeadline = -1;
        }

        private static void AnnounceToRealm(Kingdom k, string title, string body)
        {
            if (Clan.PlayerClan?.Kingdom == k || k.Leader == Hero.MainHero)
                RoyalFarmaan.FromRuler(k, title, body, "So it is written");
            else Notify($"{title}: {body}", false);
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("disaffection_status", "hindostan")]
        public static string Status(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            var sb = new System.Text.StringBuilder();
            if (Instance._warActive)
                sb.AppendLine($"SECESSION WAR: {Instance._warRebelId} vs {Instance._warOriginId}, resolves by day {Instance._warDeadline}.");
            if (Instance._members.Count == 0) sb.AppendLine("No conspiracies simmering.");
            foreach (var kv in Instance._members)
            {
                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == kv.Key);
                int days = (int)CampaignTime.Now.ToDays - (Instance._formedDay.TryGetValue(kv.Key, out int d) ? d : 0);
                sb.AppendLine($"{k?.Name}: cabal of {kv.Value.Count} house(s), simmering {days} day(s).");
            }
            var grads = ThroneWar.GraduatedIds;
            if (grads.Count > 0) sb.AppendLine("Graduated realms: " + string.Join(", ", grads));
            return sb.ToString().TrimEnd();
        }

        // Force a conspiracy in the player's realm using its most disaffected clans (testing).
        [CommandLineFunctionality.CommandLineArgumentFunction("force_conspiracy", "hindostan")]
        public static string ForceConspiracy(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k == null) return "You serve no realm.";
            if (Instance._members.ContainsKey(k.StringId)) return "A cabal already simmers here (see disaffection_status).";
            var clans = k.Clans.Where(c => Eligible(c, k)).OrderBy(c => OpinionOfRuler(c, k)).Take(3).ToList();
            if (clans.Count < DisaffectionMath.MinClans) return "Not enough eligible clans for a cabal.";
            Instance._formedDay[k.StringId] = (int)CampaignTime.Now.ToDays - DisaffectionMath.SimmerDays; // pre-simmered
            Instance._members[k.StringId] = clans.Select(c => c.StringId).ToList();
            return $"Cabal forced in {k.Name}: {string.Join(", ", clans.Select(c => c.Name.ToString()))} — pre-simmered; " +
                   "it will act on a weekly tick (note: they must ALSO be genuinely disaffected to hold together).";
        }
    }
}
