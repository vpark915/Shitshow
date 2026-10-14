using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

// Everything the park NPCs need that is made in the editor: animation and character import settings, the animator
// controller, the NavMesh, and the NPCs object in the scene. Both commands are safe to run again.
static class NpcSetup
{
    const string ClipFolder = "Assets/Animations/Mixamo";
    const string CharacterFolder = "Assets/Blender/PSXCHARACTERS/Characters_psx_01/Models/Rig";
    const string ControllerPath = "Assets/Animations/Npc.controller";
    const string RunClipPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/Locomotion--Run_N.anim.fbx";
    const string NavMeshPath = "Assets/Scenes/WashSquareGym/NpcNavMesh.asset";

    // NavMesh area slots; their costs can be tuned afterwards in Window > AI > Navigation > Areas
    const int Street = 3, Lawn = 4;

    [MenuItem("Tools/Wash Square/Set Up NPCs")]
    public static string Setup()
    {
        // Mixamo downloads: retargetable, looping, and named after the file instead of "mixamo.com"
        foreach (string path in Models(ClipFolder))
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            string clipName = System.IO.Path.GetFileNameWithoutExtension(path).Split('@').Last();
            if (importer.animationType == ModelImporterAnimationType.Human && importer.clipAnimations.Any(c => c.name == clipName)) continue;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = clipName;
                clip.loopTime = true;
                clip.lockRootRotation = clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = clip.keepOriginalPositionY = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        // the characters come rigged with Mixamo's skeleton but imported as Generic, which can't share clips
        string[] characters = Models(CharacterFolder + "/Male").Concat(Models(CharacterFolder + "/Female")).ToArray();
        foreach (string path in characters)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer.animationType == ModelImporterAnimationType.Human) continue;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.SaveAndReimport();
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller)
        {
            AnimationClip walk = Clip(ClipFolder + "/Y Bot@Standard Walk.fbx"), run = Clip(RunClipPath);
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Hangout", AnimatorControllerParameterType.Bool);
            // thresholds are the clips' own ground speeds (measured from the stride; the clips are in-place), so feet
            // don't slide: retune them in the blend tree if the clips are swapped
            AnimatorState move = controller.CreateBlendTreeInController("Move", out BlendTree tree);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip(ClipFolder + "/Y Bot@Idle.fbx"), 0);
            tree.AddChild(walk, 1.4f);
            tree.AddChild(run, 5.3f);
            AnimatorState hangout = controller.layers[0].stateMachine.AddState("Hangout");
            hangout.motion = Clip(ClipFolder + "/Y Bot@Happy Idle.fbx");
            foreach (bool on in new[] { true, false })
            {
                AnimatorStateTransition transition = on ? move.AddTransition(hangout) : hangout.AddTransition(move);
                transition.AddCondition(on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Hangout");
                transition.hasExitTime = false;
                transition.duration = 0.3f;
            }
        }

        string baked = Bake();

        // only fills in what is empty, so hand-tuned lists and counts survive a re-run
        GameObject npcs = GameObject.Find("NPCs");
        if (!npcs) npcs = new GameObject("NPCs") { transform = { position = new Vector3(0, 0.2f, -20) } }; // on the plaza south of the fountain
        if (!npcs.TryGetComponent(out NpcSpawner spawner)) spawner = npcs.AddComponent<NpcSpawner>();
        Transform blockout = GameObject.Find("washingtonSquareBlockout").transform;
        GameObject[] models = characters.Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
        string[] uniforms = { "Police", "Sheriff", "Firefighter", "Doctor", "HM" };
        if (!spawner.controller) spawner.controller = controller;
        if (spawner.civilians.Length == 0) spawner.civilians = models.Where(m => !uniforms.Any(m.name.Contains)).ToArray();
        if (spawner.police.Length == 0) spawner.police = models.Where(m => m.name.Contains("Police")).ToArray();
        if (spawner.hangouts.Length == 0)
            spawner.hangouts = blockout.Cast<Transform>().Where(t => new[] { "WSP_Fountain", "WSP_Arch", "WSP_Prop_ChessTable", "WSP_Prop_GaribaldiStatue", "WSP_Prop_Stage", "WSP_Prop_PlayStructure_Large" }.Any(t.name.StartsWith)).ToArray();
        if (spawner.jogRoute.Length == 0)
        {
            // a lap of the pavement that runs round the outside of the park
            Bounds park = blockout.Find("WSP_Park").GetComponent<Renderer>().bounds;
            park.Expand(2 * JogMargin);
            var route = new GameObject("JogRoute").transform;
            route.SetParent(npcs.transform, false);
            foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) })
            {
                var point = new GameObject("Corner").transform;
                point.SetParent(route, false);
                point.position = park.center + new Vector3(corner.x * park.extents.x, 0, corner.y * park.extents.z);
            }
            spawner.jogRoute = route.Cast<Transform>().ToArray();
        }
        EditorUtility.SetDirty(spawner);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(npcs.scene);
        return characters.Length + " characters, " + spawner.civilians.Length + " civilians, " + spawner.police.Length + " police, " + spawner.hangouts.Length + " hangouts; " + baked;
    }

    const float JogMargin = 1.5f; // metres out from the edge of the park slab

    // Run again after the park's layout, props or trees change.
    [MenuItem("Tools/Wash Square/Bake NPC NavMesh")]
    public static string Bake()
    {
        // ponytail: one flat cost for the whole roadway, so people cross anywhere; add crosswalk strips of a cheaper area when cars exist
        SetArea(Street, "Street", 6);
        SetArea(Lawn, "Lawn", 3);

        Transform blockout = GameObject.Find("washingtonSquareBlockout").transform;
        Terrain terrain = Terrain.activeTerrain;
        // ground level only: building roofs would just add unreachable islands
        Bounds bounds = blockout.Find("WSP_Streets").GetComponent<Renderer>().bounds;
        bounds.SetMinMax(new Vector3(bounds.min.x, -1, bounds.min.z), new Vector3(bounds.max.x, 4, bounds.max.z));

        var markups = new List<NavMeshBuildMarkup> { new NavMeshBuildMarkup { root = terrain.transform, overrideArea = true, area = Lawn } };
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
        // the park itself only, not the player, seagulls or anything else that moves
        sources.RemoveAll(s => s.component && s.component.transform.root != blockout && s.component.transform.root != terrain.transform);

        // trees and shrubs don't come along with the terrain collider: block a trunk-sized column at each
        TerrainData land = terrain.terrainData;
        foreach (TreeInstance tree in land.treeInstances)
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box, size = new Vector3(0.6f, 4, 0.6f), area = 1, // Not Walkable
                transform = Matrix4x4.Translate(terrain.transform.position + Vector3.Scale(tree.position, land.size)),
            });

        // the road shares a mesh with the pavements, and the lawns with the gravel: give each material its own area
        var pieces = new List<Mesh>();
        foreach (string name in new[] { "WSP_Streets", "WSP_ParkSurfaces" })
        {
            Transform part = blockout.Find(name);
            Mesh mesh = part.GetComponent<MeshFilter>().sharedMesh;
            Material[] materials = part.GetComponent<Renderer>().sharedMaterials;
            int at = sources.FindIndex(s => s.sourceObject == mesh);
            NavMeshBuildSource source = sources[at];
            sources.RemoveAt(at);
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                string material = materials[i].name.ToLower();
                if (material.Contains("grass") || material.Contains("planting")) continue; // the terrain lies on top of these
                pieces.Add(new Mesh { indexFormat = mesh.indexFormat, vertices = mesh.vertices, triangles = mesh.GetTriangles(i) });
                source.sourceObject = pieces.Last();
                source.area = material.Contains("asphalt") ? Street : 0;
                sources.Add(source);
            }
        }

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = NpcSpawner.Radius;
        settings.agentHeight = NpcSpawner.Height;
        settings.agentClimb = 0.4f;
        settings.minRegionArea = 4; // no scraps on bench seats and table tops
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        pieces.ForEach(Object.DestroyImmediate);
        // the surface component only holds and shows the mesh; its own Bake button doesn't know about roads and lawns.
        // It gets its own object, pinned to the origin, because the mesh moves with whatever object holds it.
        GameObject holder = GameObject.Find("NPC NavMesh");
        if (!holder) holder = new GameObject("NPC NavMesh");
        holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        if (!holder.TryGetComponent(out NavMeshSurface surface)) surface = holder.AddComponent<NavMeshSurface>();
        // unload the old mesh before its asset is replaced: overwriting one that is still loaded crashes the editor
        surface.RemoveData();
        surface.navMeshData = null;
        AssetDatabase.CreateAsset(data, NavMeshPath);
        surface.navMeshData = data;
        surface.AddData();
        EditorUtility.SetDirty(surface);

        return "NavMesh: " + sources.Count + " sources, " + NavMesh.CalculateTriangulation().indices.Length / 3 + " triangles";
    }

    static void SetArea(int index, string name, float cost)
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset")[0]);
        SerializedProperty area = settings.FindProperty("areas").GetArrayElementAtIndex(index);
        if (area.FindPropertyRelative("name").stringValue == name) return; // keep a cost the user has tuned
        area.FindPropertyRelative("name").stringValue = name;
        area.FindPropertyRelative("cost").floatValue = cost;
        settings.ApplyModifiedProperties();
    }

    static IEnumerable<string> Models(string folder) =>
        AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p);

    static AnimationClip Clip(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
}
