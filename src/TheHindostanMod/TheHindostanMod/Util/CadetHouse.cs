using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace TakhtyaTaboot.Util
{
    // The ONE place a clan is ever built from nothing. Clan restructuring is the
    // documented crash class (see ClaimantClan's history): CreateClan registers an
    // EMPTY shell, and the encyclopedia dereferences Leader/HomeSettlement/Banner —
    // every field below must be set or the clan page native-crashes. ClaimantClan
    // (temp claimant clans, exile houses) delegates here; FoundCadetHouse is the new
    // permanent path that lets a living kinsman split off as the head of his own house.
    public static class CadetHouse
    {
        public const string CadetIdPrefix = "tyt_cadet_";

        // ── The shared shell recipe (moved from ClaimantClan.BuildShell) ─────────────
        public static Clan BuildShell(string idPrefix, Hero head, CultureObject culture)
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

        // The Hero.Clan setter is non-public; the engine setter is what maintains both
        // clans' member lists, so we invoke it (not a raw field write).
        public static void MoveHero(Hero h, Clan clan)
        {
            if (h == null || clan == null || h.Clan == clan) return;
            var setter = AccessTools.PropertySetter(typeof(Hero), "Clan");
            if (setter != null) setter.Invoke(h, new object[] { clan });
            else AccessTools.Field(typeof(Hero), "_clan")?.SetValue(h, clan);
        }

        // ── Founding a PERMANENT cadet house ─────────────────────────────────────────
        // Splits an adult kinsman (with his spouse and children) out of `parent` as the
        // head of a new house of the same dynasty, sworn to `realm`. Returns the new
        // clan, or null if any step of the shell failed.
        public static Clan Found(Hero founder, Clan parent, Kingdom realm)
        {
            if (founder == null || parent == null) return null;
            try
            {
                Clan cadet = BuildShell(CadetIdPrefix, founder, parent.Culture);
                if (cadet == null) { TYTLog.Error("CadetHouse.Found: shell build returned null"); return null; }

                var family = new System.Collections.Generic.List<Hero> { founder };
                if (founder.Spouse != null && founder.Spouse.IsAlive && founder.Spouse.Clan == parent) family.Add(founder.Spouse);
                foreach (Hero ch in founder.Children)
                    if (ch != null && ch.IsAlive && ch.Clan == parent && ch != founder.Spouse) family.Add(ch);

                foreach (Hero h in family) MoveHero(h, cadet);
                cadet.SetLeader(founder);

                if (realm != null)
                    try { ChangeKingdomAction.ApplyByJoinToKingdom(cadet, realm, default(CampaignTime), false); } catch { }

                DynastyBehavior.Instance?.RegisterCadet(cadet, parent);
                bool hasParty = TrySpawnLordParty(founder, cadet.HomeSettlement ?? cadet.InitialHomeSettlement);
                TYTLog.Info($"CadetHouse: '{cadet.Name}' founded by {founder.Name} from {parent.Name}" +
                            (hasParty ? " (warband raised)." : " (no warband yet — leader waits at his seat)."));
                return cadet;
            }
            catch (Exception e) { TYTLog.Error("CadetHouse.Found failed", e); return null; }
        }

        // ── The known engine gap: raising the founder's warband ──────────────────────
        // Nothing in this mod has ever created a MobileParty, and the exact spawn API
        // varies by game version. Try the two common ones by REFLECTION, matching
        // parameters we can supply; on failure, park the founder at his seat and let the
        // engine's own clan-party AI raise a party in time. Verify at first compile.
        private static bool TrySpawnLordParty(Hero leader, Settlement home)
        {
            if (leader == null) return false;
            try
            {
                foreach (var (typeName, methodName) in new[]
                {
                    ("Helpers.MobilePartyHelper", "SpawnLordParty"),
                    ("TaleWorlds.CampaignSystem.Party.PartyComponents.LordPartyComponent", "CreateLordParty"),
                })
                {
                    Type type = AccessTools.TypeByName(typeName);
                    if (type == null) continue;
                    foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                                 .Where(x => x.Name == methodName))
                    {
                        object[] args = MatchArgs(m.GetParameters(), leader, home);
                        if (args == null) continue;
                        object result = m.Invoke(null, args);
                        if (result != null) return true;
                    }
                }
            }
            catch (Exception e) { TYTLog.Warn($"CadetHouse: lord-party spawn threw ({e.Message})."); }

            // Fallback: seat the founder at home; the clan-party AI takes it from there.
            try
            {
                if (home != null)
                    TeleportHeroAction.ApplyImmediateTeleportToSettlement(leader, home);
            }
            catch { }
            return false;
        }

        // Fill a parameter list from what we have (hero, settlement, clan, an id string);
        // null when a parameter cannot be satisfied.
        private static object[] MatchArgs(ParameterInfo[] pars, Hero leader, Settlement home)
        {
            var args = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                Type t = pars[i].ParameterType;
                if (t == typeof(Hero)) args[i] = leader;
                else if (t == typeof(Settlement)) { if (home == null) return null; args[i] = home; }
                else if (t == typeof(Clan)) args[i] = leader.Clan;
                else if (t == typeof(string)) args[i] = CadetIdPrefix + "party_" + leader.StringId;
                else if (pars[i].HasDefaultValue) args[i] = pars[i].DefaultValue;
                else return null;
            }
            return args;
        }
    }
}
