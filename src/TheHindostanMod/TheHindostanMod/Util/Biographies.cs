using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;

namespace TakhtyaTaboot.Util
{
    // Loads the authored hero biographies that live as text="…" attributes in the mod's
    // character XML and serves them by hero StringId.
    //
    // Why this exists: in this game build neither CharacterObject nor BasicCharacterObject
    // has any field for a character's text= attribute, so every authored bio in
    // spnpccharacters.xml / heroes.xml is silently dropped at load and Hero.EncyclopediaText
    // stays empty (the "no biography" the encyclopedia page showed). We read those same
    // attributes ourselves and hand them back through the EncyclopediaText patch — so the
    // content stays editable in XML, with no duplication into code.
    public static class Biographies
    {
        private const string ModuleId = "TakhtyaTaboot";
        private static readonly Dictionary<string, string> _bios =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        // Files scanned, in order; a later file's text for an id wins (heroes.xml overrides).
        private static readonly string[] Files = { "spnpccharacters.xml", "heroes.xml" };

        public static int Count => _bios.Count;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string dir = null;
                try { dir = TaleWorlds.ModuleManager.ModuleHelper.GetModuleFullPath(ModuleId); } catch { }
                if (string.IsNullOrEmpty(dir)) return;
                string moduleData = Path.Combine(dir, "ModuleData");

                foreach (string file in Files)
                {
                    string path = Path.Combine(moduleData, file);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        XDocument doc = XDocument.Load(path);
                        foreach (XElement el in doc.Descendants())
                        {
                            string id = (string)el.Attribute("id");
                            string text = (string)el.Attribute("text");
                            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(text)) continue;
                            _bios[id] = Strip(text);
                        }
                    }
                    catch (Exception e) { TYTLog.Warn($"Biographies: failed to read {file}: {e.Message}"); }
                }
                TYTLog.Info($"Biographies: loaded {_bios.Count} authored hero bio(s).");
            }
            catch (Exception e) { TYTLog.Error("Biographies.EnsureLoaded failed", e); }
        }

        // The bio for a hero, or null if none is authored.
        public static string Get(string heroStringId)
        {
            if (string.IsNullOrEmpty(heroStringId)) return null;
            EnsureLoaded();
            return _bios.TryGetValue(heroStringId, out string bio) ? bio : null;
        }

        public static bool Has(string heroStringId) => Get(heroStringId) != null;

        // The biography to show for a hero: the authored one if it exists, otherwise a
        // biography woven from who they actually are — so every hero in Hindostan has one.
        public static string For(Hero hero)
        {
            if (hero == null) return null;
            string authored = Get(hero.StringId);
            if (!string.IsNullOrEmpty(authored)) return authored;
            try { return Generate(hero); } catch { return null; }
        }

        private static string Generate(Hero h)
        {
            var sb = new StringBuilder();
            string clan = h.Clan?.Name?.ToString();
            string culture = h.Culture?.Name?.ToString();
            bool fem = h.IsFemale;
            string his = fem ? "her" : "his";
            int age = (int)h.Age;

            // — Who they are —
            if (h.IsKingdomLeader && h.Clan?.Kingdom != null)
                sb.Append($"{h.Name}, sovereign of {h.Clan.Kingdom.Name}");
            else if (h.Clan != null && h.Clan.Leader == h)
                sb.Append($"{h.Name}, head of the house of {clan}");
            else if (h.IsLord && clan != null)
                sb.Append($"{h.Name}, {(fem ? "a noblewoman" : "a noble")} of the house of {clan}");
            else if (h.IsWanderer)
                sb.Append($"{h.Name}, a wandering soul of {(culture ?? "Hindostan")}");
            else
                sb.Append(clan != null ? $"{h.Name}, of the house of {clan}" : $"{h.Name}");

            if (!string.IsNullOrEmpty(culture) && !h.IsKingdomLeader) sb.Append($", of the {culture} people");
            if (age > 0) sb.Append($", has seen {age} winters");
            sb.Append(".");

            // — Lineage —
            if (h.Father != null) sb.Append($" {(fem ? "Daughter" : "Son")} of {h.Father.Name}.");
            else if (h.Mother != null) sb.Append($" Child of {h.Mother.Name}.");

            // — Marriage & issue —
            if (h.Spouse != null) sb.Append($" {(fem ? "Wedded to" : "Married to")} {h.Spouse.Name}.");
            int kids = h.Children?.Count ?? 0;
            if (kids > 0) sb.Append($" {(fem ? "Mother" : "Father")} of {kids} {(kids == 1 ? "child" : "children")}.");

            // — Standing (lords) —
            if (h.IsLord && h.Clan != null)
            {
                string mansab = MansabdariBehavior.Instance?.GetTitle(h.Clan);
                List<Settlement> seats = h.Clan.Settlements?.Where(s => s.IsTown || s.IsCastle).ToList();
                if (!string.IsNullOrEmpty(mansab)) sb.Append($" Bears the mansab of {mansab}.");
                if (seats != null && seats.Count > 0)
                    sb.Append($" Holds {string.Join(", ", seats.Select(s => s.Name))}.");
            }

            // — Temper, from their nature —
            string temper = Temper(h, his);
            if (!string.IsNullOrEmpty(temper)) sb.Append($" {temper}");

            string result = sb.ToString().Trim();
            return result.Length > 0 ? result : null;
        }

        private static string Temper(Hero h, string his)
        {
            try
            {
                var parts = new List<string>();
                int valor = h.GetTraitLevel(DefaultTraits.Valor);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int gen = h.GetTraitLevel(DefaultTraits.Generosity);
                int calc = h.GetTraitLevel(DefaultTraits.Calculating);
                if (valor > 0) parts.Add("brave in the field"); else if (valor < 0) parts.Add("wary of the sword");
                if (honor > 0) parts.Add($"true to {his} word"); else if (honor < 0) parts.Add("known to break faith");
                if (mercy > 0) parts.Add("merciful to the fallen"); else if (mercy < 0) parts.Add("hard with enemies");
                if (gen > 0) parts.Add("open-handed"); else if (gen < 0) parts.Add("close with coin");
                if (calc > 0) parts.Add("a careful schemer");
                if (parts.Count == 0) return "";
                return $"{(h.IsFemale ? "She is" : "He is")} {string.Join(", ", parts.Take(3))}.";
            }
            catch { return ""; }
        }

        // Drop the leading "{=!}" / "{=id}" localization marker the XML uses.
        private static string Strip(string s)
        {
            s = s.Trim();
            if (s.StartsWith("{="))
            {
                int close = s.IndexOf('}');
                if (close >= 0) s = s.Substring(close + 1);
            }
            return s.Trim();
        }
    }
}
