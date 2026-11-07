#if H_BIRP 
using HTraceAO.Rendering.BIRP; 
#endif 
using System;
using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEngine;
using UnityEngine.Rendering;

namespace IPGames.HTraceAO.Runtime.Passes
{
	internal static class GTAO
	{
		// Kernels
		private enum HTemporalFilterGTAOKernel
		{
			TemporalReprojection  = 0,
			TemporalAccumulation  = 1,
		}
		
		private enum HSpatialFilterGTAOKernel
		{
			PointDistributionFill = 0,
			SpatialFilteringDisk  = 1,
			SpatialFilteringBox   = 2,
			OutputComposition     = 3,
			Interpolation         = 4,
		}
		
		private enum HRenderGTAOKernel
		{
			RenderGTAO = 0,
		}

		private enum HDepthPyramidKernel
		{
			GenerateDepthPyramid = 0,
		}

		// Keywords
		private const string VR_COMPATIBILITY            = "VR_COMPATIBILITY";
		private const string BICUBIC_REPROJECTION        = "BICUBIC_REPROJECTION";
		private const string VISIBILITY_BITMASKS         = "VISIBILITY_BITMASKS";
		private const string HALF_RESOLUTION             = "HALF_RESOLUTION";
		private const string DEPTH_NORMAL_OUTPUT         = "DEPTH_NORMAL_OUTPUT";
		private const string ACCURATE_TEMPORAL_REJECTION = "ACCURATE_TEMPORAL_REJECTION";
		private const string USE_NORMAL_WEIGHTING        = "USE_NORMAL_WEIGHTING";
		private const string DOUBLE_SAMPLECOUNT          = "DOUBLE_SAMPLECOUNT";

		// Computes
		internal static ComputeShader HRenderGTAO         = null;
		internal static ComputeShader HDebugGTAO          = null;
		internal static ComputeShader HTemporalFilterGTAO = null;
		internal static ComputeShader HSpatialFilterGTAO  = null;
		internal static ComputeShader HDepthPyramidGTAO   = null;
		
		// Materials
		public static Material OutputCompositionGTAO = null;

		// Profiler Samplers
		internal static ProfilingSampler DepthPyramidGenerationSampler = new ProfilingSampler(Globals.HNames.DEPTH_PYRAMID_GENERATION_SAMPLER);
		internal static ProfilingSampler OcclusionTracingSampler       = new ProfilingSampler(Globals.HNames.OCCLUSION_TRACING_SAMPLER);
		internal static ProfilingSampler TemporalFilteringSampler      = new ProfilingSampler(Globals.HNames.TEMPORAL_FILTERING_SAMPLER);
		internal static ProfilingSampler SpatialFilteringSampler       = new ProfilingSampler(Globals.HNames.SPATIAL_FILTERING_SAMPLER);
		internal static ProfilingSampler InterpolationSampler          = new ProfilingSampler(Globals.HNames.INTERPOLATION_SAMPLER);
		internal static ProfilingSampler OutputCompositionSampler      = new ProfilingSampler(Globals.HNames.OUTPUT_COMPOSITION_SAMPLER);
		internal static ProfilingSampler DebugSampler                  = new ProfilingSampler(Globals.HNames.DEBUG_SAMPLER);
		internal static ProfilingSampler BufferCleanupSampler          = new ProfilingSampler(Globals.HNames.BUFFER_CLEANUP_SAMPLER);

		// History Textures
		internal struct HistoryCameraDataGTAO : Extensions.CameraHistorySystem.ICameraHistoryData, IDisposable
		{
			private int hash;

			public Wrappers.RTWrapper OcclusionVelocityHistoryGTAO;
			public Wrappers.RTWrapper SamplecountNormalHistoryGTAO;
			public Wrappers.RTWrapper FilterDepthGTAO;

			public int GetHash() => hash;
			public void SetHash(int hashIn) => this.hash = hashIn;
			public void Dispose()
			{
				OcclusionVelocityHistoryGTAO?.HRelease();
				SamplecountNormalHistoryGTAO?.HRelease();
				FilterDepthGTAO?.HRelease();
			}
		}

		internal static readonly Extensions.CameraHistorySystem.CameraHistorySystem<HistoryCameraDataGTAO> CameraHistorySystem = new Extensions.CameraHistorySystem.CameraHistorySystem<HistoryCameraDataGTAO>();

