using System;
using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Extensions.CameraHistorySystem;
using IPGames.HTraceAO.Runtime.Globals;
using IPGames.HTraceAO.Runtime.Passes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule; //using UnityEngine.Rendering.RenderGraphModule; UNITY_2023_3_OR_NEWER
#else
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif


namespace IPGames.HTraceAO.Runtime.URP.Passes
{
	internal class PrePassURP : ScriptableRenderPass
	{
		private struct HistoryCameraData : ICameraHistoryData
		{
			private int       hash;
			public  Matrix4x4 previousViewMatrix;
			public  Matrix4x4 previousViewProjMatrix;
			public  Matrix4x4 previousInvViewProjMatrix;
			public  Vector4[] CameraPositions;
			public  Vector2   ZBufferParams;

			public int GetHash() => hash;
			public void SetHash(int hashIn) => this.hash = hashIn;
		}

		private static readonly CameraHistorySystem<HistoryCameraData> CameraHistorySystem = new CameraHistorySystem<HistoryCameraData>();
		private static int s_FrameCount = 0;

		#region --------------------------- Non Render Graph ---------------------------

#if !UNITY_6000_4_OR_NEWER
		private        ScriptableRenderer _renderer;

		protected internal void Initialize(ScriptableRenderer renderer)
		{
			_renderer = renderer;
		}

#if UNITY_2023_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			Camera camera = renderingData.cameraData.camera;

			CameraHistorySystem.SyncCamera(camera.GetHashCode(), Time.frameCount);
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
			var cmd = CommandBufferPool.Get(HNames.HTRACE_PRE_PASS_NAME);

			Camera camera = renderingData.cameraData.camera;

			// Re-sync here in case another camera's OnCameraSetup ran between our OnCameraSetup and Execute
			CameraHistorySystem.SyncCamera(camera.GetHashCode(), Time.frameCount);

			// Set URP specific AO parameters
			cmd.SetGlobalVector(HShaderParams.AmbientOcclusionParam, new Vector4(1.0f, 0.0f, 0.0f, HTraceAOSettings.ActiveProfile.GeneralSettings.DirectLightOcclusion));
			
			Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(renderingData.cameraData.GetProjectionMatrix(), true);
			Matrix4x4 projMatrixNoJitter = renderingData.cameraData.GetGPUProjectionMatrixNoJitter(0);
			Matrix4x4 viewMatrix = renderingData.cameraData.GetViewMatrix();
			Matrix4x4 viewProjMatrix = projMatrix * viewMatrix;
			Matrix4x4 invViewProjMatrix = Matrix4x4.Inverse(viewProjMatrix);

			// Detect if jittering is enabled by comparing jittered vs non-jittered projection
			bool jitteringEnabled = projMatrix != projMatrixNoJitter;
			cmd.SetGlobalInt(HShaderParams.JitteringIsEnabled, jitteringEnabled ? 1 : 0);
			
			// Update History data
			{
				ref var cameraData = ref CameraHistorySystem.GetCameraData();
				
				cmd.SetGlobalMatrix(HShaderParams.H_MATRIX_I_VP, invViewProjMatrix);
				ref var previousViewProjMatrix = ref CameraHistorySystem.GetCameraData().previousViewProjMatrix;
				cmd.SetGlobalMatrix(HShaderParams.H_MATRIX_PREV_VP, previousViewProjMatrix);
				ref var previousInvViewProjMatrix = ref CameraHistorySystem.GetCameraData().previousInvViewProjMatrix;
				cmd.SetGlobalMatrix(HShaderParams.H_MATRIX_PREV_I_VP, previousInvViewProjMatrix);
				ref var previousViewMatrix = ref CameraHistorySystem.GetCameraData().previousViewMatrix;
				cmd.SetGlobalMatrix(HShaderParams.H_MATRIX_PREV_V, previousViewMatrix); 
				
				previousViewMatrix        = camera.worldToCameraMatrix;
				previousViewProjMatrix    = viewProjMatrix;
				previousInvViewProjMatrix = invViewProjMatrix;
				
				if (cameraData.CameraPositions == null) cameraData.CameraPositions = new Vector4[6];
				cmd.SetGlobalVectorArray(HShaderParams.CameraPositionsPrevious, cameraData.CameraPositions);
				Array.Copy(cameraData.CameraPositions, 0, cameraData.CameraPositions, 1, 5);
				cameraData.CameraPositions[0] = camera.transform.position;
				
				cmd.SetGlobalVector(HShaderParams.ZBufferParamsPrev, cameraData.ZBufferParams);
				cameraData.ZBufferParams.x = (camera.farClipPlane - camera.nearClipPlane) / (camera.nearClipPlane * camera.farClipPlane);
				cameraData.ZBufferParams.y = 1.0f / camera.farClipPlane;
			}

