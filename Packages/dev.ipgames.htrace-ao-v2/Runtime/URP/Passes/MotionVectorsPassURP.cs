using System;
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
	internal class MotionVectorsPassURP : ScriptableRenderPass
	{
		// Texture names
		const string _ObjectMotionVectorsColorURP    = "_ObjectMotionVectorsColorURP";
		const string _ObjectMotionVectorsDepthURP    = "_ObjectMotionVectorsDepthURP";
		const string _CustomCameraMotionVectorsURP_0 = "_CustomCameraMotionVectorsURP_0";
		const string _CustomCameraMotionVectorsURP_1 = "_CustomCameraMotionVectorsURP_1";
		const string _MotionVectorsTagID             = "MotionVectors";
		const string _ObjectMotionMask               = "_ObjectMotionMask";
		const string _ObjectMotionDelta              = "_ObjectMotionDelta";

		private static readonly int _ObjectMotionVectorsColor = Shader.PropertyToID("_ObjectMotionVectors");
		private static readonly int _ObjectMotionVectorsDepth = Shader.PropertyToID("_ObjectMotionVectorsDepth");
		private static readonly int _BiasOffset               = Shader.PropertyToID("_BiasOffset");

		// Textures
		internal static RTHandle[] CustomCameraMotionVectorsURP = new RTHandle[2];
		internal static RTHandle   ObjectMotionVectorsColorURP;
		internal static RTHandle   ObjectMotionVectorsDepthURP;
		
		// Materials
		private static  Material MotionVectorsMaterial_URP;
		internal static Material MotionDeltaMaterial;

		private static ProfilingSampler ObjectMotionMaskProfilingSampler    = new ProfilingSampler(HNames.HTRACE_OBJECTS_MOTION_MASK_PASS_NAME);
		private static ProfilingSampler ObjectMotionDeltaProfilingSampler   = new ProfilingSampler(HNames.HTRACE_OBJECTS_MOTION_DELTA_PASS_NAME);
		private static ProfilingSampler CameraMotionVectorsProfilingSampler = new ProfilingSampler(HNames.HTRACE_CAMERA_MOTION_VECTORS_PASS_NAME);

		// Cached to avoid GC allocations
		private static readonly ShaderTagId MotionVectorsShaderTagId = new ShaderTagId(_MotionVectorsTagID);

		
		#region --------------------------- Non Render Graph ---------------------------

#if !UNITY_6000_4_OR_NEWER
		private        ScriptableRenderer _renderer;

		protected internal void Initialize(ScriptableRenderer renderer)
		{
			_renderer  = renderer;
		}

#if UNITY_2023_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			Setup(renderingData.cameraData.camera, renderingData.cameraData.renderScale, renderingData.cameraData.cameraTargetDescriptor);
		}

		private static void Setup(Camera camera, float renderScale, RenderTextureDescriptor desc)
		{
			if (MotionVectorsMaterial_URP == null) MotionVectorsMaterial_URP = new Material(HTraceAOSettings.ActiveResources.URP.MotionVectorsUrpShader);

			int width  = (int)(camera.scaledPixelWidth * renderScale);
			int height = (int)(camera.scaledPixelHeight * renderScale);

			if (desc.width != width || desc.height != height)
				desc = new RenderTextureDescriptor(width, height);

			desc.depthBufferBits    = 0;
			desc.stencilFormat      = GraphicsFormat.None;
			desc.depthStencilFormat = GraphicsFormat.None;
			desc.msaaSamples        = 1;
			desc.bindMS             = false;
			desc.enableRandomWrite  = true;
			
			RenderTextureDescriptor depthDesc = desc;
			depthDesc.depthBufferBits   = 32;
			depthDesc.enableRandomWrite = false;
			depthDesc.colorFormat       = RenderTextureFormat.Depth;
			
			ExtensionsURP.ReAllocateIfNeeded(_CustomCameraMotionVectorsURP_0, ref CustomCameraMotionVectorsURP[0], ref desc, graphicsFormat: GraphicsFormat.R16G16_SFloat);
			ExtensionsURP.ReAllocateIfNeeded(_CustomCameraMotionVectorsURP_1, ref CustomCameraMotionVectorsURP[1], ref desc, graphicsFormat: GraphicsFormat.R8_SNorm);
			ExtensionsURP.ReAllocateIfNeeded(_ObjectMotionVectorsColorURP, ref ObjectMotionVectorsColorURP, ref desc, graphicsFormat: GraphicsFormat.R16G16_SFloat);
			ExtensionsURP.ReAllocateIfNeeded(_ObjectMotionVectorsDepthURP, ref ObjectMotionVectorsDepthURP, ref depthDesc);
		}
		