		// Textures
		internal static Wrappers.RTWrapper OcclusionGTAO               = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper TemporalDataReprojectedGTAO = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper DepthPyramidGTAO            = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionNormalGTAO         = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionNormalFilteredGTAO = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper DebugGTAO                   = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionShaderGraphGTAO    = new Wrappers.RTWrapper();

		// Buffers
		internal static ComputeBuffer PointDistributionBufferGTAO;
		
		// Variables
		internal static Vector4 renderScaleFactorPrevious = Vector4.one;

		// Methods
		private static void KeywordSwitch(CommandBuffer cmd, string keyword, bool state)
		{
			if (state) cmd.EnableShaderKeyword(keyword);
			else cmd.DisableShaderKeyword(keyword);
		}

		private static void KeywordSwitch(ComputeShader compute, string keyword, bool state)
		{
			if (state) compute.EnableKeyword(keyword);
			else compute.DisableKeyword(keyword);
		}

		internal static bool RequiresMotionDelta
		{
			get
			{
				var settings = HTraceAOSettings.ActiveProfile.GTAOSettings;
				bool requiresTemporalDenoising = settings.Denoising == Globals.GTAODenoisingMode.TemporalOnly
				                                  || settings.Denoising == Globals.GTAODenoisingMode.SpatioTemporal;
				return requiresTemporalDenoising && settings.SampleCountTemporal > 1;
			}
		}

		public static void Execute(CommandBuffer cmd, Camera camera, int cameraWidth, int cameraHeight, RenderTexture cameraColorBuffer = null)
		{
#pragma warning disable CS0219
			// General bools
			bool halfResolution                  = HTraceAOSettings.ActiveProfile.GTAOSettings.Resolution == Globals.GTAOResolutionMode.Half;
			bool quarterResolution               = HTraceAOSettings.ActiveProfile.GTAOSettings.Resolution == Globals.GTAOResolutionMode.Quarter;
			bool requiresUpsacaling              = halfResolution | quarterResolution;
			bool requiresOutputComposition       = false;
			bool requiresAfterOpaqueWriting      = false;
			bool requiresShaderGraphOutput       = false;
			bool requiresNonOctahedralNormals    = false;
			bool requiresBeforeLightingInjection = true;
#pragma warning restore CS0219
			
			requiresShaderGraphOutput       = HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint == URPInjectionPoint.BeforeOpaque;
			requiresAfterOpaqueWriting      = HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint == URPInjectionPoint.AfterOpaque;
			requiresOutputComposition       = HTraceAOSettings.ActiveProfile.GTAOSettings.DebugMode == DebugModeGTAO.None;

			if (HTraceAOSettings.ActiveProfile.GeneralSettings.ReconstructNormals || HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint == URPInjectionPoint.BeforeOpaque)
				requiresNonOctahedralNormals = true;
			
			
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

			// Threshold multiplier for depth comparison filters. Based on fov and resolution
			float pixelSpreadBaseline = 2.0f * Mathf.Tan(60.0f * Mathf.Deg2Rad * 0.5f) / 1080.0f;
			float pixelSpreadActual = 2.0f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) / (cameraHeight / renderScaleFactor.y);
			float pixelSpreadMultiplier = pixelSpreadActual / pixelSpreadBaseline;
			
			// Denoising mode bools
			bool requiresTemporalDenoising = HTraceAOSettings.ActiveProfile.GTAOSettings.Denoising == Globals.GTAODenoisingMode.TemporalOnly || HTraceAOSettings.ActiveProfile.GTAOSettings.Denoising == Globals.GTAODenoisingMode.SpatioTemporal;
			bool requiresSpatialDenoising  = HTraceAOSettings.ActiveProfile.GTAOSettings.Denoising == Globals.GTAODenoisingMode.SpatialOnly  || HTraceAOSettings.ActiveProfile.GTAOSettings.Denoising == Globals.GTAODenoisingMode.SpatioTemporal;
			bool requiresMotionDelta       = RequiresMotionDelta;

