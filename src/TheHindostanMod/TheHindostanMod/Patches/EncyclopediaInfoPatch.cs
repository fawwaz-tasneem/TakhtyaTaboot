using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace TakhtyaTaboot
{
    // Serves a hero's authored biography as their encyclopedia description text.
    //
    // In this game build the character text="…" attribute is dropped at load (neither
    // CharacterObject nor BasicCharacterObject keeps it), so Hero.EncyclopediaText is empty
    // and the page showed no biography. Util.Biographies reads those same authored attributes
    // back from the mod's XML; here we hand the right one to the encyclopedia.
    //
    // The lord's *standing* (mansab, fiefs, liege, …) is no longer dumped here — it now lives
    // as clickable rows in the page's Info section (see EncyclopediaHeroStatsPatch).
    [HarmonyPatch(typeof(Hero), "EncyclopediaText", MethodType.Getter)]
    internal static class EncyclopediaInfoPatch
    {
        // One stable TextObject per hero: the serializer chokes on a FRESH TextObject built
        // inside the getter, so we build each bio's TextObject once and always hand back the
        // same instance (the bug report's recommended fix). The IsSaving guard stays as a
        // belt-and-braces fallback for the first read of a hero during a save.
        private static readonly System.Collections.Generic.Dictionary<string, TextObject> _bioCache
            = new System.Collections.Generic.Dictionary<string, TextObject>();

        private static void Postfix(Hero __instance, ref TextObject __result)
        {
            try
            {
                if (__instance == null || __instance.StringId == null) return;

                // Never compute during world generation — relations are half-wired and walking them
                // can native-crash. Bios are only needed once the campaign is up and viewable.
                if (!Util.SaveGuardBehavior.CampaignReady) return;

                if (_bioCache.TryGetValue(__instance.StringId, out TextObject cached))
                {
                    if (cached != null) __result = cached;
                    return;
                }

                // Don't build (and cache) a NEW TextObject mid-save; vanilla value is fine then.
                if (Util.SaveGuardBehavior.IsSaving) return;

                string bio = Util.Biographies.For(__instance);
                TextObject to = string.IsNullOrEmpty(bio) ? null : new TextObject(bio);
                _bioCache[__instance.StringId] = to; // cache nulls too: bios are static per hero
                if (to != null) __result = to;
            }
            catch { /* never break the encyclopedia */ }
        }
    }
}
