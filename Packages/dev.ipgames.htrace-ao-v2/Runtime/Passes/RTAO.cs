#if H_BIRP 
using HTraceAO.Rendering.BIRP; 
#endif 
using System;
using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_2022
using UnityEngine.Experimental.Rendering;
#endif

namespace IPGames.HTraceAO.Runtime.Passes
{
	internal static class RTAO
	{	
		// Kernels
		private enum HTemporalFilterRTAOKernel
		{
			TemporalReprojection  = 0,
			TemporalAccumulation  = 1,
			TemporalStabilization = 2,
		}

		private enum HSpatialFilterRTAOKernel
		{
			PointDistributionFill = 0,
			SpatialFiltering      = 1,
			Interpolation         = 2,
			OutputComposition     = 3,
		}

		private enum HThreadCompactionKernel
		{
			ShadingRateGeneration      = 0,
			ShadingRateClassification  = 1,
			DisocclusionClassification = 2,
			IndirectArguments          = 3,
		}

		private enum HRenderRTAOKernel
		{
			RenderRTAO = 0,
		}


		private enum HDepthPyramidAOKernel
		{
			GenerateRayMarchingDepth = 1,
		}

		private enum HContactOcclusionRTAOKernel
		{
			ContactOcclusion = 0,
		}

		// Keywords
		private const string ADAPTIVE_RESOLUTION          = "ADAPTIVE_RESOLUTION";
		private const string DISOCCLUSION_BOOST           = "DISOCCLUSION_BOOST";
		private const string SCREEN_SPACE_PRETRACE        = "SCREEN_SPACE_PRETRACE";
		private const string SCREEN_SPACE_PRETRACE_MASKED = "SCREEN_SPACE_PRETRACE_MASKED";
		private const string SAMPLE_REBALANCING           = "SAMPLE_REBALANCING";
		private const string CULL_BACK_FACES              = "CULL_BACK_FACES";
		private const string DEBUG_RTAS                   = "DEBUG_RTAS";
		private const string VR_COMPATIBILITY             = "VR_COMPATIBILITY";
		private const string BICUBIC_REPROJECTION         = "BICUBIC_REPROJECTION";
		private const string FIRST_PASS                   = "FIRST_PASS";
		private const string FINAL_PASS                   = "FINAL_PASS";
		private const string HALF_RESOLUTION              = "HALF_RESOLUTION";
		private const string USE_NORMAL_WEIGHTING         = "USE_NORMAL_WEIGHTING";
		private const string SHADING_RATE_VISUALIZATION   = "SHADING_RATE_VISUALIZATION";

		// Ray Tracing Pass Names
		private const string VISIBILITY_DXR_PASS                              = "VisibilityDXR";
		private const string TRACE_AMBIENT_OCCLUSION_PASS                     = "TraceAmbientOcclusion";
		private const string TRACE_AMBIENT_OCCLUSION_ADAPTIVE_RESOLUTION_PASS = "TraceAmbientOcclusionAdaptiveResolution";
		private const string TRACE_AMBIENT_OCCLUSION_DISOCCLUSION_BOOST_PASS  = "TraceAmbientOcclusionDisocclusionBoost";
		
		// Computes
		internal static ComputeShader    HRenderRTAO           = null;
		internal static ComputeShader    HDebugRTAO            = null;
		internal static ComputeShader    HTemporalFilterRTAO   = null;
		internal static ComputeShader    HSpatialFilterRTAO    = null;
		internal static ComputeShader    HThreadCompactionRTAO = null;
		internal static ComputeShader    HDepthPyramidAO       = null;
		internal static ComputeShader    HContactOcclusionRTAO = null;
		internal static RayTracingShader HRayTraceRTAO         = null;

		// Materials
		public static Material OutputCompositionRTAO = null;

		// RTAS
		internal static RayTracingAccelerationStructure RTAS = null;
		
		// Profiler Samplers
		internal static ProfilingSampler ThreadCompactionSampler           = new ProfilingSampler(Globals.HNames.THREAD_COMPACTION_SAMPLER);
		internal static ProfilingSampler OcclusionTracingSampler           = new ProfilingSampler(Globals.HNames.OCCLUSION_TRACING_SAMPLER);
		internal static ProfilingSampler DisocclusionBoostSampler          = new ProfilingSampler(Globals.HNames.DISOCCLUSION_BOOST_SAMPLER);
		internal static ProfilingSampler TemporalFilteringSampler          = new ProfilingSampler(Globals.HNames.TEMPORAL_FILTERING_SAMPLER);
		internal static ProfilingSampler SpatialFilteringSampler           = new ProfilingSampler(Globals.HNames.SPATIAL_FILTERING_SAMPLER);
		internal static ProfilingSampler RayMarchingDepthGenerationSampler = new ProfilingSampler(Globals.HNames.RAY_MARCHING_DEPTH_GENERATION_SAMPLER);
		internal static ProfilingSampler ContactOcclusionSampler           = new ProfilingSampler(Globals.HNames.CONTACT_OCCLUSION_SAMPLER);
		internal static ProfilingSampler InterpolationSampler              = new ProfilingSampler(Globals.HNames.INTERPOLATION_SAMPLER);
		internal static ProfilingSampler OutputCompositionSampler          = new ProfilingSampler(Globals.HNames.OUTPUT_COMPOSITION_SAMPLER);
		internal static ProfilingSampler DebugSampler                      = new ProfilingSampler(Globals.HNames.DEBUG_SAMPLER);
		internal static ProfilingSampler RTASBuildSampler                  = new ProfilingSampler(Globals.HNames.RTAS_BUILD_SAMPLER);
		internal static ProfilingSampler BufferCleanupSampler              = new ProfilingSampler(Globals.HNames.BUFFER_CLEANUP_SAMPLER);

		// History Textures
		internal struct HistoryCameraDataRTAO : Extensions.CameraHistorySystem.ICameraHistoryData, IDisposable
		{
			private int hash;