			var dispatchResolutionScaled8_X = Mathf.CeilToInt(cameraWidth / renderScaleFactor.x / 8.0f);
			var dispatchResolutionScaled8_Y = Mathf.CeilToInt(cameraHeight / renderScaleFactor.y / 8.0f);
			var dispatchResolutionFull8_X = Mathf.CeilToInt(cameraWidth / 8.0f);
			var dispatchResolutionFull8_Y = Mathf.CeilToInt(cameraHeight / 8.0f);
			var dispatchResolutionHalf8_X = Mathf.CeilToInt(cameraWidth / 2.0f / 8.0f);
			var dispatchResolutionHalf8_Y = Mathf.CeilToInt(cameraHeight / 2.0f / 8.0f);
			
			
			// ---------------------------------------- PARAMETERS SET ---------------------------------------- //
			
			// Global parameters
			cmd.SetGlobalVector(Globals.HShaderParams.HScaleFactorAO, renderScaleFactor);
			cmd.SetGlobalVector(Globals.HShaderParams.HPreviousScaleFactorAO, renderScaleFactorPrevious); renderScaleFactorPrevious = renderScaleFactor;
			cmd.SetGlobalVector(Globals.HShaderParams.g_HDepthToViewParamsAO, depthToViewParams);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HIntensityAO, HTraceAOSettings.ActiveProfile.GeneralSettings.Intensity * 1.2f);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HUseDithering, HTraceAOSettings.ActiveProfile.GeneralSettings.OutputDithering ? 1.0f : 0.0f);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HAnimateDithering, HTraceAOSettings.ActiveProfile.GeneralSettings.OutputDitheringAnimate ? 1.0f : 0.0f);
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HPixelSpreadMultiplier, pixelSpreadMultiplier);
			cmd.SetGlobalInt(Globals.HShaderParams.ForceNonOctahedralNormals, requiresNonOctahedralNormals ? 1 : 0);
			cmd.SetGlobalInt(Globals.HShaderParams.g_HTemporalSamplecountAO, 6);
			cmd.SetGlobalTexture(Globals.HShaderParams.Debug_Output, DebugGTAO.rt);
			
			// Render Layer Mask parameters
#if UNITY_2023_3_OR_NEWER			
			bool earlyOutExcludedReceivers = HTraceAOSettings.ActiveProfile.GTAOSettings.EarlyOutExcludedReceivers
			                                 && HTraceAOSettings.ActiveProfile.GTAOSettings.ExcludeReceiving.value != 0
			                                 && Mathf.Approximately(HTraceAOSettings.ActiveProfile.GTAOSettings.ExcludedIntensity, 0.0f);
			
			cmd.SetGlobalInt(Globals.HShaderParams.EarlyOutExcludedReceivers, earlyOutExcludedReceivers ? 1 : 0);
			cmd.SetComputeIntParam(HRenderGTAO, Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.GTAOSettings.ExcludeReceiving);
			cmd.SetComputeIntParam(HTemporalFilterGTAO, Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.GTAOSettings.ExcludeReceiving);
			cmd.SetComputeIntParam(HDepthPyramidGTAO, Globals.HShaderParams.ExcludeObjectsFromDepth, HTraceAOSettings.ActiveProfile.GTAOSettings.ExcludeCasting);
			cmd.SetComputeIntParam(HSpatialFilterGTAO, Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.GTAOSettings.ExcludeReceiving);
			cmd.SetComputeFloatParam(HSpatialFilterGTAO, Globals.HShaderParams.ExcludedIntensity, HTraceAOSettings.ActiveProfile.GTAOSettings.ExcludedIntensity);
