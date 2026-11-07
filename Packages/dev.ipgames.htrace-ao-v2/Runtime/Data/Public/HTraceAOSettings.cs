using IPGames.HTraceAO.Runtime.RuntimeResources;

namespace IPGames.HTraceAO.Runtime.Data.Public
{
	/// <summary>
	/// Change global HTrace AO settings, only for UseVolumes is disabled in HTrace AO Renderer Feature
	/// </summary>
	public class HTraceAOSettings
	{
		private static Profile.HTraceAOProfile _cachedProfile;
		private static HTraceAOResources _cachedResources;
    
		public static Profile.HTraceAOProfile ActiveProfile
		{
			get
			{
				return _cachedProfile;
			}
		}

		internal static HTraceAOResources ActiveResources => _cachedResources;

		/// <summary>
		/// Sets the profile used by HTrace AO.
		/// </summary>
		/// <param name="profile">The profile to make active.</param>
		public static void SetActiveProfile(Profile.HTraceAOProfile profile)
		{
			_cachedProfile = profile;
		}

		internal static void SetResources(HTraceAOResources resources)
		{
			_cachedResources = resources;
		}
	}
}