			public Wrappers.RTWrapper  OcclusionHistoryRTAO;
			public Wrappers.RTWrapper  VelocityHistoryRTAO;
			public Wrappers.RTWrapper  SamplecountNormalHistoryRTAO;
			public Wrappers.RTWrapper  FilterDepthRTAO;
			public Globals.TracingMode PreviousTracingMode;

			public int GetHash() => hash;
			public void SetHash(int hashIn) => this.hash = hashIn;
			public void Dispose()
			{
				OcclusionHistoryRTAO?.HRelease();
				VelocityHistoryRTAO?.HRelease();
				SamplecountNormalHistoryRTAO?.HRelease();
				FilterDepthRTAO?.HRelease();
			}
		}

		internal static readonly Extensions.CameraHistorySystem.CameraHistorySystem<HistoryCameraDataRTAO> CameraHistorySystem = new Extensions.CameraHistorySystem.CameraHistorySystem<HistoryCameraDataRTAO>();

		// Textures
		internal static Wrappers.RTWrapper OcclusionRTAO               = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionHalfRateRTAO       = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionReprojectedRTAO    = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper TemporalDataReprojectedRTAO = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper ShadingRateRTAO             = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper RayMarchingDepthRTAO        = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionNormalRTAO         = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionNormalFilteredRTAO = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper DebugRTAO                   = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionShaderGraphRTAO    = new Wrappers.RTWrapper();
			
		// Buffers
		internal static GraphicsBuffer          IndirectArgumentsRTAO;
		internal static Wrappers.HDynamicBuffer IndirectCoordsRTAO;
		internal static ComputeBuffer           RayCounterRTAO;
		internal static ComputeBuffer           DisocclusionCounterRTAO;
		internal static ComputeBuffer           PointDistributionBufferRTAO;
		
		// Variables
		internal static Vector4 renderScaleFactorPrevious = Vector4.one;
		
#if UNITY_2023_2_OR_NEWER
		private static RayTracingInstanceCullingConfig _cullingConfig;
		private static RayTracingInstanceCullingTest   _instanceTest;
		private static RayTracingInstanceCullingTest[] _instanceTests = new RayTracingInstanceCullingTest[1];
#endif

		internal static void SetupRTAS(Camera camera, int cameraHeight)
		{
#if UNITY_2023_2_OR_NEWER
			_cullingConfig = new RayTracingInstanceCullingConfig();
			_instanceTest  = new RayTracingInstanceCullingTest();

			if (HTraceAOSettings.ActiveProfile.RTAOSettings.CullingMode == Globals.CullingMode.Sphere)
			{
				_cullingConfig.flags        = RayTracingInstanceCullingFlags.EnableSphereCulling;
				_cullingConfig.sphereRadius = HTraceAOSettings.ActiveProfile.RTAOSettings.CullingDistance;
				_cullingConfig.sphereCenter = camera.transform.position;
			}
			else if (HTraceAOSettings.ActiveProfile.RTAOSettings.CullingMode == Globals.CullingMode.SolidAngle)
			{
				_cullingConfig.flags = RayTracingInstanceCullingFlags.EnableSolidAngleCulling;
				_cullingConfig.minSolidAngle = HTraceAOSettings.ActiveProfile.RTAOSettings.MinSolidAngle;
			}
#if UNITY_6000_2_OR_NEWER
			_cullingConfig.flags                                   |= RayTracingInstanceCullingFlags.EnableMeshLOD;
#endif
			_cullingConfig.flags                                   |= RayTracingInstanceCullingFlags.EnableLODCulling | RayTracingInstanceCullingFlags.IgnoreReflectionProbes;
			_cullingConfig.subMeshFlagsConfig.opaqueMaterials      =  RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
			_cullingConfig.subMeshFlagsConfig.alphaTestedMaterials =  RayTracingSubMeshFlags.Enabled;
			
			_cullingConfig.subMeshFlagsConfig.transparentMaterials          = RayTracingSubMeshFlags.Disabled;
			_cullingConfig.transparentMaterialConfig.renderQueueLowerBound = (int)RenderQueue.Transparent;
			_cullingConfig.transparentMaterialConfig.renderQueueUpperBound  = (int)RenderQueue.Overlay;

			_cullingConfig.lodParameters.fieldOfView       = camera.fieldOfView;
			_cullingConfig.lodParameters.cameraPixelHeight = camera.pixelHeight;
			_cullingConfig.lodParameters.cameraPosition    = camera.transform.position;
			_cullingConfig.lodParameters.isOrthographic    = false;
			
			_instanceTest.allowOpaqueMaterials      = true;
			_instanceTest.allowAlphaTestedMaterials = true;
			_instanceTest.layerMask                 = HTraceAOSettings.ActiveProfile.RTAOSettings.CullingMask;
			_instanceTest.shadowCastingModeMask     = (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided | (1 << (int)ShadowCastingMode.Off));
			_instanceTest.instanceMask              = 255;

			if (HTraceAOSettings.ActiveProfile.RTAOSettings.CullAlphaCutout)
			{
				_cullingConfig.alphaTestedMaterialConfig.renderQueueLowerBound = (int)RenderQueue.AlphaTest;
				_cullingConfig.alphaTestedMaterialConfig.renderQueueUpperBound = (int)RenderQueue.GeometryLast;
				_cullingConfig.subMeshFlagsConfig.alphaTestedMaterials         = RayTracingSubMeshFlags.Disabled;
				_instanceTest.allowAlphaTestedMaterials                        = false;
			}

			_instanceTests[0] = _instanceTest;
			_cullingConfig.instanceTests = _instanceTests;

			RTAS?.ClearInstances();
			RayTracingInstanceCullingResults? cullingResults = RTAS?.CullInstances(ref _cullingConfig); //this call has 32B GC, but it's Unity side 
#endif
		}

		private static void KeywordSwitch(CommandBuffer cmd, string Keyword, bool State)
		{
			if (State) cmd.EnableShaderKeyword(Keyword);
			else cmd.DisableShaderKeyword(Keyword);
		}

