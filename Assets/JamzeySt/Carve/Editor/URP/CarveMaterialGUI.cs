using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace JamzeySt.Carve.Editor.URP
{
    // URP inspector for Carve Lit and Carve Layered Lit: Carve's sections, then render face, queue and instancing.
    public class CarveMaterialGUI : ShaderGUI
    {
        static readonly GUIContent s_Cull = new GUIContent("Render Face", "Front: the usual. Both: thin surfaces seen from either side.");
        static readonly string[] s_CullOptions = { "Front", "Back", "Both" };
        static readonly int[] s_CullValues = { (int)CullMode.Back, (int)CullMode.Front, (int)CullMode.Off };

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] props)
        {
            CarveMaterialDrawer.Draw(editor, props);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
            var cull = FindProperty("_Cull", props, false);
            if (cull != null)
            {
                EditorGUI.showMixedValue = cull.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                int v = EditorGUILayout.IntPopup(s_Cull, (int)cull.floatValue, new[] { new GUIContent(s_CullOptions[0]), new GUIContent(s_CullOptions[1]), new GUIContent(s_CullOptions[2]) }, s_CullValues);
                if (EditorGUI.EndChangeCheck()) cull.floatValue = v;
                EditorGUI.showMixedValue = false;
            }
            editor.RenderQueueField();
            editor.EnableInstancingField();
        }

        public override void ValidateMaterial(Material material) => CarveLayers.SyncKeyword(material);

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            CarveLayers.SyncKeyword(material);
        }
    }
}