#if UNITY_2023_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
		{
			if (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.GTAO && HTraceAOSettings.ActiveProfile.GTAOSettings.SampleCountTemporal > 1
			    || HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.RTAO && HTraceAOSettings.ActiveProfile.RTAOSettings.SampleCountTemporal > 1)
				ConfigureInput(ScriptableRenderPassInput.Motion);

		}

#if UNITY_2023_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			var cmd = CommandBufferPool.Get(HNames.HTRACE_MV_PASS_NAME);
			
			Camera camera = renderingData.cameraData.camera;
			
			RenderMotionVectorsNonRenderGraph(cmd, camera, ref renderingData, ref context);
			
			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			CommandBufferPool.Release(cmd);
		}

		private void RenderMotionVectorsNonRenderGraph(CommandBuffer cmd, Camera camera, ref RenderingData renderingData, ref ScriptableRenderContext context)
		{
			void RenderObjectsMotionVectors(ref RenderingData renderingData, ref ScriptableRenderContext context)
			{
#if UNITY_2023_3_OR_NEWER
				if (camera.cameraType == CameraType.SceneView)
					return;
#endif

				CoreUtils.SetRenderTarget(cmd, ObjectMotionVectorsColorURP.rt, ClearFlag.All, Color.clear);
				CoreUtils.SetRenderTarget(cmd, ObjectMotionVectorsDepthURP.rt, ClearFlag.All, Color.clear);
#if UNITY_2023_1_OR_NEWER
				// We'll write not only to our own Color, but also to our own Depth target to use it later (in Camera MV) to compose per-object mv
				CoreUtils.SetRenderTarget(cmd, ObjectMotionVectorsColorURP.rt, ObjectMotionVectorsDepthURP.rt);
#else
				// Prior to 2023 camera motion vectors are rendered directly on objects, so we write to both motion mask and motion vectors via MRT
				RenderTargetIdentifier[] motionVectorsMRT = { CustomCameraMotionVectorsURP[0].rt, CustomCameraMotionVectorsURP[1].rt,};
				CoreUtils.SetRenderTarget(cmd, motionVectorsMRT, ObjectMotionVectorsDepthURP.rt);

#endif // UNITY_2023_1_OR_NEWER

				CullingResults cullingResults = renderingData.cullResults;

				ShaderTagId[] tags
#if UNITY_2023_1_OR_NEWER
				= {new ShaderTagId("MotionVectors")};
#else
				= {new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("LightweightForward"), new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("Meta")};
#endif // UNITY_2023_1_OR_NEWER

				var renderList = new UnityEngine.Rendering.RendererUtils.RendererListDesc(tags, cullingResults, camera)
				{
					rendererConfiguration = PerObjectData.MotionVectors,
					renderQueueRange      = RenderQueueRange.opaque,
					sortingCriteria       = SortingCriteria.CommonOpaque,
					layerMask             = camera.cullingMask,
					overrideMaterial
#if UNITY_2023_1_OR_NEWER
					= null,
#else
					= MotionVectorsMaterial_URP,
					overrideMaterialPassIndex = 1,
					// If somethingis wrong with our custom shader we can always use the standard one (and ShaderPass = 0) instead
					// Material ObjectMotionVectorsMaterial = new Material(Shader.Find("Hidden/Universal Render Pipeline/ObjectMotionVectors"));
					// overrideMaterialPassIndex = 0,
#endif //UNITY_2023_1_OR_NEWER
				};
				
				#if UNITY_6000_3_OR_NEWER
				CoreUtils.DrawRendererList(cmd, context.CreateRendererList(renderList));
				#else
				CoreUtils.DrawRendererList(context, cmd, context.CreateRendererList(renderList));
				#endif

#if !UNITY_2023_1_OR_NEWER
			// Prior to 2023 camera motion vectors are rendered directly on objects, so we will finish mv calculation here and won't execute camera mv
			cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionVectors, CustomCameraMotionVectorsURP[0].rt);
			cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionMask, CustomCameraMotionVectorsURP[1].rt);
			cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionDelta, HRenderer.EmptyTexture);