#endif	
			
			// Spatial Filter parameters
			cmd.SetComputeIntParam(HSpatialFilterGTAO, Globals.HShaderParams.ApplyIntensity, requiresOutputComposition ? 1 : 0);
			cmd.SetComputeFloatParam(HSpatialFilterGTAO, Globals.HShaderParams.Fadeout, HTraceAOSettings.ActiveProfile.GeneralSettings.Fadeout);
			cmd.SetComputeFloatParam(HSpatialFilterGTAO, Globals.HShaderParams.FilterRadius, HTraceAOSettings.ActiveProfile.GTAOSettings.FilterRadius); 
			cmd.SetComputeFloatParam(HSpatialFilterGTAO, Globals.HShaderParams.FilterAdaptivity, HTraceAOSettings.ActiveProfile.GTAOSettings.FilterAdaptivity);
			cmd.SetComputeIntParam(HSpatialFilterGTAO, Globals.HShaderParams.UseAccurateTemporalRejection, HTraceAOSettings.ActiveProfile.GTAOSettings.UseSimpleRejection ? 0 : 1);
			
			// Temporal Filter parameters
			cmd.SetComputeFloatParam(HTemporalFilterGTAO, Globals.HShaderParams.TemporalRejection, HTraceAOSettings.ActiveProfile.GTAOSettings.TemporalRejection);
			cmd.SetComputeIntParam(HTemporalFilterGTAO, Globals.HShaderParams.UseAccurateTemporalRejection, HTraceAOSettings.ActiveProfile.GTAOSettings.UseSimpleRejection ? 0 : 1);
				
			// Depth Pyramid parameters
			// Denoising == None has no motion-delta pass. Force the simple path so the depth pyramid does not access the missing texture.
			bool useSimpleTemporalRejection = HTraceAOSettings.ActiveProfile.GTAOSettings.UseSimpleRejection || !requiresMotionDelta;
			cmd.SetComputeIntParam(HDepthPyramidGTAO, Globals.HShaderParams.SimpleTemporalRejection, useSimpleTemporalRejection ? 1 : 0);
			
			// GTAO Render parameters
			cmd.SetComputeFloatParam(HRenderGTAO, Globals.HShaderParams.TemporalRejection, HTraceAOSettings.ActiveProfile.GTAOSettings.TemporalRejection);
			cmd.SetComputeFloatParam(HRenderGTAO, Globals.HShaderParams.WorldSpaceRadius, HTraceAOSettings.ActiveProfile.GTAOSettings.WorldSpaceRadius);
			cmd.SetComputeFloatParam(HRenderGTAO, Globals.HShaderParams.ScreenSpaceRadius, HTraceAOSettings.ActiveProfile.GTAOSettings.ScreenSpaceRadius);
			cmd.SetComputeFloatParam(HRenderGTAO, Globals.HShaderParams.ScreenSpaceRadiusScale, radiusViewMultiplier.x);
			cmd.SetComputeFloatParam(HRenderGTAO, Globals.HShaderParams.Thickness, HTraceAOSettings.ActiveProfile.GTAOSettings.Thickness);
			cmd.SetComputeIntParam(HRenderGTAO, Globals.HShaderParams.StepCount, HTraceAOSettings.ActiveProfile.GTAOSettings.StepCount);
			cmd.SetComputeIntParam(HRenderGTAO, Globals.HShaderParams.SliceCount, HTraceAOSettings.ActiveProfile.GTAOSettings.SliceCount);
			cmd.SetComputeIntParam(HRenderGTAO, Globals.HShaderParams.UseAttenuation, HTraceAOSettings.ActiveProfile.GTAOSettings.UseAttenuation ? 1 : 0);
			cmd.SetComputeIntParam(HRenderGTAO, Globals.HShaderParams.UseLinearThickness, HTraceAOSettings.ActiveProfile.GTAOSettings.ThicknessMode == Globals.GTAOThicknessMode.Linear ? 1 : 0);
			cmd.SetComputeIntParam(HRenderGTAO, Globals.HShaderParams.UseTemporalNoise, requiresTemporalDenoising || HTraceAOSettings.ActiveProfile.GTAOSettings.Denoising == Globals.GTAODenoisingMode.None ? 1 : 0);
	
			KeywordSwitch(cmd, VR_COMPATIBILITY, Globals.HRenderer.TextureXrSlices > 1);
			
			
			
			// ---------------------------------------- TEMPORAL REPROJECTION ---------------------------------------- //
			if (requiresTemporalDenoising)
			{
				using (new ProfilingScope(cmd, TemporalFilteringSampler))
				{
					KeywordSwitch(cmd, BICUBIC_REPROJECTION, HTraceAOSettings.ActiveProfile.GTAOSettings.ReprojectionFilter == Globals.ReprojectionFilter.Bicubic);
					KeywordSwitch(cmd, USE_NORMAL_WEIGHTING, HTraceAOSettings.ActiveProfile.GTAOSettings.ReprojectionFilter != Globals.ReprojectionFilter.Bicubic && HTraceAOSettings.ActiveProfile.GTAOSettings.UseNormalWeightingTemporal);

					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalReprojection, Globals.HShaderParams.TemporalData_Output, TemporalDataReprojectedGTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalReprojection, Globals.HShaderParams.DepthHistory, CameraHistorySystem.GetCameraData().FilterDepthGTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalReprojection, Globals.HShaderParams.SamplecountNormalHistory, CameraHistorySystem.GetCameraData().SamplecountNormalHistoryGTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalReprojection, Globals.HShaderParams.OcclusionVelocityHistory, CameraHistorySystem.GetCameraData().OcclusionVelocityHistoryGTAO.rt);
					cmd.DispatchCompute(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalReprojection, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
				}
			}

			
			// ---------------------------------------- BUFFER CLEANUP ---------------------------------------- //
			using (new ProfilingScope(cmd, BufferCleanupSampler))
			{
				// Don't change the clear order, it's important for URP
				CoreUtils.SetRenderTarget(cmd, CameraHistorySystem.GetCameraData().FilterDepthGTAO.rt, ClearFlag.Color, 0, CubemapFace.Unknown, -1);
				CoreUtils.SetRenderTarget(cmd, OcclusionNormalFilteredGTAO.rt, ClearFlag.Color, 0, CubemapFace.Unknown, -1);
				CoreUtils.SetRenderTarget(cmd, OcclusionNormalGTAO.rt, ClearFlag.Color, 0, CubemapFace.Unknown, -1);
				
				CoreUtils.SetRenderTarget(cmd, DepthPyramidGTAO.rt, ClearFlag.Color, 0, CubemapFace.Unknown, -1);
				CoreUtils.SetRenderTarget(cmd, DepthPyramidGTAO.rt, ClearFlag.Color, 1, CubemapFace.Unknown, -1);
				CoreUtils.SetRenderTarget(cmd, DepthPyramidGTAO.rt, ClearFlag.Color, 2, CubemapFace.Unknown, -1);
				CoreUtils.SetRenderTarget(cmd, DepthPyramidGTAO.rt, ClearFlag.Color, 3, CubemapFace.Unknown, -1);
			}
			
			
			// ---------------------------------------- DEPTH PYRAMID GENERATION ---------------------------------------- //
			using (new ProfilingScope(cmd, DepthPyramidGenerationSampler))
			{
				if (requiresMotionDelta)
				{
					HDepthPyramidGTAO.SetTextureFromGlobal((int)HDepthPyramidKernel.GenerateDepthPyramid, "_MotionDelta_Output", "g_HTraceMotionDelta");
				}
				else
				{
					// Denoising == None leaves g_HTraceMotionDelta uninitialized on a cold start, so bind a valid dummy UAV instead.
					cmd.SetComputeTextureParam(HDepthPyramidGTAO, (int)HDepthPyramidKernel.GenerateDepthPyramid, "_MotionDelta_Output", HRenderer.EmptyTexture);
				}

				cmd.SetComputeTextureParam(HDepthPyramidGTAO, (int)HDepthPyramidKernel.GenerateDepthPyramid, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedGTAO.rt);
				cmd.SetComputeTextureParam(HDepthPyramidGTAO, (int)HDepthPyramidKernel.GenerateDepthPyramid, Globals.HShaderParams.DepthPyramid_OutputMIP0, DepthPyramidGTAO.rt, 0);
				cmd.SetComputeTextureParam(HDepthPyramidGTAO, (int)HDepthPyramidKernel.GenerateDepthPyramid, Globals.HShaderParams.DepthPyramid_OutputMIP1, DepthPyramidGTAO.rt, 1);
				cmd.SetComputeTextureParam(HDepthPyramidGTAO, (int)HDepthPyramidKernel.GenerateDepthPyramid, Globals.HShaderParams.DepthPyramid_OutputMIP2, DepthPyramidGTAO.rt, 2);
				cmd.SetComputeTextureParam(HDepthPyramidGTAO, (int)HDepthPyramidKernel.GenerateDepthPyramid, Globals.HShaderParams.DepthPyramid_OutputMIP3, DepthPyramidGTAO.rt, 3);
				cmd.DispatchCompute(HDepthPyramidGTAO, (int)HDepthPyramidKernel.GenerateDepthPyramid, dispatchResolutionHalf8_X, dispatchResolutionHalf8_Y, HRenderer.TextureXrSlices);
			}
			
			 
			// ---------------------------------------- OCCLUSION TRACING ----------------------------------------- //
			using (new ProfilingScope(cmd, OcclusionTracingSampler))
			{
				KeywordSwitch(cmd, HALF_RESOLUTION, halfResolution);
				KeywordSwitch(cmd, DEPTH_NORMAL_OUTPUT, !requiresTemporalDenoising);
				KeywordSwitch(HRenderGTAO, VISIBILITY_BITMASKS, HTraceAOSettings.ActiveProfile.GTAOSettings.VisibilityBitmasks);
				KeywordSwitch(HRenderGTAO, ACCURATE_TEMPORAL_REJECTION, !HTraceAOSettings.ActiveProfile.GTAOSettings.UseSimpleRejection && requiresTemporalDenoising);
				
				cmd.SetComputeIntParam(HRenderGTAO, Globals.HShaderParams.IsFinalPass, HTraceAOSettings.ActiveProfile.GTAOSettings.Denoising == Globals.GTAODenoisingMode.None && !requiresUpsacaling ? 1 : 0);
					
				cmd.SetComputeTextureParam(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, Globals.HShaderParams.Occlusion_Output, OcclusionGTAO.rt);
				cmd.SetComputeTextureParam(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, Globals.HShaderParams.RayMarchingDepthPyramid, DepthPyramidGTAO.rt);
				cmd.SetComputeTextureParam(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalGTAO.rt);
				cmd.SetComputeTextureParam(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, Globals.HShaderParams.Depth_Output, CameraHistorySystem.GetCameraData().FilterDepthGTAO.rt);
				cmd.DispatchCompute(HRenderGTAO, (int)HRenderGTAOKernel.RenderGTAO, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
			}

			var FinalOutput = HTraceAOSettings.ActiveProfile.GTAOSettings.Denoising == Globals.GTAODenoisingMode.None ? OcclusionNormalGTAO.rt : OcclusionGTAO.rt;


			// ---------------------------------------- TEMPORAL ACCUMULATION ---------------------------------------- //
			if (requiresTemporalDenoising)
			{
				using (new ProfilingScope(cmd, TemporalFilteringSampler))
				{
					cmd.SetComputeIntParam(HTemporalFilterGTAO, Globals.HShaderParams.IsFinalPass, requiresSpatialDenoising || requiresUpsacaling ? 0 : 1);
					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalAccumulation, Globals.HShaderParams.Occlusion, OcclusionGTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalAccumulation, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalGTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalAccumulation, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedGTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalAccumulation, Globals.HShaderParams.Depth_Output, CameraHistorySystem.GetCameraData().FilterDepthGTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalAccumulation, Globals.HShaderParams.SamplecountNormalHistory_Output, CameraHistorySystem.GetCameraData().SamplecountNormalHistoryGTAO.rt);
					cmd.SetComputeTextureParam(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalAccumulation, Globals.HShaderParams.OcclusionVelocityHistory_Output, CameraHistorySystem.GetCameraData().OcclusionVelocityHistoryGTAO.rt);
					cmd.DispatchCompute(HTemporalFilterGTAO, (int)HTemporalFilterGTAOKernel.TemporalAccumulation, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);
				}
			
				FinalOutput = OcclusionNormalGTAO.rt;
			}

			
			// ---------------------------------------- SPATIAL FILTER ---------------------------------------- //
			if (requiresSpatialDenoising)
			{
				using (new ProfilingScope(cmd, SpatialFilteringSampler))
				{
					KeywordSwitch(cmd, DOUBLE_SAMPLECOUNT, HTraceAOSettings.ActiveProfile.GTAOSettings.DoubleSampleCount);
					
					if (HTraceAOSettings.ActiveProfile.GTAOSettings.SpatialFilterType == Globals.SpatialFilterType.Disk)
					{
						cmd.SetComputeBufferParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.PointDistributionFill, Globals.HShaderParams.PointDistribution_Output, PointDistributionBufferGTAO);
						cmd.DispatchCompute(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.PointDistributionFill, 1, 1, 1);

						cmd.SetComputeIntParam(HSpatialFilterGTAO, Globals.HShaderParams.IsFinalPass, requiresUpsacaling ? 0 : 1);
						cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringDisk, Globals.HShaderParams.OcclusionNormal, OcclusionNormalGTAO.rt);
						cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringDisk, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalFilteredGTAO.rt);
						cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringDisk, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedGTAO.rt);
						cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringDisk, Globals.HShaderParams.Depth, CameraHistorySystem.GetCameraData().FilterDepthGTAO.rt);
						cmd.SetComputeBufferParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringDisk, Globals.HShaderParams.PointDistribution, PointDistributionBufferGTAO);
						cmd.DispatchCompute(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringDisk, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);

						FinalOutput = OcclusionNormalFilteredGTAO.rt;
					}
					else
					{
						Vector3Int PassStep                                                                     = Vector3Int.zero;
						if (HTraceAOSettings.ActiveProfile.GTAOSettings.BoxPassCount == 3) PassStep = new Vector3Int(4, 2, 1);
						if (HTraceAOSettings.ActiveProfile.GTAOSettings.BoxPassCount == 2) PassStep = new Vector3Int(2, 1, 1);
						if (HTraceAOSettings.ActiveProfile.GTAOSettings.BoxPassCount == 1) PassStep = new Vector3Int(1, 1, 1);
						
						cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, Globals.HShaderParams.Depth, CameraHistorySystem.GetCameraData().FilterDepthGTAO.rt);
						
						cmd.SetComputeIntParam(HSpatialFilterGTAO, Globals.HShaderParams.IsFinalPass, HTraceAOSettings.ActiveProfile.GTAOSettings.BoxPassCount == 1 ? requiresUpsacaling ? 0 : 1 : 0);
						cmd.SetComputeVectorParam(HSpatialFilterGTAO, Globals.HShaderParams.FilterStep, new Vector2(Mathf.Max(PassStep.x / renderScaleFactor.x, 1), Mathf.Max(PassStep.x / renderScaleFactor.y, 1)));
						cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, Globals.HShaderParams.OcclusionNormal, OcclusionNormalGTAO.rt);
						cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalFilteredGTAO.rt);
						cmd.DispatchCompute(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);

						FinalOutput = OcclusionNormalFilteredGTAO.rt;

						if (HTraceAOSettings.ActiveProfile.GTAOSettings.BoxPassCount > 1)
						{	
							cmd.SetComputeIntParam(HSpatialFilterGTAO, Globals.HShaderParams.IsFinalPass, HTraceAOSettings.ActiveProfile.GTAOSettings.BoxPassCount == 2 ? requiresUpsacaling ? 0 : 1 : 0);
							cmd.SetComputeVectorParam(HSpatialFilterGTAO, Globals.HShaderParams.FilterStep, new Vector2(Mathf.Max(PassStep.y / renderScaleFactor.x, 1), Mathf.Max(PassStep.y / renderScaleFactor.y, 1)));
							cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, Globals.HShaderParams.OcclusionNormal, OcclusionNormalFilteredGTAO.rt);
							cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalGTAO.rt);
							cmd.DispatchCompute(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);

							FinalOutput = OcclusionNormalGTAO.rt;
						}

						if (HTraceAOSettings.ActiveProfile.GTAOSettings.BoxPassCount > 2)
						{	
							cmd.SetComputeIntParam(HSpatialFilterGTAO, Globals.HShaderParams.IsFinalPass, requiresUpsacaling ? 0 : 1);
							cmd.SetComputeVectorParam(HSpatialFilterGTAO, Globals.HShaderParams.FilterStep, new Vector2(Mathf.Max(PassStep.z / renderScaleFactor.x, 1), Mathf.Max(PassStep.z / renderScaleFactor.y, 1)));
							cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, Globals.HShaderParams.OcclusionNormal, OcclusionNormalGTAO.rt);
							cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, Globals.HShaderParams.OcclusionNormal_Output, OcclusionNormalFilteredGTAO.rt);
							cmd.DispatchCompute(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.SpatialFilteringBox, dispatchResolutionScaled8_X, dispatchResolutionScaled8_Y, Globals.HRenderer.TextureXrSlices);

							FinalOutput = OcclusionNormalFilteredGTAO.rt;
						}
					}
				}
			}
			

			// ---------------------------------------- INTERPOLATION ---------------------------------------- //
			if (requiresUpsacaling)
			{
				using (new ProfilingScope(cmd, InterpolationSampler))
				{		
					KeywordSwitch(cmd, HALF_RESOLUTION, halfResolution);
		
					cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.Interpolation, Globals.HShaderParams.OcclusionNormal, FinalOutput);
					cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.Interpolation, Globals.HShaderParams.Occlusion_Output, OcclusionGTAO.rt);
					cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.Interpolation, Globals.HShaderParams.Depth, CameraHistorySystem.GetCameraData().FilterDepthGTAO.rt);
					cmd.DispatchCompute(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.Interpolation, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);

					FinalOutput = OcclusionGTAO.rt;
				}
			}
			
			
			// ---------------------------------------- OUTPUT COMPOSITION ---------------------------------------- //
			using (new ProfilingScope(cmd, OutputCompositionSampler))
			{
				if (requiresAfterOpaqueWriting)
				{
					CoreUtils.SetRenderTarget(cmd, cameraColorBuffer);
					OutputCompositionGTAO.SetTexture(Globals.HShaderParams.Occlusion_Output, FinalOutput);
					OutputCompositionGTAO.SetFloat(Globals.HShaderParams.Fadeout, HTraceAOSettings.ActiveProfile.GeneralSettings.Fadeout);
					OutputCompositionGTAO.SetFloat(Globals.HShaderParams.ExcludedIntensity, HTraceAOSettings.ActiveProfile.GTAOSettings.ExcludedIntensity);
				#if UNITY_2023_3_OR_NEWER
					OutputCompositionGTAO.SetInt(Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.GTAOSettings.ExcludeReceiving);
				#endif
					cmd.DrawProcedural(Matrix4x4.identity, OutputCompositionGTAO, 0, MeshTopology.Triangles, 3, 1);
				}
				else
				{
					cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.OutputComposition, Globals.HShaderParams.Occlusion_Output, FinalOutput);
					cmd.SetComputeTextureParam(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.OutputComposition, Globals.HShaderParams.Occlusion_ShaderGraphOutput, OcclusionShaderGraphGTAO.rt);
					cmd.DispatchCompute(HSpatialFilterGTAO, (int)HSpatialFilterGTAOKernel.OutputComposition, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);
				}
			}
			
		
			// -------------------------------------------- DEBUG ------------------------------------------ //
			if (HTraceAOSettings.ActiveProfile.GTAOSettings.DebugMode != Globals.DebugModeGTAO.None)
			{
				using (new ProfilingScope(cmd, DebugSampler))
				{
					cmd.SetComputeIntParam(HDebugGTAO, Globals.HShaderParams.IsGTAO, 1);
					cmd.SetComputeIntParam(HDebugGTAO, Globals.HShaderParams.DebugSwitch, (int)HTraceAOSettings.ActiveProfile.GTAOSettings.DebugMode);
					cmd.SetComputeIntParam(HDebugGTAO, Globals.HShaderParams.BuffersSwitch, (int)HTraceAOSettings.ActiveProfile.GeneralSettings.HBuffer);

					cmd.SetComputeTextureParam(HDebugGTAO, 0, Globals.HShaderParams.Debug_Output, DebugGTAO.rt);
					cmd.SetComputeTextureParam(HDebugGTAO, 0, Globals.HShaderParams.DebugDepth, DepthPyramidGTAO.rt);
					cmd.SetComputeTextureParam(HDebugGTAO, 0, Globals.HShaderParams.TemporalDataReprojected, TemporalDataReprojectedGTAO.rt);
					cmd.SetComputeTextureParam(HDebugGTAO, 0, Globals.HShaderParams.g_HTraceBufferAO, FinalOutput);
					cmd.DispatchCompute(HDebugGTAO, 0, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);

					cmd.SetGlobalTexture(Globals.HShaderParams.Debug_Output, DebugGTAO.rt);
				}
			}
			
			cmd.SetGlobalTexture(Globals.HShaderParams.g_HTraceBufferAO, requiresShaderGraphOutput ? OcclusionShaderGraphGTAO.rt : FinalOutput);
			cmd.SetGlobalTexture(Globals.HShaderParams.g_ScreenSpaceOcclusionTexture, FinalOutput);
		}
	}
}
