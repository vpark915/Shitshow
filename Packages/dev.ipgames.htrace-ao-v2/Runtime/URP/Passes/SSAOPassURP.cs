using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Extensions;
using IPGames.HTraceAO.Runtime.Globals;
using IPGames.HTraceAO.Runtime.Passes;
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
	internal class SSAOPassURP : ScriptableRenderPass
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
			var cmd = CommandBufferPool.Get(HNames.HTRACE_SSAO_PASS_NAME);

			Camera camera      = renderingData.cameraData.camera;
			float  renderScale = renderingData.cameraData.renderScale;
			int    width       = (int)(camera.scaledPixelWidth * renderScale);
			int    height      = (int)(camera.scaledPixelHeight * renderScale);

			if (Shader.GetGlobalTexture(CameraNormalsTexture) == null)
				return;

			SSAO.Execute(cmd, camera, width, height, _renderer.cameraColorTargetHandle);

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
		private readonly ProfilingSampler SSAOSampler = new ProfilingSampler(HNames.HTRACE_SSAO_PASS_NAME);
		
		private class PassData
		{
			public UniversalCameraData UniversalCameraData;
			public TextureHandle       ColorTexture;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			using (var builder = renderGraph.AddUnsafePass<PassData>(HNames.HTRACE_SSAO_PASS_NAME, out var passData, SSAOSampler))
			{
				UniversalResourceData  resourceData           = frameData.Get<UniversalResourceData>();
				UniversalCameraData    universalCameraData    = frameData.Get<UniversalCameraData>();
				UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
				UniversalLightData     lightData              = frameData.Get<UniversalLightData>();

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

				SetupShared(universalCameraData.camera, universalCameraData.renderScale, universalCameraData.cameraTargetDescriptor);

				builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
			}
		}
		
		private static void ExecutePass(PassData data, UnsafeGraphContext rgContext)
		{
		    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);

		    Camera camera = data.UniversalCameraData.camera;
		    float renderScale = data.UniversalCameraData.renderScale;
		    int width  = (int)(camera.scaledPixelWidth * renderScale);
		    int height = (int)(camera.scaledPixelHeight * renderScale);

		    SSAO.Execute(cmd, camera, width, height, data.ColorTexture);
		    
		    // Overwrite global set to Shader Graph if we are at the wrong injection point
		    if (HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint != URPInjectionPoint.BeforeOpaque)
			    cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO, rgContext.defaultResources.whiteTexture);
		}
