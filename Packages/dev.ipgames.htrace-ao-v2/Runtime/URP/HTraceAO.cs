using IPGames.HTraceAO.Runtime.Data.Profile;
using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEngine;
using UnityEngine.Serialization;

namespace IPGames.HTraceAO.Runtime.URP
{
	[HelpURL(HNames.HTRACE_AO_DOCUMENTATION_LINK)]
	[ExecuteAlways, ExecuteInEditMode, ImageEffectAllowedInSceneView, DefaultExecutionOrder(100)]
	public class HTraceAO : MonoBehaviour
	{
		[Tooltip("Currently used HTrace AO profile with settings")]
		[SerializeField, FormerlySerializedAs("Profile")]
		private HTraceAOProfile _profile;

		private void OnEnable()
		{
			CheckProfile();
			HTraceAOSettings.SetActiveProfile(_profile);
		}
		
		private void OnDisable()
		{
			if (HTraceAOSettings.ActiveProfile == _profile)
				HTraceAOSettings.SetActiveProfile(null);
		}

#if UNITY_EDITOR
		void OnValidate() {
			CheckProfile();
		}

		private void Reset() {
			CheckProfile();
		}
#endif

		void CheckProfile()
		{
			if (_profile == null)
			{
				_profile      = ScriptableObject.CreateInstance<HTraceAOProfile>();
				_profile.name = "New HTrace AO Profile";
#if UNITY_EDITOR
				UnityEditor.EditorUtility.SetDirty(this);
#endif
			}

			if (isActiveAndEnabled)
				HTraceAOSettings.SetActiveProfile(_profile);
		}
	}
}
