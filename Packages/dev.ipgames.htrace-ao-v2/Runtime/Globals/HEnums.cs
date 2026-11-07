using UnityEngine;
using UnityEngine.Rendering;

namespace IPGames.HTraceAO.Runtime.Globals
{
	public enum ResolutionMode
	{
		[InspectorName("Full"), Tooltip("Renders the effect at full resolution. Highest quality but most expensive.")]
		Full = 0,
		[InspectorName("Half"), Tooltip("Renders the effect at half resolution, which processes only half of the pixels per frame. This option is designed to minimize visual impact while improving calculation times by up to 50%.")]
		Half = 1,
		[InspectorName("Quarter"), Tooltip("Renders the effect at quarter resolution. Provides significant performance improvement at the cost of some detail.")]
		Quarter = 2,
		[InspectorName("Adaptive"), Tooltip("Renders the effect at adaptive resolution, dynamically adjusting based on scene requirements for optimal performance and quality balance.")]
		Adaptive = 3,
	}

	public enum AmbientOcclusionMode
	{
		[InspectorName("SSAO"), Tooltip("Screen Space Ambient Occlusion")]
		SSAO = 0,
		[InspectorName("GTAO"), Tooltip("Ground Truth Ambient Occlusion")]
		GTAO = 1,
		[InspectorName("RTAO"), Tooltip("Ray Traced Ambient Occlusion")]
		RTAO = 2,
	}
	public enum ReprojectionFilter
	{
		[InspectorName("Linear (Fast)")]
		Linear = 0,
		[InspectorName("Bicubic (Sharp)")]
		Bicubic  = 1,
	}
	public enum TracingMode
	{
		[InspectorName("Native Ray Tracing"), Tooltip("Uses hardware ray tracing with full material evaluation for alpha cutout support.")]
		NativeRayTracing = 0,
		[InspectorName("Inline Ray Tracing"), Tooltip("Uses inline ray tracing with screen-space depth test for alpha cutout evaluation.")]
		InlineRayTracing = 1,
	}

	public enum GTAOResolutionMode
	{
		[InspectorName("Full"), Tooltip("Renders the effect at full resolution. Highest quality but most expensive.")]
		Full = 0,
		[InspectorName("Half"), Tooltip("Renders the effect at half resolution, dynamically adjusting based on scene requirements for optimal performance and quality balance.")]
		Half = 1,
		[InspectorName("Quarter"), Tooltip("Renders the effect at quarter resolution. Provides significant performance improvement at the cost of some detail.")]
		Quarter = 2,
	}

	public enum GTAOTracingMode
	{
		[InspectorName("Horizon Search"), Tooltip("Classic GTAO horizon-based occlusion search. Fast and efficient for most scenes.")]
		HorizonSearch = 0,
		[InspectorName("Visibility Bitmasks"), Tooltip("This approach provides more accurate results, especially in areas with thin geometry such as bars, fences, grills, and thin pillars, at a moderate performance cost.")]
		VisibilityBitmasks = 1,
	}

	public enum URPInjectionPoint
	{
		[InspectorName("Before Opaque (ShaderGraph)"), Tooltip("Injects the AO pass before the GBuffer pass.")]
		BeforeOpaque = 0,
		[InspectorName("Before Lighting (Injection)"), Tooltip("Injects the AO pass before the lighting pass.")]
		BeforeLighting = 1,
		[InspectorName("After Opaque (Overlay)"), Tooltip("Injects the AO pass after opaque rendering.")]
		AfterOpaque = 2,
	}

	public enum GTAODepthFormat
	{
		[InspectorName("16 Bit (Fast)"), Tooltip("Uses 16-bit depth format for faster rendering with slightly reduced precision.")]
		R16 = 0,
		// [InspectorName("32 Bit (Accurate)"), Tooltip("Uses 32-bit depth format for higher precision at the cost of additional memory and bandwidth.")]
		// R32 = 1,
	}

	public enum GTAOThicknessMode
	{
		[InspectorName("Linear"), Tooltip("Linear thickness estimation.")]
		Linear = 0,
		[InspectorName("Uniform"), Tooltip("Uniform thickness estimation.")]
		Uniform = 1,
	}

	public enum SpatialFilterType
	{
		[InspectorName("Disk"), Tooltip("Disk-shaped spatial filter with configurable radius.")]
		Disk = 0,
		[InspectorName("Box"), Tooltip("Box-shaped spatial filter with configurable pass count.")]
		Box = 1,
	}


	public enum DebugModeSSAO
	{
		[InspectorName("None")]
		None = 0,
		[InspectorName("Depth")]
		Depth,
		[InspectorName("Ambient Occlusion")]
		AmbientOcclusion,
	}

	public enum DebugModeGTAO
	{
		[InspectorName("None")]
		None = 0,
		[InspectorName("Main Buffers")]
		MainBuffers,
		[InspectorName("Ambient Occlusion")]
		AmbientOcclusion,
		[InspectorName("Temporal Disocclusion")]
		TemporalDisocclusion,
	}

	public enum DebugModeRTAO
	{
		None = 0,
		MainBuffers,
		AmbientOcclusion,
		TemporalDisocclusion,
		[InspectorName("Shading Rate Visualization")]
		ShadingRateVisualization,
		[InspectorName("Hardware Ray Tracing Scene")]
		HardwareRayTracingScene,
	}

	public enum CullingMode
	{
		[InspectorName("None")]
		None = 0,
		[InspectorName("Sphere")]
		Sphere = 1,
		[InspectorName("Solid Angle")]
		SolidAngle = 2,
	}

	public enum RTAODenoisingMode
	{
		[InspectorName("None"), Tooltip("No denoising pass applied.")]
		None = 0,
		[InspectorName("Single Pass"), Tooltip("Single denoising pass for improved noise reduction.")]
		SinglePass = 1,
		[InspectorName("Double Pass"), Tooltip("Two denoising passes for maximum noise reduction and temporal stability.")]
		DoublePass = 2,
	}

	public enum GTAODenoisingMode
	{
		[InspectorName("None"), Tooltip("No denoising pass applied.")]
		None = 0,
		[InspectorName("Spatial Only"), Tooltip("Only spatial filtering is applied. Fast but may exhibit temporal flickering.")]
		SpatialOnly = 1,
		[InspectorName("Temporal Only"), Tooltip("Only temporal accumulation is applied. Smooth but may lack spatial noise reduction.")]
		TemporalOnly = 2,
		[InspectorName("Spatio-Temporal"), Tooltip("Both temporal accumulation and spatial filtering are applied for maximum noise reduction.")]
		SpatioTemporal = 3,
	}
	
	public enum HBuffer
	{
		[InspectorName("Multi")]
		Multi = 0,
		[InspectorName("Depth")]
		Depth = 1,
		//Diffuse = 2,
		[InspectorName("Normal")]
		Normal        = 3,
		[InspectorName("Motion Mask")]
		MotionMask    = 4,
		[InspectorName("Motion Vectors")]
		MotionVectors = 5,
	}

	public enum DebugType
	{
		Log,
		Warning,
		Error,
	}

	public enum HInjectionPoint
	{
	}
}
