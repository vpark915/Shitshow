using IPGames.HTraceAO.Runtime.URP;

using IPGames.HTraceAO.Runtime.Globals;
using UnityEditor;
using UnityEngine;

namespace IPGames.HTraceAO.Editor.WindowsAndMenu
{

#if UNITY_EDITOR
	
	public class HMenuAndFilesManager : EditorWindow
	{
		[MenuItem("GameObject/Rendering/HTrace Ambient Occlusion", false, priority: 30)]
		static void CreateHTraceGameObject(MenuCommand menuCommand)
		{
#if UNITY_6000_4_OR_NEWER
			IPGames.HTraceAO.Runtime.URP.HTraceAO[] hTraces = Object.FindObjectsByType<IPGames.HTraceAO.Runtime.URP.HTraceAO>();
#elif UNITY_6000_0_OR_NEWER
			IPGames.HTraceAO.Runtime.URP.HTraceAO[] hTraces = Object.FindObjectsByType<IPGames.HTraceAO.Runtime.URP.HTraceAO>(FindObjectsSortMode.None);
#else
			IPGames.HTraceAO.Runtime.URP.HTraceAO[] hTraces = FindObjectsOfType(typeof(IPGames.HTraceAO.Runtime.URP.HTraceAO)) as IPGames.HTraceAO.Runtime.URP.HTraceAO[];
#endif
			
#if !NONE
			if (hTraces != null && hTraces.Length > 0)
			{
				return;
			}
#endif
			GameObject go = new GameObject(HNames.ASSET_NAME);
			GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
			
			go.AddComponent<IPGames.HTraceAO.Runtime.URP.HTraceAO>();

			Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
			Selection.activeObject = go;
		}

		[MenuItem("Window/HTrace/Add HTrace AO Render Feature to active RendererData", false, priority: 32)]
		private static void AddRenderFeature()
		{
			HRendererURP.AddHTraceRendererFeatureToUniversalRendererData();
		}

		[MenuItem("Window/HTrace/Open HTrace AO documentation", false, priority: 32)]
		private static void OpenDocumentation()
		{
			Application.OpenURL(HNames.HTRACE_AO_DOCUMENTATION_LINK);
		}
	}
	
#endif
}