#endif // UNITY_2023_1_OR_NEWER
			}

			void RenderCameraMotionVectors()
			{
#if UNITY_2023_1_OR_NEWER

				float DepthBiasOffset = 0;
#if UNITY_2023_1_OR_NEWER
				DepthBiasOffset = 0.00099f;
#endif // UNITY_2023_1_OR_NEWER
#if UNITY_6000_0_OR_NEWER
				DepthBiasOffset = 0;
#endif // UNITY_6000_0_OR_NEWER

				// Target target[0] is set as a Depth Buffer, just because this method requires Depth, but we don't care for it in the fullscreen pass
				RenderTargetIdentifier[] motionVectorsMRT = { CustomCameraMotionVectorsURP[0], CustomCameraMotionVectorsURP[1]};
				CoreUtils.SetRenderTarget(cmd, motionVectorsMRT, motionVectorsMRT[0]);

				MotionVectorsMaterial_URP.SetTexture(_ObjectMotionVectorsColor, ObjectMotionVectorsColorURP);
				MotionVectorsMaterial_URP.SetTexture(_ObjectMotionVectorsDepth, ObjectMotionVectorsDepthURP);
				MotionVectorsMaterial_URP.SetFloat(_BiasOffset, DepthBiasOffset);

				cmd.DrawProcedural(Matrix4x4.identity, MotionVectorsMaterial_URP, 0, MeshTopology.Triangles, 3, 1);

				// This restores color camera color target (.SetRenderTarget can be used for Forward + any Depth Priming, but doesn't work in Deferred)
#pragma warning disable CS0618
				ConfigureTarget(_renderer.cameraColorTargetHandle);
#pragma warning restore CS0618
				
				cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionVectors, CustomCameraMotionVectorsURP[0]);
				cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionMask, CustomCameraMotionVectorsURP[1]);
				cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionDelta, HRenderer.EmptyTexture);
#endif // UNITY_2023_1_OR_NEWER
			}

			RenderObjectsMotionVectors(ref renderingData, ref context);
			RenderCameraMotionVectors();
		}
#endif
		
		#endregion --------------------------- Non Render Graph ---------------------------

		#region --------------------------- Render Graph ---------------------------
		
