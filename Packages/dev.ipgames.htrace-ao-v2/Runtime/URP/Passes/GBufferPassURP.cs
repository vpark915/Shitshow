using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Extensions;
using IPGames.HTraceAO.Runtime.Globals;
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
	internal class GBufferPassURP : ScriptableRenderPass
	{
		private static readonly ProfilingSampler GBufferProfilingSampler = new ProfilingSampler(HNames.HTRACE_GBUFFER_PASS_NAME);

		// Texture names
		const string _GeometryNormalsFromDepth = "_GeometryNormalsFromDepth";

		// Textures
		internal static RTHandle GeometryNormalsFromDepthRT;

		// Compute shader
		private static ComputeShader HDepthPyramidAO;


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
			bool reconstructNormals = HTraceAOSettings.ActiveProfile != null && HTraceAOSettings.ActiveProfile.GeneralSettings.ReconstructNormals;
				
			// We need this because otherwise Unity clears depth buffer
			if (HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint == URPInjectionPoint.BeforeOpaque)
				reconstructNormals = false;

			if (reconstructNormals || HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.SSAO)
				ConfigureInput(ScriptableRenderPassInput.Depth);
			else
				ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
		}
		

#if UNITY_2023_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			var cmd = CommandBufferPool.Get(HNames.HTRACE_GBUFFER_PASS_NAME);

			using (new ProfilingScope(cmd, GBufferProfilingSampler))
			{	
				Camera camera = renderingData.cameraData.camera;
				float renderScale = renderingData.cameraData.renderScale;
				int width  = (int)(camera.scaledPixelWidth * renderScale);
				int height = (int)(camera.scaledPixelHeight * renderScale);
				
				Texture cameraNormalsTexture = Shader.GetGlobalTexture(HShaderParams.g_CameraNormalsTexture);
				Texture cameraDepthTexture = Shader.GetGlobalTexture(HShaderParams.g_CameraDepthTexture);
				
				// cameraNormalsTexture / cameraDepthTexture can be wrong (but not null!) when a preview is rendered (e.g. a material preview in the inspector window),
				// so we check it by comparing with camera res (should be equal) and we only set a Dummy if nothing has been ever set at all,
				// this prevents flickering when e.g. a mouse is hovering over the inspector window with a material preview.
				if (cameraNormalsTexture != null && cameraNormalsTexture.height == height)
				{
					cmd.SetGlobalTexture(HShaderParams.g_HTraceGBuffer2, cameraNormalsTexture);
				}
				else
				{
					if (Shader.GetGlobalTexture(HShaderParams.g_HTraceGBuffer2) == null)
						cmd.SetGlobalTexture(HShaderParams.g_HTraceGBuffer2, HRenderer.EmptyTexture);
				}
				
				if (cameraDepthTexture != null && cameraDepthTexture.height == height)
				{
					cmd.SetGlobalTexture(HShaderParams.g_HTraceDepth, cameraDepthTexture);
				}
				else
				{
					if (Shader.GetGlobalTexture(HShaderParams.g_HTraceDepth) == null)
						cmd.SetGlobalTexture(HShaderParams.g_HTraceDepth, HRenderer.EmptyTexture);
				}
				
				bool reconstructNormals = HTraceAOSettings.ActiveProfile != null && HTraceAOSettings.ActiveProfile.GeneralSettings.ReconstructNormals;
				
				// Reconstruct normals from depth
				if (reconstructNormals)
				{
					cmd.SetComputeTextureParam(HDepthPyramidAO, 2, HShaderParams.GeometryNormals_Output, GeometryNormalsFromDepthRT.rt);
					cmd.DispatchCompute(HDepthPyramidAO, 2, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), HRenderer.TextureXrSlices);
					
					cmd.SetGlobalTexture(HShaderParams.g_HTraceGBuffer2, GeometryNormalsFromDepthRT.rt);
				}
			}
			
			// Render Layer Mask (dummy)
			cmd.SetGlobalTexture(HShaderParams.g_HTraceRenderLayerMask, HRenderer.EmptyTexture);

			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			CommandBufferPool.Release(cmd);
		}
#endif // !UNITY_6000_4_OR_NEWER

		#endregion --------------------------- Non Render Graph ---------------------------

		#region --------------------------- Render Graph ---------------------------

