using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using HarmonyLib;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

namespace TakhtyaTaboot
{
    // THE ENGLISH OVERRIDE FIX (round 8). The engine NEVER loads language files for English:
    // LocalizedTextManager.LoadLanguage skips string deserialization when the language is
    // English, and MBTextManager.GetLocalizedText returns the inline fallback text before
    // consulting any table. So every <string id="innerKey"> override the mod ships in
    // ModuleData/Languages is dead for English players — which is why 'Neretzes' Folly' and
    // the vanilla family names survived round 7's re-theme. (LocalizationOverride works only
    // for GameText-sourced strings, a different pipeline.)
    //
    // This prefix intercepts the single choke point every {=key} string passes through at
    // render time and serves the mod's override first. Because the lookup happens at RENDER,
    // it also heals EXISTING SAVES: hero names and quest titles are stored as raw
    // "{=key}text" values and re-resolve every frame.
    [HarmonyPatch(typeof(MBTextManager), "GetLocalizedText")]
    public static class EnglishTextOverridePatch
    {
        private const string ModuleId = "TakhtyaTaboot";
        private static Dictionary<string, string> _overrides;

        static bool Prefix(string text, ref string __result)
        {
            if (text == null || text.Length < 4 || text[0] != '{' || text[1] != '=') return true;
            var map = _overrides ?? (_overrides = Load());
            if (map.Count == 0) return true;

            int close = text.IndexOf('}', 2);
            if (close <= 2) return true;
            string key = text.Substring(2, close - 2);
            if (!map.TryGetValue(key, out string replacement)) return true;

            __result = replacement;
            return false;
        }

        private static Dictionary<string, string> Load()
        {
            var map = new Dictionary<string, string>();
            try
            {
                string dir = Path.Combine(ModuleHelper.GetModuleFullPath(ModuleId), "ModuleData", "Languages");
                if (!Directory.Exists(dir)) return map;
                foreach (string file in Directory.GetFiles(dir, "*.xml"))
                {
                    if (Path.GetFileName(file).Equals("language_data.xml", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var doc = new XmlDocument();
                        doc.Load(file);
                        foreach (XmlNode node in doc.SelectNodes("//string"))
                        {
                            string id = node.Attributes?["id"]?.Value;
                            string value = node.Attributes?["text"]?.Value;
                            // Inner {=...} keys are short-to-medium tokens (opaque 8-char keys,
                            // or generated ids like Settlements.Settlement.text.X). The very
                            // long snake_case ids are outer GameText ids handled by
                            // LocalizationOverride; skipping them keeps the table lean.
                            if (!string.IsNullOrEmpty(id) && value != null && id.Length <= 64)
                                map[id] = value;
                        }
                    }
                    catch (Exception e) { TakhtyaTaboot.Util.TYTLog.Error("EnglishTextOverride: bad file " + file, e); }
                }
                TakhtyaTaboot.Util.TYTLog.Info($"EnglishTextOverride: {map.Count} inner-key overrides live.");
            }
            catch (Exception e) { TakhtyaTaboot.Util.TYTLog.Error("EnglishTextOverride: load failed", e); }
            return map;
        }
    }
}
