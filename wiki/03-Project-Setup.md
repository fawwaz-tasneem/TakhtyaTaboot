# Chapter 3 — Project Setup

> Step-by-step: Visual Studio, references, Harmony, SubModule.xml wiring, and auto-deployment.

**[← Chapter 2](02-Bannerlord-Architecture.md)** | **[Home](Home.md)** | **[Next: Campaign Behaviors →](04-Campaign-Behaviors.md)**

---

## Contents

- [Create the Visual Studio project](#create-the-visual-studio-project)
- [Configure output path](#configure-output-path)
- [Add DLL references](#add-dll-references)
- [Add Harmony via NuGet](#add-harmony-via-nuget)
- [Register the DLL in SubModule.xml](#register-the-dll-in-submodulexml)
- [The entry point class](#the-entry-point-class)
- [Auto-deploy post-build event](#auto-deploy-post-build-event)

---

## Create the Visual Studio Project

1. Open Visual Studio 2019 or 2022
2. **Create New Project → Class Library (.NET Framework)**
   - Name: `TheHindostanMod`
   - Framework: `.NET Framework 4.8.1` (not .NET 5/6/7 — those break Bannerlord)
   - Location: `C:\Users\tasne\Desktop\TakhtyaTaboot\src\`
3. Delete the auto-generated `Class1.cs`

---

## Configure Output Path

In Visual Studio:

1. Right-click project → **Properties**
2. **Build** tab
3. Set **Output path** to:
   ```
   C:\Users\tasne\Desktop\TakhtyaTaboot\bin\Win64_Shipping_Client\
   ```
4. Set **Platform target** to `x64`

The compiled DLL now lands in the exact folder the mod expects.

---

## Add DLL References

Right-click **References** → **Add Reference** → **Browse**.

Add these from `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\`:

```
TaleWorlds.MountAndBlade.dll
TaleWorlds.CampaignSystem.dll
TaleWorlds.Core.dll
TaleWorlds.Library.dll
TaleWorlds.Localization.dll
TaleWorlds.MountAndBlade.View.dll
TaleWorlds.GauntletUI.dll
```

Add this from `...\Modules\Native\bin\Win64_Shipping_Client\`:

```
TaleWorlds.MountAndBlade.ViewModelCollection.dll
```

**For every reference:** in the Properties panel, set **Copy Local = False**. These DLLs are already in the game — you do not ship copies with your mod.

---

## Add Harmony via NuGet

Tools → NuGet Package Manager → Package Manager Console:

```
Install-Package Lib.Harmony -Version 2.2.2
```

For the `0Harmony.dll` reference that NuGet creates: set **Copy Local = True**. Harmony must ship with your mod.

Final mod bin layout:
```
TakhtyaTaboot\bin\Win64_Shipping_Client\
  TheHindostanMod.dll    ← your code
  0Harmony.dll           ← ship this
```

---

## Register the DLL in SubModule.xml

Open `C:\Users\tasne\Desktop\TakhtyaTaboot\SubModule.xml`.

Replace `<SubModules/>` with:

```xml
<SubModules>
  <SubModule>
    <Name value="TheHindostanMod"/>
    <DLLName value="TheHindostanMod.dll"/>
    <SubModuleClassType value="TheHindostanMod.HindostanSubModule"/>
    <Assemblies/>
  </SubModule>
</SubModules>
```

`SubModuleClassType` must exactly match `Namespace.ClassName` of your entry point class.

---

## The Entry Point Class

Create `HindostanSubModule.cs`:

```csharp
using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace TheHindostanMod
{
    public class HindostanSubModule : MBSubModuleBase
    {
        // Called before any game objects exist.
        // Use ONLY for Harmony patches and static setup.
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            new Harmony("com.hindostanmod").PatchAll(typeof(HindostanSubModule).Assembly);
        }

        // Called when the player starts or loads a game.
        // Register all behaviors and models here.
        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);

            if (!(game.GameType is Campaign))
                return;  // skip multiplayer/custom battle

            var starter = (CampaignGameStarter)gameStarter;

            starter.AddBehavior(new HindostanStartingStateBehavior());
            // starter.AddBehavior(new MonsoonSeasonBehavior());
            // starter.AddBehavior(new MarathaChauthorBehavior());
            // starter.AddBehavior(new HistoricalEventsBehavior());

            // starter.AddModel(new HindostanPartySpeedModel());
        }
    }
}
```

Compile and launch the game. If there is no crash, the DLL is loading. Check:
```
C:\Users\tasne\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\default[date].log
```

---

## Auto-Deploy Post-Build Event

Every build should automatically copy the DLL to the installed mod location so you never forget to sync manually.

In Visual Studio: **Project Properties → Build Events → Post-build event**:

```bat
xcopy /Y "$(TargetDir)TheHindostanMod.dll" "D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\The Hindostan Mod\bin\Win64_Shipping_Client\"
xcopy /Y "$(TargetDir)0Harmony.dll" "D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\The Hindostan Mod\bin\Win64_Shipping_Client\"
```

After this, Build (Ctrl+Shift+B) = compile + deploy. The game always runs the latest version.

---

**[← Chapter 2](02-Bannerlord-Architecture.md)** | **[Home](Home.md)** | **[Next: Campaign Behaviors →](04-Campaign-Behaviors.md)**
