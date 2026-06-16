# Chapter 12 — Worked Example: Maratha Chauth

> A complete feature built from scratch. Every line is explained. Read this chapter to understand how all the pieces fit together.

**[← Chapter 11](11-XML-and-CSharp.md)** | **[Home](Home.md)** | **[Quick Reference →](Quick-Reference.md)**

---

## What We're Building

The Maratha Chauth was a yearly tribute system — historically, the Marathas demanded 25% of neighboring kingdoms' revenue in exchange for not raiding them. This feature implements:

- A yearly tick that fires once per in-game year
- Identify which kingdoms the Marathas are stronger than
- For AI kingdoms: transfer gold automatically and show a notification
- For the player's kingdom: show a modal dialog with Accept/Refuse choices
- Refusal triggers a war declaration
- All state persisted to save files

---

## Step 1 — Create the file

Create `Behaviors/MarathaChauthorBehavior.cs` in your Visual Studio project.

```
TheHindostanMod/
  Behaviors/
    MarathaChauthorBehavior.cs   ← new file
    HindostanStartingStateBehavior.cs
    MonsoonSeasonBehavior.cs
  HindostanSubModule.cs
```

---

## Step 2 — Namespace and using statements

```csharp
using System;
using System.Collections.Generic;
using System.Linq;                              // for FirstOrDefault, Where, etc.
using TaleWorlds.CampaignSystem;               // Kingdom, Hero, Clan, Settlement, etc.
using TaleWorlds.CampaignSystem.Actions;       // DeclareWarAction, GiveGoldAction, etc.
using TaleWorlds.Core;                         // InformationManager
using TaleWorlds.Library;                      // Color, Debug

namespace TheHindostanMod
{
```

**Why these namespaces?**
- `System` — for `Math.Min`, `Exception`
- `System.Collections.Generic` — for `List<T>`
- `System.Linq` — for LINQ operators on collections
- `TaleWorlds.CampaignSystem` — everything campaign-related lives here
- `TaleWorlds.CampaignSystem.Actions` — DeclareWarAction is here, not in CampaignSystem directly
- `TaleWorlds.Core` — InformationManager is here
- `TaleWorlds.Library` — Color and Debug are here

---

## Step 3 — Class declaration and state

```csharp
    public class MarathaChauthorBehavior : CampaignBehaviorBase
    {
        // Track which kingdoms paid this year (by StringId, not Kingdom reference —
        // safer to serialize and immune to kingdoms being eliminated)
        private List<string> _paidKingdomsThisYear = new List<string>();

        // Track the last year Chauth ran so we don't double-fire within a year
        private int _lastChauthorYear = -1;
```

**Why `List<string>` instead of `List<Kingdom>`?**
If we save a `List<Kingdom>` and one kingdom gets eliminated between sessions, the reference becomes null on load. A `List<string>` of StringIds never goes stale. We can re-look up the kingdom when needed.

**Why `_lastChauthorYear = -1`?**
-1 is a sentinel value that can never be a real campaign year (years start at 0). This guarantees Chauth fires in year 0 even on a fresh campaign.

---

## Step 4 — RegisterEvents

```csharp
        public override void RegisterEvents()
        {
            // YearlyTickEvent fires once every 84 in-game days
            CampaignEvents.YearlyTickEvent.AddNonSerializedListener(
                this,          // 'this' = the owner; engine unsubscribes when behavior is destroyed
                OnYearlyTick   // the method to call
            );
        }
```

`AddNonSerializedListener` means: "call my method when the event fires, but don't write this subscription into the save file". The subscription is re-established by `RegisterEvents()` when the game loads. Never use `AddListener` (serialized) unless you explicitly need save-file-level subscription tracking.

---

## Step 5 — SyncData

```csharp
        public override void SyncData(IDataStore dataStore)
        {
            // This method runs BOTH when saving AND when loading.
            // The IDataStore knows the direction; same code handles both.
            dataStore.SyncData("hindostan_chauth_paid",      ref _paidKingdomsThisYear);
            dataStore.SyncData("hindostan_chauth_last_year", ref _lastChauthorYear);
        }
```

**Key naming:** prefix with `hindostan_` to avoid collisions with other mods. Keys must be consistent across all saves — if you ever rename a key, old saves will lose that variable.

---

## Step 6 — The yearly tick handler

