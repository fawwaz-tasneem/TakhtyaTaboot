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
        private static void Postfix(Hero __instance, ref TextObject __result)
        {
            try
            {
                if (__instance == null) return;

                // Never compute during world generation — relations are half-wired and walking them
                // can native-crash. Bios are only needed once the campaign is up and viewable.
                if (!Util.SaveGuardBehavior.CampaignReady) return;

                // The save serializer reads EncyclopediaText for every hero. If we hand it a
                // freshly-built TextObject it cannot resolve it ("SAVE ERROR. Cant find … with
                // type TextObject", once per lord). During a save, return the vanilla value.
                if (Util.SaveGuardBehavior.IsSaving) return;

                string bio = Util.Biographies.For(__instance);
                if (!string.IsNullOrEmpty(bio)) __result = new TextObject(bio);
            }
            catch { /* never break the encyclopedia */ }
        }
    }
}
