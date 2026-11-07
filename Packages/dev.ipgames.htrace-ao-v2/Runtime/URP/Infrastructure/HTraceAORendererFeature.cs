using IPGames.HTraceAO.Runtime.Data.Profile;
using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Globals;
using IPGames.HTraceAO.Runtime.PipelelinesConfigurator;
using IPGames.HTraceAO.Runtime.RuntimeResources;
using IPGames.HTraceAO.Runtime.URP.Passes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IPGames.HTraceAO.Runtime.URP.Infrastructure
{
	[DisallowMultipleRendererFeature]
	[ExecuteAlways]
	[HelpURL(HNames.HTRACE_AO_DOCUMENTATION_LINK)]
	public class HTraceAORendererFeature :
#if UNITY_6000_0_OR_NEWER
		ScreenSpaceAmbientOcclusion
#else
		ScriptableRendererFeature
#endif
	{
		public          bool UseVolumes = false;
		public static bool IsUseVolumes { get; private set; }
		
		private PrePassURP       _prePass;
		private MotionVectorsPassURP _motionVectorsPass;
		private GBufferPassURP   _gBufferPass;
		private SSAOPassURP      _ssaoPass;
		private GTAOPassURP      _gtaoPass;
		private RTAOPassURP      _rtaoPass;
		private FinalPassURP     _finalPass;
		
		[SerializeField, HideInInspector] private HTraceAOResources Resources;
		
		private bool _initialized = false;
#if !UNITY_6000_4_OR_NEWER
		private bool _resourcesReady = false;
#endif

		private AmbientOcclusionMode _previousAmbientOcclusionMode;

		/// <summary>
		/// Initializes this feature's resources. This is called every time serialization happens.
		/// </summary>
		public override void Create()
		{
			name = HNames.ASSET_NAME_FULL;
			// ActiveProfile = Profile;
			IsUseVolumes = UseVolumes;
			Dispose();

			if (UseVolumes)
			{
				var stack = VolumeManager.instance.stack;
				HTraceAOVolume hTraceVolume = stack?.GetComponent<HTraceAOVolume>();
				SettingsBuild(hTraceVolume);
			}

			_previousAmbientOcclusionMode = HTraceAOSettings.ActiveProfile != null ? HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode : AmbientOcclusionMode.GTAO; //is it not strange?

			var InjectionPoint = HTraceAOSettings.ActiveProfile != null
				? HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint switch
				{
					URPInjectionPoint.BeforeOpaque  => RenderPassEvent.BeforeRenderingOpaques,
					URPInjectionPoint.AfterOpaque    => RenderPassEvent.AfterRenderingOpaques,
					_                                => RenderPassEvent.BeforeRenderingDeferredLights,
				}
				: RenderPassEvent.BeforeRenderingDeferredLights;

			_prePass                 = new PrePassURP();
			_prePass.renderPassEvent = InjectionPoint;
			switch (_previousAmbientOcclusionMode)
			{
				case AmbientOcclusionMode.SSAO:
					_gBufferPass                 = new GBufferPassURP();
					_gBufferPass.renderPassEvent = InjectionPoint;
					_ssaoPass                    = new SSAOPassURP();
					_ssaoPass.renderPassEvent    = InjectionPoint;
					break;
				case AmbientOcclusionMode.GTAO:
					_motionVectorsPass                 = new MotionVectorsPassURP();
					_motionVectorsPass.renderPassEvent = InjectionPoint;
					_gBufferPass                   = new GBufferPassURP();
					_gBufferPass.renderPassEvent   = InjectionPoint;
					_gtaoPass                      = new GTAOPassURP();
					_gtaoPass.renderPassEvent      = InjectionPoint;
					break;
				case AmbientOcclusionMode.RTAO:
					_motionVectorsPass                 = new MotionVectorsPassURP();
					_motionVectorsPass.renderPassEvent = InjectionPoint;
					_gBufferPass                   = new GBufferPassURP();
					_gBufferPass.renderPassEvent   = InjectionPoint;
					_rtaoPass                      = new RTAOPassURP();
					_rtaoPass.renderPassEvent      = InjectionPoint;
					break;
			}
			_finalPass                     = new FinalPassURP();
			_finalPass.renderPassEvent     = RenderPassEvent.AfterRenderingPostProcessing;

#if UNITY_EDITOR
			HPipelinesConfigurator.AlwaysIncludedShaders();
#endif

			_initialized = true;
		}

		private bool EnsureResources()
		{
			if (Resources.IsValid)
			{
				HTraceAOSettings.SetResources(Resources);
#if !UNITY_6000_4_OR_NEWER
				_resourcesReady = true;
#endif
				return true;
			}

#if UNITY_EDITOR
			if (EditorApplication.isCompiling || EditorApplication.isUpdating)
				return false;

			if (Resources.Initialize())
				EditorUtility.SetDirty(this);
#endif

			if (Resources.IsValid == false)
			{
#if !UNITY_6000_4_OR_NEWER
				_resourcesReady = false;
#endif
				return false;
			}

			HTraceAOSettings.SetResources(Resources);
#if !UNITY_6000_4_OR_NEWER
			_resourcesReady = true;
#endif
			return true;
		}

#if !UNITY_6000_4_OR_NEWER
		/// <summary>
		/// Called when render targets are allocated and ready to be used.
		/// </summary>
		/// <param name="renderer"></param>
		/// <param name="renderingData"></param>
		/// <!--https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@13.1/manual/upgrade-guide-2022-1.html-->
#if UNITY_6000_3_OR_NEWER
		[System.Obsolete]
#endif
		public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
		{
			if (_initialized == false || _resourcesReady == false)
				return;

			if (HRenderer.RenderGraphEnabled == false)
			{
				_prePass.Initialize(renderer);
				switch (_previousAmbientOcclusionMode)
				{
					case AmbientOcclusionMode.SSAO:
						_gBufferPass.Initialize(renderer);
						_ssaoPass.Initialize(renderer);
						break;
					case AmbientOcclusionMode.GTAO:
						_motionVectorsPass.Initialize(renderer);
						_gBufferPass.Initialize(renderer);
						_gtaoPass.Initialize(renderer);
						break;
					case AmbientOcclusionMode.RTAO:
						_motionVectorsPass.Initialize(renderer);
						_gBufferPass.Initialize(renderer);
						_rtaoPass.Initialize(renderer);
						break;
				}
				_finalPass.Initialize(renderer);
			}
		}
#endif

		/// <summary>
		/// Injects one or multiple ScriptableRenderPass in the renderer.
		/// </summary>
		/// <param name="renderer"></param>
		/// <param name="renderingData"></param>
		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			Shader.DisableKeyword(HShaderParams._SCREEN_SPACE_OCCLUSION);
			
			if (renderingData.cameraData.cameraType == CameraType.Reflection || renderingData.cameraData.cameraType == CameraType.Preview)
				return;
			
			var isActive = HTraceAOSettings.ActiveProfile != null;
			
			IsUseVolumes = UseVolumes;
			if (UseVolumes)
			{
				var stack        = VolumeManager.instance.stack;
				var hTraceVolume = stack.GetComponent<HTraceAOVolume>();
				isActive = hTraceVolume != null && hTraceVolume.IsActive();
				
				SettingsBuild(hTraceVolume);
			}
			
			if (!isActive)
				return;

			if (EnsureResources() == false)
				return;
			
			Shader.EnableKeyword(HShaderParams._SCREEN_SPACE_OCCLUSION); //HTraceAO specific
			
#if !UNITY_2022_1_OR_NEWER
			_prePass.Initialize(renderer);
			switch (_previousAmbientOcclusionMode)
			{
				case AmbientOcclusionMode.SSAO:
					_gBufferPass.Initialize(renderer);
					_ssaoPass.Initialize(renderer);
					break;
				case AmbientOcclusionMode.GTAO:
					_motionVectors.Initialize(renderer);
					_gBufferPass.Initialize(renderer);
					_gtaoPass.Initialize(renderer);
					break;
				case AmbientOcclusionMode.RTAO:
					_motionVectors.Initialize(renderer);
					_gBufferPass.Initialize(renderer);
					_rtaoPass.Initialize(renderer);
					break;
			}
			_finalPass.Initialize(renderer);
#endif
			if (_previousAmbientOcclusionMode != HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode || _initialized == false)
			{
				Create();
			}

			var injectionPoint = HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint switch
			{
				URPInjectionPoint.BeforeOpaque => RenderPassEvent.BeforeRenderingGbuffer,
				URPInjectionPoint.AfterOpaque   => RenderPassEvent.AfterRenderingOpaques,
				_                               => RenderPassEvent.BeforeRenderingDeferredLights,
			};

			_prePass.renderPassEvent = injectionPoint;
			renderer.EnqueuePass(_prePass);
			switch (HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode)
			{
				case AmbientOcclusionMode.SSAO:
					_gBufferPass.renderPassEvent = injectionPoint;
					_ssaoPass.renderPassEvent    = injectionPoint;
					renderer.EnqueuePass(_gBufferPass);
					renderer.EnqueuePass(_ssaoPass);
					break;
				case AmbientOcclusionMode.GTAO:
					_motionVectorsPass.renderPassEvent = injectionPoint;
					_gBufferPass.renderPassEvent   = injectionPoint;
					_gtaoPass.renderPassEvent      = injectionPoint;
					if (HTraceAOSettings.ActiveProfile.GTAOSettings.SampleCountTemporal > 1 && (HTraceAOSettings.ActiveProfile.GTAOSettings.Denoising == GTAODenoisingMode.TemporalOnly
					                                                                        || HTraceAOSettings.ActiveProfile.GTAOSettings.Denoising == GTAODenoisingMode.SpatioTemporal))
						renderer.EnqueuePass(_motionVectorsPass);
					renderer.EnqueuePass(_gBufferPass);
					renderer.EnqueuePass(_gtaoPass);
					break;
				case AmbientOcclusionMode.RTAO:
					_motionVectorsPass.renderPassEvent = injectionPoint;
					_gBufferPass.renderPassEvent   = injectionPoint;
					_rtaoPass.renderPassEvent      = injectionPoint;
					if (HTraceAOSettings.ActiveProfile.RTAOSettings.SampleCountTemporal > 1 && HTraceAOSettings.ActiveProfile.RTAOSettings.Denoising != RTAODenoisingMode.None)
						renderer.EnqueuePass(_motionVectorsPass);
					renderer.EnqueuePass(_gBufferPass);
					renderer.EnqueuePass(_rtaoPass);
					break;
			}
			renderer.EnqueuePass(_finalPass);

			_previousAmbientOcclusionMode = HTraceAOSettings.ActiveProfile.GeneralSettings.AmbientOcclusionMode;
		}

		private void SettingsBuild(HTraceAOVolume hTraceVolume)
		{
			if (hTraceVolume == null || UseVolumes == false)
				return;

			if (HTraceAOSettings.ActiveProfile == null)
			{
				HTraceAOSettings.SetActiveProfile(ScriptableObject.CreateInstance<HTraceAOProfile>());
				HTraceAOSettings.ActiveProfile.name = "New HTrace AO Profile";
			}

			var activeProfile = HTraceAOSettings.ActiveProfile;

			// General Settings
			activeProfile.GeneralSettings.AmbientOcclusionMode = hTraceVolume.AmbientOcclusionMode.value;
			activeProfile.GeneralSettings.HBuffer              = hTraceVolume.HBuffer.value;
			activeProfile.GeneralSettings.Intensity            = hTraceVolume.Intensity.value;
			activeProfile.GeneralSettings.Fadeout              = hTraceVolume.Fadeout.value;
			activeProfile.GeneralSettings.DirectLightOcclusion = hTraceVolume.DirectLightingOcclusion.value;
			activeProfile.GeneralSettings.InjectionPoint      = hTraceVolume.InjectionPoint.value;
			activeProfile.GeneralSettings.ReconstructNormals  = hTraceVolume.ReconstructNormals.value;

			// SSAO Settings
			activeProfile.SSAOSettings.DebugModeSSAO     = hTraceVolume.DebugModeSSAO.value;
			activeProfile.SSAOSettings.Thickness         = hTraceVolume.Thickness.value;
			activeProfile.SSAOSettings.Radius            = hTraceVolume.Radius.value;
#if UNITY_2023_3_OR_NEWER
			activeProfile.SSAOSettings.ExcludeCasting    = hTraceVolume.SSAOExcludeCasting.value;
			activeProfile.SSAOSettings.ExcludeReceiving  = hTraceVolume.SSAOExcludeReceiving.value;
			activeProfile.SSAOSettings.ExcludedIntensity = hTraceVolume.SSAOExcludedIntensity.value;
#endif

			// GTAO Settings
			activeProfile.GTAOSettings.DebugMode                  = hTraceVolume.DebugModeGTAO.value;
			activeProfile.GTAOSettings.Resolution                 = hTraceVolume.GTAOResolution.value;
			activeProfile.GTAOSettings.Thickness                  = hTraceVolume.GTAOThickness.value;
			activeProfile.GTAOSettings.WorldSpaceRadius           = hTraceVolume.GTAOWorldSpaceRadius.value;
			activeProfile.GTAOSettings.ScreenSpaceRadius          = hTraceVolume.GTAOScreenSpaceRadius.value;
			activeProfile.GTAOSettings.SliceCount                 = hTraceVolume.GTAOSliceCount.value;
			activeProfile.GTAOSettings.StepCount                  = hTraceVolume.GTAOStepCount.value;
			activeProfile.GTAOSettings.TracingMode                = hTraceVolume.GTAOTracingMode.value;
			activeProfile.GTAOSettings.DepthFormat                = hTraceVolume.GTAODepthFormat.value;
			activeProfile.GTAOSettings.ThicknessMode              = hTraceVolume.GTAOThicknessMode.value;
			activeProfile.GTAOSettings.UseAttenuation             = hTraceVolume.GTAOUseAttenuation.value;
			activeProfile.GTAOSettings.Denoising                  = hTraceVolume.GTAODenoising.value;
			activeProfile.GTAOSettings.SampleCountTemporal        = hTraceVolume.GTAOSampleCountTemporal.value;
			activeProfile.GTAOSettings.UseSimpleRejection         = hTraceVolume.GTAOUseSimpleRejection.value;
			activeProfile.GTAOSettings.TemporalRejection          = hTraceVolume.GTAOTemporalRejection.value;
			activeProfile.GTAOSettings.ReprojectionFilter         = hTraceVolume.GTAOReprojectionFilter.value;
			activeProfile.GTAOSettings.UseNormalWeightingTemporal = hTraceVolume.GTAOUseNormalWeightingTemporal.value;
			activeProfile.GTAOSettings.SpatialFilterType          = hTraceVolume.GTAOSpatialFilterType.value;
			activeProfile.GTAOSettings.FilterRadius               = hTraceVolume.GTAOFilterRadius.value;
			activeProfile.GTAOSettings.FilterAdaptivity           = hTraceVolume.GTAOFilterAdaptivity.value;
			activeProfile.GTAOSettings.BoxPassCount               = hTraceVolume.GTAOBoxPassCount.value;
			activeProfile.GTAOSettings.DoubleSampleCount          = hTraceVolume.GTAODoubleSampleCount.value;
#if UNITY_2023_3_OR_NEWER
			activeProfile.GTAOSettings.ExcludeCasting             = hTraceVolume.GTAOExcludeCasting.value;
			activeProfile.GTAOSettings.ExcludeReceiving           = hTraceVolume.GTAOExcludeReceiving.value;
			activeProfile.GTAOSettings.ExcludedIntensity          = hTraceVolume.GTAOExcludedIntensity.value;
			activeProfile.GTAOSettings.EarlyOutExcludedReceivers  = hTraceVolume.GTAOEarlyOutExcludedReceivers.value;
#endif

			// RTAO Settings
			activeProfile.RTAOSettings.DebugMode                  = hTraceVolume.DebugModeRTAO.value;
			activeProfile.RTAOSettings.WorldSpaceRadius           = hTraceVolume.RTAOWorldSpaceRadius.value;
			activeProfile.RTAOSettings.MaxRayBias                 = hTraceVolume.RTAOMaxRayBias.value;
			activeProfile.RTAOSettings.SampleRebalancing          = hTraceVolume.RTAOSampleRebalancing.value;
			activeProfile.RTAOSettings.CullBackfaces              = hTraceVolume.RTAOCullBackfaces.value;
			activeProfile.RTAOSettings.CullAlphaCutout            = hTraceVolume.RTAOCullAlphaCutout.value;
			activeProfile.RTAOSettings.CullingMask                = hTraceVolume.RTAOCullingMask.value;
			activeProfile.RTAOSettings.CullingMode                = hTraceVolume.RTAOCullingMode.value;
			activeProfile.RTAOSettings.CullingDistance            = hTraceVolume.RTAOCullingDistance.value;
			activeProfile.RTAOSettings.MinSolidAngle              = hTraceVolume.RTAOMinSolidAngle.value;
			activeProfile.RTAOSettings.RayCount                   = hTraceVolume.RTAORayCount.value;
			activeProfile.RTAOSettings.Resolution                 = hTraceVolume.RTAOResolution.value;
			activeProfile.RTAOSettings.Denoising                  = hTraceVolume.RTAODenoising.value;
			activeProfile.RTAOSettings.SampleCountTemporal        = hTraceVolume.RTAOSampleCountTemporal.value;
			activeProfile.RTAOSettings.StaticRejection            = hTraceVolume.RTAOStaticRejection.value;
			activeProfile.RTAOSettings.DynamicRejection           = hTraceVolume.RTAODynamicRejection.value;
			activeProfile.RTAOSettings.ReprojectionFilter         = hTraceVolume.RTAOReprojectionFilter.value;
			activeProfile.RTAOSettings.UseNormalWeightingTemporal = hTraceVolume.RTAOUseNormalWeightingTemporal.value;
			activeProfile.RTAOSettings.DisocclusionBoost          = hTraceVolume.RTAODisocclusionBoost.value;
			activeProfile.RTAOSettings.FilterRadius               = hTraceVolume.RTAOFilterRadius.value;
			activeProfile.RTAOSettings.ContactOcclusion           = hTraceVolume.RTAOContactOcclusion.value;
			activeProfile.RTAOSettings.ContactOcclusionDenoise    = hTraceVolume.RTAOContactOcclusionDenoise.value;
			activeProfile.RTAOSettings.ScreenSpacePretrace        = hTraceVolume.RTAOScreenSpacePretrace.value;
#if UNITY_2023_3_OR_NEWER
			activeProfile.RTAOSettings.PretraceMask     = hTraceVolume.RTAOPretraceMask.value;
			activeProfile.RTAOSettings.ExcludeReceiving = hTraceVolume.RTAOExcludeReceiving.value;
			activeProfile.RTAOSettings.EarlyOutExcludedReceivers = hTraceVolume.RTAOEarlyOutExcludedReceivers.value;
#endif
			activeProfile.RTAOSettings.PretraceSamples    = hTraceVolume.RTAOPretraceSamples.value;
			activeProfile.RTAOSettings.PretraceDistance   = hTraceVolume.RTAOPretraceDistance.value;
			activeProfile.RTAOSettings.PretraceThickness  = hTraceVolume.RTAOPretraceThickness.value;
			activeProfile.RTAOSettings.ExcludedIntensity  = hTraceVolume.RTAOExcludedIntensity.value;
			activeProfile.RTAOSettings.DebugSceneMismatch = hTraceVolume.RTAODebugSceneMismatch.value;
		}

		protected override void Dispose(bool disposing)
		{
			_prePass?.Dispose();
			_motionVectorsPass?.Dispose();
			_gBufferPass?.Dispose();
			_ssaoPass?.Dispose();
			_gtaoPass?.Dispose();
			_rtaoPass?.Dispose();
			_finalPass?.Dispose();

			_prePass       = null;
			_motionVectorsPass = null;
			_gBufferPass   = null;
			_ssaoPass      = null;
			_gtaoPass      = null;
			_rtaoPass      = null;
			_finalPass     = null;

			_initialized = false;
			
			Shader.DisableKeyword(HShaderParams._SCREEN_SPACE_OCCLUSION);
		}
	}
}