		public static void Execute( CommandBuffer cmd, Camera camera, int cameraWidth, int cameraHeight, RenderTexture cameraColorBuffer = null)
		{
#pragma warning disable CS0219
			// General bools
			bool useInlineRaytracing             = HTraceAOSettings.ActiveProfile.RTAOSettings.TracingMode != Globals.TracingMode.NativeRayTracing;
			bool halfResolution                  = HTraceAOSettings.ActiveProfile.RTAOSettings.Resolution == Globals.ResolutionMode.Half;
			bool quarterResolution               = HTraceAOSettings.ActiveProfile.RTAOSettings.Resolution == Globals.ResolutionMode.Quarter;
			bool adaptiveResolution              = HTraceAOSettings.ActiveProfile.RTAOSettings.Resolution == Globals.ResolutionMode.Adaptive;
			bool disocclusionBoost               = HTraceAOSettings.ActiveProfile.RTAOSettings.DisocclusionBoost && HTraceAOSettings.ActiveProfile.RTAOSettings.RayCount < 4;
			bool contactOcclusion                = !Mathf.Approximately(HTraceAOSettings.ActiveProfile.RTAOSettings.ContactOcclusion, 0);
			bool requiresOutputComposition       = false;
			bool requiresShaderGraphOutput       = false;
			bool requiresAfterOpaqueWriting      = false;
			bool requiresBeforeLightingInjection = true;
			int  canExcludeObjectsFromDepth      = 1;
#pragma warning restore CS0219

			requiresShaderGraphOutput       = HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint == URPInjectionPoint.BeforeOpaque;
			requiresAfterOpaqueWriting      = HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint == URPInjectionPoint.AfterOpaque;
			requiresOutputComposition       = HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode == DebugModeRTAO.None;
			cmd.SetGlobalInt(HShaderParams.ForceNonOctahedralNormals, HTraceAOSettings.ActiveProfile.GeneralSettings.ReconstructNormals ? 1 : 0);
			
				
			// TODO: are we sure we can't do it in Scene View as well ?
			if (IndirectCoordsRTAO.ComputeBuffer == null || camera.cameraType == CameraType.SceneView)
			{
				adaptiveResolution = false;
				disocclusionBoost  = false;
			}

			Vector2 renderScaleFactor = new Vector2(1, 1);
			if (quarterResolution) renderScaleFactor = new Vector2(2, 2);
			if (halfResolution) renderScaleFactor    = new Vector2(2, 1);

			// Parameters for fast computation of view space position
			float fovRadians = camera.fieldOfView * Mathf.Deg2Rad;
			float tanHalfFOVY = Mathf.Tan(fovRadians * 0.5f);
			float invHalfTanFov = 1.0f / tanHalfFOVY;
			Vector2 focalLen = new Vector2(invHalfTanFov * ((float)cameraHeight / (float)cameraWidth), invHalfTanFov);
			Vector2 invFocalLen = new Vector2(1 / focalLen.x, 1 / focalLen.y);
			Vector4 depthToViewParams = new Vector4(2 * invFocalLen.x, 2 * invFocalLen.y, -1 * invFocalLen.x, -1 * invFocalLen.y);

			// Screen Space horizon marching radius
			float tanHalfFOVX = tanHalfFOVY * ((float)cameraWidth / (float)cameraHeight);
			Vector2 cameraTanHalfFOV = new Vector2(tanHalfFOVX, tanHalfFOVY);
			Vector2 viewMul = new Vector2(cameraTanHalfFOV.x * 2.0f, cameraTanHalfFOV.y * -2.0f);
			Vector2 radiusViewMultiplier = new Vector2(viewMul.x * (1.0f / (float)cameraWidth), viewMul.y * (1.0f / cameraHeight));

			// Screen space ray marching thickness
			float   thickness          = Mathf.Clamp(HTraceAOSettings.ActiveProfile.RTAOSettings.PretraceThickness, 0.05f, 1.0f);
			float   n                  = camera.nearClipPlane;
			float   f                  = camera.farClipPlane;
			Vector2 thicknessScaleBias = new Vector4();
			thicknessScaleBias.x = 1.0f / (1.0f + thickness / 10.0f);
			thicknessScaleBias.y = -n / (f - n) * (thickness / 10.0f * thicknessScaleBias.x);

			// Threshold multiplier for depth comparison filters. Based on fov and resolution
			float pixelSpreadBaseline = 2.0f * Mathf.Tan(60.0f * Mathf.Deg2Rad * 0.5f) / 1080.0f;
			float pixelSpreadActual = 2.0f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) / (cameraHeight / renderScaleFactor.y);
			float pixelSpreadMultiplier = pixelSpreadActual / pixelSpreadBaseline;
			
			// Spacial filtering radii
			float filterRadiusPass1 = HTraceAOSettings.ActiveProfile.RTAOSettings.FilterRadius;
			float filterRadiusPass2 = HTraceAOSettings.ActiveProfile.RTAOSettings.FilterRadius;
			if (HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising == Globals.RTAODenoisingMode.DoublePass)
			{
				float spreadFactor = 0.25f;	
				float delta = HTraceAOSettings.ActiveProfile.RTAOSettings.FilterRadius * spreadFactor;
				filterRadiusPass1 += delta;
				filterRadiusPass2 -= delta;
			}
			
			if (HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising == Globals.RTAODenoisingMode.None)
			{	
				renderScaleFactor  = new Vector2(1, 1);
				adaptiveResolution = false;
				quarterResolution  = false;
				halfResolution     = false;
				disocclusionBoost  = false;
				contactOcclusion   = false;
			}

			if (HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode == Globals.DebugModeRTAO.HardwareRayTracingScene)
			{		
				renderScaleFactor  = new Vector2(1, 1);
				adaptiveResolution = false;
				quarterResolution  = false; 
				halfResolution     = false;
			}
			
			bool requiresUpsacaling = halfResolution | quarterResolution;
			bool contactOcclusionDenoise = HTraceAOSettings.ActiveProfile.RTAOSettings.ContactOcclusionDenoise && !requiresUpsacaling && HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising == Globals.RTAODenoisingMode.DoublePass;