			// Update Frame Counter
			cmd.SetGlobalInt(HShaderParams.FrameCount, s_FrameCount);
			s_FrameCount++;

			// Set Blue Noise texures
			HBlueNoise.SetTextures(cmd);
			
			// Set Motion Vectors & Delta dummies in case we don't run MV pass (e.g. Denoising = None, etc)
			cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionMask, HRenderer.EmptyTexture);
			cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionDelta, HRenderer.EmptyTexture);
			cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionVectors, HRenderer.EmptyTexture);
			
			// Set AO dummy in case we don't run the main AO pass (e.g. first frame skip due to some resources missing, etc)
			cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO, HRenderer.EmptyTexture);

			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			CommandBufferPool.Release(cmd);
		}
#endif

		#endregion --------------------------- Non Render Graph ---------------------------

		#region --------------------------- Render Graph ---------------------------

#if UNITY_2023_3_OR_NEWER
		private readonly ProfilingSampler PrePassSampler = new ProfilingSampler(HNames.HTRACE_PRE_PASS_NAME);
		
		RTHandle OwenScrambledRTHandle;
		RTHandle ScramblingTileXSPPRTHandle;
		RTHandle RankingTileXSPPRTHandle;
		RTHandle ScramblingTextureRTHandle;
		RTHandle STBNTextureRTHandle;
		RTHandle STBNTexture2DRTHandle;

		private class PassData
		{
			public RendererListHandle  RendererListHandle;
			public Camera              Camera;
			public Matrix4x4           ViewProjMatrix;
			public Matrix4x4           InvViewProjMatrix;
			public bool                JitteringEnabled;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			using (var builder = renderGraph.AddRasterRenderPass<PassData>(HNames.HTRACE_PRE_PASS_NAME, out var passData, PrePassSampler))
			{
				UniversalCameraData universalCameraData = frameData.Get<UniversalCameraData>();

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				Camera camera = universalCameraData.camera;

				// Compute matrices here during setup phase, not in execute (it causes errors)
				// Use GetProjectionMatrix() which includes jitter, then apply GL.GetGPUProjectionMatrix
				Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(universalCameraData.GetProjectionMatrix(), true);
				Matrix4x4 projMatrixNoJitter = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true);
				Matrix4x4 viewMatrix = universalCameraData.GetViewMatrix();
				Matrix4x4 viewProjMatrix = projMatrix * viewMatrix;
				Matrix4x4 invViewProjMatrix = Matrix4x4.Inverse(viewProjMatrix);

				passData.Camera            = camera;
				passData.ViewProjMatrix    = viewProjMatrix;
				passData.InvViewProjMatrix = invViewProjMatrix;
				passData.JitteringEnabled  = projMatrix != projMatrixNoJitter;

				CameraHistorySystem.SyncCamera(camera.GetHashCode(), Time.frameCount);

				// Set Blue Noise texures
				if (OwenScrambledRTHandle == null) OwenScrambledRTHandle = RTHandles.Alloc(HBlueNoise.OwenScrambledTexture);
				TextureHandle owenScrambledTextureHandle = renderGraph.ImportTexture(OwenScrambledRTHandle);
				builder.SetGlobalTextureAfterPass(owenScrambledTextureHandle, HBlueNoise.g_OwenScrambledTexture);

				if (ScramblingTileXSPPRTHandle == null) ScramblingTileXSPPRTHandle = RTHandles.Alloc(HBlueNoise.ScramblingTileXSPP);
				TextureHandle scramblingTileXSPPTextureHandle = renderGraph.ImportTexture(ScramblingTileXSPPRTHandle);
				builder.SetGlobalTextureAfterPass(scramblingTileXSPPTextureHandle, HBlueNoise.g_ScramblingTileXSPP);

				if (RankingTileXSPPRTHandle == null) RankingTileXSPPRTHandle = RTHandles.Alloc(HBlueNoise.RankingTileXSPP);
				TextureHandle rankingTileXSPPTextureHandle = renderGraph.ImportTexture(RankingTileXSPPRTHandle);
				builder.SetGlobalTextureAfterPass(rankingTileXSPPTextureHandle, HBlueNoise.g_RankingTileXSPP);

				if (ScramblingTextureRTHandle == null) ScramblingTextureRTHandle = RTHandles.Alloc(HBlueNoise.ScramblingTexture);
				TextureHandle scramblingTextureHandle = renderGraph.ImportTexture(ScramblingTextureRTHandle);
				builder.SetGlobalTextureAfterPass(scramblingTextureHandle, HBlueNoise.g_ScramblingTexture);
				
				if (STBNTextureRTHandle == null) STBNTextureRTHandle = RTHandles.Alloc(HBlueNoise.STBNTexture);
				TextureHandle STBNTextureHandle = renderGraph.ImportTexture(STBNTextureRTHandle);
				builder.SetGlobalTextureAfterPass(STBNTextureHandle, HBlueNoise.g_STBNTexture);
				
				if (STBNTexture2DRTHandle == null) STBNTexture2DRTHandle = RTHandles.Alloc(HBlueNoise.STBNTexture2D);
				TextureHandle STBNTexture2DHandle = renderGraph.ImportTexture(STBNTexture2DRTHandle);
				builder.SetGlobalTextureAfterPass(STBNTexture2DHandle, HBlueNoise.g_STBNTexture2D);

				builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
			}
		}

		private static void ExecutePass(PassData data, RasterGraphContext context)
		{
			var cmd = context.cmd;

			Camera camera = data.Camera;
			Matrix4x4 viewProjMatrix = data.ViewProjMatrix;
			Matrix4x4 invViewProjMatrix = data.InvViewProjMatrix;

			// Set URP specific AO parameters
			Vector4 occlusionParameters = new Vector4(1.0f, 0.0f, 0.0f, HTraceAOSettings.ActiveProfile.GeneralSettings.DirectLightOcclusion);
			cmd.SetGlobalVector(HShaderParams.AmbientOcclusionParam, occlusionParameters);

			// Set jittering detection
			cmd.SetGlobalInt(HShaderParams.JitteringIsEnabled, data.JitteringEnabled ? 1 : 0);

			// Update Hisotry data
			{
				ref var cameraData = ref CameraHistorySystem.GetCameraData();
				
				cmd.SetGlobalMatrix(HShaderParams.H_MATRIX_I_VP, invViewProjMatrix);
				ref var previousViewProjMatrix = ref CameraHistorySystem.GetCameraData().previousViewProjMatrix;
				cmd.SetGlobalMatrix(HShaderParams.H_MATRIX_PREV_VP, previousViewProjMatrix);
				ref var previousInvViewProjMatrix = ref CameraHistorySystem.GetCameraData().previousInvViewProjMatrix;
				cmd.SetGlobalMatrix(HShaderParams.H_MATRIX_PREV_I_VP, previousInvViewProjMatrix);
				ref var previousViewMatrix = ref CameraHistorySystem.GetCameraData().previousViewMatrix;
				cmd.SetGlobalMatrix(HShaderParams.H_MATRIX_PREV_V, previousViewMatrix);

				previousViewMatrix        = camera.worldToCameraMatrix;
				previousViewProjMatrix    = viewProjMatrix;
				previousInvViewProjMatrix = invViewProjMatrix;
				
				if (cameraData.CameraPositions == null) cameraData.CameraPositions = new Vector4[6];

				cmd.SetGlobalVectorArray(HShaderParams.CameraPositionsPrevious, cameraData.CameraPositions);
				Array.Copy(cameraData.CameraPositions, 0, cameraData.CameraPositions, 1, 5);
				cameraData.CameraPositions[0] = camera.transform.position;
				
				cmd.SetGlobalVector(HShaderParams.ZBufferParamsPrev, cameraData.ZBufferParams);
				cameraData.ZBufferParams.x = (camera.farClipPlane - camera.nearClipPlane) / (camera.nearClipPlane * camera.farClipPlane);
				cameraData.ZBufferParams.y = 1.0f / camera.farClipPlane;
				
			}

			// Update Frame Counter
			cmd.SetGlobalInt(HShaderParams.FrameCount, s_FrameCount);
			s_FrameCount++;
			
			// Set Motion Vectors & Delta dummies in case we don't run MV pass (e.g. Denoising = None, etc)
			cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionMask, context.defaultResources.blackTexture);
			cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionDelta, context.defaultResources.blackTexture);
			cmd.SetGlobalTexture(HShaderParams.g_HTraceMotionVectors, context.defaultResources.blackTexture);
			
			// Set AO dummy in case we don't run the main AO pass (e.g. first frame skip due to some resources missing, etc)
			cmd.SetGlobalTexture(HShaderParams.g_HTraceBufferAO,  context.defaultResources.whiteTexture);
            
#if UNITY_6000_4_OR_NEWER
			cmd.EnableShaderKeyword(HShaderParams._SCREEN_SPACE_OCCLUSION);
#endif
		}

#endif

		#endregion ---------------------------  Render Graph ---------------------------

		protected internal void Dispose()
		{
#if UNITY_2023_3_OR_NEWER
			OwenScrambledRTHandle?.Release();
			ScramblingTileXSPPRTHandle?.Release();
			RankingTileXSPPRTHandle?.Release();
			ScramblingTextureRTHandle?.Release();
#endif
			s_FrameCount = 0;
		}
	}
}
