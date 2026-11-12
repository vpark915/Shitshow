# Carve – Silhouette Parallax Occlusion Mapping for HDRP & URP

Real depth on flat surfaces: carved outlines, true intersections, relief shadows and light, on any mesh, with a height map
generator, a one-click converter from Lit (HDRP and URP) and one window for everything (Tools > Carve > Carve Hub).

## Quick start

1. Import the package; the Setup Wizard opens (Tools > Carve > Setup Wizard). Press its Fix buttons.
   URP: its first fix imports Carve URP Content.unitypackage (URP shaders, materials, demo scenes), also under Tools > Carve > Import URP Content.
2. Try Demo/Scenes/Carve_Showcase or Carve_Village (URP: Demo/Scenes/URP/*_URP). WASD, Shift, C Carve on/off, V debug views, F1 help.
3. Create a material with Carve/HDRP/Carve Lit or Carve/URP/Carve Lit, or select Lit materials > Assets > Carve > Convert to Carve.
4. Assign a Height Map and set Depth (cm). No height map? Click Generate Height Map.
5. Boxes and modular meshes: Edge Silhouettes = Baked, then Carve Hub > Meshes > bake.
6. GameObject > Carve > Quality Manager for scene-wide presets and adaptive performance.

Requirements: Unity 6.0 LTS or newer, HDRP 17+ or URP 17+ (Forward, Forward+, Deferred). URP decals need the Decal renderer feature.

Full manual: Documentation/Carve Documentation.pdf

## Support

jamzeyst@gmail.com (please include your Unity version, render pipeline and Carve version).
