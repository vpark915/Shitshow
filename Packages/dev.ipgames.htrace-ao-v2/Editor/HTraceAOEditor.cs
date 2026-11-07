#if UNITY_EDITOR
using System.IO;
using IPGames.HTraceAO.Runtime.Data.Profile;
using UnityEditor;
using UnityEngine;
using IPGames.HTraceAO.Runtime.URP.Infrastructure;

namespace IPGames.HTraceAO.Editor
{
	[CustomEditor(typeof(IPGames.HTraceAO.Runtime.URP.HTraceAO))]
	internal class HTraceAOEditor : UnityEditor.Editor
	{
		SerializedProperty _profile;
		HTraceAOProfile  _cachedProfile;
		UnityEditor.Editor _cachedProfileEditor;

		private void OnEnable()
		{
			if (target == null)
				return;
			_profile = serializedObject.FindProperty("_profile");
		}

		public override void OnInspectorGUI()
		{
			if (HTraceAORendererFeature.IsUseVolumes == true)
			{
				EditorGUILayout.HelpBox("\"Use Volumes\" checkbox in the HTrace AO Renderer feature is enabled, use the HTraceAO volume override in your scenes.", MessageType.Warning, wide: true);
				return;
			}
			EditorGUILayout.PropertyField(_profile);
			
			EditorGUILayout.Space(5);

			if (_profile.objectReferenceValue != null) 
			{
				if (_cachedProfile != _profile.objectReferenceValue) 
				{
					_cachedProfile = null;
				}
				if (_cachedProfile == null) 
				{
					_cachedProfile       = (HTraceAOProfile)_profile.objectReferenceValue;
					_cachedProfileEditor = CreateEditor(_profile.objectReferenceValue);
				}

				EditorGUILayout.BeginVertical();
				_cachedProfileEditor.OnInspectorGUI();

				EditorGUILayout.Separator();

				if (GUILayout.Button("Save As New Profile")) 
				{
					ExportProfile();
				}
				EditorGUILayout.EndVertical();
			}
			else 
			{
				EditorGUILayout.HelpBox("Create or assign a profile.", MessageType.Info);
				if (GUILayout.Button("New Profile")) 
				{
					CreateProfile();
				}
			}

			serializedObject.ApplyModifiedProperties();
		}
		
		void CreateProfile() {

			var fp = CreateInstance<HTraceAOProfile>();
			fp.name = "New HTrace AO Profile";

			string path = "Assets";
			foreach (Object obj in Selection.GetFiltered(typeof(Object), SelectionMode.Assets)) {
				path = AssetDatabase.GetAssetPath(obj);
				if (File.Exists(path)) {
					path = Path.GetDirectoryName(path);
				}
				break;
			}

			string fullPath = path + "/" + fp.name + ".asset";
			fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

			AssetDatabase.CreateAsset(fp, fullPath);
			AssetDatabase.SaveAssets();
			_profile.objectReferenceValue = fp;
			EditorGUIUtility.PingObject(fp);
		}

		void ExportProfile() {
			var fp         = (HTraceAOProfile)_profile.objectReferenceValue;
			var newProfile = Instantiate(fp);

			string path     = AssetDatabase.GetAssetPath(fp);
			string fullPath = path;
			if (string.IsNullOrEmpty(path)) {
				path = "Assets";
				foreach (Object obj in Selection.GetFiltered(typeof(Object), SelectionMode.Assets)) {
					path = AssetDatabase.GetAssetPath(obj);
					if (File.Exists(path)) {
						path = Path.GetDirectoryName(path);
					}
					break;
				}
				fullPath = path + "/" + fp.name + ".asset";
			}
			fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);
			AssetDatabase.CreateAsset(newProfile, fullPath);
			AssetDatabase.SaveAssets();
			_profile.objectReferenceValue = newProfile;
			EditorGUIUtility.PingObject(fp);
		}
	}
}
#endif
