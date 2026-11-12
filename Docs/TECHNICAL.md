# Technical: the visual stack

How ShitShow is drawn, as of November 2026. Values are the live settings in `Assets/Settings` and
`Assets/VolumeProfiles/TestProfile.asset`; the editor tools that produced them are named so they can be rerun.

## Pipeline

- **Unity 6000.3.10f1, Universal Render Pipeline 17.3**, render graph.
- **Pipeline asset:** `Settings/PC_RPAsset` (a `Mobile_RPAsset` also exists). HDR on, render scale 1, no MSAA, SRP
  Batcher on, **GPU Resident Drawer** (instanced drawing) on, 32 px colour grading LUT, depth and opaque textures on.
- **Renderer:** `Settings/PC_Renderer`, **Deferred+** rendering path, depth priming Auto, native render pass on.
- **Camera:** post-processing on, no anti-aliasing.
- **Turned off in the pipeline asset** (for build time and frame time): additional lights (so point and spot lights,
  the muzzle flash's included, light nothing), additional light shadows, light layers, light cookies. Terrain holes
  stay on: the park terrain uses them.

## Lighting

- **Sun:** one directional light, Mixed, intensity 4.5, soft shadows. Sky fill (ambient) 1.15, so the shade reads lit rather than absorbed.
- **Baked lighting** (`Settings/LvlLighting`): GPU lightmapper, 64 direct / 512 indirect / 512 environment samples, 4
  bounces, OIDN denoising, 5 texels/m at up to 4096 (3 maps), directional, high-quality compression, baked AO (1 m),
  indirect 1.0, albedo boost 1.2. **Shadowmask** mixed lighting: realtime sun shadows near, baked (with a soft 1° sun)
  past the realtime distance, where Indirect Only had left the far park unshadowed. Realtime GI (Enlighten) off. The
  probe volume (APV) bakes sky occlusion and dilates buried probes; 30 baked reflection probes at 256. Rebake with
  Generate Lighting in the Lighting window (`Clean Daylight Bake` resets much of this to older values).
- **Shadows:** 45 m distance (was 70: ~5 ms of GPU), 2 cascades (split 0.25), 2048 shadow map, soft shadows at the high quality setting,
  last cascade border fade 0.5. The sun is the only realtime light (additional lights are off, above).
- **Sky and ambient:** `Skybox/Panoramic` with Poly Haven's *Kloofendal 48d Partly Cloudy* pure-sky HDRI
  (`Tools > Wash Square > HDRI Sky`). The HDRI's sun is clamped (it would otherwise bake in as a second, unblockable
  sun) and the photo is spun so its sun lines up with the scene's light. Ambient and reflections come from the skybox.

## Renderer features (`PC_Renderer`)

| Feature | State | Notes |
|---|---|---|
| Screen Space Ambient Occlusion (URP) | **on** | Interleaved gradient noise, depth + normals, full resolution, after opaques, intensity 3.27, direct lighting strength 0.43, high samples and blur |
| Volumetric Fog (Cristian Qiu, `com.cqf.urpvolumetricfog`) | **on** | Settings on the volume (below) |
| Decal | **on** | Grime, leak and moss decals on buildings (`Tools > Wash Square > Make Weathering Decals`, `Assets/Decals`) |
| HTrace Ambient Occlusion V2 | off | Was the AO; replaced by URP SSAO. Its volume settings are still in the profile |
| HTrace SSGI | off | Screen-space GI, available |
| Ground Truth Ambient Occlusion (`Assets/GTAO`) | off | Our render graph port of bladesero's GTAO_URP |
| Screen Space Global Illumination URP (jiaozi158) | off | Alternative SSGI |
| God Rays (`Assets/god-ray-pack`, ported to render graph) | off | |
| Volumetric Fog (fullscreen pass), FullscreenCustomLighting (Mirza AERO) | off | Earlier fog experiments |

