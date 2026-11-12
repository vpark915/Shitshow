using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace JamzeySt.Carve.Editor.URP
{
    // URP health checks (Carve Hub and Setup Wizard) and the passes the cost meter times.
    static class CarveURPChecks
    {
        [InitializeOnLoadMethod]
        static void Register()
        {
            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset) && !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            CarveProject.AddCheck(Content);
            CarveProject.AddCheck(GameCameraAA);
            CarveProject.AddCheck(Decals);
            if (CarveCostMeter.Markers.Count == 0)
            {
                CarveCostMeter.Markers.Add(("DepthPrepass", "Depth prepass"));
                CarveCostMeter.Markers.Add(("DrawDepthNormalPrepass", "Depth normals"));
                CarveCostMeter.Markers.Add(("Draw GBuffer", "GBuffer"));
                CarveCostMeter.Markers.Add(("DrawOpaqueObjects", "Opaque"));
                CarveCostMeter.Markers.Add(("MainLightShadow", "Shadow maps"));
            }
        }

        // URP shaders, materials and demo scenes ship in Carve URP Content.unitypackage, so HDRP projects never see them.
        public static string ContentPackagePath =>
            AssetDatabase.FindAssets("Carve URP Content").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith(".unitypackage"));

        public static bool ContentImported => Shader.Find("Carve/URP/Carve Lit") != null;

        [MenuItem("Tools/Carve/Import URP Content", priority = 2)]
        public static void ImportContent()
        {
            string path = ContentPackagePath;
            if (path != null) AssetDatabase.ImportPackage(path, false);
            else EditorUtility.DisplayDialog("Carve", "Carve URP Content.unitypackage is not in the project. Re-import Carve from the Package Manager.", "OK");
        }

        static CarveCheck Content()
        {
            if (ContentImported) return CarveCheck.Ok("URP content imported", "Carve's URP shaders, materials and demo scenes.");
            return new CarveCheck
            {
                level = CarveCheckLevel.Error,
                title = "Import Carve's URP content",
                detail = "URP shaders, sample materials and demo scenes come in Carve URP Content.unitypackage.",
                fixLabel = "Import",
                fix = ImportContent
            };
        }

        static CarveCheck GameCameraAA()
        {
            var cam = Camera.main;
            if (cam == null)
                return new CarveCheck { level = CarveCheckLevel.Info, title = "No Main Camera in the open scenes", detail = "Tag your game camera MainCamera so Carve can check its anti-aliasing." };
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null && data.antialiasing == AntialiasingMode.TemporalAntiAliasing)
                return CarveCheck.Ok("Game camera uses TAA", cam.name);
            return new CarveCheck
            {
                level = CarveCheckLevel.Warning,
                title = "Game camera is not using TAA",
                detail = "POM detail is finer than a pixel; TAA cuts the shimmer. (Or raise Stability on the materials.)",
                fixLabel = "Use TAA",
                fix = () =>
                {
                    var d = cam.GetComponent<UniversalAdditionalCameraData>();
                    if (d == null) d = Undo.AddComponent<UniversalAdditionalCameraData>(cam.gameObject);
                    Undo.RecordObject(d, "Use TAA");
                    d.antialiasing = AntialiasingMode.TemporalAntiAliasing;
                }
            };
        }

        // Carve Decals need URP's Decal renderer feature on the renderer in use.
        static CarveCheck Decals()
        {
            bool used = CarveFind.All<DecalProjector>().Any(p => p.material != null && p.material.shader != null && p.material.shader.name.StartsWith(CarveProject.ShaderPrefix));
            if (!used) return CarveCheck.Ok("Carve Decals ready", "No Carve Decal projectors in the open scenes.");
            var renderers = AssetDatabase.FindAssets("t:UniversalRendererData").Select(g => AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            bool hasFeature = renderers.Any(r => r.rendererFeatures.Any(f => f is DecalRendererFeature && f.isActive));
            if (hasFeature) return CarveCheck.Ok("Decal renderer feature found", "Carve Decal projectors render.");
            return new CarveCheck
            {
                level = CarveCheckLevel.Warning,
                title = "Carve Decals need URP's Decal renderer feature",
                detail = "Add Renderer Feature > Decal on your Universal Renderer, or the decals won't draw.",
                fixLabel = "Show renderer",
                fix = () =>
                {
                    var r = renderers.FirstOrDefault();
                    if (r == null) return;
                    Selection.activeObject = r;
                    EditorGUIUtility.PingObject(r);
                }
            };
        }
    }
}
