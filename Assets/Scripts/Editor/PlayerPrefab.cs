using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Makes Prefabs/Player.prefab from the pigeon in WashSquareGym and links the pigeon in MechanicsGym to it. Links to
// scene objects (HUD text, minimap, camera) stay on each scene's copy as overrides; whatever the gym sets differently
// (respawns and so on) stays a gym override too. Safe to run again: it only links pigeons that aren't linked yet.
static class PlayerPrefab
{
    const string PrefabPath = "Assets/Prefabs/Player.prefab";
    static readonly string[] Scenes = { "Assets/Scenes/WashSquareGym.unity", "Assets/Scenes/MechanicsGym.unity" };

    [MenuItem("Tools/Wash Square/Make Player Prefab")]
    static void Make()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string back = EditorSceneManager.GetActiveScene().path;
        foreach (string path in Scenes)
        {
            EditorSceneManager.OpenScene(path);
            FoodPowers pigeon = Object.FindFirstObjectByType<FoodPowers>();
            if (!pigeon) { Debug.LogWarning("No pigeon (FoodPowers) in " + path); continue; }
            GameObject player = pigeon.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(player) && PrefabUtility.GetOutermostPrefabInstanceRoot(player) == player) continue;

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!asset) PrefabUtility.SaveAsPrefabAssetAndConnect(player, PrefabPath, InteractionMode.AutomatedAction);
            else PrefabUtility.ConvertToPrefabInstance(player, asset, new ConvertToPrefabInstanceSettings(), InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(player.scene);
            Debug.Log("Pigeon in " + path + " is now a " + PrefabPath + " instance", player);
        }
        if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back);
    }
}
