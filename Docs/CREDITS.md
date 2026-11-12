# Credits: outside resources

Everything in ShitShow that came from outside the project. Where a license isn't certain it says **check**:
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
| HTrace: Ambient Occlusion V2 | IPGames | `Packages/dev.ipgames.htrace-ao-v2` | In use: active on PC_Renderer, alongside URP SSAO |
| AERO: Volumetric Fog and Mist; VFX Toolkit | Mirza Beig | `Assets/Mirza` | AERO renderer feature off; demo scripts present |
| Forest Environment Dynamic Nature, Foliage/Object/Water shaders, NatureManufacture Wind | NatureManufacture | `Assets/NatureManufacture Assets` | Trees, ferns, plants, bark and cross shaders, river water, wind. Our `Shitshow/Foliage Translucent` shader is built on their wind subgraph |
| Low Poly Nature Bundle | LMHPOLY | `Assets/LMHPOLY` | Vegetation |
| Recreation park | Tolik_Cola | `Assets/Tolik_Cola` | Benches and park props |
| Quirky Series (Pigeon and Seagull packs; folder Ultimate Pack Vol.1) | Omabuarts Studio | `Assets/Quirky Series` | Pigeon and Seagull animals. Asset Store EULA |
| Low Poly Guns | **check** publisher: probably Hanusiak (Asset Store "Low Poly Guns Asset", 105979), not confirmed from the files | `Assets/Low Poly Guns` | The pigeon's gun (pistol3) |
| Toon Muzzleflash Pack | Infima Games | `Assets/Toon Muzzleflash Pack` | Muzzle flashes. Asset Store EULA |
| Procedural Skybox 2.0 Lite | **check** publisher | `Assets/Procedural Skybox 2.0 Lite` | Earlier sky; replaced by the HDRI |
| God ray pack (VolumetricLightScattering) | **check** publisher | `Assets/god-ray-pack` | Ported to render graph; currently off |
| Quick Outline | Chris Nolet | `Assets/QuickOutline` | Object outlines. MIT (**check**) |
| Prefab Brush | Archie Andrews | `Assets/Prefab Brush` | Editor tool for placing props |
| PSX characters ("Paquete de modelos psx 3": Characters_psx, Characters_psx_01, Extras) | Elbolilloduro, elbolilloduro.itch.io | `Assets/Blender/PSXCHARACTERS` | NPC people and police. Models CC0; some bundled textures are from Textures.com, which may not be redistributed on their own |
| PSX Mega Pack (pistol magazine, `pistol_mp_1_mag_loaded`) | Pizza Doggy, pizzadoggy.itch.io (credit appreciated, not required; its license forbids using the assets, or images of them, as generative-AI input or training data) | `Assets/Models/Pickups` | Ammo pickup. Its textures weren't included; ours are rebaked in Blender (`Models/Pickups/Textures`) |

## Animation

| What | From | License |
|---|---|---|
| Idle, Happy Idle, Standard Walk and other clips | Mixamo (Adobe) | Mixamo terms: free for use in games. The NPCs play edited copies (`Animations/NpcClips`, knee stretch capped) |
| Animations | Quaternius | CC0 (`Assets/Animations/Quaternius/License.txt`) |

## Textures and sky

| What | From | License |
|---|---|---|
| Asphalt031, Bricks109A, Concrete025, Concrete031, Concrete034, Concrete047A, Concrete048, Fabric030, Granite002A, Granite003A, Grass004, Gravel023, Ground054, Ground078, Marble012, Metal009, Metal027, Metal029, Metal054A, PaintedMetal002, PaintedMetal004, PavingStones046, Plastic012A, Rock020, RoofingTiles013A, Rubber004, Wood051, SurfaceImperfections013 | ambientCG | CC0 |
| asphalt_02 | Rob Tuytel, Poly Haven | CC0 |
| Kloofendal 48d Partly Cloudy (Pure Sky) HDRI | Greg Zaal (original), Jarod Guest (sky edits), Poly Haven | CC0 |

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
| Music and menu sounds: fastparkjazz.wav, in_game_chase.wav, loudSaxA.wav, sidestick.wav, jazzydrums (open hi-hat, vintage snare, low tom) | **check** source | Shipped in the game; not yet credited in-game |

## Fonts

| What | From | Used for | License |
|---|---|---|---|
| Arimo (`Assets/Resources/Fonts/Arimo-Regular.ttf`, `-Bold.ttf`; full family in `Assets/TextMesh Pro/Fonts/Arimo,DM_Sans/Arimo`) | Steve Matteson | All in-game text: HUD, menus, prompts, minimap, gym panels, loading screen | SIL Open Font License (`Arimo-License.txt`) |
| Inter (`Assets/Resources/Fonts/Inter-Regular.ttf`, `-Bold.ttf`, the 24pt cuts; full family in `Assets/Fonts/Inter`) | Rasmus Andersson | Replaced by Arimo; the HUD's number glyphs (`Resources/SubwayHUD/glyph_*.png`) are still rendered from its Bold | SIL Open Font License (`Inter-License.txt`) |
| TeX Gyre Heros (`Assets/Fonts/TeXGyreHeros-Regular.otf`, `-Bold.otf`; full family in `Assets/Fonts/TeX-Gyre-Heros`) | GUST e-foundry | No longer used (replaced by Inter); still in the project | GUST Font License, free to embed (`TeXGyreHeros-License.txt`) |
| Overpass | Delve Withrington, Dave Bailey and Thomas Jockin (Delve Fonts) for Red Hat, based on Highway Gothic | Street sign and gym blade art (baked into PNGs; the font file isn't in the project) | SIL Open Font License |
| Libre Caslon Text | Pablo Impallari | "Greenwich Village Historic District" band on the brown blades (baked into PNGs) | SIL Open Font License |

## Store page

| What | From | Used for | License |
|---|---|---|---|
| Rolling green hills under a bright blue sky | Photo by [Jonathan Rathgeb](https://unsplash.com/@jonathan_rthg?utm_source=unsplash&utm_medium=referral&utm_content=creditCopyText) on [Unsplash](https://unsplash.com/photos/rolling-green-hills-under-a-bright-blue-sky-qq2BAvQgp4Q?utm_source=unsplash&utm_medium=referral&utm_content=creditCopyText) | Game capsule art | Unsplash License |

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