Contact shadows were tried (a custom screen-space pass, then a port of keijiro's) and removed.

## Post-processing (global volume, `TestProfile`)

| Override | Values |
|---|---|
| Tonemapping | ACES |
| Color Adjustments | post exposure -0.1, saturation +9 |
| Split Toning | shadows cool blue-grey, highlights warm |
| Bloom | threshold 1.01, intensity 0.44, scatter 0.09, warm tint, Gaussian filter |
| Depth of Field | Gaussian, starts at 537 m: only the far skyline softens |
| Motion Blur | camera only, low quality, intensity 0.06 |
| Volumetric Fog | 199 m distance, 30 m tall, ground on, density 0.011, anisotropy 0.73, scattering 0.25, main light only, 8 steps |
| Vignette, HTrace SSGI | present but inactive |

## Materials and shaders

| Shader | Where | Notes |
|---|---|---|
| URP Lit | Most of the world | Blender materials are capped to a matte smoothness on import (`MatteImports`) |
| URP Lit | Trash cans, the beech trees' far billboards | Trash cans keep emission for `Glow`; billboards are cutout, double-sided |
| `Carve/URP/Carve Lit` (JamzeySt Carve) | Buildings, ground, road, sidewalk (24 materials) | Silhouette parallax with relief self-shadowing and occlusion. `Tools > Wash Square > Make Carve Maps` bakes each set's stretched height, packed mask (R 1, G AO, A smoothness) and height pyramid, and BlockoutPostprocessor puts Carve on the blockout's parallax materials once they exist. No detail map, so these lost the weathering grime layer. Potato mode uses Carve's Low preset |
| `Shitshow/Foliage Translucent` | Tree leaves, ground plants (fern, lily of the valley) | Leaves lit through from behind; NatureManufacture wind subgraph (`Assets/Foliage`). A copy of NatureManufacture's Foliage shader, so the plants moved onto it to keep that shader (~1,900 variants) out of the build |
| NatureManufacture Bark, Cross, Water River | Trunks, ground foliage, fountain water | |
| Flat Kit Stylized Surface and Water | Pigeon, Seagull, gun, some water | Cel-stepped ramps in `Assets/RampTextures` |
| `PoliceOutline` (`Resources`) | Officers | A red rim so police read at a distance |
| URP Terrain Lit | Park lawn | ambientCG Grass004 terrain layer; legacy grass detail and NatureManufacture plant meshes, drawn to 35 m at 0.8 density (was 60 m: grass and trees were ~8 ms) |

Texture sets are ambientCG (CC0); `BlockoutPostprocessor` gives each blockout material its ambientCG normal, smoothness
and stain maps on every reimport of the Washington Square blend.

## Characters

- **Pigeon and Seagull:** Quirky Series birds with Flat Kit shading. `BirdOutfit` dresses the players' birds on every
  scene load (and `NetBird`'s models online): the Pigeon detective in a deerstalker (`Resources/Outfits/PigeonHat`),
  the Seagull in a red headband (`SeagullHeadband`) whose two tails are hand-rolled spring bones (gravity, damping and
  a slight breeze). Park birds stay plain. The outfits were modelled in Blender in the bird's own space
  (`Blender/Outfits`, generator `outfits.py.txt`), are turned 180° on import, and are lined up through the bird's rest
  pose, with their anchor bone riding the bird's head bone.
- **Grounding:** `BirdFeet` measures the posed mesh and lowers the pigeon model by the gap the CharacterController's
  skin width leaves (`RigDrop`, reused by `NetBird`), and drops each Seagull onto whatever is under it.
- **NPCs:** PSX characters on Mixamo and Quaternius clips (`Animations/Npc.controller`). `NpcSpawner` keeps spawns
  apart on the NavMesh and skips models without textures. Mixamo's walk overstretches the knee ("Lower Leg Stretch"
  up to 1.46), which folds the PSX rigs' calves sideways mid-stride, so the controller uses copies in
  `Animations/NpcClips` with that muscle capped at 1 (`Tools > Wash Square > Straighten NPC Knees`; rerun after
  reimporting the clips).
- **Pickups:** the ammo magazine's textures were missing from its pack and were rebaked in Blender
  (`Models/Pickups/Textures`: `MagBody` and `MagAmmo`, generator `magbake.py.txt`).

## Performance

**Normal** (the game as set up in the assets): HTrace GTAO on (Before Lighting, 0.6 m radius, full resolution) and
the other renderer features off, no bloom, depth of field or motion blur, sun shadows to 60 m in two cascades fading
out over the last 20%, grass to 35 m, trees to 250 m (billboards from 30 m with a 10 m cross-fade), LOD bias 1, Carve at
its Low preset (`GraphicsQuality.CarveLow`). Every LOD group cross-fades (dithered) rather than popping
(`GraphicsQuality.CrossFade`, at each scene load); benches never cull. People fade out past about 60 m (a one-level LOD
group each, `CrowdCulling`) and cast shadows to 25 m.

**Potato** (Settings > Graphics, `GraphicsQuality`) is for weak machines: no renderer features, no parallax (Carve flat),
no sun shadows, no grass, trees half as far, LOD bias halved (so people fade at ~30 m), 75% render scale, no people shadows.

Profiled in the editor (`Tools > Wash Square > Log Feature Costs`, Play mode, offline): ~22 ms GPU and ~18 ms main
thread a frame. The crowd is the biggest GPU cost (~9 ms: every person is a skinned mesh, unbatched, drawn about five
times a frame), then grass and trees, Carve, sun shadows and the volumetric fog (~3-7 ms each).

- `CrowdCulling` (starts itself): people past 25 m cast no shadow and people past 100 m aren't drawn, per camera as it
  renders (`forceRenderingOff`, so `Npc` keeps control of `enabled`). Halved in Potato.
- Carve relief fades out between 25 and 60 m but is cheap per pixel: 6-16 steps, 2 refine, 8 shadow steps, Auto LOD 0.6, 1024 height maps (`CarveMaps.Performance`). SSAO falls off at 150 m (was 4.6) and HTrace GTAO fades out at 150 m (was 65.5).
- SSAO at half resolution; shadows 45 m; grass 35 m at 0.8 density, tree billboards from 20 m, 30 full trees.

## Effects

- **Blood, egg blast, shell bits:** quick streak particles (`Weapon.Spray`).
- **Egg splat:** a rigged fried egg (`Resources/EggSplat`, `EggSplat.cs`). 21 bones drop onto the ground under them so
  it drapes over slopes and kerbs; it pops in, then shrinks away.
- **Fountain:** splash flipbook and mist (`WaterSplashes`), from a Blender-rendered flipbook.
- **Hit flash:** `Glow` tints a body red, or blue for a bagel-blocked hit.
- **Traffic signals:** emissive lenses and textured walk faces swapped by `TrafficSignal` on a shared clock.

## UI

- **HUD:** NYC subway signage (`WeaponHUD`): numbers as coloured line bullets, cards designed in Figma ("Subway
  Signage", exports in `Resources/SubwayHUD`); its digit glyphs are re-rendered in TeX Gyre Heros Bold. Signs kick on firing and sway when idle; Headshot and Kill signs burst
  over the crosshair.
- **Menus, match board, tutorial steps:** `SubwayGUI` (IMGUI on a 1280x720 canvas): black signs with a white rule,
  TeX Gyre Heros (a free Helvetica clone; Regular, Bold for titles), numbered line-bullet buttons. They float, pop in, and burst on selection. In-world prompts get the same
  sign backing, pop in on hover, and burst on E.
- **Cursor:** `CursorLock` keeps it locked and hidden whenever no SubwayGUI menu is up.
- **Start menu:** `MenuShowcase` (added by `HuntMatch`) turns the live park into one full-screen collage behind the
  menu sign: slow drifting wide shots (fountain, arch, food trucks, a street corner, overhead, the whole city) cut
  with close ones (angled fronts of the pigeon and the seagull, following a flock). It draws through its own camera;
  the game camera stays enabled with culling mask 0 and post-processing off, because the minimap, prompts and gun go
  by `Camera.main`. The HUD and minimap are hidden. When a game starts the view flies onto the pigeon's camera, then
  hands back. Signs re-appear by frame count, not time, so slow frames don't re-pop the menu.
- **Firing:** tap to fire (holding doesn't repeat; a click is buffered 0.15 s). Flock birds become trigger hitboxes
  while flying, and `Weapon.AimPoint` hits triggers that carry `Health`, so they can be shot in the air.
- **Minimap:** baked per scene (`Tools > Wash Square > Bake Minimap`, `Textures/Minimap_<scene>.png`).

## Audio

- **Sound bank:** `Resources/SoundBank` lists every sound by name, with its clips (one picked at random each play),
  volume, pitch range, carrying distance and cooldown. Code calls `Sfx.Play("name", position)`, or `Sfx.Play("name")`
  for flat UI sounds. A name with no clips is silent, so hooks can go in before their sounds exist.
- **`Sfx`:** a pool of 24 reused audio sources kept across scenes, with linear rolloff (silent at the far distance)
  and no doppler.
- **Starting on time:** clips are trimmed to their first audible sample, imported as Decompress On Load with preload
  on (Vorbis, mono), and the DSP buffer is 256 samples (Best Latency) so a sound starts within a few milliseconds.
- **Hooked up:** shot, police shot (just the gunfire), reload, dry fire, neck snap (assassinate), the punch when the
  player's shot hits a living target (`body hit`, `headshot hit`; nothing for walls or misses), body thud (`Health.Thud`, `thudDelay` after anyone goes limp), and
  NPC footsteps. The other player hears the shot, reload and neck snap through `NetBird.SoundRpc`.
- **NPC footsteps:** `Footsteps` (added by `Npc`) plays `npc step` when a foot comes down in the animation, read from
  the foot bones, for people moving within 25 m of the camera. Footsteps are a background sound: at most 8 play at
  once and they never cut off another sound.
- **Player footsteps:** `PlayerSteps` (put on every player rig by itself) plays `player step` while the
  pigeon is on the ground under its own steam (not flying or riding) with the move keys held; letting go cuts off
  the step that's sounding. The bird has no foot bones, so steps come from its speed: one per 0.35 m, at least 0.11 s
  apart. Quiet (6%) and pitched up for small feet.
- **Ambience:** `Ambience` loops `park ambience` flat under everything from the first scene, starting at a random point
  and fading in. The clip streams from disk. Its last 3 s were crossfaded into its start so the loop has no seam.
- **Jazzy drums (flat, `Sounds/jazzydrums`):** every `SubwayGUI.Button` plays `menu hover` (the open hi-hat, a little
  quieter and higher) as the mouse comes onto it, and `menu click` (the same hi-hat) the moment it's clicked, before
  the sign bursts. `WeaponHUD` plays `kill` (the snare) with each Kill sign, and a streak
  adds `double kill` / `triple kill` (the low tom, pitched up for a triple) over it, since a streak's deed comes in the
  same frame as its kill.

## World signage

- **Street signs:** designed in Figma ("Wash Square Street Signs": green DOT blades, brown Greenwich Village Historic
  District blades, Broadway guide, One Way, Don't Honk, walk faces, gym station blades, DOT info panel), exported to
  `Resources/StreetSigns`. `StreetFurniture` turns a PNG into a plate as wide as its art, cut out at the rounded
  corners, with a metal back. The import keeps non-power-of-two sizes (the plate's width comes from the art).
- **Placement:** `Tools > Wash Square > Place Street Furniture` (blades on corner lamps, regulatory signs, a traffic
  signal and walk post per park corner); `Update Mechanics Gym` builds the gym's pole signs; `Build Tutorial` copies
  the gym.

## Asset pipeline

- **Blender:** `.blend` files sit in `Assets/Blender` and Unity imports them directly (Blender must be installed).
  Scripted models keep their generator beside them as `*.py.txt`; run with
  `Blender -b -P <script> -- <out.blend>`. For rigged or rotated-on-spawn models, turn on Bake Axis Conversion in the
  import settings. The egg's skinned meshes come in flattened at the root, so its prefab is rebuilt by re-parenting
  them under their shapes.
- **Figma:** sign and HUD art exported as RGBA PNG. The Figma Starter plan limits MCP calls, so some of the sign set
  was rendered locally from the same component spec (Overpass and Libre Caslon, matching the Figma layout to the pixel).
- **Prefabs from code:** cars, Citi Bikes, trash cans and the player are rebuilt by `Tools > Wash Square > Build Car
  Prefabs`, `Build Citi Bike Prefab`, `Build Trash Can Prefab` and `Make Player Prefab`.
- **Build size:** `Shrink Build`, `Fewer Shader Variants` and `Apply Performance Settings` trim the build; `Log Frame
  Stats` and `Log Feature Costs` (play mode) measure it.

## Builds

- **Shipped scenes:** `WashSquareHunt` (the game), `MechanicsGym` and `Tutorial` (both opened from the game's menu).
  `Shrink Build` keeps exactly these three.
- **Shader variant stripping:** most of the build time is compiling shader variants (a cold cache took over six hours;
  the terrain shader alone was ~42,000 variants at ~0.65 s each). To keep it down:
  - Graphics settings: lightmap modes custom, keeping only plain and directional (the bake is Indirect Only); fog
    modes custom, keeping only Exp2; instancing variants Strip Unused.
  - The pipeline asset features turned off above (additional lights, their shadows, light layers, cookies) remove
    their keywords from every lit shader.
  - `Editor/StripUnusedShaders` drops all ProBuilder shaders: ProBuilder keeps its default material in a Resources
    folder, so its vertex colour shader shipped with 6,000+ variants though no scene uses it.
- Later builds reuse the shader cache in `Library`, so only the first build after a settings or shader change is
  slow. Don't delete `Library` before a build.

## Known loose ends

- `TestProfile` saturation is at +9 and post exposure at -0.1, adjusted by hand. **Don't run Clean Daylight Bake** to rebake: it resets the sun, sky, grade and bake settings to its own older values; use Generate Lighting in the Lighting window. Whites (the egg, the sidewalks)
  run bright in full sun.
- **`Archive/`** (outside Assets, so Unity ignores it; still in git) holds what no scene, prefab, Resources folder, render
  pipeline or script used: ContactShadows-master, Assets/Fonts, ~360 MB of unplaced NatureManufacture models and
  textures, FlatKit's demo content, unused materials, sounds and Starter Assets samples. Each file kept its `.meta`, so
  moving one back to the same path under Assets restores it with its references. Shader include files and the probe
  volume's `.bytes` data aren't tracked as dependencies, so they were kept. `Shitshow/Parallax Lit` (the old parallax
  shader, `Archive/Parallax`) is there too: Carve replaced it.
- The beech trees' far "Cross" billboards are on URP Lit (cutout, double-sided, tinted by their old Healthy Color), not
  NatureManufacture's Cross shader, which was ~5,200 variants of the build's shader compile.
- Traffic cars don't stop for the signals yet.
- With the NPC knee capped at straight, the stance leg can look a little stiff; uncap a clip by pointing the
  controller back at its original if it reads worse.
- The muzzle flash still carries a point light, which lights nothing while additional lights are off.
