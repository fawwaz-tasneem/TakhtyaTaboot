using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterCreation;

namespace TakhtyaTaboot
{
    // Forces the Hindostan culture names onto the character-creation selection
    // screen. The screen reads CharacterCreationCultureVM.NameText, which is set
    // from the vanilla str_culture_rich_name.{id} string. This postfix overwrites
    // it directly, so the result is deterministic regardless of localization load.
    [HarmonyPatch(typeof(CharacterCreationCultureVM))]
    public static class CultureNamePatch
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "empire",   "Mughals"     },
            { "empire_w", "Bengalis"    },
            { "empire_s", "Hyderabadis" },
            { "sturgia",  "Afghans"     },
            { "aserai",   "Mysoreans"   },
            { "vlandia",  "Rajputs"     },
            { "battania", "Marathas"    },
            { "khuzait",  "Sikhs"       },
        };

        // There is exactly one constructor: (CultureObject, Action<...>).
        static MethodBase TargetMethod()
            => typeof(CharacterCreationCultureVM).GetConstructors()[0];

        static void Postfix(CharacterCreationCultureVM __instance)
        {
            string id = __instance.Culture?.StringId;
            if (id != null && Names.TryGetValue(id, out string name))
            {
                __instance.NameText = name;
                __instance.ShortenedNameText = name;
            }
        }
    }
}
