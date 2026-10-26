using System;
using UnityEditor;
using UnityEngine;

// Inspector for Shitshow/Parallax Lit: URP's own Lit inspector (internal, so made by reflection), then the parallax
// occlusion black/white points. Depth is Lit's own Surface Inputs > Height Map > Scale.
public class ParallaxLitGUI : ShaderGUI
{
    static readonly Type LitGUI = Type.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader, Unity.RenderPipelines.Universal.Editor");
    ShaderGUI lit;
    ShaderGUI Lit => lit ??= (ShaderGUI)Activator.CreateInstance(LitGUI);

    public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
    {
        Lit.OnGUI(editor, properties);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Parallax Occlusion", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Depth without geometry, the way games do gravel, cobbles and brick: set Surface Inputs > Height Map "
            + "(Scale = depth in texture repeats). Black/White Point stretch a low-contrast height map to the full depth.", MessageType.None);
        foreach (string name in new[] { "_ParallaxMin", "_ParallaxMax" })
        {
            MaterialProperty property = FindProperty(name, properties);
            editor.ShaderProperty(property, property.displayName);
        }
    }

    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader) => Lit.AssignNewShaderToMaterial(material, oldShader, newShader);
    public override void ValidateMaterial(Material material) => Lit.ValidateMaterial(material);
    public override void OnClosed(Material material) => Lit.OnClosed(material);
}