```csharp
        private void OnYearlyTick()
        {
            // ElapsedYearsUntilNow counts full years since campaign start
            int currentYear = (int)Campaign.Current.CampaignStartTime.ElapsedYearsUntilNow;

            // Guard: only fire once per year
            if (currentYear == _lastChauthorYear) return;
            _lastChauthorYear = currentYear;
            _paidKingdomsThisYear.Clear();  // reset the paid list for this year

            // Find the Maratha kingdom (StringId "battania" is the engine ID we use)
            Kingdom marathas = Kingdom.All.FirstOrDefault(k => k.StringId == "battania");

            // If Marathas don't exist or were eliminated, skip
            if (marathas == null || marathas.IsEliminated)
            {
                Debug.Print("[Hindostan] Chauth skipped — Maratha kingdom not active");
                return;
            }

            float marathaStrength = marathas.TotalStrength;
            Debug.Print($"[Hindostan] Chauth year {currentYear}: Maratha strength = {marathaStrength:F0}");

            // Iterate all kingdoms as a snapshot (.ToList() prevents iteration errors
            // if a kingdom gets eliminated mid-loop)
            foreach (Kingdom target in Kingdom.All.ToList())
            {
                if (target == marathas)            continue;  // skip Marathas themselves
                if (target.IsEliminated)           continue;  // skip eliminated kingdoms
                if (marathas.IsAtWarWith(target))  continue;  // war replaces tribute
                if (target.RulingClan == null)     continue;  // safety guard

                // Historical condition: only demand from weaker kingdoms
                if (target.TotalStrength > marathaStrength * 0.80f) continue;

                // Calculate 25% of ruling clan's gold
                int tribute = (int)(target.RulingClan.Gold * 0.25f);
                if (tribute < 500) continue;  // too small to bother demanding

                CollectChauth(marathas, target, tribute);
                _paidKingdomsThisYear.Add(target.StringId);
            }
        }
```

---

## Step 7 — CollectChauth — route to AI vs player

```csharp
        private void CollectChauth(Kingdom marathas, Kingdom target, int amount)
        {
            // Determine if the player's clan or kingdom is the target
            bool playerIsTarget =
                target.Leader      == Hero.MainHero ||
                target.RulingClan  == Hero.MainHero?.Clan;

            if (playerIsTarget)
            {
                // Player gets a choice
                ShowPlayerChauthorPopup(marathas, target, amount);
            }
            else
            {
                // AI kingdom: just transfer silently
                TransferChauth(marathas, target, amount);

                // Show a brief world-event notification
                InformationManager.DisplayMessage(new InformationMessage(
                    $"The Marathas collected {amount:N0} gold as Chauth from {target.Name}.",
                    Color.FromUint(0xFFFF9933)));  // orange = Maratha color
            }
        }
```

`Hero.MainHero?.Clan` uses `?.` because `MainHero` is null in some edge cases (loading screens, custom battle mode — but the `if (!(game.GameType is Campaign))` guard in `OnGameStart` makes this unlikely).

---

## Step 8 — The player popup

```csharp
        private void ShowPlayerChauthorPopup(Kingdom marathas, Kingdom target, int amount)
        {
            bool canAfford = target.RulingClan.Gold >= amount;

            // Build the body text
            string body =
                $"The Peshwa's agent stands before you with a demand: pay {amount:N0} gold " +
                $"as Chauth — 25% of your treasury — or face Maratha raiding parties " +
                $"across your territory.\n\n" +
                $"Maratha strength: {marathas.TotalStrength:F0}\n" +
                $"Your strength:    {target.TotalStrength:F0}\n\n" +
                $"Your treasury:    {target.RulingClan.Gold:N0} gold";

            InformationManager.ShowInquiry(new InquiryData(
                titleText:               "Maratha Chauth Demand",
                text:                    body,
                isAffirmativeOptionShown: canAfford,    // hide "Pay" if you can't afford it
                isNegativeOptionShown:   true,
                affirmativeText:         $"Pay {amount:N0} gold",
                negativeText:            "Refuse — we do not pay tribute",

                affirmativeAction: () =>
                {
                    TransferChauth(marathas, target, amount);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"You paid {amount:N0} gold as Chauth to the Maratha Confederacy."));
                },

                negativeAction: () =>
                {
                    // Refusal = war
                    if (!marathas.IsAtWarWith(target))
                        DeclareWarAction.Apply(
                            marathas, target,
                            DeclareWarAction.DeclareWarDetail.Default);

                    InformationManager.DisplayMessage(new InformationMessage(
                        "The Marathas have declared war in response to your refusal!",
                        Color.FromUint(0xFFCC2200)));  // red
                }
            ));
        }
```

---

## Step 9 — The gold transfer

```csharp
        private void TransferChauth(Kingdom marathas, Kingdom target, int amount)
        {
            // Never take more gold than the kingdom actually has
            int actualAmount = Math.Min(amount, target.RulingClan.Gold);

            target.RulingClan.Gold   -= actualAmount;
            marathas.RulingClan.Gold += actualAmount;

            Debug.Print(
                $"[Hindostan] Chauth: {actualAmount} gold " +
                $"{target.StringId} → battania");
        }
    }  // end class
}  // end namespace
```

---

## Step 10 — Register in HindostanSubModule

