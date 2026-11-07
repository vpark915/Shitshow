using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Extensions;
using IPGames.HTraceAO.Runtime.Globals;
using IPGames.HTraceAO.Runtime.Passes;
using IPGames.HTraceAO.Runtime.Wrappers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule; //using UnityEngine.Rendering.RenderGraphModule; UNITY_2023_3_OR_NEWER
#else
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif


namespace IPGames.HTraceAO.Runtime.URP.Passes
{
	internal class RTAOPassURP : ScriptableRenderPass
	{
		private const string RT_IS_NOT_SUPPORTED_MESSAGE = "Realtime RayTracing is not supported!";
		private static readonly int CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");

		#region --------------------------- Non Render Graph ---------------------------

#if !UNITY_6000_4_OR_NEWER
		private ScriptableRenderer _renderer;

		protected internal void Initialize(ScriptableRenderer renderer)
		{
			_renderer = renderer;
		}

#if UNITY_2023_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			RTAO.CameraHistorySystem.SyncCamera(renderingData.cameraData.camera.GetHashCode(), Time.frameCount);
			SetupShared(renderingData.cameraData.camera, renderingData.cameraData.renderScale, renderingData.cameraData.cameraTargetDescriptor);
		}

#if UNITY_2023_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
		{
		}

#if UNITY_2023_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			var cmd = CommandBufferPool.Get(HNames.HTRACE_RTAO_PASS_NAME);

			Camera camera = renderingData.cameraData.camera;
			float renderScale = renderingData.cameraData.renderScale;

			// Re-sync here in case another camera's OnCameraSetup ran between our OnCameraSetup and Execute
			RTAO.CameraHistorySystem.SyncCamera(camera.GetHashCode(), Time.frameCount);
			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);

			if (Shader.GetGlobalTexture(CameraNormalsTexture) == null)
				return;

			if (HRenderer.SupportsInlineRayTracing == false) // URP has only Inline Raytracing, but we output realtime RT error to avoid confusing users
			{
				HExtensions.DebugPrint(DebugType.Error,RT_IS_NOT_SUPPORTED_MESSAGE);
				cmd.SetGlobalVector(HShaderParams.AmbientOcclusionParam, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));

				context.ExecuteCommandBuffer(cmd);
				cmd.Clear();
				CommandBufferPool.Release(cmd);
				return;
			}

			RTAO.Execute(cmd, camera, width, height, _renderer.cameraColorTargetHandle);
			
			ConfigureTarget(_renderer.cameraColorTargetHandle);
			
			// Overwrite global set to Shader Graph if we are at the wrong injection point
			if (HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint != URPInjectionPoint.BeforeOpaque)
				cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO, Texture2D.whiteTexture);

			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			CommandBufferPool.Release(cmd);
			return;
		}
#endif

		#endregion --------------------------- Non Render Graph ---------------------------

		#region --------------------------- Render Graph ---------------------------

#if UNITY_2023_3_OR_NEWER
		private readonly ProfilingSampler RTAOSampler = new ProfilingSampler(HNames.HTRACE_RTAO_PASS_NAME);
		
		private class PassData
		{
			public UniversalCameraData UniversalCameraData;
			public TextureHandle       ColorTexture;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{

			using (var builder = renderGraph.AddUnsafePass<PassData>(HNames.HTRACE_RTAO_PASS_NAME, out var passData, RTAOSampler))
			{
				UniversalCameraData    universalCameraData    = frameData.Get<UniversalCameraData>();
				UniversalResourceData  resourceData           = frameData.Get<UniversalResourceData>();

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				TextureHandle colorTexture = default;
				if (HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint == URPInjectionPoint.AfterOpaque)
				{
					colorTexture = resourceData.activeColorTexture;
					if (!colorTexture.IsValid())
						return;
					builder.UseTexture(colorTexture, AccessFlags.Write);
				}

				passData.UniversalCameraData = universalCameraData;
				passData.ColorTexture        = colorTexture;

				Camera                  camera      = universalCameraData.camera;
				float                   renderScale = universalCameraData.renderScale;
				RenderTextureDescriptor desc        = universalCameraData.cameraTargetDescriptor;
				
				RTAO.CameraHistorySystem.SyncCamera(camera.GetHashCode(), Time.frameCount);

				SetupShared(camera, renderScale, desc);

				builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
			}
		}

		private void ExecutePass(PassData data, UnsafeGraphContext rgContext)
		{
			var cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);

			Camera camera = data.UniversalCameraData.camera;
			float renderScale = data.UniversalCameraData.renderScale;
			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);

			if (HRenderer.SupportsInlineRayTracing == false) // URP has only Inline Raytracing, but we output realtime RT error to avoid confusing users
			{
				HExtensions.DebugPrint(DebugType.Error,RT_IS_NOT_SUPPORTED_MESSAGE);
				cmd.SetGlobalVector(HShaderParams.AmbientOcclusionParam, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));

				return;
			}

			RTAO.Execute(cmd, camera, width, height, data.ColorTexture);
			
			// Overwrite global set to Shader Graph if we are at the wrong injection point
			if (HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint != URPInjectionPoint.BeforeOpaque)
				cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO, rgContext.defaultResources.whiteTexture);
		}
