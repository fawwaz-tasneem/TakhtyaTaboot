using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace TakhtyaTaboot.Util
{
    // Creates and dissolves a TEMPORARY cadet clan for a succession claimant, so a prince can wage his
    // own war for the throne. Clan restructuring is the documented crash class (a Clan keeps a maintained
    // member list and there is no clean public "move hero between clans" API), so this is isolated, heavily
    // guarded, and validated through the hindostan.test_tempclan console command BEFORE any live use.
    public static class ClaimantClan
    {
        // heroId -> original clanId, so Dissolve can return each moved hero to its house.
        // NOT saved here (this is a static helper, not a behavior): SuccessionBehavior persists it
        // through Export/Import in its SyncData, because temp clans can survive across a save/load
        // mid-civil-war and Dissolve must still know where each hero came from afterwards.
        private static readonly Dictionary<string, string> _origin = new Dictionary<string, string>();

        // ── Persistence hooks (called from SuccessionBehavior.SyncData) ─────────────
        public static void Export(out List<string> heroIds, out List<string> clanIds)
        {
            heroIds = _origin.Keys.ToList();
            clanIds = _origin.Values.ToList();
        }

        public static void Import(List<string> heroIds, List<string> clanIds)
        {
            _origin.Clear();
            if (heroIds == null || clanIds == null) return;
            for (int i = 0; i < heroIds.Count && i < clanIds.Count; i++)
                if (!string.IsNullOrEmpty(heroIds[i]) && !string.IsNullOrEmpty(clanIds[i]))
                    _origin[heroIds[i]] = clanIds[i];
        }

        public static Clan Create(Hero leader, CultureObject culture)
        {
            if (leader == null) return null;
            Clan origin = leader.Clan;
            try
            {
                Clan clan = BuildShell("tyt_claim_", leader, culture);
                if (clan == null) { TYTLog.Error("ClaimantClan.Create: shell build returned null"); return null; }

                if (origin != null) _origin[leader.StringId] = origin.StringId;
                MoveHero(leader, clan);          // Hero.Clan setter maintains both clans' member lists
                clan.SetLeader(leader);          // PUBLIC — this was the missing step (leader was null)

                TYTLog.Info($"ClaimantClan.Create: '{clan.Name}' id={clan.StringId} leader={clan.Leader?.Name?.ToString() ?? "NULL"} " +
                            $"members={SafeCount(clan)} home={clan.HomeSettlement?.Name?.ToString() ?? "none"} " +
                            $"culture={clan.Culture?.StringId ?? "none"} init={clan.IsInitialized} kingdom={clan.Kingdom?.Name?.ToString() ?? "none"}");
                return clan;
            }
            catch (Exception e) { TYTLog.Error("ClaimantClan.Create failed", e); return null; }
        }

        // A PERMANENT new house for a banished/exiled hero and his immediate family. Unlike Create this
        // is NOT tracked for dissolve — the deposed survives, stripped of crown and lands, free to scheme.
        public static Clan CreateExileHouse(Hero head)
        {
            if (head == null) return null;
            try
            {
                Clan exile = BuildShell("tyt_exile_", head, head.Culture);
                if (exile == null) { TYTLog.Error("CreateExileHouse: shell build returned null"); return null; }

                // The family that follows him out: spouse and children currently of his house.
                var family = new List<Hero> { head };
                if (head.Spouse != null && head.Spouse.IsAlive && head.Spouse.Clan == head.Clan) family.Add(head.Spouse);
                foreach (Hero ch in head.Children)
                    if (ch != null && ch.IsAlive && ch.Clan == head.Clan && ch != head.Spouse) family.Add(ch);

                foreach (Hero h in family) MoveHero(h, exile);
                exile.SetLeader(head);

                TYTLog.Info($"ClaimantClan.CreateExileHouse: '{exile.Name}' id={exile.StringId} leader={exile.Leader?.Name?.ToString() ?? "NULL"} members={SafeCount(exile)}");
                return exile;
            }
            catch (Exception e) { TYTLog.Error("CreateExileHouse failed", e); return null; }
        }

        // CreateClan only registers an EMPTY shell — there is no InitializeClan in 1.3.x. The encyclopedia
        // (and most systems) deref Leader/HomeSettlement/Banner, so every one must be set or the clan page
        // native-crashes. Name/InformalName use non-public setters; Culture/Banner/SetInitialHomeSettlement
        // are public engine methods. Caller moves heroes in and calls SetLeader.
        private static Clan BuildShell(string idPrefix, Hero head, CultureObject culture)
        {
            string id = idPrefix + head.StringId + "_" + (int)CampaignTime.Now.ToDays;
            Clan clan = Clan.CreateClan(id);
            if (clan == null) return null;

            TextObject name = new TextObject("{=!}House of " + head.Name);
            AccessTools.PropertySetter(typeof(Clan), "Name")?.Invoke(clan, new object[] { name });
            AccessTools.PropertySetter(typeof(Clan), "InformalName")?.Invoke(clan, new object[] { name });
            clan.Culture = culture ?? head.Culture ?? head.Clan?.Culture;
            clan.Banner = Banner.CreateRandomClanBanner(MBRandom.RandomInt());

            Settlement home = head.HomeSettlement ?? head.Clan?.HomeSettlement ?? head.Clan?.InitialHomeSettlement;
            if (home != null) clan.SetInitialHomeSettlement(home);

            if (!clan.IsInitialized)
                AccessTools.PropertySetter(typeof(Clan), "IsInitialized")?.Invoke(clan, new object[] { true });
            return clan;
        }

        // Return every moved hero to its origin house and destroy the temp clan.
        public static void Dissolve(Clan temp)
        {
            if (temp == null) return;
            try
            {
                foreach (Hero h in temp.Heroes.ToList())
                {
                    Clan back = _origin.TryGetValue(h.StringId, out string oid)
                        ? Clan.All.FirstOrDefault(c => c.StringId == oid) : null;
                    if (back == null || back.IsEliminated || back == temp)
                    {
                        // Origin lost (e.g. a pre-fix save) or destroyed: never leave the hero in
                        // the doomed temp clan — fall back to close kin, then the realm's rulers.
                        back = FallbackHouse(h, temp);
                        if (back != null)
                            TYTLog.Warn($"ClaimantClan.Dissolve: origin of {h.Name} unknown/destroyed; returning to {back.Name}.");
                    }
                    if (back != null) MoveHero(h, back);
                    else TYTLog.Error($"ClaimantClan.Dissolve: no house found for {h.Name}; hero left clanless.");
                    _origin.Remove(h.StringId);
                }
                if (temp.Kingdom != null) try { ChangeKingdomAction.ApplyByLeaveKingdom(temp, false); } catch { }
                DestroyClanAction.Apply(temp);
                TYTLog.Info($"ClaimantClan.Dissolve: {temp.StringId} dissolved.");
            }
            catch (Exception e) { TYTLog.Error("ClaimantClan.Dissolve failed", e); }
        }

        private static Clan FallbackHouse(Hero h, Clan temp)
        {
            foreach (Clan candidate in new[] { h.Father?.Clan, h.Mother?.Clan, h.Spouse?.Clan,
                                               temp.Kingdom?.RulingClan })
                if (candidate != null && candidate != temp && !candidate.IsEliminated)
                    return candidate;
            return null;
        }

        private static int SafeCount(Clan c) { try { return c.Heroes?.Count ?? -1; } catch { return -1; } }

        // The Hero.Clan setter is non-public; the engine setter is what maintains both clans' member
        // lists, so we invoke it (not a raw field write) to keep invariants intact.
        private static void MoveHero(Hero h, Clan clan)
        {
            if (h == null || clan == null || h.Clan == clan) return;
            var setter = AccessTools.PropertySetter(typeof(Hero), "Clan");
            if (setter != null) setter.Invoke(h, new object[] { clan });
            else AccessTools.Field(typeof(Hero), "_clan")?.SetValue(h, clan);
        }
    }
}
