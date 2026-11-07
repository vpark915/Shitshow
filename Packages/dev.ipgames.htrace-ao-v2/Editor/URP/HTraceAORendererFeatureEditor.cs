#if UNITY_EDITOR
using IPGames.HTraceAO.Runtime.URP.Infrastructure;
using UnityEditor;

namespace HTraceAO.Editor.URP
{
	[CustomEditor(typeof(HTraceAORendererFeature), true)]
	public class HTraceAORendererFeatureEditor : UnityEditor.Editor
	{
		private SerializedProperty useVolumes;

		private void OnEnable()
		{
			useVolumes = serializedObject.FindProperty("UseVolumes");
		}
		
		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			
			EditorGUILayout.PropertyField(useVolumes);

			serializedObject.ApplyModifiedProperties();
		}
	}
}
#endif
