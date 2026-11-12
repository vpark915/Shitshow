# Architecture

How ShitShow fits together, as of November 2026: how a frame is drawn, and which scripts control what. For each
setting's exact values see `TECHNICAL.md`; each script's own header comment has the details of what it does.

## Graphics

### One frame

```
  Assets/Settings/PC_RPAsset  (URP 17.3, render graph, HDR, SRP Batcher, GPU Resident Drawer)
        |
        v
  PC_Renderer  (Deferred+, depth priming, native render pass)
        |
        |   shadow pass ........ sun only, 2 cascades, 70 m, 2048 map
        |                         (additional lights, their shadows, light layers and cookies are off)
        v
  +------------------------------------------------------------------+
  | G-buffer (opaques)                                               |
  |   URP Lit ............... most of the world                      |
  |   Carve Lit ............. buildings, ground, road, sidewalk      |
  |                           (silhouette POM, relief shadows)       |
  |   Shitshow/Parallax Lit . trash cans (POM, keeps emission)       |
  |   Foliage Translucent ... tree leaves (NatureManufacture wind)   |
  |   NM Bark / Cross ....... trunks, ground plants                  |
  |   URP Terrain Lit ....... park lawn (+ grass detail to 60 m)     |
  |   Flat Kit Stylized ..... pigeon, seagull, gun (cel ramps)       |
  |   PoliceOutline ......... red rim on officers                    |
  +------------------------------------------------------------------+
        |
        v
  Decals ............. grime, leaks, moss on buildings
  SSAO (URP) ......... full res, depth + normals, after opaques
        |
        v
  Deferred lighting
        sun (realtime direct + shadows)
      + lightmaps (3, Indirect Only bake: bounce light)
      + skybox ambient and reflections (Kloofendal HDRI, sun clamped)
        |
        v
  Skybox, then transparents  (water, particles, egg splats, tracers)
        |
        v
  Volumetric Fog  (com.cqf.urpvolumetricfog, main light only, 8 steps)
        |
        v
  Post-processing  (global volume, VolumeProfiles/TestProfile)
        Bloom -> Depth of Field (far skyline only) -> Motion Blur (camera, 0.06)
        -> Color Adjustments + Split Toning -> ACES tonemap
        |
        v
  UI on top
        WeaponHUD (subway-sign HUD, uGUI canvas)    DamageIndicator (same canvas)
        Minimap                                     SubwayGUI (IMGUI menus, 1280x720)
        Loading (covers everything during scene changes)
```

Off but still in the project: HTrace AO and SSGI, `Assets/GTAO`, jiaozi158 SSGI, god rays, the older fog passes.

### Cameras

```
  Main Camera  (Camera.main; Cinemachine third-person follow on the pigeon's CinemachineCameraTarget)
     |   HeadRide pulls it back while riding a person or a vehicle
     |   Weapon aims from its screen centre; prompts, minimap and the gun all go by it
     |
     +-- in the start menu: culling mask 0, post off (still enabled, for the above)
     |
  MenuShowcase camera  (start menu only)
     |   draws the park collage behind the menu sign,
     |   then flies onto the main camera's view when a game starts and hands back
     |
  Minimap  (top-down picture taken once at start; Settings/Minimap_Renderer)
```

### Where the look comes from

```
  editor tools (Tools > Wash Square)        assets they write              used at runtime by
  ----------------------------------        ---------------------          ------------------
  Clean Daylight Bake, Punchy Lighting  ->  lightmaps, scene lighting  ->  deferred lighting
  HDRI Sky                              ->  skybox material            ->  sky, ambient, reflections
  Daylight Grade, Cheaper Shadows,
  Stronger Reflections                  ->  PC_RPAsset, TestProfile    ->  pipeline, post
  Make Weathering Decals                ->  Assets/Decals              ->  decal feature
  Bake Minimap                          ->  Textures/Minimap_<scene>   ->  Minimap
  Make Carve Maps                       ->  Carve heights, masks,      ->  Carve Lit
                                            pyramids
  BlockoutPostprocessor, MatteImports   ->  materials on import        ->  G-buffer
  Ship Shader Trace                     ->  StreamingAssets/ShaderWarmup -> ShaderWarmup
  Fewer Shader Variants, StripUnusedShaders -> smaller build            (build time only)
```

### Effects

