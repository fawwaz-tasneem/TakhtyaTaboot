using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

namespace TheHindostanMod
{
    // English text is served from GameTexts._gameTextManager (a Dictionary<string,
    // GameText>), keyed by the OUTER semantic id + variation — NOT by the inner {=}
    // translation key. Our std_module_strings_xml.xml is keyed by inner keys (the
    // mod author's convention), so it never matched what the English renderer reads.
    //
    // hindostan_string_map.xml (generated from Native's own files) maps each inner
    // key -> the outer id(s) that actually carry it. At runtime we resolve each
    // override to its outer GameText + variation and overwrite that variation's text.
    // Confirmed working: title id str_faction_noble_name_with_title.empire, etc.
    public static class LocalizationOverride
    {
        private const string ModuleId = "TheHindostanMod";
        private static readonly string DebugPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                         "hindostan_loc_debug.txt");

        private static Dictionary<string, string> _texts;          // inner key -> text
        private static Dictionary<string, List<string>> _map;      // inner key -> outer ids

        private static readonly FieldInfo MgrField =
            typeof(GameTexts).GetField("_gameTextManager", BindingFlags.NonPublic | BindingFlags.Static);

        public static void Apply()
        {
            try
            {
                EnsureParsed();
                if (_texts.Count == 0) return;

                var mgr = MgrField?.GetValue(null) as GameTextManager;
                if (mgr == null) { Log("APPLY: GameTextManager not ready yet"); return; }

                int done = 0, failed = 0;
                foreach (var kv in _texts)
                {
                    List<string> targets = _map.TryGetValue(kv.Key, out var outs) && outs.Count > 0
                        ? outs : new List<string> { kv.Key };

                    foreach (var target in targets)
                    {
                        try { OverrideGameText(mgr, target, kv.Value); done++; }
                        catch { failed++; }
                    }
                }

                string sample = "(n/a)";
                try { sample = GameTexts.FindText("str_faction_noble_name_with_title", "empire").ToString(); } catch { }
                Log($"APPLY: {done} variations set ({failed} failed). empire noble title='{sample}'");
            }
            catch (Exception e) { Log("Apply failed: " + e); }
        }

        // Resolve an outer id ("base" or "base.variation") to its GameText + variation
        // and overwrite just that variation (never clears the list — sibling cultures
        // share the same GameText).
        private static void OverrideGameText(GameTextManager mgr, string outerId, string text)
        {
            GameText gt = mgr.GetGameText(outerId);
            string variation = "";

            if (gt == null)
            {
                int idx = outerId.LastIndexOf('.');
                if (idx > 0)
                {
                    var baseGt = mgr.GetGameText(outerId.Substring(0, idx));
                    if (baseGt != null) { gt = baseGt; variation = outerId.Substring(idx + 1); }
                }
            }
            if (gt == null) { gt = mgr.AddGameText(outerId); variation = ""; }

            gt.SetVariationWithId(variation, new TextObject(text),
                                  new List<GameTextManager.ChoiceTag>());
        }

        private static void LoadStrings(string path)
        {
            if (!File.Exists(path)) { Log("strings file not found: " + path); return; }
            var doc = new XmlDocument(); doc.Load(path);
            foreach (XmlNode n in doc.SelectNodes("//strings/string"))
            {
                string id = n.Attributes?["id"]?.Value, t = n.Attributes?["text"]?.Value;
                if (!string.IsNullOrEmpty(id) && t != null) _texts[id] = t;
            }
        }

        private static void EnsureParsed()
        {
            if (_texts != null) return;
            _texts = new Dictionary<string, string>();
            _map = new Dictionary<string, List<string>>();
            try
            {
                string root = ModuleHelper.GetModuleFullPath(ModuleId) ?? "";

                LoadStrings(Path.Combine(root, "ModuleData", "Languages", "std_module_strings_xml.xml"));
                // Bulk demonym swaps (Sturgian->Afghan, etc.) generated from vanilla strings.
                LoadStrings(Path.Combine(root, "ModuleData", "hindostan_demonym_overrides.xml"));

                string mapPath = Path.Combine(root, "ModuleData", "hindostan_string_map.xml");
                if (File.Exists(mapPath))
                {
                    var doc = new XmlDocument(); doc.Load(mapPath);
                    foreach (XmlNode n in doc.SelectNodes("//m"))
                    {
                        string i = n.Attributes?["i"]?.Value, o = n.Attributes?["o"]?.Value;
                        if (string.IsNullOrEmpty(i) || string.IsNullOrEmpty(o)) continue;
                        if (!_map.TryGetValue(i, out var l)) { l = new List<string>(); _map[i] = l; }
                        l.Add(o);
                    }
                }

                Log($"Parsed {_texts.Count} texts, {_map.Count} mapped keys.", true);
            }
            catch (Exception e) { Log("EnsureParsed failed: " + e.Message, true); }
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
