# Credits: outside resources

Everything in Pigeon vs Seagull that came from outside the project. Where a license isn't certain it says **check**:
look it up before shipping.

## Engine and Unity packages

| What | From | Notes |
|---|---|---|
| Unity 6 (6000.3.10f1) | Unity Technologies | Engine |
| Universal Render Pipeline 17.3 | Unity | Rendering |
| Input System, Netcode for GameObjects, Multiplayer Services, Multiplayer Play Mode, AI Navigation, Cinemachine, ProBuilder, Timeline, Recorder, uGUI, TextMesh Pro, Visual Scripting, Test Framework | Unity | Unity Companion License |
| Starter Assets: Third Person Controller | Unity | Player controller and input (modified: air jumps, launch, jump refill) |
| Unity AI Assistant | Unity | Editor tooling only |

## Open-source packages (Git)

| What | From | Notes |
|---|---|---|
| URP Volumetric Light / Fog (`com.cqf.urpvolumetricfog`) | Cristian Qiu, github.com/CristianQiu/Unity-URP-Volumetric-Light | The volumetric fog in use. License: **check** repo |
| Unity SSGI URP (`com.jiaozi158.unityssgiurp`) | jiaozi158, github.com/jiaozi158/UnitySSGIURP | Installed, currently off. License: **check** repo |
| URP Fog Volumes (`com.sinnwrig.fogvolumes`) | sinnwrig, github.com/sinnwrig/URP-Fog-Volumes | Installed. License: **check** repo |
| GTAO_URP | bladesero (GitHub) | Ported to render graph in `Assets/GTAO`; currently off. License: **check** repo |
| ContactShadows | Keijiro Takahashi / Unity Technologies, github.com/keijiro/ContactShadows | In `Assets/ContactShadows-master`, unused (Built-in pipeline only; contact shadows were dropped for URP SSAO). Safe to delete. Unity example content |

## Asset Store and other packs

| What | From | Where | Notes |
|---|---|---|---|
| Flat Kit | Dustyroom | `Assets/FlatKit` | Stylized surface and water shaders. Asset Store license |
| HTrace: Screen Space Global Illumination | IPGames | `Assets/HTraceSSGI` | Installed, currently off |
| HTrace: Ambient Occlusion V2 | IPGames | `Packages/dev.ipgames.htrace-ao-v2` | Installed, currently off (URP SSAO in use) |
| AERO: Volumetric Fog and Mist; VFX Toolkit | Mirza Beig | `Assets/Mirza` | AERO renderer feature off; demo scripts present |
| Forest Environment Dynamic Nature, Foliage/Object/Water shaders, NatureManufacture Wind | NatureManufacture | `Assets/NatureManufacture Assets` | Trees, ferns, plants, bark and cross shaders, river water, wind. Our `Shitshow/Foliage Translucent` shader is built on their wind subgraph |
| Low Poly Nature Bundle | LMHPOLY | `Assets/LMHPOLY` | Vegetation |
| Recreation park | Tolik_Cola | `Assets/Tolik_Cola` | Benches and park props |
| Quirky Series (Ultimate Pack Vol.1, Mega Packs 1 and 4) | Omabuarts Studio (**check**) | `Assets/Quirky Series` | Pigeon and Seagull animals |
| Low Poly Guns | **check** publisher | `Assets/Low Poly Guns` | The pigeon's gun |
| Toon Muzzleflash Pack | **check** publisher | `Assets/Toon Muzzleflash Pack` | Muzzle flashes |
| Procedural Skybox 2.0 Lite | **check** publisher | `Assets/Procedural Skybox 2.0 Lite` | Earlier sky; replaced by the HDRI |
| God ray pack (VolumetricLightScattering) | **check** publisher | `Assets/god-ray-pack` | Ported to render graph; currently off |
| Quick Outline | Chris Nolet | `Assets/QuickOutline` | Object outlines. MIT (**check**) |
| Prefab Brush | **check** publisher | `Assets/Prefab Brush` | Editor tool for placing props |
| PSX characters (Characters_psx, Characters_psx_01, Extras) | **check** author | `Assets/Blender/PSXCHARACTERS` | NPC people and police |
| PSX Mega Pack (pistol magazine, `pistol_mp_1_mag_loaded`) | **check** author | `Assets/Models/Pickups` | Ammo pickup. Its textures weren't included; ours are rebaked in Blender (`Models/Pickups/Textures`) |

## Animation

| What | From | License |
|---|---|---|
| Idle, Happy Idle, Standard Walk and other clips | Mixamo (Adobe) | Mixamo terms: free for use in games. The NPCs play edited copies (`Animations/NpcClips`, knee stretch capped) |
| Animations | Quaternius | CC0 (`Assets/Animations/Quaternius/License.txt`) |

