using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Tools > Wash Square > Build Tutorial: Scenes/Tutorial made fresh from the Mechanics Gym (run Update Mechanics Gym
// first), with the Tutorial walking through every mechanic on its subway sign, the welcome sign retitled, and its own
// minimap. Run again after the gym changes.
static class TutorialBuild
{
    internal const string ScenePath = "Assets/Scenes/Tutorial.unity";
    const string GymPath = "Assets/Scenes/MechanicsGym.unity";

    [MenuItem("Tools/Wash Square/Build Tutorial")]
    static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(GymPath, ScenePath)) { Debug.LogError("Build Tutorial: couldn't copy " + GymPath); return; }
        var scene = EditorSceneManager.OpenScene(ScenePath);

        // the welcome sign: the Tutorial blade and words
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!t || t.name != "Sign MECHANICS GYM") continue; // (the old blade, destroyed below, may still be in the list)
            t.name = "Sign TUTORIAL";
            Transform blade = t.Find("Blade");
            if (blade)
            {
                StreetFurniture.Sign("gym_tutorial", t, blade.position, blade.rotation, 0.34f, twoSided: true).name = "Blade";
                Object.DestroyImmediate(blade.gameObject);
            }
            foreach (Text words in t.GetComponentsInChildren<Text>(true))
                words.text = "Welcome to Wash Square. Follow the sign at the top of the screen: it takes you through everything, station by station. Do what it says and it moves on; Tab skips a step. Esc: menu.";
            break;
        }

        GymMenu menu = Object.FindFirstObjectByType<GymMenu>();
        if (menu) menu.title = "Tutorial";
        new GameObject("Tutorial").AddComponent<Tutorial>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorApplication.delayCall += MinimapBake.Bake; // Textures/Minimap_Tutorial.png; saves the scene
        Debug.Log("Build Tutorial: " + ScenePath + " made from the gym");
    }
}
