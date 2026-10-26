using UnityEditor;
using UnityEngine;

// Makes Resources/HuntTasks (the Seagull's task pool, filled with HuntTasks.Defaults) if it isn't there, and
// Tools > Wash Square > Hunt Tasks selects it for editing.
static class HuntTasksAsset
{
    const string Path = "Assets/Resources/HuntTasks.asset";

    [InitializeOnLoadMethod]
    static void Ensure() => EditorApplication.delayCall += () => Load();

    [MenuItem("Tools/Wash Square/Hunt Tasks")]
    static void Select() => Selection.activeObject = Load();

    static HuntTasks Load()
    {
        var pool = AssetDatabase.LoadAssetAtPath<HuntTasks>(Path);
        if (pool) return pool;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        pool = ScriptableObject.CreateInstance<HuntTasks>();
        pool.tasks = HuntTasks.Defaults();
        AssetDatabase.CreateAsset(pool, Path);
        AssetDatabase.SaveAssets();
        return pool;
    }
}