#endif

		#endregion --------------------------- Render Graph ---------------------------

		#region ------------------------------------ Shared ------------------------------------
		
		private static void SetupShared(Camera camera, float renderScale, RenderTextureDescriptor desc)
		{
			var computes = HTraceAOSettings.ActiveResources.Computes;
#if UNITY_2023_2_OR_NEWER
			if (HRenderer.SupportsInlineRayTracing && RTAO.HRenderRTAO == null) RTAO.HRenderRTAO = computes.HRenderRTAO;
#endif
			if (RTAO.HTemporalFilterRTAO == null) RTAO.HTemporalFilterRTAO     = computes.HTemporalFilterRTAO;
			if (RTAO.HSpatialFilterRTAO == null) RTAO.HSpatialFilterRTAO       = computes.HSpatialFilterRTAO;
			if (RTAO.HThreadCompactionRTAO == null) RTAO.HThreadCompactionRTAO = computes.HThreadCompactionRTAO;
			if (RTAO.HDepthPyramidAO == null) RTAO.HDepthPyramidAO             = computes.HDepthPyramidAO;
			if (RTAO.HDebugRTAO == null) RTAO.HDebugRTAO                       = computes.HDebugAO;
			if (RTAO.HContactOcclusionRTAO == null) RTAO.HContactOcclusionRTAO = computes.HContactOcclusionRTAO;

			if (RTAO.OutputCompositionRTAO == null) RTAO.OutputCompositionRTAO = new Material(HTraceAOSettings.ActiveResources.URP.OutputCompositionUrpShader);

			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);
			if (desc.width != width || desc.height != height)
				desc = new RenderTextureDescriptor(width, height);
			
			desc.depthStencilFormat = GraphicsFormat.None;
			desc.enableRandomWrite  = true;
			
			RenderTextureDescriptor descHalf = desc;
			descHalf.width  = desc.width / 2;
			descHalf.height = desc.height / 2;

			ref var cameraData = ref RTAO.CameraHistorySystem.GetCameraData();
			if (cameraData.OcclusionHistoryRTAO == null) cameraData.OcclusionHistoryRTAO               = new RTWrapper();
			if (cameraData.VelocityHistoryRTAO == null) cameraData.VelocityHistoryRTAO                 = new RTWrapper();
			if (cameraData.SamplecountNormalHistoryRTAO == null) cameraData.SamplecountNormalHistoryRTAO = new RTWrapper();
			if (cameraData.FilterDepthRTAO == null) cameraData.FilterDepthRTAO                           = new RTWrapper();

			cameraData.OcclusionHistoryRTAO.ReAllocateIfNeeded("_OcclusionHistory", ref desc, graphicsFormat: GraphicsFormat.R16G16_SFloat);  // TODO: make this R16_SFloat when Denoising Mode is set to Single Pass
			cameraData.VelocityHistoryRTAO.ReAllocateIfNeeded("_VelocityHistory", ref desc, graphicsFormat: GraphicsFormat.R16_SFloat);
			cameraData.SamplecountNormalHistoryRTAO.ReAllocateIfNeeded("_SamplecountNormalHistory", ref desc, graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm);
			cameraData.FilterDepthRTAO.ReAllocateIfNeeded("_FilterDepth", ref desc, graphicsFormat: GraphicsFormat.R32_SFloat);

			RTAO.OcclusionRTAO.ReAllocateIfNeeded("_Occlusion", ref desc, graphicsFormat: GraphicsFormat.R8G8_UNorm);
			RTAO.OcclusionShaderGraphRTAO.ReAllocateIfNeeded("_OcclusionShaderGraphRTAO", ref desc, graphicsFormat: GraphicsFormat.R8_UNorm);
			RTAO.OcclusionHalfRateRTAO.ReAllocateIfNeeded("_OcclusionHalfRate", ref descHalf, graphicsFormat: GraphicsFormat.R8G8_UNorm);  // TODO: use Dummy if Resolution is NOT set to Half
			RTAO.OcclusionReprojectedRTAO.ReAllocateIfNeeded("_OcclusionReprojected", ref desc, graphicsFormat: GraphicsFormat.R16G16_SFloat); // TODO: make this R16_SFloat when Denoising Mode is set to Single Pass
			RTAO.TemporalDataReprojectedRTAO.ReAllocateIfNeeded("_TemporalDataReprojected", ref desc, graphicsFormat: GraphicsFormat.R32_SFloat);
			RTAO.ShadingRateRTAO.ReAllocateIfNeeded("_ShadingRate", ref descHalf, graphicsFormat: GraphicsFormat.R8_UInt);  // TODO: do NOT allocate it if Resolution is set to Half or Full
			RTAO.RayMarchingDepthRTAO.ReAllocateIfNeeded("_RayMarchingDepth", ref desc, graphicsFormat: GraphicsFormat.R16_SFloat); // TODO: do NOT allocate it if Contact Occlusion and Screen Space Pretrace are disabled
			RTAO.OcclusionNormalRTAO.ReAllocateIfNeeded("_OcclusionNormal", ref desc, graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm);
			RTAO.OcclusionNormalFilteredRTAO.ReAllocateIfNeeded("_OcclusionNormalFiltered", ref desc, graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm);
			RTAO.DebugRTAO.ReAllocateIfNeeded("_Debug", ref desc, graphicsFormat: GraphicsFormat.B10G11R11_UFloatPack32);  // TODO: can we not allocate it if debug is off?
			
			if (RTAO.PointDistributionBufferRTAO == null) RTAO.PointDistributionBufferRTAO = new ComputeBuffer(HRenderer.TextureXrSlices * 16, 3 * sizeof(float));
			if (RTAO.IndirectArgumentsRTAO == null)  RTAO.IndirectArgumentsRTAO = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3 * HRenderer.TextureXrSlices, sizeof(uint));
			if (RTAO.IndirectCoordsRTAO == null) RTAO.IndirectCoordsRTAO = new HDynamicBuffer(BufferType.ComputeBuffer, 2 * sizeof(uint), HRenderer.TextureXrSlices, avoidDownscale: false);

			if (camera.cameraType != CameraType.SceneView)
			{
				if (HTraceAOSettings.ActiveProfile.RTAOSettings.Resolution == ResolutionMode.Half || HTraceAOSettings.ActiveProfile.RTAOSettings.Resolution == ResolutionMode.Adaptive || HTraceAOSettings.ActiveProfile.RTAOSettings.DisocclusionBoost == true)
				{
					Vector2Int cameraResolution = new Vector2Int(width, height);
					RTAO.IndirectCoordsRTAO?.ReAllocIfNeeded(cameraResolution);
				}
				else
				{
					RTAO.IndirectCoordsRTAO.HRelease();
				}
			}

			if (RTAO.RayCounterRTAO == null)
			{
				RTAO.RayCounterRTAO = new ComputeBuffer(2 * TextureXR.slices, sizeof(uint));
				uint[] zeroArray = new uint[2 * TextureXR.slices];
				RTAO.RayCounterRTAO.SetData(zeroArray);
			}
			if (RTAO.DisocclusionCounterRTAO == null)
			{
				RTAO.DisocclusionCounterRTAO = new ComputeBuffer(2 * TextureXR.slices, sizeof(uint));
				uint[] zeroArray = new uint[2 * TextureXR.slices];
				RTAO.DisocclusionCounterRTAO.SetData(zeroArray);
			}

			if (RTAO.RTAS == null)
