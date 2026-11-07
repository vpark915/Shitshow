using IPGames.HTraceAO.Runtime.Data.Public;
using UnityEngine;

namespace IPGames.HTraceAO.Runtime.Data.Profile
{
	[CreateAssetMenu(fileName = "HTraceAO Profile", menuName = "HTrace/AO Profile", order = 251)]
	public class HTraceAOProfile : ScriptableObject
	{
		[SerializeField] public Public.GeneralSettings GeneralSettings = new Public.GeneralSettings();
		[SerializeField] public Public.SSAOSettings    SSAOSettings    = new Public.SSAOSettings();
		[SerializeField] public Public.GTAOSettings    GTAOSettings    = new Public.GTAOSettings();
		[SerializeField] public Public.RTAOSettings    RTAOSettings    = new Public.RTAOSettings();
		[SerializeField] public DebugSettings   DebugSettings   = new DebugSettings();

#if UNITY_EDITOR
		/// <summary>
		/// Copies all settings from another profile to this profile.
		/// </summary>
		/// <param name="sourceProfile">The profile to copy settings from</param>
		public void CopySettingsFrom(HTraceAOProfile sourceProfile)
		{
			if (sourceProfile == null)
				return;

			string json;

			json = UnityEngine.JsonUtility.ToJson(sourceProfile.GeneralSettings);
			UnityEngine.JsonUtility.FromJsonOverwrite(json, GeneralSettings);

			json = UnityEngine.JsonUtility.ToJson(sourceProfile.SSAOSettings);
			UnityEngine.JsonUtility.FromJsonOverwrite(json, SSAOSettings);

			json = UnityEngine.JsonUtility.ToJson(sourceProfile.GTAOSettings);
			UnityEngine.JsonUtility.FromJsonOverwrite(json, GTAOSettings);

			json = UnityEngine.JsonUtility.ToJson(sourceProfile.RTAOSettings);
			UnityEngine.JsonUtility.FromJsonOverwrite(json, RTAOSettings);

			json = UnityEngine.JsonUtility.ToJson(sourceProfile.DebugSettings);
			UnityEngine.JsonUtility.FromJsonOverwrite(json, DebugSettings);
		}
#endif
	}
}