```
  Weapon.Spray ......... blood, egg blast, shell bits (streak particles)
  EggSplat ............. rigged fried egg, bones dropped onto the ground
  WaterSplashes ........ fountain flipbook ring + mist, splashes where things move through
  Glow ................. emission on its own material copies: red hit flash, blue bagel block, food hover
  TrafficSignal ........ emissive lenses and walk faces on one shared clock
  BirdOutfit ........... deerstalker / headband, spring-bone tails
  BirdFeet ............. lowers the bird models onto the ground
```

## Scripts

### Layers

```
  +=====================================================================================+
  |  MATCH / MODE           HuntMatch (WashSquareHunt)   Tutorial   GymMenu (MechanicsGym)|
  |                         HuntTasks (asset)  RoundReset  MenuShowcase  Credits           |
  +=====================================================================================+
           | roles, rounds, clock, tasks, menus         ^ deeds (Weapon.Did), deaths
           v                                            |
  +=====================================================================================+
  |  PLAYER RIG  (the scene's pigeon: one per game, the local player drives it)          |
  |    ThirdPersonController   Weapon   Health   FoodPowers   HeadRide   PieceEater       |
  |    PlayerSteps   BirdFeet   BirdOutfit   RagdollToggle   Glow                         |
  +=====================================================================================+
           | shoots, rides, eats, searches              ^ damage, panic, pursuit
           v                                            |
  +=====================================================================================+
  |  WORLD                                                                               |
  |    People:    NpcSpawner -> Npc (+ Footsteps, Health, RagdollToggle)   TargetDummy   |
  |    Birds:     BirdFlocks                                                             |
  |    Vehicles:  Vehicle <- Car, CitiBike      TrafficCar drives a Car                  |
  |    Things:    FoodItem  TrashCan  Pickup  EggShell  EggSplat  chess pieces           |
  |    Scenery:   TrafficSignal  StreetFurniture  WaterSplashes  PlayStateOnStart        |
  +=====================================================================================+

  +=====================================================================================+
  |  ONLINE                 NetBird (one per player, Netcode player prefab)              |
  +=====================================================================================+
  |  UI                     WeaponHUD  DamageIndicator  Minimap  SubwayGUI  Loading      |
  +=====================================================================================+
  |  SERVICES (start themselves on scene load, live across scenes)                       |
  |    Loading  CursorLock  GameSettings  Music  Ambience  Sfx (+ SoundBank asset)       |
  |    ShaderWarmup  GraphicsQuality (normal / potato)                                   |
  +=====================================================================================+
```

### Scenes and what's in them

```
  WashSquareHunt  (the game; first in the build)
     HuntMatch + NetworkManager      -> start menu, online match, offline play
     player rig, NPCs object (NpcSpawner), BirdFlocks, cars, bikes, food, trash cans
     HUD canvas (WeaponHUD, DamageIndicator), Minimap
          |                                   |
          | "Mechanics Gym" button            | "Tutorial" button
          v                                   v
  MechanicsGym  (offline practice)       Tutorial  (copy of the gym, built by Build Tutorial)
     GymMenu (Escape menu, back to        Tutorial (step-by-step signs)
     the first scene), TargetDummy        GymMenu

  every scene change goes through Loading.Load(scene), never SceneManager.LoadScene
```

### The player rig: who controls what

```
                        PlayerInput / StarterAssetsInputs
                                     |
          +--------------------------+---------------------------+
          |                          |                           |
          v                          v                           v
  ThirdPersonController         Weapon                     FoodPowers
   move, sprint, jump,           hitscan from screen        stash of eaten foods,
   extra jumps (flying)          centre, ammo, reload,      timed abilities
   MoveSpeed / SprintSpeed  <----- tracer, Spray            (Sprint changes speed,
          ^   ^                   events: Fired, Hit,        x speedScale from HuntMatch),
          |   |                   Changed, Did (deeds)       egg bomb, bread flock, bagel
          |   |                       |                           ^
          |   +-- HeadRide            |                           |
          |       land on a head      v                     PieceEater
          |       or a Vehicle:   Health                     look + E: chess piece,
          |       drives Npc /     TakeDamage, Died, Hurt,   FoodItem, TrashCan
          |       Car / CitiBike,  ragdoll (RagdollToggle),  -> FoodPowers / Minimap reveal
          |       camera pull-back, Glow flash, Thud
          |       assassinate
          |
     PlayerSteps (footstep sounds from speed)   BirdFeet / BirdOutfit (looks only)
```