#if UNITY_2023_2_OR_NEWER
			{
				RayTracingAccelerationStructure.Settings settings = new RayTracingAccelerationStructure.Settings();
				settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
				settings.managementMode     = RayTracingAccelerationStructure.ManagementMode.Manual;
				settings.layerMask          = 255;
				RTAO.RTAS                   = new RayTracingAccelerationStructure(settings);
			}
#else
				RTAO.RTAS = new RayTracingAccelerationStructure();
#endif
		}

		protected internal void Dispose()
		{
			RTAO.OcclusionRTAO.HRelease();
			RTAO.OcclusionShaderGraphRTAO?.HRelease();
			RTAO.OcclusionHalfRateRTAO.HRelease();
			RTAO.OcclusionReprojectedRTAO.HRelease();
			RTAO.TemporalDataReprojectedRTAO.HRelease();
			RTAO.ShadingRateRTAO.HRelease();
			RTAO.RayMarchingDepthRTAO.HRelease();
			RTAO.OcclusionNormalRTAO.HRelease();
			RTAO.OcclusionNormalFilteredRTAO.HRelease();
			RTAO.DebugRTAO.HRelease();

			RTAO.RTAS.HRelease();
			RTAO.RTAS = null;

			RTAO.RayCounterRTAO?.HRelease();
			RTAO.DisocclusionCounterRTAO?.HRelease();
			RTAO.IndirectCoordsRTAO?.HRelease();
			RTAO.IndirectArgumentsRTAO?.HRelease();
			RTAO.PointDistributionBufferRTAO?.HRelease();
			RTAO.RayCounterRTAO              = null;
			RTAO.IndirectCoordsRTAO          = null;
			RTAO.IndirectArgumentsRTAO       = null;
			RTAO.PointDistributionBufferRTAO = null;
			RTAO.DisocclusionCounterRTAO     = null;

			RTAO.CameraHistorySystem.Cleanup();
		}

		#endregion
	}
}