Open `HindostanSubModule.cs` and add the behavior:

```csharp
protected override void OnGameStart(Game game, IGameStarter gameStarter)
{
    base.OnGameStart(game, gameStarter);
    if (!(game.GameType is Campaign)) return;

    var starter = (CampaignGameStarter)gameStarter;

    starter.AddBehavior(new HindostanStartingStateBehavior());
    starter.AddBehavior(new MonsoonSeasonBehavior());
    starter.AddBehavior(new MarathaChauthorBehavior());  // ← add this line
}
```

---

## Step 11 — Build and test

1. Press **Ctrl+Shift+B** to build. The post-build event copies the DLL automatically.
2. Launch Bannerlord → New Campaign → pick any culture
3. When the first in-game year passes (84 days), the Chauth should fire
4. Watch the log stream:
   ```
   [Hindostan] Chauth year 0: Maratha strength = 4200
   [Hindostan] Chauth: 12500 gold empire_s → battania
   ```
5. If you're playing as a Maratha-adjacent kingdom, expect the popup

---

## Complete File

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TheHindostanMod
{
    public class MarathaChauthorBehavior : CampaignBehaviorBase
    {
        private List<string> _paidKingdomsThisYear = new List<string>();
        private int _lastChauthorYear = -1;

        public override void RegisterEvents()
        {
            CampaignEvents.YearlyTickEvent.AddNonSerializedListener(this, OnYearlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hindostan_chauth_paid",      ref _paidKingdomsThisYear);
            dataStore.SyncData("hindostan_chauth_last_year", ref _lastChauthorYear);
        }

        private void OnYearlyTick()
        {
            int currentYear = (int)Campaign.Current.CampaignStartTime.ElapsedYearsUntilNow;
            if (currentYear == _lastChauthorYear) return;
            _lastChauthorYear = currentYear;
            _paidKingdomsThisYear.Clear();

            Kingdom marathas = Kingdom.All.FirstOrDefault(k => k.StringId == "battania");
            if (marathas == null || marathas.IsEliminated) return;

            float marathaStrength = marathas.TotalStrength;

            foreach (Kingdom target in Kingdom.All.ToList())
            {
                if (target == marathas || target.IsEliminated) continue;
                if (marathas.IsAtWarWith(target))              continue;
                if (target.RulingClan == null)                 continue;
                if (target.TotalStrength > marathaStrength * 0.80f) continue;

                int tribute = (int)(target.RulingClan.Gold * 0.25f);
                if (tribute < 500) continue;

                CollectChauth(marathas, target, tribute);
                _paidKingdomsThisYear.Add(target.StringId);
            }
        }

        private void CollectChauth(Kingdom marathas, Kingdom target, int amount)
        {
            bool playerIsTarget =
                target.Leader     == Hero.MainHero ||
                target.RulingClan == Hero.MainHero?.Clan;

            if (playerIsTarget)
                ShowPlayerChauthorPopup(marathas, target, amount);
            else
            {
                TransferChauth(marathas, target, amount);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"The Marathas collected {amount:N0} gold as Chauth from {target.Name}.",
                    Color.FromUint(0xFFFF9933)));
            }
        }

        private void ShowPlayerChauthorPopup(Kingdom marathas, Kingdom target, int amount)
        {
            bool canAfford = target.RulingClan.Gold >= amount;

            string body =
                $"The Peshwa's agent demands {amount:N0} gold as Chauth — " +
                $"25% of your treasury — or face Maratha raids.\n\n" +
                $"Your treasury: {target.RulingClan.Gold:N0} gold";

            InformationManager.ShowInquiry(new InquiryData(
                "Maratha Chauth Demand", body,
                isAffirmativeOptionShown: canAfford,
                isNegativeOptionShown:   true,
                affirmativeText: $"Pay {amount:N0} gold",
                negativeText:    "Refuse",
                affirmativeAction: () =>
                {
                    TransferChauth(marathas, target, amount);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"You paid {amount:N0} gold as Chauth."));
                },
                negativeAction: () =>
                {
                    if (!marathas.IsAtWarWith(target))
                        DeclareWarAction.Apply(marathas, target,
                            DeclareWarAction.DeclareWarDetail.Default);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The Marathas have declared war!", Color.FromUint(0xFFCC2200)));
                }
            ));
        }

        private void TransferChauth(Kingdom marathas, Kingdom target, int amount)
        {
            int actual = Math.Min(amount, target.RulingClan.Gold);
            target.RulingClan.Gold   -= actual;
            marathas.RulingClan.Gold += actual;
            Debug.Print($"[Hindostan] Chauth: {actual} gold {target.StringId} → battania");
        }
    }
}
```

---

**[← Chapter 11](11-XML-and-CSharp.md)** | **[Home](Home.md)** | **[Quick Reference →](Quick-Reference.md)**