### Events: who listens to what

```
  Weapon.Did(weapon, deed) ------> HuntMatch.Deed     (Seagull task progress)
            "egg bomb", "kill",    Tutorial           (step checks)
            "search trash", ...    WeaponHUD          (Kill / Headshot / streak signs)
                                   NetBird            (one-off animations for the other game)

  Weapon.Fired(weapon) ----------> Npc                (civilians flee, police pursue)
                                   NpcSpawner, BirdFlocks (panic, take off)
                                   WeaponHUD          (signs kick)
  weapon.Hit / weapon.Changed ---> WeaponHUD          (hit signs, ammo)

  Health.Died -------------------> Weapon (drop gun), FoodPowers (end ability), Npc,
                                   BirdFlocks, TargetDummy
  Health.Hurt -------------------> DamageIndicator    (red edges, arrow), BirdFlocks
  (HuntMatch and NetBird poll health.IsDead each frame instead: respawns, round end, dead flag)

  HuntMatch.RoundBegan ----------> TrashCan           (restock)
  HuntMatch.BeginRound ----------> RoundReset.Run     (fresh crowd, flocks, food, vehicles)
                                   Minimap.ResetRound

  SubwayGUI.MenuOpen ------------> CursorLock         (free the cursor)
  Music.Open (set by menus) -----> Music              (menu track vs game track)
  GameSettings (PlayerPrefs) ----> Sfx, Music, Ambience volumes; mouse sensitivity
```

### Online: what's shared and what isn't

```
     GAME A (host / server)                              GAME B (client)
  +----------------------------+                    +----------------------------+
  | HuntMatch (server side)    |   NetworkVariables |  HuntMatch (client side)   |
  |   phase, round, hunter,    | =================> |   reads them, BeginRound   |
  |   clock, tasksDone, seed,  |                    |   when the round changes   |
  |   kill times, deaths,      | <================= |   ReadyRpc, SeagullDiedRpc,|
  |   ready, match             |   RPCs to server   |   PigeonDiedRpc, TaskDone, |
  |                            |                    |   StepRpc                  |
  |   PingRpc, StepRpc ------->|  to both / other   |                            |
  +----------------------------+                    +----------------------------+

  +----------------------------+                    +----------------------------+
  | my rig (scene pigeon)      |                    | my rig (scene pigeon)      |
  |        | copies pos,       |                    |        |                   |
  |        v speed, death      |                    |        v                   |
  | my NetBird (owner) --------+-- NetworkTransform +--> shows as pigeon/seagull |
  |                            |   speed, airborne, |                            |
  |                            |   dead, mount,     |                            |
  |                            |   vehicle          |                            |
  |                            |                    |  a shot hits it:           |
  |  rig.Health.TakeDamage <---+---- HitRpc --------+-- NetBird                  |
  |                            |   SoundRpc, ActRpc |                            |
  |                            |   (shot, reload,   |                            |
  |                            |    eat, neck snap) |                            |
  +----------------------------+                    +----------------------------+

  NOT shared (each game runs its own): NPCs, police, flocks, traffic, food, trash cans,
  chess pieces, pickups, splashes. RoundReset puts each game's park back every round.

  Connecting: Unity Relay via Multiplayer Services (sessions, join code).
     Host() -> session with "map" and "version" properties
     Join() -> refuses a different version; loads the host's map and rejoins if it differs
     NetworkConfig.ProtocolVersion = hash of Application.version (Netcode turns away other builds)
```

### Editor tools (`Assets/Scripts/Editor`, Tools > Wash Square)

```
  build the world     ParkGenerator  WashSquareHunt  HuntGym  MechanicsGymUpdate  TutorialBuild
                      StreetFurnitureTool  NpcSetup  MinimapBake  WeatheringDecals
  build prefabs       CarPrefabs  CitiBikePrefab  TrashCanPrefab  PlayerPrefab
  look                CleanBake  HdriSky  PunchyLighting  ParallaxLitGUI  LookCapture
  import              BlockoutPostprocessor  MatteImports  NpcKnees  CarveMaps
  data                HuntTasksAsset  HuntGymSettings  WashSquareHuntSettings
  build size, speed   BuildSize  StripUnusedShaders  ShaderTrace  FrameStats
```
