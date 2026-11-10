using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Tools > Wash Square > Straighten NPC Knees: the NPCs' leg muscle curves capped at straight. Mixamo's Standard Walk
// drives the stance knee's "Lower Leg Stretch" muscle to 1.46 (1 is fully straight): Y Bot's rig takes it, but on the
// PSX characters it comes out as the knee folding about 33° backwards and sideways at mid-stride, a calf that snaps
// out to the side. Each clip the NPC controller uses that overstretches gets a copy in Animations/NpcClips with those
// curves capped at 1, and the controller uses the copy. Run again after reimporting the clips.
static class NpcKnees
{
    const string ControllerPath = "Assets/Animations/Npc.controller", Folder = "Assets/Animations/NpcClips";

    [MenuItem("Tools/Wash Square/Straighten NPC Knees")]
    static void Run()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Animations", "NpcClips");
        int fixedClips = 0;
        foreach (AnimatorControllerLayer layer in controller.layers)
            foreach (ChildAnimatorState child in layer.stateMachine.states)
            {
                AnimatorState state = child.state;
                if (state.motion is BlendTree tree)
                {
                    ChildMotion[] motions = tree.children;
                    for (int i = 0; i < motions.Length; i++)
                        if (motions[i].motion is AnimationClip clip && Straightened(clip) is AnimationClip fix) { motions[i].motion = fix; fixedClips++; }
                    tree.children = motions;
                    EditorUtility.SetDirty(tree);
                }
                else if (state.motion is AnimationClip clip && Straightened(clip) is AnimationClip fix) { state.motion = fix; fixedClips++; }
            }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("Straighten NPC Knees: " + fixedClips + " clip slots now use knee-capped copies in " + Folder);
    }

    // a copy of the clip with every leg stretch muscle capped at straight, or null if it never goes past straight
    static AnimationClip Straightened(AnimationClip source)
    {
        if (AssetDatabase.GetAssetPath(source).StartsWith(Folder)) source = Original(source) ?? source; // already a copy: rebuild from the import
        var legs = AnimationUtility.GetCurveBindings(source).Where(b => b.propertyName.EndsWith("Lower Leg Stretch")).ToArray();
        if (!legs.Any(b => AnimationUtility.GetEditorCurve(source, b).keys.Any(k => k.value > 1f))) return null;

        string path = Folder + "/" + source.name + ".anim";
        var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (!copy) { copy = new AnimationClip(); AssetDatabase.CreateAsset(copy, path); }
        copy.ClearCurves();
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
            if (legs.Contains(binding))
            {
                Keyframe[] keys = curve.keys;
                for (int k = 0; k < keys.Length; k++)
                    if (keys[k].value > 1f) { keys[k].value = 1f; keys[k].inTangent = keys[k].outTangent = 0; }
                curve.keys = keys;
            }
            AnimationUtility.SetEditorCurve(copy, binding, curve);
        }
        AnimationUtility.SetAnimationClipSettings(copy, AnimationUtility.GetAnimationClipSettings(source));
        copy.frameRate = source.frameRate;
        EditorUtility.SetDirty(copy);
        // remember where it came from, so a rerun starts from the imported clip, not from this copy
        AssetImporter.GetAtPath(path).userData = AssetDatabase.GetAssetPath(source) + "|" + source.name;
        return copy;
    }

    static AnimationClip Original(AnimationClip copy)
    {
        string[] from = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(copy)).userData.Split('|');
        if (from.Length != 2) return null;
        return AssetDatabase.LoadAllAssetsAtPath(from[0]).OfType<AnimationClip>().FirstOrDefault(c => c.name == from[1]);
    }
}
