using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;
using TaleWorlds.ObjectSystem;

namespace TakhtyaTaboot
{
    // Root cause of Calradic NPC names:
    //  (1) Per-culture name lists from tyt_spcultures.xml MERGE with Native's lists
    //      rather than replacing them, so vanilla names remain in the pool.
    //  (2) The empire (Mughal) culture draws from NameGenerator._imperialNames*,
    //      and town notables/traders draw from _merchantNames/_artisanNames/
    //      _preacherNames/_gangLeaderNames — none of which the culture XML touches.
    //
    // Fix: after NameGenerator.InitializePersonNames() runs, replace every culture's
    // name lists and all the special pools with the Indian names parsed from our XML.
    [HarmonyPatch(typeof(NameGenerator), "InitializePersonNames")]
    public static class NameOverridePatch
    {
        private const string ModuleId = "TakhtyaTaboot";
        private static readonly string DebugPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                         "hindostan_name_debug.txt");

        static void Postfix(NameGenerator __instance)
        {
            try
            {
                var data = ParseCultureNames();
                if (data.Count == 0) { Log("No culture name data parsed.", true); return; }

                // (1) Replace each culture's male/female/clan lists.
                int culturesFixed = 0;
                foreach (var culture in MBObjectManager.Instance.GetObjectTypeList<CultureObject>())
                {
                    if (!data.TryGetValue(culture.StringId, out var names)) continue;
                    ReplaceList(culture, "_maleNameList",   names.Male);
                    ReplaceList(culture, "_femaleNameList", names.Female);
                    ReplaceList(culture, "_clanNameList",   names.Clan);
                    culturesFixed++;
                }

                // (2) Replace NameGenerator special pools.
                // Imperial pool = empire (Mughal) names.
                if (data.TryGetValue("empire", out var emp))
                {
                    ReplacePool(__instance, "_imperialNamesMale",   emp.Male);
                    ReplacePool(__instance, "_imperialNamesFemale", emp.Female);
                }

                // Notable pools (merchants, artisans, preachers, gang leaders) are
                // culture-agnostic in vanilla — fill them from a merged Indian pool.
                var mergedMale = new List<TextObject>();
                foreach (var kv in data) mergedMale.AddRange(kv.Value.Male);
                ReplacePool(__instance, "_merchantNames",   mergedMale);
                ReplacePool(__instance, "_artisanNames",    mergedMale);
                ReplacePool(__instance, "_preacherNames",   mergedMale);
                ReplacePool(__instance, "_gangLeaderNames", mergedMale);

                Log($"Name override applied. Cultures fixed: {culturesFixed}; merged pool: {mergedMale.Count}.", true);

                // Piggyback the localization overwrite here — this hook is confirmed to run,
                // and by now the game's GameTexts are fully loaded.
                LocalizationOverride.Apply();
            }
            catch (Exception e)
            {
                Log("Postfix failed: " + e, true);
            }
        }

        private static void ReplaceList(CultureObject culture, string field, List<TextObject> names)
        {
            var list = AccessTools.Field(typeof(CultureObject), field)?.GetValue(culture) as MBList<TextObject>;
            if (list == null || names.Count == 0) return;
            list.Clear();
            foreach (var n in names) list.Add(n);
        }

        private static void ReplacePool(NameGenerator ng, string field, List<TextObject> names)
        {
            var list = AccessTools.Field(typeof(NameGenerator), field)?.GetValue(ng) as MBList<TextObject>;
            if (list == null || names.Count == 0) return;
            list.Clear();
            foreach (var n in names) list.Add(n);
        }

        private struct CultureNames
        {
            public List<TextObject> Male;
            public List<TextObject> Female;
            public List<TextObject> Clan;
        }

        private static Dictionary<string, CultureNames> ParseCultureNames()
        {
            var result = new Dictionary<string, CultureNames>();
            string root = ModuleHelper.GetModuleFullPath(ModuleId);
            string path = Path.Combine(root ?? "", "ModuleData", "tyt_spcultures.xml");
            if (!File.Exists(path)) { Log("tyt_spcultures.xml not found: " + path, true); return result; }

            var doc = new XmlDocument();
            doc.Load(path);
            var cultures = doc.SelectNodes("//Culture");
            if (cultures == null) return result;

            foreach (XmlNode c in cultures)
            {
                string id = c.Attributes?["id"]?.Value;
                if (string.IsNullOrEmpty(id)) continue;
                result[id] = new CultureNames
                {
                    Male   = ReadNames(c, "male_names"),
                    Female = ReadNames(c, "female_names"),
                    Clan   = ReadNames(c, "clan_names"),
                };
            }
            return result;
        }

        private static List<TextObject> ReadNames(XmlNode cultureNode, string listName)
        {
            var list = new List<TextObject>();
            var container = cultureNode.SelectSingleNode(listName);
            if (container == null) return list;
            foreach (XmlNode n in container.ChildNodes)
            {
                string val = n.Attributes?["name"]?.Value;
                if (!string.IsNullOrEmpty(val)) list.Add(new TextObject(val));
            }
            return list;
        }

        private static void Log(string line, bool reset = false)
        {
            try
            {
                if (reset) File.WriteAllText(DebugPath, line + Environment.NewLine);
                else File.AppendAllText(DebugPath, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
