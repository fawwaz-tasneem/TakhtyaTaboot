# 23 — Creating the Campaign Map (Heightmap → Terrain → Settlements → Navmesh)

A practical, end-to-end guide to building the India campaign map for Takht ya Taboot. Written to
solve the concrete problems hit so far: **jaggy Himalayas**, **pixelated/“discretised” plains**, and
the unknowns of **settlement placement, navmeshes, water, and vegetation**.

> The map is a **scene** (named `Main_map`) edited in the **Bannerlord Modding Kit** editor, not in
> XML. XML (`settlements.xml`, `spkingdoms.xml`) only *links data* to entities you place in that scene.

---

## 0. Tools you need (one-time)

| Tool | Why | Notes |
|---|---|---|
| **Bannerlord Modding Kit** | The official in-engine editor (terrain sculpt, flora, navmesh, entity placement). | Free on Steam for owners: library → *Tools* → “Mount & Blade II: Bannerlord – Modding Kit”. Launch it, then **Editor**. |
| **A 16-bit-capable image editor** | To fix the heightmap. | Photoshop, **Krita** (free), GIMP 2.10+ (16-bit mode), or Affinity. *Must* support 16-bit grayscale. |
| **An erosion/terrain tool (optional but ideal)** | Makes mountains look like mountains, not spikes. | **Gaea** (free Community tier), World Machine, or World Creator. This is what turns your “3D profile” into believable ridgelines. |
| **Your existing subcontinent profile** | Source heightmap. | You already have this. The problem is its **bit depth + scaling**, see §2. |

