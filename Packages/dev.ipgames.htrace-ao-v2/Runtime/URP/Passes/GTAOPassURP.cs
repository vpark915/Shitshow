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
	internal class GTAOPassURP : ScriptableRenderPass
	{
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
			GTAO.CameraHistorySystem.SyncCamera(renderingData.cameraData.camera.GetHashCode(), Time.frameCount);
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
			var cmd = CommandBufferPool.Get(HNames.HTRACE_GTAO_PASS_NAME);

			Camera camera = renderingData.cameraData.camera;
			float renderScale = renderingData.cameraData.renderScale;

			// Re-sync here in case another camera's OnCameraSetup ran between our OnCameraSetup and Execute
			GTAO.CameraHistorySystem.SyncCamera(camera.GetHashCode(), Time.frameCount);
			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);

			if (Shader.GetGlobalTexture(CameraNormalsTexture) == null)
				return;

			GTAO.Execute(cmd, camera, width, height, _renderer.cameraColorTargetHandle);
			
			ConfigureTarget(_renderer.cameraColorTargetHandle);
			
			// Overwrite global set to Shader Graph if we are at the wrong injection point
			if (HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint != URPInjectionPoint.BeforeOpaque)
				cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO, Texture2D.whiteTexture);

			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			CommandBufferPool.Release(cmd);
			return;
		}
#endif // !UNITY_6000_4_OR_NEWER

		#endregion --------------------------- Non Render Graph ---------------------------

		#region --------------------------- Render Graph ---------------------------

#if UNITY_2023_3_OR_NEWER
		private static readonly ProfilingSampler GTAOSampler = new ProfilingSampler(HNames.HTRACE_GTAO_PASS_NAME);
		
		private class PassData
		{
			public UniversalCameraData UniversalCameraData;
			public TextureHandle       ColorTexture;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{

			using (var builder = renderGraph.AddUnsafePass<PassData>(HNames.HTRACE_GTAO_PASS_NAME, out var passData, GTAOSampler))
			{
				UniversalCameraData universalCameraData = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				// Denoising == None does not create g_HTraceMotionDelta, so declaring it here would produce a missing RenderGraph resource error.
				if (GTAO.RequiresMotionDelta)
					builder.UseGlobalTexture(HShaderParams.g_HTraceMotionDelta, AccessFlags.ReadWrite);

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

				Camera camera = universalCameraData.camera;
				float renderScale = universalCameraData.renderScale;
				RenderTextureDescriptor desc = universalCameraData.cameraTargetDescriptor;

				GTAO.CameraHistorySystem.SyncCamera(camera.GetHashCode(), Time.frameCount);

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

			GTAO.Execute(cmd, camera, width, height, data.ColorTexture);
			
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
			if (GTAO.HRenderGTAO == null) GTAO.HRenderGTAO                 = computes.HRenderGTAO;
			if (GTAO.HDebugGTAO == null) GTAO.HDebugGTAO                   = computes.HDebugAO;
			if (GTAO.HTemporalFilterGTAO == null) GTAO.HTemporalFilterGTAO = computes.HTemporalFilterGTAO;
			if (GTAO.HSpatialFilterGTAO == null) GTAO.HSpatialFilterGTAO   = computes.HSpatialFilterGTAO;
			if (GTAO.HDepthPyramidGTAO == null) GTAO.HDepthPyramidGTAO     = computes.HDepthPyramidAO;
			
			if (GTAO.OutputCompositionGTAO == null) GTAO.OutputCompositionGTAO = new Material(HTraceAOSettings.ActiveResources.URP.OutputCompositionUrpShader);

			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);
			if (desc.width != width || desc.height != height)
				desc = new RenderTextureDescriptor(width, height);

			desc.depthStencilFormat = GraphicsFormat.None;
			desc.enableRandomWrite  = true;

			ref var cameraData = ref GTAO.CameraHistorySystem.GetCameraData();
			if (cameraData.OcclusionVelocityHistoryGTAO == null) cameraData.OcclusionVelocityHistoryGTAO = new RTWrapper();
			if (cameraData.SamplecountNormalHistoryGTAO == null) cameraData.SamplecountNormalHistoryGTAO = new RTWrapper();
			if (cameraData.FilterDepthGTAO == null) cameraData.FilterDepthGTAO                           = new RTWrapper();
			
			cameraData.OcclusionVelocityHistoryGTAO.ReAllocateIfNeeded("_OcclusionVelocityHistory", ref desc, graphicsFormat: GraphicsFormat.R16G16_SFloat);
			cameraData.SamplecountNormalHistoryGTAO.ReAllocateIfNeeded("_SamplecountNormalHistory", ref desc, graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm);
			cameraData.FilterDepthGTAO.ReAllocateIfNeeded("_FilterDepth", ref desc, graphicsFormat: GraphicsFormat.R32_SFloat);

			GTAO.OcclusionGTAO.ReAllocateIfNeeded("_Occlusion", ref desc, graphicsFormat: GraphicsFormat.R8G8_UNorm);
			GTAO.OcclusionShaderGraphGTAO.ReAllocateIfNeeded("_OcclusionShaderGraphGTAO", ref desc, graphicsFormat: GraphicsFormat.R8_UNorm);
			GTAO.TemporalDataReprojectedGTAO.ReAllocateIfNeeded("_TemporalDataReprojected", ref desc, graphicsFormat: GraphicsFormat.R32_UInt);
			GTAO.DepthPyramidGTAO.ReAllocateIfNeeded("_DepthPyramid", ref desc, graphicsFormat: HTraceAOSettings.ActiveProfile.GTAOSettings.DepthFormat == GTAODepthFormat.R16 ? GraphicsFormat.R16_SFloat : GraphicsFormat.R32_SFloat, useMipMap: true);
			GTAO.OcclusionNormalGTAO.ReAllocateIfNeeded("_OcclusionNormal", ref desc, graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm);
			GTAO.OcclusionNormalFilteredGTAO.ReAllocateIfNeeded("_OcclusionNormalFiltered", ref desc, graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm);
			GTAO.DebugGTAO.ReAllocateIfNeeded("_Debug", ref desc, graphicsFormat: GraphicsFormat.R16G16B16A16_SFloat);

			if (GTAO.PointDistributionBufferGTAO == null) GTAO.PointDistributionBufferGTAO = new ComputeBuffer(HRenderer.TextureXrSlices * 16, 3 * sizeof(float));
		}

		protected internal void Dispose()
		{
			GTAO.CameraHistorySystem.Cleanup();

			GTAO.OcclusionGTAO?.HRelease();
			GTAO.OcclusionShaderGraphGTAO?.HRelease();
			GTAO.TemporalDataReprojectedGTAO?.HRelease();
			GTAO.DepthPyramidGTAO?.HRelease();
			GTAO.OcclusionNormalGTAO?.HRelease();
			GTAO.OcclusionNormalFilteredGTAO?.HRelease();
			GTAO.DebugGTAO?.HRelease();

			GTAO.PointDistributionBufferGTAO?.HRelease();
			GTAO.PointDistributionBufferGTAO = null;
		}

		#endregion
	}
}
