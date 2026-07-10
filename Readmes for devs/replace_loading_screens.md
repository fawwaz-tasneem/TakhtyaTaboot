# How to Override Loading Screens in Bannerlord

**Target:** Replace native loading paintings with custom assets (Total Conversion friendly).
**Tools Required:** Bannerlord Modding Kit, Image Editor (GIMP/Photoshop).

## Phase 1: Asset Preparation

Before opening the tools, your images must be formatted correctly to avoid "stretching" or errors.

1. **Resolution:** Resize/Crop all images to **1920 x 1080** (Standard UI aspect ratio).
2. **Format:** Save as `.png`.
3. **Naming:** Name them simply for your own organization (e.g., `my_mod_load_01.png`), but remember the game uses internal IDs, not your filenames.

## Phase 2: Importing (Resource Browser)

You must use the Modding Kit to "bake" the images into a format the game can read (`.tpac`).

1. Launch **Bannerlord Modding Kit**.
2. Open the Resource Browser: Press `ALT + ~` (tilde)  type `resource.show_resource_browser`.
3. Navigate to your mod: `YourModName > Assets > GauntletUI`.
* *If the folder doesn't exist, create it in Windows first, then right-click > "Scan for New Assets".*


4. **Import:** Click "Import New Assets" and select your PNGs.
5. **Configure Settings (CRITICAL):**
Select all imported images and set the following in the Inspector (right panel):
* **Type:** `Albedo (DXT1/DXT5)`
* **[x] Do Not Generate Mips** (Prevents "Power of Two" errors).
* **[x] Dont Resize in Atlas** (Keeps full quality).


6. **Note the Coordinates:**
Click each image individually and write down its **Sheet ID**, **X**, and **Y** values from the Inspector. You will need these for the code.
7. **Save:** Press `CTRL+S`. This generates the `.tpac` file in your Assets folder.

## Phase 3: The Code (SpriteData.xml)

Create or edit: `Modules/YourModName/GUI/SpriteParts/SpriteData.xml`.

This file tells the game: *"When you want to show `loading_screen_1`, use my image at these coordinates instead."*

```xml
<SpriteData>
    <SpriteCategories>
        <SpriteCategory Name="gauntletui">
            <SpriteSheetCount>1</SpriteSheetCount>
            <SpriteSheetSize ID="1" Width="4096" Height="4096" />
            <AlwaysLoad/>
        </SpriteCategory>
    </SpriteCategories>

    <Sprites>
        <Sprite Name="loading_screen_1" CategoryName="gauntletui" SheetID="1" SpriteAt="0,0" Width="1920" Height="1080" />
        
        <Sprite Name="loading_screen_2" CategoryName="gauntletui" SheetID="1" SpriteAt="1920,0" Width="1920" Height="1080" />
        
        <Sprite Name="loading_screen_empire_1" CategoryName="gauntletui" SheetID="1" SpriteAt="0,1080" Width="1920" Height="1080" />
    </Sprites>
</SpriteData>

```

## Phase 4: Activation (SubModule.xml)

Ensure your `SubModule.xml` loads the sprite data.

```xml
<Xmls>
    <XmlNode>
        <XmlName id="SpriteData" path="GUI/SpriteParts/SpriteData"/>
    </XmlNode>
</Xmls>

```

---

## Appendix: Faction Specific Screens

To replace screens for specific cultures (e.g., changing Empire to Mughals), you must override their specific IDs.

**How to Find Them:**
Open `Modules/Native/GUI/SpriteParts/SpriteData.xml` and search for "loading".

**Common Faction IDs to Hijack:**
If your mod replaces a native culture, override the corresponding ID below:

| Native Culture | ID to Override in XML | Total Conversion Use Case |
| --- | --- | --- |
| **Empire** | `loading_screen_empire_1`<br>

<br>`loading_screen_empire_2` | Mughals, Romans, etc. |
| **Vlandia** | `loading_screen_vlandia_1`<br>

<br>`loading_screen_vlandia_2` | British, Knights, Europeans |
| **Sturgia** | `loading_screen_sturgia_1` | Russians, Vikings |
| **Aserai** | `loading_screen_aserai_1` | Ottomans, Desert Factions |
| **Khuzait** | `loading_screen_khuzait_1` | Mongols, Marathas |
| **Battania** | `loading_screen_battania_1` | Celts, Tribal Factions |

**Example:**
To make an image appear **only** when playing as or fighting the Mughals (who replaced the Empire), add this line to your `SpriteData.xml`:

```xml
<Sprite Name="loading_screen_empire_1" CategoryName="gauntletui" SheetID="2" SpriteAt="0,0" Width="1920" Height="1080" />
