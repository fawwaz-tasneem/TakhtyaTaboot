using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // No noble house may stand masterless (playtest round 2). Two engine paths dump clans to
    // Kingdom == null: vanilla's FactionDiscontinuationCampaignBehavior scatters a fortless
    // kingdom the moment it loses its last settlement, and any DestroyKingdomAction kicks every
    // remaining clan out. For the mod's own claim kingdoms (hind_rebel_*) that raced our weekly
    // fold-back — the reported bug: a resolved civil war leaving its backers realm-less, where
    // vanilla then DESTROYS the landless ones after 28 days.
    //
    // Three nets, outermost first:
    //   1. Vanilla's discontinuation of hind_rebel_* kingdoms is VETOED — their lifecycle
    //      belongs to the mod's resolution code (Succession/CivilWar/AccessionWar fold-backs).
    //   2. A clan scattered by the destruction of a TRACKED claim kingdom rejoins its origin
    //      realm immediately (the house comes home, exactly what FoldBack would have done).
    //   3. Any other masterless noble house gets ClanRehomeMath.GraceDays to sort itself out,
    //      then swears to the best realm by faith, friendship and nearness (pure, tested).
    // A daily sweep also adopts orphans that pre-date this behavior (old saves), and dissolves
    // fortless UNTRACKED rebel kingdoms the veto would otherwise keep alive forever.
    public class ClanSafetyNetBehavior : CampaignBehaviorBase
    {
        private const int UntrackedRebelGraceDays = 14; // fortless untracked rebel realm lives this long

        public static ClanSafetyNetBehavior Instance { get; private set; }

        // Parallel lists (SyncData convention): orphan clan ids + the day each was first seen.
        private List<string> _orphanIds = new List<string>();
        private List<int> _orphanDays = new List<int>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("ClanSafetyNet.DailyTick", Sweep));
            CampaignEvents.CanKingdomBeDiscontinuedEvent.AddNonSerializedListener(this, OnCanKingdomBeDiscontinued);
        }

        public override void SyncData(IDataStore ds)
        {
            ds.SyncData("tyt_orphan_ids", ref _orphanIds);
            ds.SyncData("tyt_orphan_days", ref _orphanDays);
        }

        // Net 1 — vanilla must not scatter a claim kingdom; the mod folds its houses home itself.
        private void OnCanKingdomBeDiscontinued(Kingdom k, ref bool result)
        {
            if (ThroneWar.IsRebelKingdom(k)) result = false;
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (!WorldGen.Ready) return; // skip the parallel world-gen distribution (see Util/WorldGen.cs)
            if (clan == null) return;
            if (newKingdom != null) { Forget(clan.StringId); return; }
            if (!IsProtectable(clan)) return;

            // Net 2 — scattered by a tracked claim kingdom's destruction: the house comes home now.
            if (detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveByKingdomDestruction
                && oldKingdom != null && ThroneWar.IsRebelKingdom(oldKingdom))
            {
                Kingdom origin = OriginRealmOf(oldKingdom.StringId);
                if (origin != null && !origin.IsEliminated && origin.Leader != null)
                {
                    try
                    {
                        ChangeKingdomAction.ApplyByJoinToKingdom(clan, origin, default(CampaignTime), false);
                        Notify($"The {clan.Name} return to the fold of {origin.Name}.");
                        return;
                    }
                    catch (System.Exception e) { TYTLog.Error("ClanSafetyNet: fold-home failed, falling back to re-home", e); }
                }
            }

            Remember(clan.StringId);
        }

        // ── Net 3: the daily sweep ───────────────────────────────────────────────────
        private void Sweep()
        {
            int today = (int)CampaignTime.Now.ToDays;

            // Adopt masterless houses this behavior never saw change hands (old saves, missed events).
            foreach (Clan c in Clan.All)
                if (c.Kingdom == null && IsProtectable(c) && !_orphanIds.Contains(c.StringId))
                    Remember(c.StringId);

            // Re-home everyone past grace.
            for (int i = _orphanIds.Count - 1; i >= 0; i--)
            {
                Clan clan = Clan.All.FirstOrDefault(c => c.StringId == _orphanIds[i]);
                if (clan == null || clan.Kingdom != null || !IsProtectable(clan))
                { RemoveAt(i); continue; }
                if (!ClanRehomeMath.DueForRehome(_orphanDays[i], today)) continue;
                if (Rehome(clan)) RemoveAt(i);
            }

            // Tidy: a fortless rebel kingdom NOBODY tracks (its crisis resolved around it) would
            // live forever under the discontinuation veto — dissolve it the mod's way instead:
            // houses re-homed, then the husk retired.
            foreach (Kingdom rebel in Kingdom.All.Where(k => ThroneWar.IsRebelKingdom(k) && !k.IsEliminated
                                                             && !k.Settlements.Any()).ToList())
            {
                if (OriginRealmOf(rebel.StringId) != null) continue; // its war still owns it
                if (rebel.Clans.Any(c => c != null && !c.IsEliminated))
                {
                    // Give a freshly orphaned war a fortnight of grace before the husk is swept.
                    if (today - LastSeenRebelDay(rebel.StringId, today) < UntrackedRebelGraceDays) continue;
                    foreach (Clan c in rebel.Clans.ToList())
                        if (IsProtectable(c)) Rehome(c);
                }
                if (!rebel.Clans.Any(c => c != null && !c.IsEliminated))
                {
                    rebel.RulingClan = null;
                    try { DestroyKingdomAction.Apply(rebel); } catch { }
                    TYTLog.Info($"ClanSafetyNet: dissolved the fortless untracked rebel realm {rebel.StringId}.");
                }
            }
        }

        // First day this fortless rebel realm was noticed (piggybacks the orphan lists with a
        // "kingdom:" prefix so no extra synced state is needed).
        private int LastSeenRebelDay(string kingdomId, int today)
        {
            string key = "kingdom:" + kingdomId;
            int i = _orphanIds.IndexOf(key);
            if (i >= 0) return _orphanDays[i];
            _orphanIds.Add(key); _orphanDays.Add(today);
            return today;
        }

        // ── The choice of a new liege ────────────────────────────────────────────────
        private bool Rehome(Clan clan)
        {
            Settlement anchor = clan.Settlements.FirstOrDefault()
                                ?? clan.HomeSettlement ?? clan.InitialHomeSettlement;
            var candidates = new List<ClanRehomeMath.Candidate>();
            Religion clanFaith = ReligionBehavior.Instance?.GetReligion(clan.Leader) ?? Religion.None;

            foreach (Kingdom k in Kingdom.All)
            {
                if (k == null || k.IsEliminated || k.Leader == null) continue;
                if (ThroneWar.IsRebelKingdom(k)) continue;               // never into a claim war
                if (UnifiedEmpireBehavior.IsDormant(k)) continue;        // never into a dormant shell
                if (!k.Clans.Any(c => c != null && !c.IsEliminated)) continue;

                bool faith = clanFaith != Religion.None
                             && (ReligionBehavior.Instance?.GetReligion(k.Leader) ?? Religion.None) == clanFaith;
                float relation = OpinionBehavior.Instance?.EffectiveOpinion(clan.Leader, k.Leader)
                                 ?? CharacterRelationManager.GetHeroRelation(clan.Leader, k.Leader);
                float distance = 9999f;
                if (anchor != null)
                    foreach (Settlement s in k.Settlements)
                    {
                        float d = anchor.GetPosition2D.Distance(s.GetPosition2D);
                        if (d < distance) distance = d;
                    }
                else distance = 0f; // a house with no seat rides wherever the court is warmest

                candidates.Add(new ClanRehomeMath.Candidate(k.StringId, faith, relation, distance));
            }

            string bestId = ClanRehomeMath.PickBest(candidates);
            Kingdom best = bestId == null ? null : Kingdom.All.FirstOrDefault(k => k.StringId == bestId);
            if (best == null) return false;

            try
            {
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, best, default(CampaignTime), false);
                Notify($"The masterless house of {clan.Name} swears to {best.Name}.");
                TYTLog.Info($"ClanSafetyNet: re-homed {clan.StringId} into {best.StringId}.");
                return true;
            }
            catch (System.Exception e) { TYTLog.Error($"ClanSafetyNet: re-home of {clan.StringId} failed", e); return false; }
        }

        // Houses the net covers: living noble clans that answer to nobody. The mod's own
        // deliberate outsiders — temp claimant hosts mid-war and banished exile houses — are
        // left to the systems that made them.
        private static bool IsProtectable(Clan c)
            => c != null && !c.IsEliminated && c.Leader != null && c.Leader.IsAlive
               && c != Clan.PlayerClan && c.IsNoble
               && !c.IsBanditFaction && !c.IsMinorFaction && !c.IsRebelClan && !c.IsUnderMercenaryService
               && c.StringId != null
               && !c.StringId.StartsWith("tyt_claim_") && !c.StringId.StartsWith("tyt_exile_");

        private static Kingdom OriginRealmOf(string rebelKingdomId)
            => SuccessionBehavior.Instance?.OriginRealmOf(rebelKingdomId)
               ?? CivilWarBehavior.Instance?.OriginRealmOf(rebelKingdomId)
               ?? AccessionWarBehavior.Instance?.OriginRealmOf(rebelKingdomId)
               ?? DisaffectionBehavior.Instance?.OriginRealmOf(rebelKingdomId);

        private void Remember(string clanId)
        {
            if (_orphanIds.Contains(clanId)) return;
            _orphanIds.Add(clanId);
            _orphanDays.Add((int)CampaignTime.Now.ToDays);
        }

        private void Forget(string clanId)
        {
            int i = _orphanIds.IndexOf(clanId);
            if (i >= 0) RemoveAt(i);
        }

        private void RemoveAt(int i)
        {
            _orphanIds.RemoveAt(i);
            if (i < _orphanDays.Count) _orphanDays.RemoveAt(i);
        }

        private static void Notify(string text)
            => InformationManager.DisplayMessage(new InformationMessage(text, Color.FromUint(0xFFD4AF37)));
    }
}
