using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

namespace TakhtyaTaboot
{
    public enum Religion { None, Islam, Hindu, Sikh }

    // A minimal religion system. For now its only effect is naming: a hero draws their
    // first name from their religion's name pool, which lets a Muslim noble in a Hindu
    // realm (or vice versa) be named correctly. Religion is intended to grow later
    // (tolerance, marriage, conversion) — GetReligion is the stable entry point.
    public class ReligionBehavior : CampaignBehaviorBase
    {
        private const string ModuleId = "TakhtyaTaboot";

        public static ReligionBehavior Instance { get; private set; }

        // Default religion by culture.
        private static readonly Dictionary<string, Religion> CultureReligion = new Dictionary<string, Religion>
        {
            { "empire",   Religion.Islam }, { "empire_w", Religion.Islam }, { "empire_s", Religion.Islam },
            { "sturgia",  Religion.Islam },
            { "aserai",   Religion.Hindu }, { "vlandia",  Religion.Hindu }, { "battania", Religion.Hindu },
            { "khuzait",  Religion.Sikh  },
        };

        // Per-clan overrides where a clan's faith differs from its culture's default.
        private static readonly Dictionary<string, Religion> ClanReligion = new Dictionary<string, Religion>
        {
            { "clan_aserai_2", Religion.Islam }, // Tipu Sultan's house — Muslim in Hindu Mysore
            { "clan_aserai_3", Religion.Islam }, // Hyder Ali's house
        };

        // Which cultures feed each religion's name pool.
        private static readonly Dictionary<string, Religion> PoolSourceCulture = CultureReligion;

        private readonly Dictionary<Religion, List<string>> _male = new Dictionary<Religion, List<string>>();
        private readonly Dictionary<Religion, List<string>> _female = new Dictionary<Religion, List<string>>();
        private readonly object _poolLock = new object();
        private volatile bool _loaded;

        public override void RegisterEvents() { Instance = this; }
        public override void SyncData(IDataStore dataStore) { }

        public Religion GetReligion(Hero hero) => GetReligion(hero, 0);

        // The faith of a culture's populace (used e.g. by revolt unrest to detect a
        // province ruled by a lord of a different religion).
        public Religion GetCultureReligion(CultureObject culture)
            => culture != null && CultureReligion.TryGetValue(culture.StringId, out var r) ? r : Religion.None;

        private Religion GetReligion(Hero hero, int depth)
        {
            if (hero == null) return Religion.None;
            // Rule: a child inherits the father's religion. Walk up the paternal line.
            if (depth < 8 && hero.Father != null && hero.Father != hero)
            {
                Religion fr = GetReligion(hero.Father, depth + 1);
                if (fr != Religion.None) return fr;
            }
            if (hero.Clan != null && ClanReligion.TryGetValue(hero.Clan.StringId, out var r)) return r;
            string cid = hero.Culture?.StringId;
            return cid != null && CultureReligion.TryGetValue(cid, out var cr) ? cr : Religion.None;
        }

        public string GetReligionName(Hero hero)
        {
            EnsurePools();
            Religion r = GetReligion(hero);
            if (r == Religion.None) return null;
            var pool = (hero != null && hero.IsFemale) ? _female : _male;
            if (!pool.TryGetValue(r, out var list) || list.Count == 0) return null;
            return list[MBRandom.RandomInt(list.Count)];
        }

        // Lazily build the name pools ONCE, thread-safely. GenerateHeroFirstName (our Harmony
        // hook) is called from the engine's PARALLEL hero-creation at world-gen, so an unguarded
        // check-then-populate here let multiple threads write the shared dictionaries/lists at
        // once — corrupting the managed heap and native-crashing (0xC0000005) intermittently mid
        // world-gen. Double-checked locking; _loaded is published (volatile) only after the pools
        // are fully built, so lock-free readers either wait or see a complete, read-only structure.
        private void EnsurePools()
        {
            if (_loaded) return;
            lock (_poolLock)
            {
                if (_loaded) return;
                foreach (Religion r in new[] { Religion.Islam, Religion.Hindu, Religion.Sikh })
                { _male[r] = new List<string>(); _female[r] = new List<string>(); }

                try
                {
                    string path = Path.Combine(ModuleHelper.GetModuleFullPath(ModuleId) ?? "", "ModuleData", "tyt_spcultures.xml");
                    if (File.Exists(path))
                    {
                        var doc = new XmlDocument(); doc.Load(path);
                        foreach (XmlNode c in doc.SelectNodes("//Culture"))
                        {
                            string id = c.Attributes?["id"]?.Value;
                            if (id == null || !PoolSourceCulture.TryGetValue(id, out var rel)) continue;
                            AddNames(c.SelectSingleNode("male_names"), _male[rel]);
                            AddNames(c.SelectSingleNode("female_names"), _female[rel]);
                        }
                    }
                }
                catch { /* leave pools empty -> falls back to vanilla naming */ }

                _loaded = true;   // publish only after the pools are fully built
            }
        }

        private static void AddNames(XmlNode container, List<string> into)
        {
            if (container == null) return;
            foreach (XmlNode n in container.ChildNodes)
            {
                string v = n.Attributes?["name"]?.Value;
                if (!string.IsNullOrEmpty(v)) into.Add(v);
            }
        }
    }

    // Names by religion rather than strictly by culture.
    [HarmonyPatch(typeof(NameGenerator), "GenerateHeroFirstName")]
    public static class ReligionNamePatch
    {
        // Named historical figures (the emperors) whose display name MUST be fixed, never drawn from
        // the random culture pool. Sourced from the succession plan so there is one list of truth.
        private static readonly Dictionary<string, string> Fixed = BuildFixed();
        private static Dictionary<string, string> BuildFixed()
        {
            var d = new Dictionary<string, string>();
            foreach (var r in Util.ImperialSuccessionPlan.Reigns) d[r.HeroId] = r.Name;
            return d;
        }

        static bool Prefix(Hero hero, ref TextObject __result)
        {
            if (hero != null && Fixed.TryGetValue(hero.StringId, out string fixedName))
            { __result = new TextObject(fixedName); return false; }

            string name = ReligionBehavior.Instance?.GetReligionName(hero);
            if (string.IsNullOrEmpty(name)) return true; // vanilla / culture fallback
            __result = new TextObject(name);
            return false;
        }
    }
}