## Textures and sky

| What | From | License |
|---|---|---|
| Asphalt031, Bricks109A, Concrete025, Concrete034, Concrete047A, Concrete048, Fabric030, Granite002A, Granite003A, Grass004, Gravel023, Ground054, Ground078, Marble012, Metal009, Metal027, Metal029, Metal054A, PaintedMetal002, PaintedMetal004, PavingStones046, Plastic012A, RoofingTiles013A, Rubber004, Wood051, SurfaceImperfections013 | ambientCG | CC0 |
| asphalt_02 | Poly Haven | CC0 |
| Kloofendal 48d Partly Cloudy (Pure Sky) HDRI | Poly Haven (Greg Zaal) | CC0 |

## Sounds

All in `Assets/Sounds`, trimmed so each starts on its first sample. Attribution lines as the authors asked are in
`Assets/Sounds/attribution texts.txt`.

| What | From | License |
|---|---|---|
| 9mm pistol shot (the gun, police shots) | michorvath, freesound.org/s/427592 | CC0 |
| Glock19_reload.wav (reload) | Fr3yr, freesound.org/s/100804 | CC BY 3.0: **credit "Glock19_reload.wav by Fr3yr" in the game's credits** |
| Punch impact hit (the player's shots hitting a target) | Universfield (Pixabay, file 567196) | Pixabay Content License: **check**; free to use, no credit required |
| Bone break 5 (neck snap) | floraphonic (Pixabay, 218513) | Pixabay Content License |
| 22 rifle dryfiring (dry fire) | freesound_community (Pixabay, 87480) | Pixabay Content License |
| Impossible horror body drop (body thud) | imagne (Pixabay, 152091) | Pixabay Content License |
| Footsteps dirt gravel (NPC footsteps, cut into 8 steps) | freesound_community (Pixabay, 6823) | Pixabay Content License |
| Running on dirt road (player footsteps, cut into 8 steps) | spinopel (Pixabay, 345729) | Pixabay Content License |
| Park (park ambience, made into a seamless loop) | freesound_community (Pixabay, 6026) | Pixabay Content License |

## Fonts

| What | From | Used for | License |
|---|---|---|---|
| Inter (`Assets/Resources/Fonts/Inter-Regular.ttf`, `-Bold.ttf`, the 24pt cuts; full family in `Assets/Fonts/Inter`) | Rasmus Andersson | HUD, menus, prompts, minimap, gym panels; the HUD's number glyphs (`Resources/SubwayHUD/glyph_*.png`) are rendered from its Bold | SIL Open Font License (`Inter-License.txt`) |
| TeX Gyre Heros (`Assets/Fonts/TeXGyreHeros-Regular.otf`, `-Bold.otf`; full family in `Assets/Fonts/TeX-Gyre-Heros`) | GUST e-foundry | No longer used (replaced by Inter); still in the project | GUST Font License, free to embed (`TeXGyreHeros-License.txt`) |
| Overpass | Red Hat (Delve Withrington), based on Highway Gothic | Street sign and gym blade art (baked into PNGs; the font file isn't in the project) | SIL Open Font License |
| Libre Caslon Text | Pablo Impallari | "Greenwich Village Historic District" band on the brown blades (baked into PNGs) | SIL Open Font License |

## Reference material

| What | Where | Notes |
|---|---|---|
| Food truck and cart photos (foodtruck.webp, hotdogfoodtruck.jpeg, halaltruck.jpg, hotdogfoodcart2.jpg) | `Assets/Blender/FoodCarts/FoodTruckRef`, `Assets/Models/Food` | Modelling reference only: **check** their source, and keep them out of builds |
| NYC street sign photos | Shared in development chat | Reference for the street signs and signals; not in the project |

## Made for this project

- **Figma:** the subway-signage HUD (file "Subway Signage") and the NYC street sign set (file "Wash Square Street
  Signs"). Part of the sign set (28 PNGs) was rendered locally from the same component spec when the Figma plan's
  call limit ran out.
- **Textures:** the magazine's body and ammo textures, baked in Blender.
- **Blender:** the Pigeon's deerstalker and the Seagull's red headband (`Blender/Outfits`), the Washington Square blockout, cars, Citi Bikes and dock, food carts and trucks, props (chess board, hot dog, bench, pills bottle, spotted egg, trash can, wallet, magazine), the traffic signal and walk signal posts, the fried egg splat, and the fountain splash. Several are built by scripts saved beside them (`*.py.txt`).
- **Code:** everything in `Assets/Scripts`, plus the `Shitshow/Parallax Lit` and `Shitshow/Foliage Translucent` shaders.

## Tools

Unity, Blender 4.5, Figma, and Claude Code (Anthropic), which helped write code, shaders, Blender scripts and these docs.
