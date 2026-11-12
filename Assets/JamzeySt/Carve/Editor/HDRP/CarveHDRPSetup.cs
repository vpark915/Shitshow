using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace JamzeySt.Carve.Editor.HDRP
{
    // HDRP surface options Carve depends on. Switching a material from HDRP/Lit to Carve keeps Lit's values
    // (Depth Offset off), which silently disables real depth. Carve cuts its outlines itself (clip in the shader), so
    // HDRP Alpha Clipping stays off: HDRP would otherwise draw the material twice (forced depth prepass, then GBuffer).
    static class CarveHDRPSetup
    {
        [InitializeOnLoadMethod]
        static void Register()
        {
            EditorApplication.delayCall += UpgradeProjectMaterials;
            CarveMaterialDrawer.SetupProblem = Problem;
            CarveMaterialDrawer.FixSetup = Fix;
            CarveProject.AddCheck(() =>
            {
                var bad = new List<Material>();
                foreach (var r in CarveProject.SceneRenderers())
                    foreach (var m in r.sharedMaterials)
                        if (CarveProject.IsCarve(m) && Problem(m) != null && !bad.Contains(m)) bad.Add(m);
                return bad.Count == 0
                    ? CarveCheck.Ok("Carve materials set up for depth", "Depth Offset on; outlines cut by Carve, drawn once.")
                    : new CarveCheck { level = CarveCheckLevel.Error, title = $"{bad.Count} Carve material(s) need their surface options fixed", detail = "Depth Offset off (usually left over from switching a Lit material to Carve: relief has no real depth), or HDRP Alpha Clipping on (the material is drawn twice).", fixLabel = "Fix", fix = () => { foreach (var m in bad) Fix(m); } };
            });
        }

        // Carve Lit and Carve Layered Lit cut their outlines in the shader; Shader Graphs built on the Carve POM node cut them
        // through the graph's Alpha, so those keep HDRP Alpha Clipping.
        static bool CutsOwnOutlines(Material m) =>
            CarveProject.IsCarve(m) && m.shader.name.StartsWith("Carve/HDRP/") && m.HasProperty("_CarveSilhouette") && m.HasProperty("_AlphaCutoffEnable");

        static bool ClipOn(Material m) => m.GetFloat("_AlphaCutoffEnable") > 0.5f;

        public static void SyncClip(Material m)
        {
            if (CutsOwnOutlines(m)) m.SetFloat("_AlphaCutoffEnable", 0f);
        }

        // Carve materials from before self-clipping have HDRP Alpha Clipping on; switch it off once per project.
        // Carve materials are found by their shader GUID in the .mat text, so only those get loaded.
        static void UpgradeProjectMaterials()
        {
            string key = "JamzeySt.Carve.SelfClipUpgrade.v1." + Application.dataPath;
            if (EditorPrefs.GetBool(key, false)) return;
            EditorPrefs.SetBool(key, true);
            var shaderGuids = new List<string>(AssetDatabase.FindAssets("Carve t:Shader"));
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat")) continue;
                string text;
                try { text = System.IO.File.ReadAllText(path); } catch { continue; }
                if (!shaderGuids.Exists(text.Contains)) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || !CutsOwnOutlines(m) || !ClipOn(m)) continue;
                SyncClip(m);
                HDMaterial.ValidateMaterial(m);
                CarveLayers.SyncKeyword(m);
                EditorUtility.SetDirty(m);
                count++;
            }
            if (count > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[Carve] {count} Carve material(s) now draw once (outlines cut by Carve, HDRP Alpha Clipping off).");
            }
        }

        public static string Problem(Material m)
        {
            if (!CarveProject.IsCarve(m) || !m.HasProperty("_DepthOffsetEnable")) return null;
            var issues = new List<string>();
            if (m.GetFloat("_DepthOffsetEnable") < 0.5f || !m.IsKeywordEnabled("_DEPTHOFFSET_ON")) issues.Add("Depth Offset is off");
            if (CutsOwnOutlines(m) && ClipOn(m)) issues.Add("HDRP Alpha Clipping is on (drawn twice)");
            return issues.Count == 0 ? null : string.Join("; ", issues);
        }

        public static void Fix(Material m)
        {
            Undo.RecordObject(m, "Fix Carve Material Setup");
            m.SetFloat("_DepthOffsetEnable", 1f);
            m.SetFloat("_ConservativeDepthOffsetEnable", 1f);
            SyncClip(m);
            HDMaterial.ValidateMaterial(m);
            CarveLayers.SyncKeyword(m);
            EditorUtility.SetDirty(m);
        }
    }
}
