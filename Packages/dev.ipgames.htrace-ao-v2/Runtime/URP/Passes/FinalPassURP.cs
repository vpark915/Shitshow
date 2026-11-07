using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Globals;
using IPGames.HTraceAO.Runtime.Passes;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule; //using UnityEngine.Rendering.RenderGraphModule; UNITY_2023_3_OR_NEWER
#else
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace IPGames.HTraceAO.Runtime.URP.Passes
{
	internal class FinalPassURP : ScriptableRenderPass
	{
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
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			if (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.SSAO && HTraceAOSettings.ActiveProfile.SSAOSettings.DebugModeSSAO == DebugModeSSAO.None ||
			    HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.GTAO && HTraceAOSettings.ActiveProfile.GTAOSettings.DebugMode == DebugModeGTAO.None ||
			    HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.RTAO && HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode == DebugModeRTAO.None)
				return;

			var cmd = CommandBufferPool.Get(HNames.HTRACE_FINAL_PASS_NAME);

			RTHandle debugInput = null;

			if (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.SSAO)
				debugInput = SSAO.DebugSSAO.rt;

			if (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.GTAO)
				debugInput = GTAO.DebugGTAO.rt;

			if (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.RTAO)
				debugInput = RTAO.DebugRTAO.rt;

			Blitter.BlitCameraTexture(cmd, debugInput, _renderer.cameraColorTargetHandle);

			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			CommandBufferPool.Release(cmd);
		}
#endif
		
		#endregion --------------------------- Non Render Graph ---------------------------

		#region --------------------------- Render Graph ---------------------------

#if UNITY_2023_3_OR_NEWER
		private readonly ProfilingSampler FinalSampler = new ProfilingSampler(HNames.HTRACE_FINAL_PASS_NAME);
		
		private class PassData
		{
			public TextureHandle ColorTexture;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			using (var builder = renderGraph.AddUnsafePass<PassData>(HNames.HTRACE_FINAL_PASS_NAME, out var passData, FinalSampler))
			{
				UniversalResourceData  resourceData           = frameData.Get<UniversalResourceData>();
				UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);

				TextureHandle colorTexture = universalRenderingData.renderingMode == RenderingMode.Deferred ? resourceData.activeColorTexture : resourceData.cameraColor;
				if (!colorTexture.IsValid())
					return;

				passData.ColorTexture = colorTexture;

				builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
			}
		}

		private static void ExecutePass(PassData data, UnsafeGraphContext rgContext)
		{
			if (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.SSAO && HTraceAOSettings.ActiveProfile.SSAOSettings.DebugModeSSAO == DebugModeSSAO.None ||
			    HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.GTAO && HTraceAOSettings.ActiveProfile.GTAOSettings.DebugMode == DebugModeGTAO.None ||
			    HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.RTAO && HTraceAOSettings.ActiveProfile.RTAOSettings.DebugMode == DebugModeRTAO.None)
				return;

			var cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);

			RTHandle debugInput = null;

			if (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.SSAO)
				debugInput = SSAO.DebugSSAO.rt;

			if (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.GTAO)
				debugInput = GTAO.DebugGTAO.rt;

			if (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode == AmbientOcclusionMode.RTAO)
				debugInput = RTAO.DebugRTAO.rt;

			Blitter.BlitCameraTexture(cmd, debugInput, data.ColorTexture); 
		}
#endif

		#endregion --------------------------- Render Graph ---------------------------

		protected internal void Dispose()
		{
		}
	}
}
