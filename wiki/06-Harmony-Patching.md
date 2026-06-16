# Chapter 6 — Harmony Patching

> Harmony lets you modify methods in Bannerlord's DLLs without recompiling them. Use this for anything the model/behavior system doesn't expose.

**[← Chapter 5](05-Game-Model-Overrides.md)** | **[Home](Home.md)** | **[Next: Game Menus →](07-Game-Menus-and-Dialogues.md)**

---

## Contents

- [The three patch types](#the-three-patch-types)
- [Special parameter names](#special-parameter-names)
- [Postfix patch — character creation culture names](#postfix-patch--character-creation-culture-names)
- [Prefix patch — skip the original method](#prefix-patch--skip-the-original-method)
- [Patching a property getter or setter](#patching-a-property-getter-or-setter)
- [Using AccessTools for private members](#using-accesstools-for-private-members)
- [Finding what to patch](#finding-what-to-patch)
- [Debugging failed patches](#debugging-failed-patches)

---

## The Three Patch Types

| Type | Runs | Use case |
|------|------|----------|
| **Prefix** | Before the original | Intercept, change inputs, or skip the original entirely |
| **Postfix** | After the original | Read or modify the output without replacing the method |
| **Transpiler** | Edits IL bytecode | Most powerful; skip unless you need it — complex and fragile |

In the Hindostan Mod, **postfix** is the most commonly needed type.

---

## Special Parameter Names

Harmony injects special values into your patch method using naming conventions:

| Name | Type | What it is |
|------|------|------------|
| `__instance` | Same as patched class | The object the method ran on (`this`) |
| `__result` | Same as return type (use `ref`) | The return value — modify with `ref` |
| `__0`, `__1` | Same as param type | The original method's parameters by position |
| Same name as original param | Same as param type | Original method's parameter by name |

Your patch method must be `public static`. It can have any subset of these parameters — you don't have to declare all of them.

---

## Postfix Patch — Character Creation Culture Names

The character creation screen reads the *kingdom's* `short_name`, not the culture's name, for non-empire cultures. The engine assembles the UI in `CharacterCreationCultureStageVM.RefreshCultureDetails`. We run after it to overwrite what it wrote.

```csharp
using HarmonyLib;
using System.Collections.Generic;
using SandBox.ViewModelCollection.CharacterCreation;

namespace TheHindostanMod
{
    [HarmonyPatch(typeof(CharacterCreationCultureStageVM), "RefreshCultureDetails")]
    public static class CharacterCreationCultureNamePatch
    {
        private static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
        {
            { "empire",   "Mughal"   },
            { "aserai",   "Mysore"   },
            { "sturgia",  "Afghans"  },
            { "vlandia",  "Rajputs"  },
            { "battania", "Marathas" },
            { "khuzait",  "Sikhs"    },
        };

        private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            { "empire",
                "The Mughal Empire was once the envy of the world. Now fractured, its culture " +
                "of Persian administration and heavy Sowar cavalry endures across Delhi, Bengal, " +
                "and Hyderabad. Masters of the siege cannon, Mughals fight with the desperation " +
                "of a dying giant." },
            { "aserai",
                "Mysore guards the southern plateau, where jungle, fortress, and trade route " +
                "converge. Swift Nayaka infantry and rocket artillery defend terrain that has " +
                "broken every northern invader." },
            { "sturgia",
                "The Durrani Afghans come from the high passes of the Hindu Kush — a warrior " +
                "people forged by Ahmad Shah's iron will. Their Jezailchi musketeers and " +
                "thundering cavalry move fast and strike hard." },
            { "vlandia",
                "The Rajputs are Hindostan's ancient warrior aristocracy. Their heavy lancers " +
                "are unmatched on open ground; their hilltop forts have never been taken by " +
                "storm. Pride is their greatest strength and their most fatal flaw." },
            { "battania",
                "Born in the Sahyadri hills, the Marathas built an empire through Ganimi Kava " +
                "— the guerrilla art. Swift cavalry and nimble infantry strike where least " +
                "expected and melt into the forest before the answer comes." },
            { "khuzait",
                "The Sikhs of the Punjab are a martial faith-order forged into the Khalsa. " +
                "Organised into Misls, they answer to no king but the Almighty. Their morale " +
                "in battle is sustained by scripture rather than coin." },
        };

        // Postfix: runs after RefreshCultureDetails finishes
        public static void Postfix(CharacterCreationCultureStageVM __instance)
        {
            var selected = __instance.SelectedCulture;
            if (selected?.Culture == null) return;

            string id = selected.Culture.StringId;

            if (DisplayNames.TryGetValue(id, out string name))
                __instance.SelectedCultureName = name;

            if (Descriptions.TryGetValue(id, out string desc))
                __instance.CultureDescription = desc;
        }
    }
}
```

If `SelectedCultureName` is not publicly settable (some Bannerlord versions make it `private set`), see [Using AccessTools](#using-accesstools-for-private-members) below.

---

## Prefix Patch — Skip the Original Method

Return `false` from a prefix to skip the original method entirely. The original runs only if you return `true`.

```csharp
// Override tribute calculation for the Maratha kingdom
[HarmonyPatch(typeof(Kingdom), "get_TributeAmountToDemand")]
public static class KingdomTributePatch
{
    public static bool Prefix(Kingdom __instance, ref int __result)
    {
        if (__instance.StringId != "battania")
            return true;  // not Maratha — run original

        // Our custom Chauth amount
        __result = (int)(__instance.TotalStrength * 25.0f);
        return false;  // skip original
    }
}
```

---

## Patching a Property Getter or Setter

Properties compile down to methods named `get_PropertyName` and `set_PropertyName`:

```csharp
// Patch the getter for Kingdom.IsEliminated
[HarmonyPatch(typeof(Kingdom), "get_IsEliminated")]
public static class KingdomEliminatedPatch
{
    public static void Postfix(Kingdom __instance, ref bool __result)
    {
        // Prevent Bengal from ever being marked eliminated (it's too wealthy)
        if (__instance.StringId == "empire_w")
            __result = false;
    }
}
```

---

## Using AccessTools for Private Members

When the property or method you want to access is `private` or `internal`, `AccessTools` gives you a reflection handle without dealing with raw `System.Reflection` API:

```csharp
// Read a private field
var goldField = AccessTools.Field(typeof(Clan), "_gold");
int currentGold = (int)goldField.GetValue(clan);
goldField.SetValue(clan, currentGold + 5000);

// Write a private-set property
var prop = AccessTools.Property(typeof(CharacterCreationCultureStageVM), "SelectedCultureName");
prop?.SetValue(__instance, "Mughal");

// Call a private method
var method = AccessTools.Method(typeof(Kingdom), "UpdateKingdomName");
method?.Invoke(kingdom, new object[] { new TextObject("{=!}Mughal Empire") });
```

---

## Finding What to Patch

**Problem:** You want to change something in the game but don't know which C# method controls it.

**Workflow:**

1. **ILSpy or dnSpy** — Open a Bannerlord DLL file and browse or search for keywords. ILSpy is free: [github.com/icsharpcode/ILSpy](https://github.com/icsharpcode/ILSpy)
   - Open: `D:\SteamLibrary\...\bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll`
   - Press F3 to search types/members

2. **Search by what you see** — If the UI shows "Vlandians", search for that string in ILSpy to find which method produces it.

3. **BUTR decompiled source** — The Bannerlord community maintains a browsable source mirror. Search "Bannerlord decompiled source github".

4. **Bannerlord Modding Discord** — `#modding-help` channel. Describe what you want to change. Others know the API.

**Practical example:**

> "I want to change the name shown in character creation."

1. Open `TaleWorlds.MountAndBlade.ViewModelCollection.dll` in ILSpy
2. Search types for `CharacterCreation`
3. Find `CharacterCreationCultureStageVM`
4. Look at its properties: `SelectedCultureName`, `CultureDescription`
5. Find which method sets them: `RefreshCultureDetails`
6. Patch `RefreshCultureDetails` with a postfix → confirmed working

---

## Debugging Failed Patches

If your patch is not running:

1. **Verify Harmony ID is unique** — `new Harmony("com.hindostanmod.unique")`. Use reverse-domain style.
2. **Verify `PatchAll` is called in `OnSubModuleLoad`** — not `OnGameStart`.
3. **Add a smoke test log** inside the patch:
   ```csharp
   public static void Postfix(CharacterCreationCultureStageVM __instance)
   {
       InformationManager.DisplayMessage(new InformationMessage("[Hindostan] patch running"));
       // ...
   }
   ```
4. **Wrap `PatchAll` in a try/catch** to surface Harmony errors:
   ```csharp
   protected override void OnSubModuleLoad()
   {
       base.OnSubModuleLoad();
       try
       {
           new Harmony("com.hindostanmod").PatchAll(typeof(HindostanSubModule).Assembly);
       }
       catch (Exception e)
       {
           Debug.Print($"[Hindostan] Harmony patch failed: {e}");
       }
   }
   ```
5. **Verify the target method signature** — extra parameters or wrong parameter types silently prevent the patch from applying.
6. **Check the log file** at `C:\Users\tasne\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\default[date].log` for Harmony-related lines.

---

**[← Chapter 5](05-Game-Model-Overrides.md)** | **[Home](Home.md)** | **[Next: Game Menus →](07-Game-Menus-and-Dialogues.md)**