			var dispatchResolutionScaled8_X = Mathf.CeilToInt(cameraWidth / renderScaleFactor.x / 8.0f);
			var dispatchResolutionScaled8_Y = Mathf.CeilToInt(cameraHeight / renderScaleFactor.y / 8.0f);
			var dispatchResolutionFull16_X = Mathf.CeilToInt(cameraWidth / renderScaleFactor.x / 16.0f);
			var dispatchResolutionFull16_Y = Mathf.CeilToInt(cameraHeight / renderScaleFactor.y / 16.0f);
			var dispatchResolutionFull8_X = Mathf.CeilToInt(cameraWidth / 8.0f);
			var dispatchResolutionFull8_Y = Mathf.CeilToInt(cameraHeight / 8.0f);
			var dispatchResolutionHalf8_X = Mathf.CeilToInt(cameraWidth / 2.0f / 8.0f);
			var dispatchResolutionHalf8_Y = Mathf.CeilToInt(cameraHeight / 2.0f / 8.0f);
			
			
			// ---------------------------------------- PARAMETERS SET ---------------------------------------- //
			
			// Global parameters
			cmd.SetGlobalVector(Globals.HShaderParams.HScaleFactorAO, renderScaleFactor);
			cmd.SetGlobalVector(Globals.HShaderParams.HPreviousScaleFactorAO, renderScaleFactorPrevious); renderScaleFactorPrevious = renderScaleFactor;
			cmd.SetGlobalVector(Globals.HShaderParams.g_HDepthToViewParamsAO, depthToViewParams);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HTemporalDynamicRejectionAO, HTraceAOSettings.ActiveProfile.RTAOSettings.DynamicRejection);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HTemporalStaticRejectionAO, HTraceAOSettings.ActiveProfile.RTAOSettings.StaticRejection);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HIntensityAO, HTraceAOSettings.ActiveProfile.GeneralSettings.Intensity);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HUseDithering, HTraceAOSettings.ActiveProfile.GeneralSettings.OutputDithering ? 1.0f : 0.0f);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HAnimateDithering, HTraceAOSettings.ActiveProfile.GeneralSettings.OutputDitheringAnimate ? 1.0f : 0.0f);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HPixelSpreadMultiplier, pixelSpreadMultiplier);
			cmd.SetGlobalInt(Globals.HShaderParams.g_HTemporalSamplecountAO, HTraceAOSettings.ActiveProfile.RTAOSettings.SampleCountTemporal);
			cmd.SetGlobalTexture(Globals.HShaderParams.Debug_Output, DebugRTAO.rt);
			
			
			// Contact Occlusion parameters
			cmd.SetComputeFloatParam(HContactOcclusionRTAO, Globals.HShaderParams.ScreenSpaceRadiusScale, radiusViewMultiplier.x);
			cmd.SetComputeFloatParam(HContactOcclusionRTAO, Globals.HShaderParams.ContactOcclusionIntensity, HTraceAOSettings.ActiveProfile.RTAOSettings.ContactOcclusion);

			// Spatial Filter parameters
			cmd.SetComputeIntParam(HSpatialFilterRTAO, Globals.HShaderParams.ApplyIntensity, requiresOutputComposition ? 1 : 0);
			cmd.SetComputeFloatParam(HSpatialFilterRTAO, Globals.HShaderParams.Fadeout, HTraceAOSettings.ActiveProfile.GeneralSettings.Fadeout);
			cmd.SetComputeFloatParam(HSpatialFilterRTAO, Globals.HShaderParams.MaxRayDistance, HTraceAOSettings.ActiveProfile.RTAOSettings.WorldSpaceRadius);
			cmd.SetComputeIntParam(HSpatialFilterRTAO, Globals.HShaderParams.IsSinglePass, HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising == Globals.RTAODenoisingMode.DoublePass ? 0 : 1);
				
			// Render Layer Mask parameters
#if UNITY_2023_3_OR_NEWER
			bool earlyOutExcludedReceivers = HTraceAOSettings.ActiveProfile.RTAOSettings.EarlyOutExcludedReceivers
			                                 && HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludeReceiving.value != 0
			                                 && Mathf.Approximately(HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludedIntensity, 0.0f);
			
			cmd.SetGlobalInt(Globals.HShaderParams.EarlyOutExcludedReceivers, earlyOutExcludedReceivers ? 1 : 0);
			cmd.SetComputeIntParam(HDepthPyramidAO, Globals.HShaderParams.RayMarchingLayerMask, (int)HTraceAOSettings.ActiveProfile.RTAOSettings.PretraceMask.value);
			cmd.SetComputeIntParam(HDepthPyramidAO, Globals.HShaderParams.ExcludeObjectsFromDepth, (int)HTraceAOSettings.ActiveProfile.RTAOSettings.PretraceMask.value != -1 ? canExcludeObjectsFromDepth : 0);
			cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludeReceiving);
			cmd.SetComputeIntParam(HTemporalFilterRTAO, Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludeReceiving);
			cmd.SetComputeIntParam(HSpatialFilterRTAO, Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludeReceiving);
			cmd.SetComputeFloatParam(HSpatialFilterRTAO, Globals.HShaderParams.ExcludedIntensity, HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludedIntensity);
			cmd.SetComputeIntParam(HThreadCompactionRTAO, Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludeReceiving);
			cmd.SetComputeIntParam(HContactOcclusionRTAO, Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludeReceiving);
