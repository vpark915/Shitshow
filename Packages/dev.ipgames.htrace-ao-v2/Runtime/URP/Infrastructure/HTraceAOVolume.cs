
using System;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IPGames.HTraceAO.Runtime.URP.Infrastructure
{
#if UNITY_2023_1_OR_NEWER
	[VolumeComponentMenu("Lighting/HTrace: Ambient Occlusion"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
	[VolumeComponentMenuForRenderPipeline("Lighting/HTrace: Ambient Occlusion", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_6000_3_OR_NEWER
	[DisplayInfo(name = HNames.ASSET_NAME_FULL)]
#endif
#if UNITY_2023_3_OR_NEWER
	[VolumeRequiresRendererFeatures(typeof(HTraceAORendererFeature))]
#endif
	[HelpURL(HNames.HTRACE_AO_DOCUMENTATION_LINK)]
	public sealed class HTraceAOVolume : VolumeComponent, IPostProcessComponent
	{
		public HTraceAOVolume()
		{
#if !UNITY_6000_3_OR_NEWER
			displayName = HNames.ASSET_NAME_FULL;
#endif
		}
		
		[Tooltip("Enable HTrace Ambient Occlusion.")]
		public BoolParameter Enable = new BoolParameter(true, BoolParameter.DisplayType.EnumPopup);
		
		public AmbientOcclusionModeParameter AmbientOcclusionMode = new(value: Globals.AmbientOcclusionMode.GTAO, overrideState: false);
		[InspectorName("Buffer"), Tooltip("Visualizes the debug mode for different buffers.")]
		public HBufferParameter              HBuffer              = new(value: Globals.HBuffer.Multi, overrideState: false);
		
		[InspectorName("Fadeout"), Tooltip("Defines the distance in meters at which the occlusion effect fades out. Setting to 0 disables fadeout entirely.")]
		public MinFloatParameter Fadeout = new MinFloatParameter(100f, 0f, false);

		[InspectorName("Intensity"), Tooltip("Guides the final intensity of the ambient occlusion, higher values result in darker ambient occlusion.")]
		public ClampedFloatParameter Intensity = new ClampedFloatParameter(2f, 0.0f, 4.0f, false);

		[InspectorName("Injection Point"), Tooltip("Determines where AO renders in the URP pipeline.")]
		public URPInjectionPointParameter InjectionPoint = new URPInjectionPointParameter(value: URPInjectionPoint.BeforeLighting, overrideState: false);

		[InspectorName("Reconstruct Normals"), Tooltip("Reconstructs normals from the depth buffer. Normals reconstructed this way are flat and represent raw polygonal geometry, without accounting for smoothing groups or normal map details.")]
		public BoolParameter ReconstructNormals = new BoolParameter(false, false);

		[InspectorName("Debug Mode"), Tooltip("Selects the debug visualization.")]
		public DebugModeSSAOParameter        DebugModeSSAO        = new(value: Globals.DebugModeSSAO.None, overrideState: false);
	
		[InspectorName("Thickness"), Tooltip("Control the thickness of the surfaces on screen.")]
		public ClampedFloatParameter Thickness = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);

		public ClampedIntParameter Radius = new ClampedIntParameter(2, 1, 4);

#if UNITY_2023_3_OR_NEWER
		[InspectorName("Exclude Casting"), Tooltip("Excludes objects from casting AO on a per-layer basis via Rendering Layer Mask.")]
		public RenderingLayerMaskParameter SSAOExcludeCasting = new(value: default, overrideState: false);

		[InspectorName("Exclude Receiving"), Tooltip("Excludes objects from receiving AO on a per-layer basis via the Rendering Layer Mask.")]
		public RenderingLayerMaskParameter SSAOExcludeReceiving = new(value: default, overrideState: false);

		[InspectorName("Intensity"), Tooltip("Controls the AO intensity on excluded receiving surfaces. 0 = no AO, 1 = full AO.")]
		public ClampedFloatParameter SSAOExcludedIntensity = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);
#endif
		
		[InspectorName("Direct Lighting Occlusion"), Tooltip("Defines how visible the effect is in areas exposed to direct lighting. Use 0 for maximum physical accuracy.")]
		public ClampedFloatParameter DirectLightingOcclusion = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);
		
		[InspectorName("Debug Mode"), Tooltip("Selects the debug visualization. Depth shows the depth buffer. Main Buffers displays pipeline buffers at different stages. Ambient Occlusion shows the final AO output. Temporal Disocclusion shows temporal accumulation state. Shading Rate Visualization and Hardware Ray Tracing Scene are RTAO only.")]
		public DebugModeGTAOParameter DebugModeGTAO          = new(value: Globals.DebugModeGTAO.None, overrideState: false);

		[InspectorName("Resolution"), Tooltip("Determines the resolution at which the effect is rendered. Full uses 100% of pixels, Half uses 50% in a checkerboard pattern, Quarter uses 25%.")]
		public GTAOResolutionModeParameter GTAOResolution = new GTAOResolutionModeParameter(value: GTAOResolutionMode.Full, overrideState: false);

		[InspectorName("Thickness Value"), Tooltip("Controls the virtual thickness of objects. Higher values strengthen occlusion cast by all objects.")]
		public ClampedFloatParameter GTAOThickness = new ClampedFloatParameter(0.2f, 0.05f, 1.0f, false);

		[InspectorName("World Space Radius"), Tooltip("Defines the maximum distance in meters for occluder search.")]
		public ClampedFloatParameter GTAOWorldSpaceRadius = new ClampedFloatParameter(1f, 0.1f, 5.0f, false);

		[InspectorName("Screen Space Radius"), Tooltip("Maximum occluder search radius in pixels. Helps maintain GTAO visibility at greater distances by automatically selecting whichever radius is larger (world-space or screen-space) per pixel.")]
		public ClampedIntParameter GTAOScreenSpaceRadius = new ClampedIntParameter(25, 1, 256, false);

		[InspectorName("Slice Count"), Tooltip("Specifies the number of directions (slices) used to evaluate occlusion.")]
		public ClampedIntParameter GTAOSliceCount = new ClampedIntParameter(2, 1, 4, false);

		[InspectorName("Step Count"), Tooltip("Specifies the number of steps taken along each direction (slice).")]
		public ClampedIntParameter GTAOStepCount = new ClampedIntParameter(16, 8, 32, false);

		[InspectorName("Tracing Mode"), Tooltip("Defines the algorithm used for occlusion calculation.")]
		public GTAOTracingModeParameter GTAOTracingMode = new GTAOTracingModeParameter(value: Globals.GTAOTracingMode.HorizonSearch, overrideState: false);

		[InspectorName("Depth Format"), Tooltip("Controls the depth buffer precision used for GTAO rendering.")]
		public GTAODepthFormatParameter GTAODepthFormat = new GTAODepthFormatParameter(value: Globals.GTAODepthFormat.R16, overrideState: false);

		[InspectorName("Thickness Mode"), Tooltip("Defines the thickness calculation method for Visibility Bitmasks. Linear increases thickness with distance, while Uniform keeps it constant.")]
		public GTAOThicknessModeParameter GTAOThicknessMode = new GTAOThicknessModeParameter(value: Globals.GTAOThicknessMode.Linear, overrideState: false);

		[InspectorName("Use Attenuation"), Tooltip("Specifies whether falloff should be applied during occlusion evaluation. Enabling matches non-bitmask contrast; disabling resembles RTAO more closely.")]
		public BoolParameter GTAOUseAttenuation = new BoolParameter(true, false);

		[InspectorName("Denoising Mode"), Tooltip("Defines the denoising approach.")]
		public GTAODenoisingModeParameter GTAODenoising = new GTAODenoisingModeParameter(value: GTAODenoisingMode.SpatioTemporal, overrideState: false);

		[InspectorName("Sample Count"), Tooltip("Specifies the number of temporally accumulated frames. More samples lead to better noise reduction with no additional performance cost, while fewer samples make occlusion more reactive.")]
		public ClampedIntParameter GTAOSampleCountTemporal = new ClampedIntParameter(8, 0, 12, false);

		[InspectorName("Use Simple Rejection"), Tooltip("Switches temporal rejection to a simplified TAA-like color clamping mechanism applied uniformly across the entire frame. Saves resources compared to the advanced algorithm that selectively handles dynamic occlusion based on motion.")]
		public BoolParameter GTAOUseSimpleRejection = new BoolParameter(false, false);

		[InspectorName("Rejection Threshold"), Tooltip("Controls the strictness of temporal history rejection. In Simple mode, applies across the entire frame; otherwise applies only to occlusion on moving objects. Higher values reject more aggressively, reducing ghosting but increasing noise.")]
		public ClampedFloatParameter GTAOTemporalRejection = new ClampedFloatParameter(0.8f, 0.0f, 1.0f, false);

		[InspectorName("Reprojection Filter"), Tooltip("Defines the reprojection filter for temporal history fetch.")]
		public ReprojectionFilterParameter GTAOReprojectionFilter = new ReprojectionFilterParameter(value: ReprojectionFilter.Linear, overrideState: false);
		
		[InspectorName("Use Normal Weighting"), Tooltip("Considers the difference in surface normals during temporal history reprojection. Mitigates artifacts where occlusion incorrectly bleeds between surfaces, particularly on intricate geometry.")]
		public BoolParameter GTAOUseNormalWeightingTemporal = new BoolParameter(true, false);

		[InspectorName("Filter Type"), Tooltip("Selects the spatial filtering approach. Box gathers samples from neighboring pixels for stronger noise reduction but may over-blur. Disk uses an anisotropic disk aligned with the surface normal for better detail preservation.")]
		public SpatialFilterTypeParameter GTAOSpatialFilterType = new SpatialFilterTypeParameter(value: SpatialFilterType.Box, overrideState: false);

		[InspectorName("Filter Radius"), Tooltip("Radius of the anisotropic filtering disk. Higher values provide stronger noise reduction, but may cause halo-like artifacts around objects. Setting to 0 disables spatial filtering except in disocclusion areas.")]
		public ClampedFloatParameter GTAOFilterRadius = new ClampedFloatParameter(0.5f, 0.0f, 1.0f, false);

		[InspectorName("Filter Adaptivity"), Tooltip("Adaptively reduces filter radius in crevices and occluded areas to prevent over-blurring while preserving details. High values may diminish denoising effectiveness.")]
		public ClampedFloatParameter GTAOFilterAdaptivity = new ClampedFloatParameter(0.5f, 0.0f, 1.0f, false);

		[InspectorName("Pass Count"), Tooltip("Number of box filter passes. Each consecutive pass samples more distant pixels, making the blur wider and stronger at the cost of performance.")]
		public ClampedIntParameter GTAOBoxPassCount = new ClampedIntParameter(2, 1, 3, false);

		[InspectorName("Double Sample Count"), Tooltip("Doubles the number of spatial filter sample taps, making the filter twice as costly but twice as efficient at noise reduction.")]
		public BoolParameter GTAODoubleSampleCount = new BoolParameter(false, false);

#if UNITY_2023_3_OR_NEWER
		[InspectorName("Exclude Casting"), Tooltip("Excludes objects from casting AO on a per-layer basis via Rendering Layer Mask.")]
		public RenderingLayerMaskParameter GTAOExcludeCasting = new(value: default, overrideState: false);

		[InspectorName("Exclude Receiving"), Tooltip("Excludes objects from receiving AO on a per-layer basis via the Rendering Layer Mask.")]
		public RenderingLayerMaskParameter GTAOExcludeReceiving = new(value: default, overrideState: false);

		[InspectorName("Intensity"), Tooltip("Controls the AO intensity on excluded receiving surfaces. 0 = no AO, 1 = full AO.")]
		public ClampedFloatParameter GTAOExcludedIntensity = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);

		[InspectorName("Early Out"), Tooltip("Skips AO tracing and filtering for excluded receivers. Intended for spatially isolated objects")]
		public BoolParameter GTAOEarlyOutExcludedReceivers = new BoolParameter(false, false);
#endif
		
		[InspectorName("Debug Mode"), Tooltip("Selects the debug visualization. Depth shows the depth buffer. Main Buffers displays pipeline buffers at different stages. Ambient Occlusion shows the final AO output. Temporal Disocclusion shows temporal accumulation state. Shading Rate Visualization and Hardware Ray Tracing Scene are RTAO only.")]
		public DebugModeRTAOParameter DebugModeRTAO = new(value: Globals.DebugModeRTAO.None, overrideState: false);

		[InspectorName("World Space Radius"), Tooltip("Defines the maximum distance in meters for ray tracing.")]
		public ClampedFloatParameter RTAOWorldSpaceRadius = new ClampedFloatParameter(1f, 0.1f, 5.0f, false);

		[InspectorName("Max Ray Bias"), Tooltip("Controls the maximum ray bias (offset) from a surface, used to avoid self-intersection with geometry. Keep this value minimal to preserve fine detail capture.")]
		public ClampedFloatParameter RTAOMaxRayBias = new ClampedFloatParameter(0.001f, 0.001f, 0.05f, false);

		[InspectorName("Ray Count"), Tooltip("Specifies the number of rays launched into the scene to evaluate occlusion.")]
		public ClampedIntParameter RTAORayCount = new ClampedIntParameter(4, 1, 8, false);

		[InspectorName("Resolution"), Tooltip("Determines the resolution at which the effect is rendered. Full (100%), Half (50% checkerboarded), Quarter (25%), Adaptive (variable resolution using 2x2 tiles based on geometry uniformity).")]
		public ResolutionModeParameter RTAOResolution = new ResolutionModeParameter(value: ResolutionMode.Adaptive, overrideState: false);

		[InspectorName("Denoising Mode"), Tooltip("Controls the denoising approach.")]
		public RTAODenoisingModeParameter RTAODenoising = new RTAODenoisingModeParameter(value: RTAODenoisingMode.DoublePass, overrideState: false);

		[InspectorName("Sample Count Temporal"), Tooltip("Specifies the number of temporally accumulated frames. More samples lead to better noise reduction with no additional performance cost, while fewer samples make occlusion more reactive.")]
		public ClampedIntParameter RTAOSampleCountTemporal = new ClampedIntParameter(16, 8, 16, false);

		[InspectorName("Reprojection Filter"), Tooltip("Defines the reprojection filter for temporal history fetch.")]
		public ReprojectionFilterParameter RTAOReprojectionFilter = new ReprojectionFilterParameter(value: ReprojectionFilter.Linear, overrideState: false);

		[InspectorName("Use Normal Weighting"), Tooltip("Considers surface normal differences during temporal history reprojection. Mitigates artifacts on small, irregular geometry where occlusion leaks between surfaces.")]
		public BoolParameter RTAOUseNormalWeightingTemporal = new BoolParameter(true, false);

		[InspectorName("Disocclusion Boost"), Tooltip("Enables additional sampling to reduce noise and improve temporal stability in areas with insufficient history.")]
		public BoolParameter RTAODisocclusionBoost = new BoolParameter(true, false);

		[InspectorName("Filter Radius"), Tooltip("Radius of the anisotropic filtering disk. Higher values provide stronger noise reduction, but may cause halo-like artifacts around objects beyond a certain value.")]
		public ClampedFloatParameter RTAOFilterRadius = new ClampedFloatParameter(0.5f, 0.0f, 1.0f, false);

		[InspectorName("Screen Space Pretrace"), Tooltip("Activates screen-space ray marching before hardware ray tracing. Its primary goal is to hide the mismatch between the rasterized scene and its ray-tracing counterpart.")]
		public BoolParameter RTAOScreenSpacePretrace = new BoolParameter(false, false);
		
#if UNITY_2023_3_OR_NEWER
		[InspectorName("Pretrace Mask"), Tooltip("Excludes objects from casting screen-space AO on a per-layer basis via Rendering Layer Mask during Screen Space Pretrace.")]
		public RenderingLayerMaskParameter RTAOPretraceMask = new(value: RenderingLayerMask.defaultRenderingLayerMask, overrideState: false);
#endif
		
		[InspectorName("Pretrace Samples"), Tooltip("Defines how many steps the screen-space ray marching algorithm takes across the depth buffer to find an intersection.")]
		public ClampedIntParameter RTAOPretraceSamples = new ClampedIntParameter(12, 8, 24, false);

		[InspectorName("Pretrace Distance"), Tooltip("Maximum distance in meters that can be ray-marched in screen space. Samples are distributed along this distance.")]
		public ClampedFloatParameter RTAOPretraceDistance = new ClampedFloatParameter(0.5f, 0.1f, 1.0f, false);

		[InspectorName("Pretrace Thickness"), Tooltip("Controls the virtual thickness of objects during screen-space pretrace.")]
		public ClampedFloatParameter RTAOPretraceThickness = new ClampedFloatParameter(0.1f, 0.05f, 1.0f, false);

		[InspectorName("Sample Rebalancing"), Tooltip("Stochastically reduces the ray count on random pixels to keep the total number of rays per frame as constant as possible, avoiding sudden performance spikes.")]
		public BoolParameter RTAOSampleRebalancing = new BoolParameter(false, false);
		
		[InspectorName("Cull Backfaces"), Tooltip("Specifies whether rays can collide with the backfacing sides of polygons.")]
		public BoolParameter RTAOCullBackfaces = new BoolParameter(false, false);

		[InspectorName("Cull Alpha Cutout"), Tooltip("Culls all geometry with Alpha Cutout properties from the ray tracing acceleration structure.")]
		public BoolParameter RTAOCullAlphaCutout = new BoolParameter(false, false);

		[InspectorName("Culling Mask"), Tooltip("Culls objects from the ray tracing acceleration structure on a per-layer basis. Individual meshes can alternatively be culled via the Mesh Renderer's Ray Tracing Mode setting.")]
		public LayerEnumParameter RTAOCullingMask = new(value: ~0, overrideState: false);

		[InspectorName("Culling Mode"), Tooltip("Specifies which technique is used to cull geometry from the ray tracing acceleration structure.")]
		public CullingModeParameter RTAOCullingMode = new CullingModeParameter(value: CullingMode.None, overrideState: false);

		[InspectorName("Culling Distance"), Tooltip("Maximum distance for sphere-based culling.")]
		public FloatParameter RTAOCullingDistance = new FloatParameter(100f, false);

		[InspectorName("Min Solid Angle"), Tooltip("Minimum solid angle threshold for solid angle-based culling.")]
		public ClampedFloatParameter RTAOMinSolidAngle = new ClampedFloatParameter(1f, 0.01f, 180f, false);
		
		[InspectorName("Rejection Static"), Tooltip("Controls rejection strength for occlusion from static objects cast onto dynamic objects. Higher values activate rejection at slower speeds. Recommended to keep low.")]
		public ClampedFloatParameter RTAOStaticRejection = new ClampedFloatParameter(0.1f, 0.0f, 1.0f, false);

		[InspectorName("Rejection Dynamic"), Tooltip("Controls rejection strength for occlusion from dynamic objects cast onto static and other dynamic objects. Higher values activate rejection at slower speeds. Recommended to keep high.")]
		public ClampedFloatParameter RTAODynamicRejection = new ClampedFloatParameter(0.3f, 0.0f, 1.0f, false);
		
		[InspectorName("Contact Occlusion"), Tooltip("Adds a screen-space occlusion pass with a very short radius to restore small details lost during spatial filtering. Disabled when set to 0.")]
		public ClampedFloatParameter RTAOContactOcclusion = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);

		[InspectorName("Denoise"), Tooltip("Activates denoising of the Contact Occlusion effect.")]
		public BoolParameter RTAOContactOcclusionDenoise = new BoolParameter(true, false);

#if UNITY_2023_3_OR_NEWER
		[InspectorName("Exclude Receiving"), Tooltip("Excludes objects from receiving AO on a per-layer basis via the Rendering Layer Mask.")]
		public RenderingLayerMaskParameter RTAOExcludeReceiving = new(value: default, overrideState: false);
#endif

		[InspectorName("Intensity"), Tooltip("Controls the AO intensity on excluded receiving surfaces. 0 = no AO, 1 = full AO.")]
		public ClampedFloatParameter RTAOExcludedIntensity = new ClampedFloatParameter(0f, 0.0f, 1.0f, false);

		[InspectorName("Early Out"), Tooltip("Skips AO tracing and filtering for excluded receivers. Intended for spatially isolated objects")]
		public BoolParameter RTAOEarlyOutExcludedReceivers = new BoolParameter(false, false);
		
		[InspectorName("Scene Mismatch"), Tooltip("Visualizes differences between the visible scene and the ray tracing acceleration structure.")]
		public BoolParameter RTAODebugSceneMismatch = new BoolParameter(false, false); 

		public bool IsActive()
		{
			return Enable.value;
		}

#if !UNITY_2023_2_OR_NEWER
		public bool IsTileCompatible() => false;
#endif
	}

	[Serializable]
	public sealed class AmbientOcclusionModeParameter : VolumeParameter<AmbientOcclusionMode>
	{
		public AmbientOcclusionModeParameter(AmbientOcclusionMode value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class DebugModeSSAOParameter : VolumeParameter<DebugModeSSAO>
	{
		public DebugModeSSAOParameter(DebugModeSSAO value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class HBufferParameter : VolumeParameter<HBuffer>
	{
		public HBufferParameter(HBuffer value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class DebugModeGTAOParameter : VolumeParameter<DebugModeGTAO>
	{
		public DebugModeGTAOParameter(DebugModeGTAO value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class DebugModeRTAOParameter : VolumeParameter<DebugModeRTAO>
	{
		public DebugModeRTAOParameter(DebugModeRTAO value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class LayerEnumParameter : VolumeParameter<LayerMask>
	{
		public LayerEnumParameter(LayerMask value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class ReprojectionFilterParameter : VolumeParameter<ReprojectionFilter>
	{
		public ReprojectionFilterParameter(ReprojectionFilter value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class ResolutionModeParameter : VolumeParameter<ResolutionMode>
	{
		public ResolutionModeParameter(ResolutionMode value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class GTAOResolutionModeParameter : VolumeParameter<GTAOResolutionMode>
	{
		public GTAOResolutionModeParameter(GTAOResolutionMode value, bool overrideState = false) : base(value, overrideState) { }
	}

#if UNITY_2023_3_OR_NEWER
	[Serializable]
	public sealed class RenderingLayerMaskParameter : VolumeParameter<RenderingLayerMask>
	{
		public RenderingLayerMaskParameter(RenderingLayerMask value, bool overrideState = false) : base(value, overrideState) { }
	}
#endif

	[Serializable]
	public sealed class URPInjectionPointParameter : VolumeParameter<URPInjectionPoint>
	{
		public URPInjectionPointParameter(URPInjectionPoint value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class RTAODenoisingModeParameter : VolumeParameter<RTAODenoisingMode>
	{
		public RTAODenoisingModeParameter(RTAODenoisingMode value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class CullingModeParameter : VolumeParameter<CullingMode>
	{
		public CullingModeParameter(CullingMode value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class GTAOTracingModeParameter : VolumeParameter<GTAOTracingMode>
	{
		public GTAOTracingModeParameter(GTAOTracingMode value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class GTAODepthFormatParameter : VolumeParameter<GTAODepthFormat>
	{
		public GTAODepthFormatParameter(GTAODepthFormat value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class GTAOThicknessModeParameter : VolumeParameter<GTAOThicknessMode>
	{
		public GTAOThicknessModeParameter(GTAOThicknessMode value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class SpatialFilterTypeParameter : VolumeParameter<SpatialFilterType>
	{
		public SpatialFilterTypeParameter(SpatialFilterType value, bool overrideState = false) : base(value, overrideState) { }
	}

	[Serializable]
	public sealed class GTAODenoisingModeParameter : VolumeParameter<GTAODenoisingMode>
	{
		public GTAODenoisingModeParameter(GTAODenoisingMode value, bool overrideState = false) : base(value, overrideState) { }
	}
}

