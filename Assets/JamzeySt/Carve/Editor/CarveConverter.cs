using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JamzeySt.Carve.Editor
{
    // One material's conversion: what it becomes, what carries over and what doesn't. Values are read from the
    // source when planned, so Apply works on the source (in place) or on a copy.
    public class CarveConversion
    {
        public Material source;
        public string from;
        public string to;
        public readonly List<string> carried = new List<string>();
        public readonly List<string> lost = new List<string>();
        public Action<Material> apply;
    }

    // Lit -> Carve (and back). Each pipeline registers rules for its own shaders; the Hub's Convert tab and the
    // Assets/Carve menu run them.
    public static class CarveConverter
    {
        static readonly List<Func<Material, CarveConversion>> s_ToCarve = new List<Func<Material, CarveConversion>>();
        static readonly List<Func<Material, CarveConversion>> s_Back = new List<Func<Material, CarveConversion>>();

        public static void AddRule(Func<Material, CarveConversion> rule) { if (!s_ToCarve.Contains(rule)) s_ToCarve.Add(rule); }
        public static void AddBackRule(Func<Material, CarveConversion> rule) { if (!s_Back.Contains(rule)) s_Back.Add(rule); }

        public static CarveConversion Plan(Material m) => First(s_ToCarve, m);
        public static CarveConversion PlanBack(Material m) => First(s_Back, m);

        static CarveConversion First(List<Func<Material, CarveConversion>> rules, Material m)
        {
            if (m == null || m.shader == null || !AssetDatabase.Contains(m)) return null;
            foreach (var rule in rules)
            {
                var c = rule(m);
                if (c != null) return c;
            }
            return null;
        }

        // Materials a rule can convert: in the open scenes, or every material asset under Assets/.
        public static List<Material> Candidates(bool sceneOnly)
        {
            var set = new HashSet<Material>();
            if (sceneOnly)
            {
                foreach (var r in CarveFind.All<Renderer>())
                    foreach (var m in r.sharedMaterials)
                        if (m != null && Plan(m) != null) set.Add(m);
            }
            else
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (Plan(m) != null) set.Add(m);
                }
            }
            var list = new List<Material>(set);
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // In place: the materials themselves change (Undo restores them). Copies: "<name>_Carve.mat" next to each
        // original, optionally swapped onto the renderers in the open scenes. Returns the converted materials.
        public static List<Material> Convert(IList<CarveConversion> plans, bool copies, bool useCopiesInScenes, bool generateHeights)
        {
            var result = new List<Material>();
            var swap = new Dictionary<Material, Material>();
            Undo.SetCurrentGroupName(copies ? "Create Carve copies" : "Convert to Carve");
            int group = Undo.GetCurrentGroup();
            try
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    var plan = plans[i];
                    EditorUtility.DisplayProgressBar("Carve", $"Converting {plan.source.name}", (float)i / plans.Count);
                    Material target = plan.source;
                    if (copies)
                    {
                        string src = AssetDatabase.GetAssetPath(plan.source);
                        string path = AssetDatabase.GenerateUniqueAssetPath($"{Path.GetDirectoryName(src).Replace('\\', '/')}/{plan.source.name}_Carve.mat");
                        target = new Material(plan.source);
                        AssetDatabase.CreateAsset(target, path);
                        Undo.RegisterCreatedObjectUndo(target, "Create Carve copy");
                        swap[plan.source] = target;
                    }
                    else
                        Undo.RecordObject(target, "Convert to Carve");
                    plan.apply(target);
                    EditorUtility.SetDirty(target);
                    result.Add(target);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (copies && useCopiesInScenes) SwapInScenes(swap);
            AfterConvert(result, generateHeights);
            Undo.CollapseUndoOperations(group);
            return result;
        }

        public static List<Material> ConvertBack(IList<CarveConversion> plans)
        {
            var result = new List<Material>();
            Undo.SetCurrentGroupName("Convert back from Carve");
            int group = Undo.GetCurrentGroup();
            foreach (var plan in plans)
            {
                Undo.RecordObject(plan.source, "Convert back from Carve");
                plan.apply(plan.source);
                EditorUtility.SetDirty(plan.source);
                result.Add(plan.source);
            }
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            CarveProject.InvalidateScan();
            return result;
        }

        static void AfterConvert(List<Material> mats, bool generateHeights)
        {
            var arr = mats.ToArray();
            if (generateHeights) CarveHeightGenerator.GenerateFor(Array.FindAll(arr, CarveHeightGenerator.CanGenerate));
            CarvePyramidBaker.Assign(Array.FindAll(arr, CarvePyramidBaker.UsesPyramid));
            AssetDatabase.SaveAssets();
            CarveProject.InvalidateScan();
        }

        static void SwapInScenes(Dictionary<Material, Material> swap)
        {
            foreach (var r in CarveFind.All<Renderer>())
            {
                var shared = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null && swap.TryGetValue(shared[i], out var copy)) { shared[i] = copy; changed = true; }
                if (!changed) continue;
                Undo.RecordObject(r, "Use Carve copies");
                r.sharedMaterials = shared;
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
            }
        }

        public static float F(Material m, string prop, float fallback) => m.HasProperty(prop) ? m.GetFloat(prop) : fallback;
        public static int I(Material m, string prop, int fallback = 0) => m.HasProperty(prop) ? Mathf.RoundToInt(m.GetFloat(prop)) : fallback;
        public static bool HasTex(Material m, string prop) => m.HasProperty(prop) && m.GetTexture(prop) != null;

        [MenuItem("Assets/Carve/Convert to Carve", false, 2000)]
        static void ConvertSelected()
        {
            var plans = SelectedPlans(Plan);
            if (plans.Count == 0) return;
            Convert(plans, false, false, true);
            Debug.Log($"[Carve] Converted {plans.Count} material(s) to Carve (Undo restores them).");
        }

        [MenuItem("Assets/Carve/Convert to Carve", true)]
        static bool ConvertSelectedValid() => SelectedPlans(Plan).Count > 0;

        [MenuItem("Assets/Carve/Convert back to Lit", false, 2001)]
        static void BackSelected()
        {
            var plans = SelectedPlans(PlanBack);
            if (plans.Count == 0) return;
            ConvertBack(plans);
            Debug.Log($"[Carve] Converted {plans.Count} material(s) back to Lit.");
        }

        [MenuItem("Assets/Carve/Convert back to Lit", true)]
        static bool BackSelectedValid() => SelectedPlans(PlanBack).Count > 0;

        static List<CarveConversion> SelectedPlans(Func<Material, CarveConversion> plan)
        {
            var list = new List<CarveConversion>();
            foreach (var o in Selection.objects)
                if (o is Material m && plan(m) is CarveConversion c) list.Add(c);
            return list;
        }
    }
}

