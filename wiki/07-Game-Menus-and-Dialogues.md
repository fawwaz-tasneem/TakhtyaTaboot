# Chapter 7 — Game Menus and Dialogues

> Add custom options to settlement menus, show modal popups, and send HUD notifications.

**[← Chapter 6](06-Harmony-Patching.md)** | **[Home](Home.md)** | **[Next: Game Objects →](08-Game-Objects.md)**

---

## Contents

- [Settlement menus](#settlement-menus)
- [Elephant recruitment — full example](#elephant-recruitment--full-example)
- [Inquiry dialogs (modal popups)](#inquiry-dialogs-modal-popups)
- [HUD notifications](#hud-notifications)

---

## Settlement Menus

When the player enters a town, a menu appears. You inject custom options into existing menus via `AddGameMenuOption` in `OnGameStart`.

```csharp
// In HindostanSubModule.OnGameStart, after registering behaviors:
AddCustomMenuOptions(starter);

private void AddCustomMenuOptions(CampaignGameStarter starter)
{
    starter.AddGameMenuOption(
        menuId:     "town",             // inject into the existing town menu
        optionId:   "hire_elephants",   // unique ID for this option
        optionText: "{=!}Visit the elephant stables",

        // Condition: called to decide if this option appears and is enabled
        condition: delegate(MenuCallbackArgs args)
        {
            var s = Settlement.CurrentSettlement;
            bool eligible = s?.StringId is "town_A1"    // Srirangapatna (Mysore)
                                        or "town_EN2"   // Shahjahanabad (Delhi)
                                        or "town_EW2";  // Murshidabad (Bengal)

            args.IsEnabled = eligible;
            args.Tooltip = eligible
                ? new TextObject("{=!}Purchase war elephants for your party")
                : new TextObject("{=!}This settlement has no elephant stables");

            return eligible; // false = hide option entirely; true = show it
        },

        // Consequence: called when the player clicks the option
        consequence: delegate(MenuCallbackArgs args)
        {
            OpenElephantPurchaseDialog();
        },

        isLeave: false,  // true = this is a "leave menu" option (back button)
        index: 5         // position in the list (higher number = further down)
    );
}
```

**Commonly used `menuId` values:**

| `menuId` | Where |
|----------|-------|
| `"town"` | Inside a town |
| `"castle_outside"` | Outside a castle |
| `"village"` | Inside a village |
| `"siege_menu"` | During a siege |

---

## Elephant Recruitment — Full Example

```csharp
private void OpenElephantPurchaseDialog()
{
    int available  = MBRandom.RandomInt(1, 4);  // 1–3 elephants available
    int priceEach  = 3000;
    int totalCost  = available * priceEach;
    int playerGold = Hero.MainHero.Gold;
    bool canAfford = playerGold >= totalCost;

    string bodyText =
        $"The mahout master presents {available} war elephant(s), " +
        $"each priced at {priceEach} gold.\n\n" +
        $"Total cost: {totalCost:N0} gold\n" +
        $"Your treasury: {playerGold:N0} gold";

    InformationManager.ShowInquiry(new InquiryData(
        titleText:               "Elephant Stables",
        text:                    bodyText,
        isAffirmativeOptionShown: canAfford,
        isNegativeOptionShown:   true,
        affirmativeText:         $"Purchase ({totalCost} gold)",
        negativeText:            "Leave",
        affirmativeAction: () =>
        {
            Hero.MainHero.Gold -= totalCost;

            // "war_elephant" must match the id= in your NPCCharacters XML
            var elephantChar = MBObjectManager.Instance
                .GetObject<CharacterObject>("war_elephant");

            if (elephantChar != null)
                MobileParty.MainParty.MemberRoster.AddToCounts(elephantChar, available);

            InformationManager.DisplayMessage(new InformationMessage(
                $"{available} war elephant(s) added to your party."));
        },
        negativeAction: () => { }
    ));
}
```

---

## Inquiry Dialogs (Modal Popups)

`InformationManager.ShowInquiry` shows a blocking popup that pauses the game until the player responds.

```csharp
InformationManager.ShowInquiry(new InquiryData(
    titleText:               "Dialog Title",
    text:                    "The body text of the popup.",
    isAffirmativeOptionShown: true,   // show the "yes" button
    isNegativeOptionShown:   true,   // show the "no/cancel" button
    affirmativeText:         "Accept",
    negativeText:            "Refuse",
    affirmativeAction: () =>
    {
        // runs when player clicks "Accept"
    },
    negativeAction: () =>
    {
        // runs when player clicks "Refuse"
    }
));
```

To show only one button (acknowledge-only), set `isNegativeOptionShown: false` and provide only `affirmativeAction`.

### Chauth popup example

```csharp
private void ShowPlayerChauthorPopup(Kingdom marathas, Kingdom target, int amount)
{
    bool canAfford = target.RulingClan.Gold >= amount;

    InformationManager.ShowInquiry(new InquiryData(
        titleText: "Maratha Chauth Demand",
        text:
            $"The Peshwa's agent has arrived demanding {amount:N0} gold as Chauth — " +
            $"25% of your treasury — or face Maratha raids across your territory.\n\n" +
            $"Your treasury: {target.RulingClan.Gold:N0} gold.",
        isAffirmativeOptionShown: canAfford,
        isNegativeOptionShown:   true,
        affirmativeText: $"Pay {amount:N0} gold",
        negativeText:    "Refuse — we pay no tribute",
        affirmativeAction: () =>
        {
            target.RulingClan.Gold   -= amount;
            marathas.RulingClan.Gold += amount;
            InformationManager.DisplayMessage(new InformationMessage(
                $"You paid {amount:N0} gold as Chauth to the Maratha Confederacy."));
        },
        negativeAction: () =>
        {
            if (!marathas.IsAtWarWith(target))
                DeclareWarAction.Apply(marathas, target,
                    DeclareWarAction.DeclareWarDetail.Default);

            InformationManager.DisplayMessage(new InformationMessage(
                "The Marathas have declared war in response to your refusal!",
                Color.FromUint(0xFFCC2200)));
        }
    ));
}
```

---

## HUD Notifications

### Simple message (bottom-right ticker)

```csharp
InformationManager.DisplayMessage(new InformationMessage("Your text here."));
```

### Colored message

`Color.FromUint` takes an ARGB hex value: `0xFFRRGGBB`.

```csharp
// Red — war/danger
InformationManager.DisplayMessage(new InformationMessage(
    "Nadir Shah's army approaches Delhi!",
    Color.FromUint(0xFFCC2200)));

// Blue — monsoon/weather
InformationManager.DisplayMessage(new InformationMessage(
    "The monsoon has arrived.",
    Color.FromUint(0xFF4488FF)));

// Gold — wealth/prosperity
InformationManager.DisplayMessage(new InformationMessage(
    "Bengal's trade revenues are booming.",
    Color.FromUint(0xFFD4AF37)));

// Orange — Maratha events
InformationManager.DisplayMessage(new InformationMessage(
    "The Marathas collected Chauth from Hyderabad.",
    Color.FromUint(0xFFFF9933)));
```

### Debug-only log (log file only, not on screen)

```csharp
Debug.Print("[Hindostan] " + someDebugInfo);
```

This goes to `C:\Users\tasne\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\default[date].log`.

---

**[← Chapter 6](06-Harmony-Patching.md)** | **[Home](Home.md)** | **[Next: Game Objects →](08-Game-Objects.md)**
