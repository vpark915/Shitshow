#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace IPGames.HTraceAO.Editor
{
	internal static class HEditorStyles
	{
		public static float defaultLineSpace = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
		public static float additionalLineSpace = 10f;
		public static float helpBoxHeight = EditorGUIUtility.singleLineHeight * 2;
		public static float checkBoxOffsetWidth = 15f;
		public static float checkBoxWidth = 15f;
		public static float tabOffset = 8f;

		// Buttons name
		public const string FixButtonName    = "Fix";
		public const string ChangeButtonName = "Change";
		public const string OpenButtonName   = "Open";

		// Infos, Warnings, Errors
		public const string Info_ResolutionForcedToFull = "Resolution is forced to Full when denoising is disabled.";

		// General Tab
		public static GUIContent InjectionPoint          = new GUIContent("Injection Point", "Determines where AO renders in the URP pipeline.");
		public static GUIContent ReconstructNormals      = new GUIContent("Reconstruct Normals", "Reconstructs normals from the depth buffer. Normals reconstructed this way are flat and represent raw polygonal geometry, without accounting for smoothing groups or normal map details.");
		public static GUIContent GlobalSettingsContent   = new GUIContent("Global Settings");
		public static GUIContent AOMode                  = new GUIContent("Mode", "Selects the ambient occlusion algorithm.");
		public static GUIContent DebugModeContent        = new GUIContent("Debug Mode", "Selects the debug visualization. Depth shows the depth buffer. Main Buffers displays pipeline buffers at different stages. Ambient Occlusion shows the final AO output. Temporal Disocclusion shows temporal accumulation state. Shading Rate Visualization and Hardware Ray Tracing Scene are RTAO only.");
		public static GUIContent HBuffer                 = new GUIContent("Buffer", "Visualizes the debug mode for different buffers.");
		public static GUIContent Intensity               = new GUIContent("Intensity", "Guides the final intensity of the ambient occlusion, higher values result in darker ambient occlusion.");
		public static GUIContent DirectLightingOcclusion = new GUIContent("Direct Lighting Occlusion", "Defines how visible the effect is in areas exposed to direct lighting. Use 0 for maximum physical accuracy.");
		public static GUIContent ExcludedIntensity       = new GUIContent("           Intensity", "Controls the AO intensity on excluded receiving surfaces. 0 = no AO, 1 = full AO.");
		public static GUIContent EarlyOutExcludedReceivers = new GUIContent("           Early Out", "Skips AO tracing and filtering for excluded receivers. Intended for spatially isolated objects");
		public static GUIContent Fadeout                 = new GUIContent("Fadeout", "Defines the distance in meters at which the occlusion effect fades out. Setting to 0 disables fadeout entirely.");

		// Shared Denoising
		public static GUIContent OutputDithering        = new GUIContent("Output Dithering", "Applies a dithering pattern to the final AO buffer to reduce banding artifacts caused by the 8-bit buffer precision.");
		public static GUIContent OutputDitheringAnimate = new GUIContent("         Animate", "Animates the dithering pattern over time for improved temporal integration.");

		// SSAO Tab
		public static GUIContent SSAO_Thickness = new GUIContent("Thickness", "Control the thickness of the surfaces on screen.");
		public static GUIContent SSAO_Radius    = new GUIContent("Radius", "Defines the maximum distance in meters for occluder search.");

		// GTAO Tab
		public static GUIContent GTAO_Thickness                  = new GUIContent("Thickness Value", "Controls the virtual thickness of objects. Higher values strengthen occlusion cast by all objects.");
		public static GUIContent GTAO_WorldSpaceRadius           = new GUIContent("World Space Radius", "Defines the maximum distance in meters for occluder search.");
		public static GUIContent GTAO_ScreenSpaceRadius          = new GUIContent("Screen Space Radius", "Maximum occluder search radius in pixels. Helps maintain GTAO visibility at greater distances by automatically selecting whichever radius is larger (world-space or screen-space) per pixel.");
		public static GUIContent GTAO_SliceCount                 = new GUIContent("Slice Count", "Specifies the number of directions (slices) used to evaluate occlusion.");
		public static GUIContent GTAO_StepCount                  = new GUIContent("Step Count", "Specifies the number of steps taken along each direction (slice).");
		public static GUIContent GTAO_UseAttenuation             = new GUIContent("Use Attenuation", "Specifies whether falloff should be applied during occlusion evaluation. Enabling matches non-bitmask contrast; disabling resembles RTAO more closely.");
		public static GUIContent GTAO_TracingMode                = new GUIContent("Tracing Mode", "Defines the algorithm used for occlusion calculation.");
		public static GUIContent GTAO_DepthFormat                = new GUIContent("Depth Format", "Controls the depth buffer precision used for GTAO rendering.");
		public static GUIContent GTAO_ThicknessMode              = new GUIContent("Thickness Mode", "Defines the thickness calculation method for Visibility Bitmasks. Linear increases thickness with distance, while Uniform keeps it constant.");
		public static GUIContent GTAO_Resolution                 = new GUIContent("Resolution", "Determines the resolution at which the effect is rendered. Full uses 100% of pixels, Half uses 50% in a checkerboard pattern, Quarter uses 25%.");
		public static GUIContent GTAO_UseSimpleRejection         = new GUIContent("Use Simple Rejection", "Switches temporal rejection to a simplified TAA-like color clamping mechanism applied uniformly across the entire frame. Saves resources compared to the advanced algorithm that selectively handles dynamic occlusion based on motion.");
		public static GUIContent GTAO_TemporalRejection          = new GUIContent("Rejection Threshold", "Controls the strictness of temporal history rejection. In Simple mode, applies across the entire frame; otherwise applies only to occlusion on moving objects. Higher values reject more aggressively, reducing ghosting but increasing noise.");
		public static GUIContent GTAO_ReprojectionFilter         = new GUIContent("Reprojection Filter", "Defines the reprojection filter for temporal history fetch.");
		public static GUIContent GTAO_UseNormalWeightingTemporal = new GUIContent("Use Normal Weighting", "Considers the difference in surface normals during temporal history reprojection. Mitigates artifacts where occlusion incorrectly bleeds between surfaces, particularly on intricate geometry.");
		public static GUIContent GTAO_SpatialFilterType          = new GUIContent("Filter Type", "Selects the spatial filtering approach. Box gathers samples from neighboring pixels for stronger noise reduction but may over-blur. Disk uses an anisotropic disk aligned with the surface normal for better detail preservation.");
		public static GUIContent GTAO_FilterRadius               = new GUIContent("Filter Radius", "Radius of the anisotropic filtering disk. Higher values provide stronger noise reduction, but may cause halo-like artifacts around objects. Setting to 0 disables spatial filtering except in disocclusion areas.");
		public static GUIContent GTAO_FilterAdaptivity           = new GUIContent("Filter Adaptivity", "Adaptively reduces filter radius in crevices and occluded areas to prevent over-blurring while preserving details. High values may diminish denoising effectiveness.");
		public static GUIContent GTAO_BoxPassCount               = new GUIContent("Pass Count", "Number of box filter passes. Each consecutive pass samples more distant pixels, making the blur wider and stronger at the cost of performance.");
		public static GUIContent GTAO_DoubleSampleCount          = new GUIContent("Double Sample Count", "Doubles the number of spatial filter sample taps, making the filter twice as costly but twice as efficient at noise reduction.");
		public static GUIContent GTAO_DenoisingTabContent        = new GUIContent("Denoising");
		public static GUIContent GTAO_Denoising                  = new GUIContent("Denoising Mode", "Defines the denoising approach.");
		public static GUIContent GTAO_ExcludeCasting             = new GUIContent("Exclude Casting", "Excludes objects from casting AO on a per-layer basis via Rendering Layer Mask.");
		public static GUIContent GTAO_ExcludeReceiving           = new GUIContent("Exclude Receiving", "Excludes objects from receiving AO on a per-layer basis via the Rendering Layer Mask.");
		
		// RTAO Tab
		public static GUIContent RTAO_SpecularOcclusion          = new GUIContent("Specular Occlusion", "Defines the level of occlusion applied to the specular lighting component.");
		public static GUIContent RTAO_TracingMode                = new GUIContent("Tracing Mode", "Defines the ray tracing method. Native Ray Tracing supports full material evaluation for alpha cutout, while Inline Ray Tracing uses screen-space depth testing.");
		public static GUIContent RTAO_CullAlphaCutout            = new GUIContent("Cull Alpha Cutout", "Culls all geometry with Alpha Cutout properties from the ray tracing acceleration structure.");
		public static GUIContent RTAO_CullingMask                = new GUIContent("Culling Mask", "Culls objects from the ray tracing acceleration structure on a per-layer basis. Individual meshes can alternatively be culled via the Mesh Renderer's Ray Tracing Mode setting.");
		public static GUIContent RTAO_CullingMode                = new GUIContent("Culling Mode", "Specifies which technique is used to cull geometry from the ray tracing acceleration structure.");
		public static GUIContent RTAO_WorldSpaceRadius           = new GUIContent("World Space Radius", "Defines the maximum distance in meters for ray tracing.");
		public static GUIContent RTAO_MaxRayBias                 = new GUIContent("Max Ray Bias", "Controls the maximum ray bias (offset) from a surface, used to avoid self-intersection with geometry. Keep this value minimal to preserve fine detail capture.");
		public static GUIContent RTAO_RayCount                   = new GUIContent("Ray Count", "Specifies the number of rays launched into the scene to evaluate occlusion.");
		public static GUIContent RTAO_SampleRebalancing          = new GUIContent("Sample Rebalancing", "Stochastically reduces the ray count on random pixels to keep the total number of rays per frame as constant as possible, avoiding sudden performance spikes.");
		public static GUIContent RTAO_CullBackfaces              = new GUIContent("Cull Backfaces", "Specifies whether rays can collide with the backfacing sides of polygons.");
		public static GUIContent RTAO_Resolution                 = new GUIContent("Resolution", "Determines the resolution at which the effect is rendered. Full (100%), Half (50% checkerboarded), Quarter (25%), Adaptive (variable resolution using 2x2 tiles based on geometry uniformity).");
		public static GUIContent RTAO_SampleCountTemporal        = new GUIContent("Sample Count", "Specifies the number of temporally accumulated frames. More samples lead to better noise reduction with no additional performance cost, while fewer samples make occlusion more reactive.");
		public static GUIContent RTAO_StaticRejection            = new GUIContent("Rejection Static", "Controls rejection strength for occlusion from static objects cast onto dynamic objects. Higher values activate rejection at slower speeds. Recommended to keep low.");
		public static GUIContent RTAO_DynamicRejection           = new GUIContent("Rejection Dynamic", "Controls rejection strength for occlusion from dynamic objects cast onto static and other dynamic objects. Higher values activate rejection at slower speeds. Recommended to keep high.");
		public static GUIContent RTAO_ReprojectionFilter         = new GUIContent("Reprojection Filter", "Defines the reprojection filter for temporal history fetch.");
		public static GUIContent RTAO_UseNormalWeightingTemporal = new GUIContent("Use Normal Weighting", "Considers surface normal differences during temporal history reprojection. Mitigates artifacts on small, irregular geometry where occlusion leaks between surfaces.");
		public static GUIContent RTAO_DisocclusionBoost          = new GUIContent("Disocclusion Boost", "Enables additional sampling to reduce noise and improve temporal stability in areas with insufficient history.");
		public static GUIContent RTAO_FilterRadius               = new GUIContent("Filter Radius", "Radius of the anisotropic filtering disk. Higher values provide stronger noise reduction, but may cause halo-like artifacts around objects beyond a certain value.");
		public static GUIContent RTAO_ContactOcclusion           = new GUIContent("Contact Occlusion", "Adds a screen-space occlusion pass with a very short radius to restore small details lost during spatial filtering. Disabled when set to 0.");
		public static GUIContent RTAO_ContactOcclusionDenoise    = new GUIContent("             Denoise", "Activates denoising of the Contact Occlusion effect.");
		public static GUIContent RTAO_DenoisingTabContent        = new GUIContent("Denoising");
		public static GUIContent RTAO_Denoising                  = new GUIContent("Denoising Mode", "Controls the denoising approach.");
		public static GUIContent RTAO_ScreenSpacePretrace        = new GUIContent("Screen Space Pretrace", "Activates screen-space ray marching before hardware ray tracing. Its primary goal is to hide the mismatch between the rasterized scene and its ray-tracing counterpart.");
		public static GUIContent RTAO_PretraceMask               = new GUIContent("Mask", "Excludes objects from casting screen-space AO on a per-layer basis via Rendering Layer Mask during Screen Space Pretrace.");
		public static GUIContent RTAO_PretraceSamples            = new GUIContent("Samples", "Defines how many steps the screen-space ray marching algorithm takes across the depth buffer to find an intersection.");
		public static GUIContent RTAO_PretraceDistance           = new GUIContent("Distance", "Maximum distance in meters that can be ray-marched in screen space. Samples are distributed along this distance.");
		public static GUIContent RTAO_PretraceThickness          = new GUIContent("Thickness", "Controls the virtual thickness of objects during screen-space pretrace.");

		// Debug Tab
		public static GUIContent DebugContent       = new GUIContent("Debug");
		public static GUIContent hTraceLayerContent = new GUIContent("HTrace Layer", "Excludes objects from HTrace rendering on a per-layer basis.");

		public static GUIStyle bold = new GUIStyle()
		{
			alignment = TextAnchor.MiddleLeft,
			margin = new RectOffset(),
			padding = new RectOffset(2, 0, 0, 0),
			fontSize = 12,
			normal = new GUIStyleState()
			{
				textColor = new Color(0.903f, 0.903f, 0.903f, 1f),
			},
			fontStyle = FontStyle.Bold,
		};
		
		public static GUIStyle hiddenFoldout = new GUIStyle()
		{
			alignment = TextAnchor.MiddleLeft,
			margin = new RectOffset(),
			padding = new RectOffset(),
			fontSize = 12,
			normal = new GUIStyleState()
			{
				//textColor = new Color(0.703f, 0.703f, 0.703f, 1f), //default color
				textColor = new Color(0.500f, 0.500f, 0.500f, 1f),
			},
			fontStyle = FontStyle.Bold,
		};

		public static GUIStyle headerFoldout = new GUIStyle()
		{
			alignment = TextAnchor.MiddleLeft,
			margin = new RectOffset(),
			padding = new RectOffset(),
			fontSize = 12,
			normal = new GUIStyleState()
			{
				textColor = new Color(0.903f, 0.903f, 0.903f, 1f),
			},
			fontStyle = FontStyle.Bold,
		};

		private static GUIStyle _versionStyle;
		public static GUIStyle VersionStyle
		{
			get
			{
				if (_versionStyle == null)
				{
					_versionStyle = new GUIStyle(GUI.skin.label)
					{
						//padding = new RectOffset(left: 10, right: 10, top: 2, bottom: 2),
						fontStyle = FontStyle.Bold,
						fontSize = 10,
					};
				}
				return _versionStyle;
			}
		}
		
		//buttons gui styles
		public static Color warningBackgroundColor = new Color(1,1, 0);
		public static Color warningColor           = new Color(1, 1, 1);

		public static GUIStyle foldout = EditorStyles.foldout;
	}
}
#endif