#if UNITY_2023_3_OR_NEWER
		private class ObjectMVPassDataMotionMask
		{
			public RendererListHandle RendererListHandle;
			public TextureHandle      MotionVectorsTexture;
		}

		private class ObjectMVPassDataMotionDelta
		{
			public RendererListHandle RendererListHandle;
			public TextureHandle      MotionDeltaTexture;
		}

		private class CameraMVPassData { }
		
		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			UniversalCameraData    universalCameraData    = frameData.Get<UniversalCameraData>();
			UniversalResourceData  resourceData           = frameData.Get<UniversalResourceData>();
			UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();

			if (MotionDeltaMaterial == null) MotionDeltaMaterial = CoreUtils.CreateEngineMaterial(HTraceAOSettings.ActiveResources.SRP.ObjectMotionDeltaSrpShader);

			ConfigureInput(ScriptableRenderPassInput.Motion | ScriptableRenderPassInput.Depth);

			TextureHandle depthTexture = resourceData.activeDepthTexture;
			TextureHandle motionVectorsTexture = resourceData.motionVectorColor;

			// Create textures that will be shared between passes
			TextureDesc descDepth = depthTexture.GetDescriptor(renderGraph);
			descDepth.colorFormat = GraphicsFormat.R16G16_SFloat;
			descDepth.name = _ObjectMotionMask;
			TextureHandle objectMotionMaskTexture = renderGraph.CreateTexture(descDepth);

			descDepth.name = _ObjectMotionDelta;
			descDepth.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
			descDepth.enableRandomWrite = true;
			TextureHandle objectMotionDeltaTexture = renderGraph.CreateTexture(descDepth);

			// Pass 1: Draw to Motion Mask texture
			using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<ObjectMVPassDataMotionMask>(HNames.HTRACE_OBJECTS_MOTION_MASK_PASS_NAME, out var passData, ObjectMotionMaskProfilingSampler))
			{
				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				if (motionVectorsTexture.IsValid())
					builder.UseTexture(motionVectorsTexture, AccessFlags.Read);
				
				passData.MotionVectorsTexture = motionVectorsTexture;

				AddRendererListMotionMask(renderGraph, universalCameraData, universalRenderingData, passData, builder);

				builder.SetRenderAttachment(objectMotionMaskTexture, 0);
				builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.ReadWrite);
				
				builder.SetGlobalTextureAfterPass(objectMotionMaskTexture, HShaderParams.g_HTraceMotionMask);

				builder.SetRenderFunc((ObjectMVPassDataMotionMask data, RasterGraphContext context) =>
				{		
					RasterCommandBuffer cmd = context.cmd; 
					
					// We set it here instead of using "SetGlobalTextureAfterPass" because otherwise it conflicts with native Motion Blur
					if (data.MotionVectorsTexture.IsValid())
					{
						try { cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionVectors, data.MotionVectorsTexture); }
						catch (InvalidOperationException) { Shader.SetGlobalTexture(HShaderParams.g_HTraceMotionVectors, Texture2D.blackTexture); }
					}

					cmd.ClearRenderTarget(false, true, Color.black);
					cmd.DrawRendererList(data.RendererListHandle);
				});
			}

			// Pass 2: Draw to Motion Delta texture
			using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<ObjectMVPassDataMotionDelta>(HNames.HTRACE_OBJECTS_MOTION_DELTA_PASS_NAME, out var passData, ObjectMotionDeltaProfilingSampler))
			{
				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				AddRendererListMotionDelta(renderGraph, universalCameraData, universalRenderingData, passData, builder);

				builder.SetRenderAttachment(objectMotionDeltaTexture, 0);
				builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.ReadWrite);
				
				builder.SetGlobalTextureAfterPass(objectMotionDeltaTexture, HShaderParams.g_HTraceMotionDelta);
				passData.MotionDeltaTexture = objectMotionDeltaTexture;

				builder.SetRenderFunc((ObjectMVPassDataMotionDelta data, RasterGraphContext context) =>
				{
					RasterCommandBuffer cmd = context.cmd;
					cmd.ClearRenderTarget(false, true, Color.black);
					cmd.DrawRendererList(data.RendererListHandle);
					Shader.SetGlobalTexture(HShaderParams.g_HTraceMotionDelta, data.MotionDeltaTexture);
				});
			}

			// Render Graph + Game View - no need to render camera mv, as they are already available to us in this combination
			if (universalCameraData.cameraType == CameraType.Game)
				return;

			// Pass 3: Draw to Motion Vectors texture (camera motion)
			using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<CameraMVPassData>(HNames.HTRACE_CAMERA_MOTION_VECTORS_PASS_NAME, out var passData, CameraMotionVectorsProfilingSampler))
			{
				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				TextureHandle colorTexture = resourceData.activeColorTexture;
				
				if (MotionVectorsMaterial_URP == null) MotionVectorsMaterial_URP = new Material(HTraceAOSettings.ActiveResources.URP.MotionVectorsUrpShader);
				
				TextureDesc desc = colorTexture.GetDescriptor(renderGraph);
				desc.colorFormat = GraphicsFormat.R16G16_SFloat;
				desc.name  = _CustomCameraMotionVectorsURP_0;
				TextureHandle cameraMotionVectorsTexture = renderGraph.CreateTexture(desc);
			
				builder.SetRenderAttachment(cameraMotionVectorsTexture, 0);
				builder.SetGlobalTextureAfterPass(cameraMotionVectorsTexture, HShaderParams.g_HTraceMotionVectors);
					
				// Per-objects MV can be rendered in Scene View, but they are incorrect, so we overwrite them with our own camera MV
				builder.SetRenderFunc((CameraMVPassData data, RasterGraphContext context) =>
				{
					RasterCommandBuffer cmd = context.cmd;
					
					// These are dummy properties for our shader, because we only need camera MV
					MotionVectorsMaterial_URP.SetTexture(_ObjectMotionVectorsColor, context.defaultResources.blackTexture);
					MotionVectorsMaterial_URP.SetTexture(_ObjectMotionVectorsDepth, context.defaultResources.whiteTexture);
					MotionVectorsMaterial_URP.SetFloat(_BiasOffset, 0);

					cmd.DrawProcedural(Matrix4x4.identity, MotionVectorsMaterial_URP, 0, MeshTopology.Triangles, 3, 1);
				});
			} 
		}
		
		private static void AddRendererListMotionMask(RenderGraph renderGraph, UniversalCameraData universalCameraData, UniversalRenderingData universalRenderingData, ObjectMVPassDataMotionMask passData, IRasterRenderGraphBuilder builder)
		{
			RenderStateBlock MovingObjectsRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
			MovingObjectsRenderStateBlock.depthState =  new DepthState(false, CompareFunction.Equal);
			MovingObjectsRenderStateBlock.mask       |= RenderStateMask.Depth;

			var renderList = new UnityEngine.Rendering.RendererUtils.RendererListDesc(MotionVectorsShaderTagId, universalRenderingData.cullResults, universalCameraData.camera)
			{
				rendererConfiguration = PerObjectData.MotionVectors,
				renderQueueRange      = RenderQueueRange.opaque,
				sortingCriteria       = SortingCriteria.CommonOpaque,
				stateBlock            = MovingObjectsRenderStateBlock,
				layerMask             = universalCameraData.camera.cullingMask,
			};

			passData.RendererListHandle = renderGraph.CreateRendererList(renderList);
			builder.UseRendererList(passData.RendererListHandle);
		}

		private static void AddRendererListMotionDelta(RenderGraph renderGraph, UniversalCameraData universalCameraData, UniversalRenderingData universalRenderingData, ObjectMVPassDataMotionDelta passData, IRasterRenderGraphBuilder builder)
		{
			RenderStateBlock MovingObjectsRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
			MovingObjectsRenderStateBlock.depthState =  new DepthState(false, CompareFunction.Equal);
			MovingObjectsRenderStateBlock.mask       |= RenderStateMask.Depth;

			var renderList = new UnityEngine.Rendering.RendererUtils.RendererListDesc(MotionVectorsShaderTagId, universalRenderingData.cullResults, universalCameraData.camera)
			{
				rendererConfiguration = PerObjectData.MotionVectors,
				renderQueueRange      = RenderQueueRange.opaque,
				sortingCriteria       = SortingCriteria.CommonOpaque,
				stateBlock            = MovingObjectsRenderStateBlock,
				layerMask             = universalCameraData.camera.cullingMask,
				overrideMaterial      = MotionDeltaMaterial,
			};

			passData.RendererListHandle = renderGraph.CreateRendererList(renderList);
			builder.UseRendererList(passData.RendererListHandle);
		}
#endif
		
		#endregion ---------------------------  Render Graph ---------------------------

		#region --------------------------- Shared ---------------------------

		protected internal void Dispose()
		{
			CustomCameraMotionVectorsURP[0]?.Release();
			CustomCameraMotionVectorsURP[1]?.Release();
			ObjectMotionVectorsColorURP?.Release();
			ObjectMotionVectorsDepthURP?.Release();
		}

		#endregion --------------------------- Shared ---------------------------
	}
}