Work in `C:\Users\tasne\Desktop\TakhtyaTaboot\SceneObj\<your_map>\`. You already have
`india_map`, `new_india_prelim`, `indiamap_seashelf_corrected`, and `Main_map_HOLD` — pick one as the
working copy and keep the others as backups (do **not** delete them yet).

---

## 1. How Bannerlord stores a campaign map (mental model)

A campaign-map scene is a folder under `SceneObj/<name>/` containing, among others:

- **`terrain.tdata` / heightmap data** — the elevation surface. This is what looks wrong right now.
- **`flora.dat` + flora layers** — grass/bushes/trees painted onto the terrain.
- **navigation mesh** (`*.navmesh` baked into the scene) — the invisible walkable surface parties path on.
- **entities** — settlement visuals, water planes, props, atmosphere — placed in the editor.
- **`atmosphere.xml`** (you already have these) — lighting/fog/colour grade.

The **campaign systems** then bind to it via:
- `settlements.xml` — each `<Settlement posX= posY=>` ties a data record to a *named entity* in the scene.
- The scene name registered as the world map (the `Sandbox` campaign loads `Main_map`).

So map-making is: **fix terrain → carve water → paint flora → place settlement entities → bake navmesh → link XML → test.**

---

## 2. THE FIX for jaggy Himalayas & pixelated plains (read this first)

Both symptoms are the **same root cause**: an **8-bit heightmap with a single linear height scale**.

### Why it happens
- An 8-bit grayscale image has only **256 height levels**. Bannerlord stretches those 256 values across
  your *entire* vertical range (sea level → Himalayan peaks).
- **Himalayas (jaggy):** the brightest values jump in big vertical steps. Each step becomes a visible
  terrace/cliff. On steep, thin ranges those steps read as **jagged spikes/edges** instead of slopes.
- **Plains (pixelated):** the Indo-Gangetic plain spans a *tiny* real elevation change, so it occupies
  maybe **2–5 of the 256 levels**. Each of those few levels renders as a flat plateau with a hard edge —
  that’s the “discretised / pixel” look. It’s **quantisation banding**, the same artefact as a gradient
  saved as an 8-bit GIF.

### The fix (do all three)
1. **Re-export the heightmap as 16-bit grayscale.** 16-bit = **65,536 levels**. Terracing/banding
   disappears on both mountains *and* plains. Export as **16-bit PNG** or **16-bit RAW**. If your source
   3D profile is currently 8-bit, regenerate it at 16-bit from the original mesh/DEM — you cannot recover
   lost precision by just converting an 8-bit file to 16-bit.
2. **Lower the vertical scale (height exaggeration).** In the editor’s terrain import you set a
   **min/max height** (metres). If max height is huge to make the Himalayas tall, the plains get crushed.
   Use a realistic-but-tamed range (the campaign map is stylised — vanilla mountains aren’t to scale).
   Start modest and raise only the mountain band; gameplay readability beats geographic accuracy.
3. **Increase heightmap resolution** so horizontal sampling is dense enough for steep terrain. For a
   subcontinent-sized map export at **4096×4096 or 8192×8192**. Thin steep features (Himalayan ridges)
   need samples or adjacent vertices differ wildly → triangulated spikes.

### Make mountains actually look like mountains
A smooth scaled blob has no ridge detail; raising it just makes a smooth lump or (at 8-bit) a stepped
lump. Real ranges have fractal ridgelines. Two ways to get them:
- **Erosion pass (best):** import your profile into **Gaea/World Machine**, apply **hydraulic +
  thermal erosion**, export 16-bit. This carves valleys and sharp ridgelines automatically — instant
  “mountain” read.
- **In-editor sculpting:** use the terrain **Smooth** brush to kill stair-steps, then the **Noise/Ridge**
  and **Raise/Lower** brushes to add ridge variation by hand. Slower, less natural, but no extra tool.

### Importing the fixed heightmap
In the editor: select the terrain → **Terrain panel → Import Heightmap** → choose your 16-bit file →
set **terrain size** (world units) and **min/max height** → import. Then:
- **Smooth** brush over any remaining seams (especially the square edges of your profile — soften them
  into coastline/sea shelf so the map doesn’t end in a cliff wall).
- Check the plains in a flat camera angle — banding should be gone. If you still see steps, your source
  was 8-bit; regenerate it.

---

## 3. Terrain materials (so it isn’t one flat colour)

Terrain texture is **painted by layer**, each layer a material (sand, grass, rock, snow, cracked earth).

1. Terrain → **Material/Layer panel**. Each terrain has up to 8 (engine-dependent) blend layers.
2. Assign vanilla campaign-map terrain materials (reuse Calradia’s map materials — they’re shared
   assets) or author Indian-appropriate ones later.
3. Paint with the **terrain paint brush**: snow/rock on the Himalayas, green on the Gangetic plain,
   ochre/arid on the Deccan and Thar, lush on the Malabar/Bengal coasts.
4. **Auto-slope/height rules** (if your editor build supports them) can auto-place rock on steep faces
   and snow above an altitude — fast first pass, then hand-touch.

> Terrain *type* under the paint also feeds party speed/visuals; keep it consistent with what you’ll
> later mark in the navmesh (forest = slower, etc.).

---

## 4. Water — sea, rivers, lakes

Bannerlord water is **water-plane entities/meshes placed at a Z height**, with terrain carved beneath.

- **The sea around the subcontinent:** place a large **water plane** entity at sea-level Z covering the
  whole map border. Sculpt your terrain’s coastal shelf to slope *below* that plane so the coastline
  reads naturally (this is what `indiamap_seashelf_corrected` was likely addressing). Soften your square
  profile’s edges into shelf here.
- **Lakes:** carve a basin in the terrain (Lower brush), drop a **water plane** at the basin’s rim Z.
  The water fills visually to that height; the terrain below is what makes it a lake vs a puddle.
- **Rivers (Ganga, Yamuna, Indus, Godavari, etc.):**
  1. Use the **Lower/erosion** brush to carve a continuous channel from mountains to sea.
  2. Lay a **river water mesh** (vanilla has river/water prefabs) along the channel, or chain water
     planes following the channel Z as it descends.
  3. Rivers should sit slightly **below** surrounding terrain or they’ll look like they float.
  4. Rivers also act as **soft barriers** — plan crossings (fords/bridges) where roads meet them, and
     mind the navmesh (parties must cross only at intended points; see §6).

> Start with the **sea + 3–4 major rivers + a couple of lakes**. Don’t model every tributary; the map
> is gameplay-stylised.

---

## 5. Vegetation / flora (grass, bushes, forests)

Flora is **painted in layers**, like terrain materials, from a palette of **flora kinds**.

1. Open the **Flora panel**. It lists flora kinds (grasses, bushes, trees) defined in
   `flora_kinds.xml` (reuse vanilla kinds initially; author Indian species later).
2. **Paint grass/bush layers** with the flora brush across plains and hills. Density and kind per stroke.
3. **Forests:** paint tree flora groups — e.g. Western Ghats and sub-Himalayan terai as dense forest,
   Thar as sparse/none, Deccan as scrub.
4. **Performance:** flora is the #1 campaign-map FPS sink. Keep density sane; use lower-poly distant
   billboards (LODs) that vanilla flora kinds already provide. Don’t carpet the whole map at max density.
5. Flora respects terrain type — it won’t grow on water/rock if the kind is configured that way.

> Tie flora to the climate you painted in §3: snow-rock Himalaya (bare), green well-watered Gangetic
> plain, arid Thar/Deccan scrub, lush Malabar/Bengal. This single step does the most for “this is India.”

---

## 6. The navigation mesh (parties can’t move without it)

The navmesh is the **invisible walkable surface** the campaign AI and the player party path along. **No
navmesh = no movement.** This is the step most first-time map makers miss and then “the map doesn’t work.”

### What it does on the world map
- Defines **where parties can walk** (land) vs **cannot** (sea, lakes, impassable high mountains).
- Carries **face flags / terrain types** that influence **movement speed** (roads fast, forest/mountain
  slow) and which faces are blocked.
- Roads/tracks the AI prefers are encoded here too.

### How to build it
1. Finish terrain + water first (navmesh conforms to the surface; rebuild if you re-sculpt heavily).
2. Use the editor’s **Navigation Mesh** tools to **generate/bake** the navmesh over the land surface.
3. **Exclude water and impassable peaks:** either don’t generate navmesh there, or delete/disable those
   faces, or mark them blocked. Parties will then route around the Himalayas and across rivers only where
   navmesh bridges the channel.
4. **Connectivity:** make sure all reachable land is **one connected navmesh island** (plus intended
   crossings). A settlement on a disconnected patch is unreachable → AI breaks. Leave deliberate
   **passes** through mountains (Khyber, Bolan) and **fords/bridges** across rivers as navmesh links.
5. **Paint face types** for speed (road vs plain vs forest vs mountain) where your build supports it.
6. **Re-bake** after any significant terrain/water change.

> Test connectivity early with a couple of placeholder settlements and a manual walk in-game before you
> place all ~100+ settlements.

---

## 7. Placing settlements (towns, castles, villages)

A settlement is **two things that must agree**:
1. A **named entity in the scene** (the visual: walls/keep/village props + a party spawn + gate).
2. A **record in `ModuleData/settlements.xml`** with matching `id` and a `posX/posY` that lands on that
   entity’s map position.

### In the editor
1. Place the settlement **visual entity** (reuse vanilla town/castle/village prefabs to start).
2. **Name/tag it with the settlement’s string id** exactly as in `settlements.xml`
   (e.g. `town_EN1`, or your Indian ids like `town_Delhi`). The id is the contract between scene and XML.
3. Ensure its footprint sits on **navmesh** (a town off the navmesh is unreachable).
4. Place its child markers if the prefab needs them (gate position, party spawn) — vanilla prefabs carry
   these; verify they’re on walkable ground.
5. Villages bind to a parent town/castle (already defined in your `settlements.xml` `bound` fields).

### In `settlements.xml`
- Each `<Settlement id="..." posX="..." posY="...">` — **posX/posY must match where you placed the
  entity** on the map (the editor shows entity coordinates; copy them in). Mismatch = the icon/party
  spawns in the wrong place or floats.
- You already have `settlements.xml` (132 KB) and `spkingdoms.xml` wired with Indian names and banners —
  so this is mostly **reconciling each record’s posX/posY to the new map** and making sure every `id`
  has a matching named entity in the scene.

> Workflow tip: place **a handful** of settlements, fix their posX/posY, test in-game that the party
> reaches them, *then* batch the rest. Reconciling 100+ positions blind is how maps end up broken.

---

## 8. Atmosphere & colour grade

You already have `atmosphere.xml` in each scene folder. This controls sun angle, fog, ambient colour,
bloom, and the overall colour grade. After terrain/flora look right, tune atmosphere for an Indian
palette (warmer sun, dustier haze over the Deccan/Thar). Keep a backup before editing — atmosphere
mistakes make the whole map look flat/washed out.

---

## 9. Registering & testing the map

1. The campaign loads the world-map scene named **`Main_map`**. Your working folder must end up as the
   scene the campaign loads (this is why there’s a `Main_map_HOLD` — it’s a parked candidate). When ready,
   make your finished scene the active `Main_map` (back up the current one first).
2. **Iterate in this order** and test after each:
   - Terrain imports & looks right (no banding/spikes) — §2.
   - Navmesh bakes & a party can walk the whole landmass — §6.
   - A few settlements reachable with correct positions — §7.
   - Water/flora/atmosphere polish — §4, §5, §8.
3. Launch with the mod, start a campaign, and **walk the map**. Watch `tyt_log.txt` (see
   [10 — Debugging](10-Debugging.md)) for settlement-binding or scene-load errors.

---

## 10. Common pitfalls (and the symptom you’ll see)

| Symptom | Cause | Fix |
|---|---|---|
| Jagged/spiky mountains | 8-bit heightmap; no erosion; too-low resolution | 16-bit + erosion + higher res (§2) |
| Pixelated/terraced plains | 8-bit quantisation across full height range | 16-bit + lower vertical scale (§2) |
| Map ends in a vertical wall at the edges | Square profile not faded into sea shelf | Sculpt coastal shelf below the sea plane (§2, §4) |
| Parties don’t move / AI frozen | No navmesh, or settlement off-mesh | Bake navmesh; ensure connectivity (§6) |
| Settlement icon in the sea / floating | `posX/posY` ≠ entity position | Match XML to entity coords (§7) |
| Town unreachable, AI ignores it | Disconnected navmesh island | One connected mesh + intended passes/fords (§6) |
| Tanked FPS on the map | Flora density too high | Lower density, rely on LODs (§5) |
| Rivers look like they float | Water mesh above surrounding terrain | Carve channel below terrain (§4) |

---

## 11. Suggested order of attack (your situation)

You already have the profile and several scene folders. Recommended sequence:

1. **Regenerate the heightmap at 16-bit** from your source 3D profile (this alone fixes both your
   current complaints). Run an **erosion pass** for ridgelines.
2. Re-import into a clean working scene; **smooth the square edges into a sea shelf**.
3. Paint terrain materials (climate zones) and **bake a first navmesh**; walk-test connectivity.
4. Place **5–10 key settlements** (Delhi, Agra, Pune, Hyderabad, Lahore, etc.), match posX/posY, test reach.
5. Add **sea + major rivers + a few lakes**; re-bake navmesh around them (crossings/passes).
6. Paint **flora** by climate zone.
7. Batch the remaining settlements, reconciling positions.
8. Tune **atmosphere**, then make it the active `Main_map` and full-campaign test.

> Steps 1–3 are the unblock. Everything after is iteration. Keep `india_map` / `new_india_prelim` /
> `Main_map_HOLD` as backups until the new scene is verified in a full campaign.