#endif

		#endregion --------------------------- Render Graph ---------------------------
		
		#region --------------------------- Shared ---------------------------
		
		private static void SetupShared(Camera camera, float renderScale, RenderTextureDescriptor desc)
		{
		    var computes = HTraceAOSettings.ActiveResources.Computes;
		    if (SSAO.HRenderSSAO == null) SSAO.HRenderSSAO             = computes.HRenderSSAO;
		    if (SSAO.HDenoiseSSAO == null) SSAO.HDenoiseSSAO           = computes.HDenoiseSSAO;
		    if (SSAO.HDepthPyramidSSAO == null) SSAO.HDepthPyramidSSAO = computes.HDepthPyramidSSAO;
		    if (SSAO.HDebugSSAO == null) SSAO.HDebugSSAO               = computes.HDebugAO;

		    if (SSAO.OutputCompositionSSAO == null) SSAO.OutputCompositionSSAO = new Material(HTraceAOSettings.ActiveResources.URP.OutputCompositionUrpShader);

		    int width  = (int)(camera.scaledPixelWidth * renderScale);
		    int height = (int)(camera.scaledPixelHeight * renderScale);

		    if (desc.width != width || desc.height != height)
		        desc = new RenderTextureDescriptor(width, height);

		    desc.depthBufferBits    = 0; // Color and depth cannot be combined in RTHandles
		    desc.stencilFormat      = GraphicsFormat.None;
		    desc.depthStencilFormat = GraphicsFormat.None;
		    desc.msaaSamples        = 1;
		    desc.bindMS             = false;
		    desc.enableRandomWrite  = true;

		    RenderTextureDescriptor depthDesc = desc;
		 
		    SSAO.DepthTiledSSAO.ReAllocateIfNeeded("_DepthTiled", ref desc, width: (int)(HMath.DepthResolutionFunc(width) / 8.0f), height: (int)(HMath.DepthResolutionFunc(height) / 8.0f),
			    depth: TextureXR.slices * 16, graphicsFormat: GraphicsFormat.R32_SFloat, dimension: TextureDimension.Tex2DArray, useMipMap: true);
		    SSAO.DepthPyramidSSAO.ReAllocateIfNeeded("_DepthPyramid_SSAO", ref depthDesc, width: HMath.DepthResolutionFunc(width) / 2, height: HMath.DepthResolutionFunc(height) / 2,
			    graphicsFormat: GraphicsFormat.R32_SFloat, useMipMap: true);
		    SSAO.DepthIntermediatePyramidSSAO.ReAllocateIfNeeded("_DepthIntermediatePyramid_SSAO", ref depthDesc, width: HMath.DepthResolutionFunc(width) / 4, height: HMath.DepthResolutionFunc(height) / 4,
			    graphicsFormat: GraphicsFormat.R32_SFloat, useMipMap: true);

		    SSAO.OcclusionSSAO_1.ReAllocateIfNeeded("_Occlusion_1", ref desc, width: width / 2, height: height / 2, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionSSAO_2.ReAllocateIfNeeded("_Occlusion_2", ref desc, width: width / 4, height: height / 4, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionSSAO_3.ReAllocateIfNeeded("_Occlusion_3", ref desc, width: width / 8, height: height / 8, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionSSAO_4.ReAllocateIfNeeded("_Occlusion_4", ref desc, width: width / 16, height: height / 16, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionCombinedSSAO_0.ReAllocateIfNeeded("_OcclusionCombined_0", ref desc, width: width, height: height, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionCombinedSSAO_1.ReAllocateIfNeeded("_OcclusionCombined_1", ref desc, width: width / 2, height: height / 2, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionCombinedSSAO_2.ReAllocateIfNeeded("_OcclusionCombined_2", ref desc, width: width / 4, height: height / 4, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.OcclusionCombinedSSAO_3.ReAllocateIfNeeded("_OcclusionCombined_3", ref desc, width: width / 8, height: height / 8, graphicsFormat: GraphicsFormat.R8_UNorm, dimension: TextureDimension.Tex2D);
		    SSAO.DebugSSAO.ReAllocateIfNeeded("_Debug", ref desc, graphicsFormat: GraphicsFormat.R16G16B16A16_SFloat);
		    SSAO.OcclusionShaderGraphSSAO.ReAllocateIfNeeded("_OcclusionShaderGraphSSAO", ref desc, graphicsFormat: GraphicsFormat.R8_UNorm);
		}

		protected internal void Dispose()
		{
			SSAO.OcclusionSSAO_1.HRelease();
			SSAO.OcclusionSSAO_2.HRelease();
			SSAO.OcclusionSSAO_3.HRelease();
			SSAO.OcclusionSSAO_4.HRelease();
			SSAO.OcclusionCombinedSSAO_0.HRelease();
			SSAO.OcclusionCombinedSSAO_1.HRelease();
			SSAO.OcclusionCombinedSSAO_2.HRelease();
			SSAO.OcclusionCombinedSSAO_3.HRelease();
			SSAO.DebugSSAO.HRelease();

			SSAO.DepthTiledSSAO.HRelease();
			SSAO.DepthPyramidSSAO.HRelease();
			SSAO.DepthIntermediatePyramidSSAO.HRelease();
			SSAO.OcclusionShaderGraphSSAO?.HRelease();
		}

		#endregion --------------------------- Shared ---------------------------
    }
}