#if UNITY_2023_3_OR_NEWER
		private class GBufferPassData
		{
			public TextureHandle[] GBufferTextures;
			public TextureHandle   DepthTexture;
			public TextureHandle   NormalsTexture;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			bool reconstructNormals = HTraceAOSettings.ActiveProfile != null && HTraceAOSettings.ActiveProfile.GeneralSettings.ReconstructNormals;

			if (reconstructNormals || HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.SSAO)
				ConfigureInput(ScriptableRenderPassInput.Depth);
			else
				ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);

			UniversalCameraData    universalCameraData    = frameData.Get<UniversalCameraData>();
			UniversalResourceData  resourceData           = frameData.Get<UniversalResourceData>();
			UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();

			Camera camera = universalCameraData.camera;
			float renderScale = universalCameraData.renderScale;
			RenderTextureDescriptor desc = universalCameraData.cameraTargetDescriptor;

			SetupShared(camera, renderScale, desc);

			using (var builder = renderGraph.AddUnsafePass<GBufferPassData>(HNames.HTRACE_GBUFFER_PASS_NAME, out var passData, GBufferProfilingSampler))
			{
				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				TextureHandle depthTexture = resourceData.cameraDepthTexture;

				builder.UseTexture(depthTexture, AccessFlags.Read);

				passData.DepthTexture = depthTexture;

				if (!reconstructNormals)
				{
					TextureHandle normalsTexture = resourceData.cameraNormalsTexture;
					builder.UseTexture(normalsTexture, AccessFlags.Read);
					passData.NormalsTexture = normalsTexture;
				}
				passData.GBufferTextures = resourceData.gBuffer;

				// Register GBuffer textures for deferred rendering modes
				if (universalRenderingData.renderingMode == RenderingMode.Deferred
#if UNITY_6000_1_OR_NEWER
				    || universalRenderingData.renderingMode == RenderingMode.DeferredPlus
#endif
				   )
				{
					if (resourceData.gBuffer.Length >= 6 && resourceData.gBuffer[5].IsValid())
						builder.UseTexture(resourceData.gBuffer[5], AccessFlags.Read);
				}

				builder.SetRenderFunc((GBufferPassData data, UnsafeGraphContext context) =>
				{
					var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
					
					// Depth
					cmd.SetGlobalTexture(HShaderParams.g_HTraceDepth, data.DepthTexture);
					
					// Normals
					if (reconstructNormals)
					{
						int width  = (int)(universalCameraData.camera.scaledPixelWidth * universalCameraData.renderScale);
						int height = (int)(universalCameraData.camera.scaledPixelHeight * universalCameraData.renderScale);

						cmd.SetComputeTextureParam(HDepthPyramidAO, 2, HShaderParams.GeometryNormals_Output, GeometryNormalsFromDepthRT.rt);
						cmd.DispatchCompute(HDepthPyramidAO, 2, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), HRenderer.TextureXrSlices);
					
						cmd.SetGlobalTexture(HShaderParams.g_HTraceGBuffer2, GeometryNormalsFromDepthRT.rt);
					}
					else
					{
						cmd.SetGlobalTexture(HShaderParams.g_HTraceGBuffer2, data.NormalsTexture);
					}
					
					// Render Layer Mask
					if (HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint != URPInjectionPoint.BeforeOpaque)
					{
						Texture renderLayerMaskTexture = (data.GBufferTextures != null && data.GBufferTextures.Length >= 6 && data.GBufferTextures[5].IsValid()) ? (Texture)data.GBufferTextures[5] : Texture2D.blackTexture;
						cmd.SetGlobalTexture(HShaderParams.g_HTraceRenderLayerMask, renderLayerMaskTexture);
					}
					else
					{
						cmd.SetGlobalTexture(HShaderParams.g_HTraceRenderLayerMask, context.defaultResources.blackTexture);
					}
				});
			}
		}
#endif

		#endregion ---------------------------  Render Graph ---------------------------
		
		private static void SetupShared(Camera camera, float renderScale, RenderTextureDescriptor desc)
		{
			if (HDepthPyramidAO == null) HDepthPyramidAO = HTraceAOSettings.ActiveResources.Computes.HDepthPyramidAO;

			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);
			if (desc.width != width || desc.height != height)
				desc = new RenderTextureDescriptor(width, height);

			desc.depthStencilFormat = GraphicsFormat.None;
			desc.enableRandomWrite  = true;
			desc.msaaSamples        = 1;

			ExtensionsURP.ReAllocateIfNeeded(_GeometryNormalsFromDepth, ref GeometryNormalsFromDepthRT, ref desc, graphicsFormat: GraphicsFormat.R8G8B8A8_SNorm);
		}
		
		protected internal void Dispose()
		{
			GeometryNormalsFromDepthRT?.Release();
			GeometryNormalsFromDepthRT = null;
		}
	}
}
