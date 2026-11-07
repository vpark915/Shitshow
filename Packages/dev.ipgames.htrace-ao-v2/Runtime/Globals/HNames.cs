namespace IPGames.HTraceAO.Runtime.Globals
{
	public static class HNames
	{
		public const string ASSET_NAME                                = "HTraceAO";
		public const string ASSET_NAME_FULL_BASE                      = "HTrace Ambient Occlusion V2";
		public const string ASSET_NAME_FULL                           = "HTrace Ambient Occlusion V2 URP";
		public const string ASSET_NAME_FULL_WITH_DOTS                 = "HTrace: Ambient Occlusion V2";
		public const string HTRACE_AO_DOCUMENTATION_LINK              = "https://docs.ipgames.dev/ambient-occlusion/";
		public const string HTRACE_AO_DOCUMENTATION_LINK_KNOWN_ISSUES = "https://docs.ipgames.dev/ambient-occlusion/known-issues";
		public const string HTRACE_AO_DOCUMENTATION_LINK_SETUP        = "https://docs.ipgames.dev/ambient-occlusion/setup-and-best-practices";
		public const string HTRACE_DISCORD_LINK                       = "https://discord.com/invite/Nep56Efu7A";
		public const string HTRACE_DISCORD_BUGS_AO_LINK               = "https://discord.gg/Qajurv4ucJ";
		public const string HTRACE_AO_VERSION                         = "2.2.2";
		
		//Prefs
		public const string HTRACE_SHOW_KEY             = "HTraceAO_ShowWelcomeWindow";
		public const string HTRACE_WELCOME_SHOW_SESSION = "HTraceAO_ShowWelcomeWindowSessions";
		public const string HTRACE_UNITY_VERSION_KEY    = "HTraceAO_UnityVersion";

		// ---------------- Profiling ----------------
		public const string HTRACE_PRE_PASS_NAME                   = "HTraceAO Pre Pass";
		public const string HTRACE_MV_PASS_NAME                    = "HTraceAO Motion Vectors Pass";
		public const string HTRACE_OBJECTS_MOTION_MASK_PASS_NAME   = "HTraceAO Objects Motion Mask Pass";
		public const string HTRACE_OBJECTS_MOTION_DELTA_PASS_NAME  = "HTraceAO Objects Motion Delta Pass";
		public const string HTRACE_CAMERA_MOTION_VECTORS_PASS_NAME = "HTraceAO Camera Motion Vectors Pass";
		public const string HTRACE_GBUFFER_PASS_NAME               = "HTraceAO GBuffer Pass";
		public const string HTRACE_SSAO_PASS_NAME                  = "HTraceAO SSAO Pass";
		public const string HTRACE_GTAO_PASS_NAME                  = "HTraceAO GTAO Pass";
		public const string HTRACE_RTAO_PASS_NAME                  = "HTraceAO RTAO Pass";
		public const string HTRACE_FINAL_PASS_NAME                 = "HTraceAO Final Pass";

		// public const string COPY_MOVING_STENCIL_SAMPLER = "Copy Moving Stencil";
		// public const string COMPOSE_MOTION_VECTORS_SAMPLER = "Compose Motion Vectors";

		public const string RENDER_OCCLUSION_SAMPLER            = "Render Occlusion";
		public const string INTERPOLATION_SAMPLER             = "Interpolation";
		public const string DISOCCLUSION_BOOST_SAMPLER        = "Disocclusion Boost";
		public const string DEPTH_PYRAMID_GENERATION_SAMPLER  = "Depth Pyramid Generation";
		public const string RAY_MARCHING_DEPTH_GENERATION_SAMPLER = "Ray Marching Depth Generation";
		public const string DEBUG_SAMPLER                     = "Debug";
		public const string THREAD_COMPACTION_SAMPLER         = "Thread Compaction";
		public const string OCCLUSION_TRACING_SAMPLER         = "Occlusion Tracing";
		public const string TEMPORAL_FILTERING_SAMPLER        = "Temporal Filtering";
		public const string SPATIAL_FILTERING_SAMPLER         = "Spatial Filtering";
		public const string CONTACT_OCCLUSION_SAMPLER         = "Contact Occlusion Rendering";
		public const string RTAS_BUILD_SAMPLER                = "RTAS Build";
		public const string BUFFER_CLEANUP_SAMPLER            = "Buffer Cleanup";
		public const string OUTPUT_COMPOSITION_SAMPLER        = "Output Composition";

		public const string KEYWORD_SWITCHER = "HTRACE_OVERRIDE_AO";
		public const string INT_SWITCHER = "_HTRACE_INT_OVERRIDE";

	}
}