#endif

			// Thread Compaction parameters
			cmd.SetComputeIntParam(HThreadCompactionRTAO, Globals.HShaderParams.UseDisocclusionBoost, disocclusionBoost ? 1 : 0);
			cmd.SetComputeIntParam(HThreadCompactionRTAO, Globals.HShaderParams.UseAdaptiveResolution, adaptiveResolution ? 1 : 0);
			cmd.SetComputeIntParam(HThreadCompactionRTAO, Globals.HShaderParams.RayTracedCounter, !useInlineRaytracing ? 1 : 0);

			// Inline Ray Tracing parameters
			cmd.SetComputeVectorParam(HRenderRTAO, Globals.HShaderParams.RayMarchingThicknessParams, thicknessScaleBias);
			cmd.SetComputeFloatParam(HRenderRTAO, Globals.HShaderParams.MaxRayBias, HTraceAOSettings.ActiveProfile.RTAOSettings.MaxRayBias);
			cmd.SetComputeFloatParam(HRenderRTAO, Globals.HShaderParams.MaxRayDistance, HTraceAOSettings.ActiveProfile.RTAOSettings.WorldSpaceRadius);
			cmd.SetComputeFloatParam(HRenderRTAO, Globals.HShaderParams.RayMarchingDistance, HTraceAOSettings.ActiveProfile.RTAOSettings.PretraceDistance);
			cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.RaySampleCount, HTraceAOSettings.ActiveProfile.RTAOSettings.RayCount);
			cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.IsFinalPass, HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising == Globals.RTAODenoisingMode.None ? 1 : 0);
			cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.DebugSceneMismatchRTAS, HTraceAOSettings.ActiveProfile.RTAOSettings.DebugSceneMismatch ? 1 : 0);
			cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.RayMarchingSamplecount, HTraceAOSettings.ActiveProfile.RTAOSettings.PretraceSamples);
			cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.JitterCoordForHalfRes, quarterResolution ? 1 : 0);

			// Native Ray Tracing parameters
			
			KeywordSwitch(cmd, VR_COMPATIBILITY, Globals.HRenderer.TextureXrSlices > 1);
			
			
			
			// ---------------------------------------- TEMPORAL REPROJECTION ---------------------------------------- //
			if (HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising != Globals.RTAODenoisingMode.None)
			{
				using (new ProfilingScope(cmd, TemporalFilteringSampler))
				{
					KeywordSwitch(cmd, BICUBIC_REPROJECTION, HTraceAOSettings.ActiveProfile.RTAOSettings.ReprojectionFilter == Globals.ReprojectionFilter.Bicubic);
					KeywordSwitch(cmd, USE_NORMAL_WEIGHTING, HTraceAOSettings.ActiveProfile.RTAOSettings.ReprojectionFilter != Globals.ReprojectionFilter.Bicubic && HTraceAOSettings.ActiveProfile.RTAOSettings.UseNormalWeightingTemporal);

					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalReprojection, Globals.HShaderParams.Occlusion_Output, OcclusionReprojectedRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalReprojection, Globals.HShaderParams.TemporalData_Output, TemporalDataReprojectedRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalReprojection, Globals.HShaderParams.DepthHistory, CameraHistorySystem.GetCameraData().FilterDepthRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalReprojection, Globals.HShaderParams.VelocityHistory, CameraHistorySystem.GetCameraData().VelocityHistoryRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalReprojection, Globals.HShaderParams.Occlusion_History, CameraHistorySystem.GetCameraData().OcclusionHistoryRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalReprojection, Globals.HShaderParams.SamplecountNormalHistory, CameraHistorySystem.GetCameraData().SamplecountNormalHistoryRTAO.rt);
					cmd.DispatchCompute(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalReprojection, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
				}	
			}
		
			
			// ---------------------------------------- BUFFER CLEANUP ---------------------------------------- //
			using (new ProfilingScope(cmd, BufferCleanupSampler))
			{
				// Don't change the clear order, it's important for URP
				CoreUtils.SetRenderTarget(cmd, CameraHistorySystem.GetCameraData().FilterDepthRTAO.rt, ClearFlag.Color, 0, CubemapFace.Unknown, -1);
				CoreUtils.SetRenderTarget(cmd, OcclusionNormalFilteredRTAO.rt, ClearFlag.Color, 0, CubemapFace.Unknown, -1);
				CoreUtils.SetRenderTarget(cmd, OcclusionNormalRTAO.rt, ClearFlag.Color, 0, CubemapFace.Unknown, -1);
			}
			
			
			// ---------------------------------------- THREAD COMPACTION ---------------------------------------- //
			if (adaptiveResolution || disocclusionBoost)
			{
				using (new ProfilingScope(cmd, ThreadCompactionSampler))
				{
					KeywordSwitch(cmd, SHADING_RATE_VISUALIZATION, HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode == Globals.DebugModeRTAO.ShadingRateVisualization);
					KeywordSwitch(cmd, DISOCCLUSION_BOOST, disocclusionBoost || HTraceAOSettings.ActiveProfile.RTAOSettings.SampleRebalancing);

					if (adaptiveResolution)
					{
						cmd.SetComputeTextureParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.ShadingRateGeneration, Globals.HShaderParams.ShadingRate_Output, ShadingRateRTAO.rt);
						cmd.DispatchCompute(HThreadCompactionRTAO, (int)HThreadCompactionKernel.ShadingRateGeneration, dispatchResolutionHalf8_X, dispatchResolutionHalf8_Y, Globals.HRenderer.TextureXrSlices);
					}
					
					cmd.SetComputeTextureParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.ShadingRateClassification, Globals.HShaderParams.ShadingRate, ShadingRateRTAO.rt);
					cmd.SetComputeTextureParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.ShadingRateClassification, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedRTAO.rt);
					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.ShadingRateClassification, Globals.HShaderParams.RayCounter, RayCounterRTAO);
					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.ShadingRateClassification, Globals.HShaderParams.DisocclusionCounter, DisocclusionCounterRTAO);
					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.ShadingRateClassification, Globals.HShaderParams.IndirectCoords_Output, IndirectCoordsRTAO.ComputeBuffer);
					cmd.DispatchCompute(HThreadCompactionRTAO, (int)HThreadCompactionKernel.ShadingRateClassification, dispatchResolutionFull16_X, dispatchResolutionFull16_Y, Globals.HRenderer.TextureXrSlices);

					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.IndirectArguments, Globals.HShaderParams.RayCounter, RayCounterRTAO);
					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.IndirectArguments, Globals.HShaderParams.DisocclusionCounter, DisocclusionCounterRTAO);
					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.IndirectArguments, Globals.HShaderParams.IndirectArguments_Output, IndirectArgumentsRTAO);
					cmd.DispatchCompute(HThreadCompactionRTAO, (int)HThreadCompactionKernel.IndirectArguments, 1, 1, Globals.HRenderer.TextureXrSlices);
				}
			}
			
			
			// ---------------------------------------- RAY MARCHING DEPTH GENERATION ---------------------------------------- //
			if (HTraceAOSettings.ActiveProfile.RTAOSettings.ScreenSpacePretrace || contactOcclusion)
			{
				using (new ProfilingScope(cmd, RayMarchingDepthGenerationSampler))
				{
					cmd.SetComputeTextureParam(HDepthPyramidAO, (int)HDepthPyramidAOKernel.GenerateRayMarchingDepth, Globals.HShaderParams.RayMarchingDepth_Output, RayMarchingDepthRTAO.rt);
					cmd.DispatchCompute(HDepthPyramidAO, (int)HDepthPyramidAOKernel.GenerateRayMarchingDepth, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);
				}
			}

			
			// ---------------------------------------- RTAS BUILD (INLINE RAYTRACING) ---------------------------------------- //
			if (useInlineRaytracing)
			{
				#if UNITY_2023_2_OR_NEWER
					using (new ProfilingScope(cmd, RTASBuildSampler))
					{
						SetupRTAS(camera, cameraHeight);
						cmd.BuildRayTracingAccelerationStructure(RTAS);
					}
				#endif
			}

			
			// ---------------------------------------- OCCLUSION TRACING ---------------------------------------- //
			using (new ProfilingScope(cmd, OcclusionTracingSampler))
			{
				if (!useInlineRaytracing)
				{
				}
				else
				{
					#if UNITY_2023_2_OR_NEWER
						KeywordSwitch(cmd, SCREEN_SPACE_PRETRACE, HTraceAOSettings.ActiveProfile.RTAOSettings.ScreenSpacePretrace && (int)HTraceAOSettings.ActiveProfile.RTAOSettings.PretraceMask.value == -1);
						KeywordSwitch(cmd, SCREEN_SPACE_PRETRACE_MASKED, HTraceAOSettings.ActiveProfile.RTAOSettings.ScreenSpacePretrace && (int)HTraceAOSettings.ActiveProfile.RTAOSettings.PretraceMask.value != -1);
						KeywordSwitch(cmd, ADAPTIVE_RESOLUTION, adaptiveResolution);
						KeywordSwitch(cmd, DISOCCLUSION_BOOST, false);
						KeywordSwitch(cmd, CULL_BACK_FACES, HTraceAOSettings.ActiveProfile.RTAOSettings.CullBackfaces);
						KeywordSwitch(cmd, DEBUG_RTAS, HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode == Globals.DebugModeRTAO.HardwareRayTracingScene);
						KeywordSwitch(cmd, SAMPLE_REBALANCING, HTraceAOSettings.ActiveProfile.RTAOSettings.SampleRebalancing);

						cmd.SetComputeTextureParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.Debug_Output, DebugRTAO.rt);
						cmd.SetComputeTextureParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.RayMarchingDepth, RayMarchingDepthRTAO.rt);
						cmd.SetComputeTextureParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedRTAO.rt);
						cmd.SetComputeTextureParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.Occlusion_Output, OcclusionRTAO.rt);
						cmd.SetComputeTextureParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.OcclusionHalfRate_Output, OcclusionHalfRateRTAO.rt);
						cmd.SetComputeBufferParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.RayCounter, RayCounterRTAO);
						cmd.SetComputeBufferParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.DisocclusionCounter, DisocclusionCounterRTAO);
						cmd.SetComputeBufferParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.TracingCoords, IndirectCoordsRTAO.ComputeBuffer);
						cmd.SetRayTracingAccelerationStructure(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.RTAS, RTAS);

						if (adaptiveResolution)
						{
							cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.IndexXR, 0);
							cmd.DispatchCompute(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, IndirectArgumentsRTAO, 0);

							if (Globals.HRenderer.TextureXrSlices > 1)
							{
								cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.IndexXR, 1);
								cmd.DispatchCompute(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, IndirectArgumentsRTAO, sizeof(uint) * 3);
							}
						}
						else
						{
							cmd.DispatchCompute(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
						}
					#endif
				}
			}


			// ---------------------------------------- DISOCCLUSION BOOST ---------------------------------------- //
			if (disocclusionBoost)
			{
				using (new ProfilingScope(cmd, DisocclusionBoostSampler))
				{	
					KeywordSwitch(cmd, ADAPTIVE_RESOLUTION, false);
					KeywordSwitch(cmd, DISOCCLUSION_BOOST, true);
					KeywordSwitch(cmd, SAMPLE_REBALANCING, false);
					
					cmd.SetComputeTextureParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.DisocclusionClassification, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedRTAO.rt);
					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.DisocclusionClassification, Globals.HShaderParams.RayCounter, RayCounterRTAO);
					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.DisocclusionClassification, Globals.HShaderParams.IndirectCoords_Output, IndirectCoordsRTAO.ComputeBuffer);
					cmd.DispatchCompute(HThreadCompactionRTAO, (int)HThreadCompactionKernel.DisocclusionClassification, dispatchResolutionFull16_X, dispatchResolutionFull16_Y, Globals.HRenderer.TextureXrSlices);
					
					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.IndirectArguments, Globals.HShaderParams.RayCounter, RayCounterRTAO);
					cmd.SetComputeBufferParam(HThreadCompactionRTAO, (int)HThreadCompactionKernel.IndirectArguments, Globals.HShaderParams.IndirectArguments_Output, IndirectArgumentsRTAO);
					cmd.DispatchCompute(HThreadCompactionRTAO, (int)HThreadCompactionKernel.IndirectArguments, 1, 1, Globals.HRenderer.TextureXrSlices);
				
					if (!useInlineRaytracing)
					{
					}
					else
					{
					#if UNITY_2023_2_OR_NEWER
						cmd.SetComputeBufferParam(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, Globals.HShaderParams.RayCounter, RayCounterRTAO);
						cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.IndexXR, 0);
						cmd.DispatchCompute(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, IndirectArgumentsRTAO, 0);

						if (Globals.HRenderer.TextureXrSlices > 1)
						{
							cmd.SetComputeIntParam(HRenderRTAO, Globals.HShaderParams.IndexXR, 1);
							cmd.DispatchCompute(HRenderRTAO, (int)HRenderRTAOKernel.RenderRTAO, IndirectArgumentsRTAO, sizeof(uint) * 3);
						}
					#endif
					}
				}	
			}

			var FinalOutput = OcclusionRTAO.rt;


			if (HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising != Globals.RTAODenoisingMode.None)
			{
				
				// ---------------------------------------- TEMPORAL ACCUMULATION 1 PASS ---------------------------------------- //
				using (new ProfilingScope(cmd, TemporalFilteringSampler))
				{
					KeywordSwitch(cmd, ADAPTIVE_RESOLUTION, adaptiveResolution);
					
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.ShadingRate, ShadingRateRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.Occlusion, OcclusionRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.OcclusionHalfRate, OcclusionHalfRateRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.OcclusionReprojected, OcclusionReprojectedRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.Depth_Output, CameraHistorySystem.GetCameraData().FilterDepthRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.VelocityHistory_Output, CameraHistorySystem.GetCameraData().VelocityHistoryRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.OcclusionHistory_Output, CameraHistorySystem.GetCameraData().OcclusionHistoryRTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, Globals.HShaderParams.SamplecountNormalHistory_Output, CameraHistorySystem.GetCameraData().SamplecountNormalHistoryRTAO.rt);
					cmd.DispatchCompute(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalAccumulation, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
				}
				
			
				// ---------------------------------------- SPATIAL FILTER 1 PASS ---------------------------------------- //
				using (new ProfilingScope(cmd, SpatialFilteringSampler))
				{
					KeywordSwitch(cmd, FIRST_PASS, true);

					cmd.SetComputeBufferParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.PointDistributionFill, Globals.HShaderParams.PointDistribution_Output, PointDistributionBufferRTAO);
					cmd.DispatchCompute(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.PointDistributionFill, 1, 1, 1);

					cmd.SetComputeFloatParam(HSpatialFilterRTAO, Globals.HShaderParams.FilterRadius, filterRadiusPass1);
					cmd.SetComputeIntParam(HSpatialFilterRTAO, Globals.HShaderParams.IsFinalPass, (requiresUpsacaling || HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising == Globals.RTAODenoisingMode.DoublePass) ? 0 : 1);
					cmd.SetComputeIntParam(HSpatialFilterRTAO, Globals.HShaderParams.IsBeforeInterpolationPass, (requiresUpsacaling && HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising == Globals.RTAODenoisingMode.SinglePass) ? 1 : 0);

					cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.SpatialFiltering, Globals.HShaderParams.OcclusionNormal, OcclusionNormalRTAO.rt);
					cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.SpatialFiltering, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalFilteredRTAO.rt);
					cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.SpatialFiltering, Globals.HShaderParams.Depth, CameraHistorySystem.GetCameraData().FilterDepthRTAO.rt);
					cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.SpatialFiltering, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedRTAO.rt);
					cmd.SetComputeBufferParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.SpatialFiltering, Globals.HShaderParams.PointDistribution, PointDistributionBufferRTAO);
					cmd.DispatchCompute(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.SpatialFiltering, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
					
					FinalOutput = OcclusionNormalFilteredRTAO.rt;
				}

				
				// ---------------------------------------- CONTACT OCCLUSION (DENOISING ON) ---------------------------------------- //
				if (contactOcclusion && contactOcclusionDenoise)
				{
					using (new ProfilingScope(cmd, ContactOcclusionSampler))
					{
						KeywordSwitch(cmd, FINAL_PASS, false);

						cmd.SetComputeTextureParam(HContactOcclusionRTAO, (int)HContactOcclusionRTAOKernel.ContactOcclusion, Globals.HShaderParams.RayMarchingDepth, RayMarchingDepthRTAO.rt);
						cmd.SetComputeTextureParam(HContactOcclusionRTAO, (int)HContactOcclusionRTAOKernel.ContactOcclusion, Globals.HShaderParams.Occlusion_Output, OcclusionNormalFilteredRTAO.rt);
						cmd.DispatchCompute(HContactOcclusionRTAO, (int)HContactOcclusionRTAOKernel.ContactOcclusion, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
					}
				}
				
				
				if (HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising == Globals.RTAODenoisingMode.DoublePass)
				{
					// ---------------------------------------- TEMPORAL ACCUMULATION 2 PASS ---------------------------------------- //
					using (new ProfilingScope(cmd, TemporalFilteringSampler))
					{
						cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalStabilization, Globals.HShaderParams.OcclusionNormal, OcclusionNormalFilteredRTAO.rt);
						cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalStabilization, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalRTAO.rt);
						cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalStabilization, Globals.HShaderParams.OcclusionReprojected, OcclusionReprojectedRTAO.rt);
						cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalStabilization, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedRTAO.rt);
						cmd.SetComputeTextureParam(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalStabilization, Globals.HShaderParams.OcclusionHistory_Output, CameraHistorySystem.GetCameraData().OcclusionHistoryRTAO.rt);

						cmd.DispatchCompute(HTemporalFilterRTAO, (int)HTemporalFilterRTAOKernel.TemporalStabilization, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
					}


					// ---------------------------------------- SPATIAL FILTER 2 PASS ---------------------------------------- //
					using (new ProfilingScope(cmd, SpatialFilteringSampler))
					{
						KeywordSwitch(cmd, FIRST_PASS, false);

						cmd.SetComputeFloatParam(HSpatialFilterRTAO, Globals.HShaderParams.FilterRadius, filterRadiusPass2);
						cmd.SetComputeIntParam(HSpatialFilterRTAO, Globals.HShaderParams.IsFinalPass, requiresUpsacaling ? 0 : 1);
						cmd.SetComputeIntParam(HSpatialFilterRTAO, Globals.HShaderParams.IsBeforeInterpolationPass, requiresUpsacaling ? 1 : 0);
						
						cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.SpatialFiltering, Globals.HShaderParams.OcclusionNormal, OcclusionNormalRTAO.rt);
						cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.SpatialFiltering, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalFilteredRTAO.rt);
						cmd.DispatchCompute(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.SpatialFiltering, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
						
						FinalOutput = OcclusionNormalFilteredRTAO.rt;
					}
				}
				
				
				// ---------------------------------------- INTERPOLATION ---------------------------------------- //
				if (requiresUpsacaling)
				{
					using (new ProfilingScope(cmd, InterpolationSampler))
					{	
						KeywordSwitch(cmd, HALF_RESOLUTION, halfResolution);
						
						cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.Interpolation, Globals.HShaderParams.OcclusionNormal, FinalOutput);
						cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.Interpolation, Globals.HShaderParams.Occlusion_Output, OcclusionRTAO.rt);
						cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.Interpolation, Globals.HShaderParams.Depth, CameraHistorySystem.GetCameraData().FilterDepthRTAO.rt);
						cmd.DispatchCompute(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.Interpolation, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);

						FinalOutput = OcclusionRTAO.rt;
					}
				}
			
				
				// ------------------------------------- CONTACT OCCLUSION (DENOISING OFF) -------------------------------------- //
				if (contactOcclusion && !contactOcclusionDenoise)
				{
					using (new ProfilingScope(cmd, ContactOcclusionSampler))
					{	
						KeywordSwitch(cmd, FINAL_PASS, true);
						
						cmd.SetComputeTextureParam(HContactOcclusionRTAO, (int)HContactOcclusionRTAOKernel.ContactOcclusion, Globals.HShaderParams.RayMarchingDepth, RayMarchingDepthRTAO.rt);
						cmd.SetComputeTextureParam(HContactOcclusionRTAO, (int)HContactOcclusionRTAOKernel.ContactOcclusion, Globals.HShaderParams.Occlusion_Output, FinalOutput);
						cmd.DispatchCompute(HContactOcclusionRTAO, (int)HContactOcclusionRTAOKernel.ContactOcclusion, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);
					}
				}
			}
			
			
			// ---------------------------------------- OUTPUT COMPOSITION ---------------------------------------- //
			using (new ProfilingScope(cmd, OutputCompositionSampler))
			{
				if (requiresAfterOpaqueWriting)
				{
					CoreUtils.SetRenderTarget(cmd, cameraColorBuffer);
					OutputCompositionRTAO.SetTexture(Globals.HShaderParams.Occlusion_Output, FinalOutput);
					OutputCompositionRTAO.SetFloat(Globals.HShaderParams.Fadeout, HTraceAOSettings.ActiveProfile.GeneralSettings.Fadeout);
					OutputCompositionRTAO.SetFloat(Globals.HShaderParams.ExcludedIntensity, HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludedIntensity);
				#if UNITY_2023_3_OR_NEWER
					OutputCompositionRTAO.SetInt(Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.RTAOSettings.ExcludeReceiving);
				#endif
					cmd.DrawProcedural(Matrix4x4.identity, OutputCompositionRTAO, 0, MeshTopology.Triangles, 3, 1);
				}
				else
				{
					cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.OutputComposition, Globals.HShaderParams.Occlusion_Output, FinalOutput);
					cmd.SetComputeTextureParam(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.OutputComposition, Globals.HShaderParams.Occlusion_ShaderGraphOutput, OcclusionShaderGraphRTAO.rt);
					cmd.DispatchCompute(HSpatialFilterRTAO, (int)HSpatialFilterRTAOKernel.OutputComposition, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);
				}
			}
			
			
			// -------------------------------------------- DEBUG ------------------------------------------ //
			if (HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode == Globals.DebugModeRTAO.ShadingRateVisualization && !adaptiveResolution)
				CoreUtils.SetRenderTarget(cmd, DebugRTAO.rt, ClearFlag.Color, Color.red, 0, CubemapFace.Unknown, -1);

			if (   HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode != Globals.DebugModeRTAO.None
			    && HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode != Globals.DebugModeRTAO.HardwareRayTracingScene
			    && HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode != Globals.DebugModeRTAO.ShadingRateVisualization)
			{
				using (new ProfilingScope(cmd, DebugSampler))
				{		
				
					cmd.SetComputeIntParam(HDebugRTAO, Globals.HShaderParams.IsGTAO, 0);
					cmd.SetComputeIntParam(HDebugRTAO, Globals.HShaderParams.DebugSwitch, (int)HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode);
					cmd.SetComputeIntParam(HDebugRTAO, Globals.HShaderParams.BuffersSwitch, (int)HTraceAOSettings.ActiveProfile.GeneralSettings.HBuffer);

					cmd.SetComputeTextureParam(HDebugRTAO, 0, Globals.HShaderParams.Debug_Output, DebugRTAO.rt);
					cmd.SetComputeTextureParam(HDebugRTAO, 0, Globals.HShaderParams.DebugDepth, TemporalDataReprojectedRTAO.rt); // Dummy
					cmd.SetComputeTextureParam(HDebugRTAO, 0, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedRTAO.rt);
					cmd.SetComputeTextureParam(HDebugRTAO, 0, Globals.HShaderParams.g_HTraceBufferAO, FinalOutput);
					cmd.DispatchCompute(HDebugRTAO, 0, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);

					cmd.SetGlobalTexture(Globals.HShaderParams.Debug_Output, DebugRTAO.rt);
				}
			}

			cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO, requiresShaderGraphOutput ? OcclusionShaderGraphRTAO.rt : FinalOutput);
			cmd.SetGlobalTexture(HShaderParams.g_ScreenSpaceOcclusionTexture, FinalOutput);
		}
	}
}
