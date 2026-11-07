using System;
using System.Reflection;
using IPGames.HTraceAO.Runtime.Globals;
using IPGames.HTraceAO.Runtime.URP;
using IPGames.HTraceAO.Runtime.URP.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using IPGames.HTraceAO.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.AnimatedValues;

namespace HTraceAO.Editor.URP
{
	[CanEditMultipleObjects]
#if UNITY_2022_2_OR_NEWER
	[CustomEditor(typeof(HTraceAOVolume))]
#else
[VolumeComponentEditor(typeof(HTraceAOVolume))]
#endif
	public class HTraceAOVolumeEditorURP : VolumeComponentEditor
	{
		private const string NO_RENDERER_FEATURE_MESSAGE  = "HTrace Ambient Occlusion feature is missing in the active URP renderer.";
		private const string RENDERER_FEATURE_OFF_MESSAGE = "HTrace Ambient Occlusion is disabled in the active URP renderer.";
		private const string RT_IS_NOT_SUPPORTED_MESSAGE = "Realtime Raytracing is not supported!";

		private Texture2D m_Icon;

		SerializedDataParameter p_Enable;

		// General
		SerializedDataParameter p_AmbientOcclusionMode;
		SerializedDataParameter p_HBuffer;
		SerializedDataParameter p_Intensity;
		SerializedDataParameter p_Fadeout;
		SerializedDataParameter p_DirectLightingOcclusion;
		SerializedDataParameter p_InjectionPoint;
		SerializedDataParameter p_ReconstructNormals;

		// SSAO
		SerializedDataParameter p_SSAO_DebugMode;
		SerializedDataParameter p_Thickness;
		SerializedDataParameter p_Radius;
		SerializedDataParameter p_SSAOExcludeCasting;
		SerializedDataParameter p_SSAOExcludeReceiving;
		SerializedDataParameter p_SSAOExcludedIntensity;

		// GTAO
		SerializedDataParameter p_GTAO_DebugMode;
		SerializedDataParameter p_GTAOResolution;
		SerializedDataParameter p_GTAOThickness;
		SerializedDataParameter p_GTAOWorldSpaceRadius;
		SerializedDataParameter p_GTAOScreenSpaceRadius;
		SerializedDataParameter p_GTAOSliceCount;
		SerializedDataParameter p_GTAOStepCount;
		SerializedDataParameter p_GTAOTracingMode;
		//SerializedDataParameter p_GTAODepthFormat;
		SerializedDataParameter p_GTAOThicknessMode;
		SerializedDataParameter p_GTAOUseAttenuation;
		SerializedDataParameter p_GTAODenoising;
		SerializedDataParameter p_GTAOSampleCountTemporal;
		SerializedDataParameter p_GTAOTemporalRejection;
		SerializedDataParameter p_GTAOUseSimpleRejection;
		SerializedDataParameter p_GTAOReprojectionFilter;
		SerializedDataParameter p_GTAOUseNormalWeightingTemporal;
		SerializedDataParameter p_GTAOSpatialFilterType;
		SerializedDataParameter p_GTAOFilterRadius;
		SerializedDataParameter p_GTAOFilterAdaptivity;
		SerializedDataParameter p_GTAOBoxPassCount;
		SerializedDataParameter p_GTAODoubleSampleCount;
		SerializedDataParameter p_GTAOExcludeCasting;
		SerializedDataParameter p_GTAOExcludeReceiving;
		SerializedDataParameter p_GTAOExcludedIntensity;
		SerializedDataParameter p_GTAOEarlyOutExcludedReceivers;

		// RTAO
		SerializedDataParameter p_RTAO_DebugMode;
		SerializedDataParameter p_RTAOWorldSpaceRadius;
		SerializedDataParameter p_RTAOMaxRayBias;
		SerializedDataParameter p_RTAORayCount;
		SerializedDataParameter p_RTAOResolution;
		SerializedDataParameter p_RTAODenoising;
		SerializedDataParameter p_RTAOSampleCountTemporal;
		SerializedDataParameter p_RTAOReprojectionFilter;
		SerializedDataParameter p_RTAOUseNormalWeightingTemporal;
		SerializedDataParameter p_RTAODisocclusionBoost;
		SerializedDataParameter p_RTAOFilterRadius;
		SerializedDataParameter p_RTAOScreenSpacePretrace;
#if UNITY_2023_3_OR_NEWER
		SerializedDataParameter p_RTAOPretraceMask;
#endif
		SerializedDataParameter p_RTAOPretraceSamples;
		SerializedDataParameter p_RTAOPretraceDistance;
		SerializedDataParameter p_RTAOPretraceThickness;
		SerializedDataParameter p_RTAOSampleRebalancing;
		SerializedDataParameter p_RTAOCullBackfaces;
		SerializedDataParameter p_RTAOCullAlphaCutout;
		SerializedDataParameter p_RTAOCullingMask;
		SerializedDataParameter p_RTAOCullingMode;
		SerializedDataParameter p_RTAOCullingDistance;
		SerializedDataParameter p_RTAOMinSolidAngle;
		SerializedDataParameter p_RTAOStaticRejection;
		SerializedDataParameter p_RTAODynamicRejection;
		SerializedDataParameter p_RTAOContactOcclusion;
		SerializedDataParameter p_RTAOContactOcclusionDenoise;
		SerializedDataParameter p_RTAOExcludeReceiving;
		SerializedDataParameter p_RTAOExcludedIntensity;
		SerializedDataParameter p_RTAOEarlyOutExcludedReceivers;
		SerializedDataParameter p_RTAODebugSceneMismatch;

		// Menu state
		private bool _showVisualsArea = true;
		private bool _showQualityArea = true;
		private bool _showTemporalArea = true;
		private bool _showSpatialArea = true;
		private bool _showRayTracingCullingArea = true;
		private bool _showRenderingArea = true;

		// Main foldout groups
		private AnimBool AnimBoolGeneralTab;
		private AnimBool AnimBoolDenoisingTab;

		public override void OnEnable()
		{
			var o = new PropertyFetcher<HTraceAOVolume>(serializedObject);

			m_Icon = Resources.Load<Texture2D>("AO UI Card");

			p_Enable = Unpack(o.Find(x => x.Enable));

			// General Settings
			p_AmbientOcclusionMode    = Unpack(o.Find(x => x.AmbientOcclusionMode));
			p_HBuffer                 = Unpack(o.Find(x => x.HBuffer));
			p_Intensity               = Unpack(o.Find(x => x.Intensity));
			p_Fadeout                 = Unpack(o.Find(x => x.Fadeout));
			p_DirectLightingOcclusion = Unpack(o.Find(x => x.DirectLightingOcclusion));
			p_InjectionPoint          = Unpack(o.Find(x => x.InjectionPoint));
			p_ReconstructNormals      = Unpack(o.Find(x => x.ReconstructNormals));

			// SSAO Settings
			p_SSAO_DebugMode = Unpack(o.Find(x => x.DebugModeSSAO));
			p_Thickness      = Unpack(o.Find(x => x.Thickness));
			p_Radius         = Unpack(o.Find(x => x.Radius));
#if UNITY_2023_3_OR_NEWER
			p_SSAOExcludeCasting    = Unpack(o.Find(x => x.SSAOExcludeCasting));
			p_SSAOExcludeReceiving  = Unpack(o.Find(x => x.SSAOExcludeReceiving));
			p_SSAOExcludedIntensity = Unpack(o.Find(x => x.SSAOExcludedIntensity));
#endif

			// GTAO Settings
			p_GTAO_DebugMode        = Unpack(o.Find(x => x.DebugModeGTAO));
			p_GTAOResolution        = Unpack(o.Find(x => x.GTAOResolution));
			p_GTAOThickness         = Unpack(o.Find(x => x.GTAOThickness));
			p_GTAOWorldSpaceRadius  = Unpack(o.Find(x => x.GTAOWorldSpaceRadius));
			p_GTAOScreenSpaceRadius = Unpack(o.Find(x => x.GTAOScreenSpaceRadius));
			p_GTAOSliceCount        = Unpack(o.Find(x => x.GTAOSliceCount));
			p_GTAOStepCount         = Unpack(o.Find(x => x.GTAOStepCount));
			p_GTAOTracingMode       = Unpack(o.Find(x => x.GTAOTracingMode));
			//p_GTAODepthFormat = Unpack(o.Find(x => x.GTAODepthFormat));
			p_GTAOThicknessMode              = Unpack(o.Find(x => x.GTAOThicknessMode));
			p_GTAOUseAttenuation             = Unpack(o.Find(x => x.GTAOUseAttenuation));
			p_GTAODenoising                  = Unpack(o.Find(x => x.GTAODenoising));
			p_GTAOSampleCountTemporal        = Unpack(o.Find(x => x.GTAOSampleCountTemporal));
			p_GTAOTemporalRejection          = Unpack(o.Find(x => x.GTAOTemporalRejection));
			p_GTAOUseSimpleRejection         = Unpack(o.Find(x => x.GTAOUseSimpleRejection));
			p_GTAOReprojectionFilter         = Unpack(o.Find(x => x.GTAOReprojectionFilter));
			p_GTAOUseNormalWeightingTemporal = Unpack(o.Find(x => x.GTAOUseNormalWeightingTemporal));
			p_GTAOSpatialFilterType          = Unpack(o.Find(x => x.GTAOSpatialFilterType));
			p_GTAOFilterRadius               = Unpack(o.Find(x => x.GTAOFilterRadius));
			p_GTAOFilterAdaptivity           = Unpack(o.Find(x => x.GTAOFilterAdaptivity));
			p_GTAOBoxPassCount               = Unpack(o.Find(x => x.GTAOBoxPassCount));
			p_GTAODoubleSampleCount          = Unpack(o.Find(x => x.GTAODoubleSampleCount));
#if UNITY_2023_3_OR_NEWER
			p_GTAOExcludeCasting    = Unpack(o.Find(x => x.GTAOExcludeCasting));
			p_GTAOExcludeReceiving  = Unpack(o.Find(x => x.GTAOExcludeReceiving));
			p_GTAOExcludedIntensity = Unpack(o.Find(x => x.GTAOExcludedIntensity));
			p_GTAOEarlyOutExcludedReceivers = Unpack(o.Find(x => x.GTAOEarlyOutExcludedReceivers));
#endif

			// RTAO Settings
			p_RTAO_DebugMode                 = Unpack(o.Find(x => x.DebugModeRTAO));
			p_RTAOWorldSpaceRadius           = Unpack(o.Find(x => x.RTAOWorldSpaceRadius));
			p_RTAOMaxRayBias                 = Unpack(o.Find(x => x.RTAOMaxRayBias));
			p_RTAORayCount                   = Unpack(o.Find(x => x.RTAORayCount));
			p_RTAOResolution                 = Unpack(o.Find(x => x.RTAOResolution));
			p_RTAODenoising                  = Unpack(o.Find(x => x.RTAODenoising));
			p_RTAOSampleCountTemporal        = Unpack(o.Find(x => x.RTAOSampleCountTemporal));
			p_RTAOReprojectionFilter         = Unpack(o.Find(x => x.RTAOReprojectionFilter));
			p_RTAOUseNormalWeightingTemporal = Unpack(o.Find(x => x.RTAOUseNormalWeightingTemporal));
			p_RTAODisocclusionBoost          = Unpack(o.Find(x => x.RTAODisocclusionBoost));
			p_RTAOFilterRadius               = Unpack(o.Find(x => x.RTAOFilterRadius));
			p_RTAOScreenSpacePretrace        = Unpack(o.Find(x => x.RTAOScreenSpacePretrace));
#if UNITY_2023_3_OR_NEWER
			p_RTAOPretraceMask = Unpack(o.Find(x => x.RTAOPretraceMask));
#endif
			p_RTAOPretraceSamples         = Unpack(o.Find(x => x.RTAOPretraceSamples));
			p_RTAOPretraceDistance        = Unpack(o.Find(x => x.RTAOPretraceDistance));
			p_RTAOPretraceThickness       = Unpack(o.Find(x => x.RTAOPretraceThickness));
			p_RTAOSampleRebalancing       = Unpack(o.Find(x => x.RTAOSampleRebalancing));
			p_RTAOCullBackfaces           = Unpack(o.Find(x => x.RTAOCullBackfaces));
			p_RTAOCullAlphaCutout         = Unpack(o.Find(x => x.RTAOCullAlphaCutout));
			p_RTAOCullingMask             = Unpack(o.Find(x => x.RTAOCullingMask));
			p_RTAOCullingMode             = Unpack(o.Find(x => x.RTAOCullingMode));
			p_RTAOCullingDistance         = Unpack(o.Find(x => x.RTAOCullingDistance));
			p_RTAOMinSolidAngle           = Unpack(o.Find(x => x.RTAOMinSolidAngle));
			p_RTAOStaticRejection         = Unpack(o.Find(x => x.RTAOStaticRejection));
			p_RTAODynamicRejection        = Unpack(o.Find(x => x.RTAODynamicRejection));
			p_RTAOContactOcclusion        = Unpack(o.Find(x => x.RTAOContactOcclusion));
			p_RTAOContactOcclusionDenoise = Unpack(o.Find(x => x.RTAOContactOcclusionDenoise));
			p_RTAODebugSceneMismatch      = Unpack(o.Find(x => x.RTAODebugSceneMismatch));
#if UNITY_2023_3_OR_NEWER
			p_RTAOExcludeReceiving = Unpack(o.Find(x => x.RTAOExcludeReceiving));
			p_RTAOEarlyOutExcludedReceivers = Unpack(o.Find(x => x.RTAOEarlyOutExcludedReceivers));
#endif
			p_RTAOExcludedIntensity = Unpack(o.Find(x => x.RTAOExcludedIntensity));

			// Initialize AnimBool for foldouts
			AnimBoolGeneralTab = new AnimBool(true);
			AnimBoolGeneralTab.valueChanged.RemoveAllListeners();
			AnimBoolGeneralTab.valueChanged.AddListener(Repaint);

			AnimBoolDenoisingTab = new AnimBool(true);
			AnimBoolDenoisingTab.valueChanged.RemoveAllListeners();
			AnimBoolDenoisingTab.valueChanged.AddListener(Repaint);

			base.OnEnable();
		}

		public override void OnInspectorGUI()
		{
			if (m_Icon != null)
			{
				//GUILayout.Label(m_Icon, HEditorStyles.icon, GUILayout.ExpandWidth(false));
				Rect rect = GUILayoutUtility.GetAspectRect((float)m_Icon.width / m_Icon.height);
				rect.xMin += 4;
				rect.xMax -= 4;
				GUI.DrawTexture(rect, m_Icon, ScaleMode.ScaleToFit);
				EditorGUILayout.Space(5f);
			}

			var htraceAoRendererFeature = HRendererURP.GetRendererFeatureByTypeName(nameof(HTraceAORendererFeature)) as HTraceAORendererFeature;
			if (htraceAoRendererFeature == null)
			{
				EditorGUILayout.Space();
				CoreEditorUtils.DrawFixMeBox(NO_RENDERER_FEATURE_MESSAGE, MessageType.Error, HEditorStyles.FixButtonName, () =>
				{
					HRendererURP.AddHTraceRendererFeatureToUniversalRendererData();
					GUIUtility.ExitGUI();
				});
				//EditorGUILayout.HelpBox(NO_RENDERER_FEATURE_MESSAGE, MessageType.Error, wide: true);
				return;
			}
			else if (!htraceAoRendererFeature.isActive)
			{
				EditorGUILayout.Space();
				CoreEditorUtils.DrawFixMeBox(RENDERER_FEATURE_OFF_MESSAGE, MessageType.Warning, HEditorStyles.FixButtonName, () =>
				{
					htraceAoRendererFeature.SetActive(true);
					GUIUtility.ExitGUI();
				});
				EditorGUILayout.Space();
			}

			if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.RTAO)
			{
				if (HRenderer.SupportsInlineRayTracing == false) // URP has only Inline Raytracing, but we output realtime RT error to avoid confusing users
					EditorGUILayout.HelpBox(RT_IS_NOT_SUPPORTED_MESSAGE, MessageType.Error);
			}
			
			if (HTraceAORendererFeature.IsUseVolumes == false)
			{
				CoreEditorUtils.DrawFixMeBox("\"Use Volumes\" checkbox in the HTrace AO Renderer feature is disabled, use the HTraceAO component in your scenes.", MessageType.Warning, HEditorStyles.ChangeButtonName, () =>
				{
					htraceAoRendererFeature.UseVolumes = true;
					GUIUtility.ExitGUI();
				});
				return;
			}

			bool isBeforeOpaque = (URPInjectionPoint)p_InjectionPoint.value.enumValueIndex == URPInjectionPoint.BeforeOpaque;
			bool isAfterOpaque  = (URPInjectionPoint)p_InjectionPoint.value.enumValueIndex == URPInjectionPoint.AfterOpaque;
			bool supportsRenderingLayerMasks = false;

			var universalRendererData = HRendererURP.UniversalRendererData;
			var urpAsset = HRendererURP.UrpAsset;
#if UNITY_2023_2_OR_NEWER
			if (universalRendererData != null && urpAsset != null && urpAsset.useRenderingLayers)
			{
				supportsRenderingLayerMasks = universalRendererData.renderingMode == RenderingMode.Deferred
#if UNITY_6000_1_OR_NEWER
				                              || universalRendererData.renderingMode == RenderingMode.DeferredPlus
#endif
					;
			}
#endif
			
			// ------------------------------------- Global settings ----------------------------------------------------------

			PropertyField(p_Enable);
			EditorGUILayout.Space(5f);

			using (new HEditorUtils.FoldoutScope(AnimBoolGeneralTab, out var shouldDraw, HEditorStyles.GlobalSettingsContent.text))
			{
				if (shouldDraw)
				{
					PropertyField(p_AmbientOcclusionMode);

					switch ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex)
					{
						case AmbientOcclusionMode.SSAO:
							PropertyField(p_SSAO_DebugMode);
							break;
						case AmbientOcclusionMode.GTAO:
							PropertyField(p_GTAO_DebugMode);
							if((DebugModeGTAO)p_GTAO_DebugMode.value.enumValueIndex == DebugModeGTAO.MainBuffers)
							{
								PropertyField(p_HBuffer);
							}

							if((DebugModeGTAO)p_GTAO_DebugMode.value.enumValueIndex == DebugModeGTAO.TemporalDisocclusion && p_GTAOSampleCountTemporal.value.intValue == 0)
								EditorGUILayout.HelpBox("Temporal Denoiser disabled", MessageType.Info);

							break;
						case AmbientOcclusionMode.RTAO:
							PropertyField(p_RTAO_DebugMode);
							if((DebugModeRTAO)p_RTAO_DebugMode.value.enumValueIndex == DebugModeRTAO.MainBuffers)
							{
								PropertyField(p_HBuffer);
							}

							if((DebugModeRTAO)p_RTAO_DebugMode.value.enumValueIndex == DebugModeRTAO.TemporalDisocclusion && p_RTAOSampleCountTemporal.value.intValue == 0)
								EditorGUILayout.HelpBox("Temporal Denoiser disabled", MessageType.Info);

							if((DebugModeRTAO)p_RTAO_DebugMode.value.enumValueIndex == DebugModeRTAO.HardwareRayTracingScene)
								PropertyField(p_RTAODebugSceneMismatch);

							break;
					}

					EditorGUILayout.Space(3f);

					_showRenderingArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showRenderingArea, "Pipeline Integration");
					if (_showRenderingArea)
					{
						using (new IndentLevelScope())
						{
							PropertyField(p_InjectionPoint);
							if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex != AmbientOcclusionMode.SSAO)
								PropertyField(p_ReconstructNormals);
						}
					}
					EditorGUILayout.EndFoldoutHeaderGroup();
					EditorGUILayout.Space(3f);

					// -------------------------------------------- SSAO --------------------------------------------

					if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.SSAO)
					{
						_showVisualsArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualsArea, "Visuals");
						if (_showVisualsArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_Fadeout);
								PropertyField(p_Intensity);
								PropertyField(p_Radius);
								PropertyField(p_Thickness);
								if (!isAfterOpaque)
									PropertyField(p_DirectLightingOcclusion);
#if UNITY_2023_3_OR_NEWER
								if (!isBeforeOpaque)
								{
									using (new EditorGUI.DisabledScope(!supportsRenderingLayerMasks))
									{
										PropertyField(p_SSAOExcludeCasting);
										PropertyField(p_SSAOExcludeReceiving);
										if (p_SSAOExcludeReceiving.value.FindPropertyRelative("m_Bits").uintValue != 0)
										{
											using (new IndentLevelScope())
											{
												PropertyField(p_SSAOExcludedIntensity);
											}
										}
									}
								}
#endif
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);
					}

					// -------------------------------------------- GTAO --------------------------------------------

					if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.GTAO)
					{
						_showVisualsArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualsArea, "Visuals");
						if (_showVisualsArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_Fadeout);
								PropertyField(p_Intensity);
								PropertyField(p_GTAOWorldSpaceRadius);
								if (p_GTAOWorldSpaceRadius.value.floatValue > 2.5f && (GTAOTracingMode)p_GTAOTracingMode.value.intValue != GTAOTracingMode.VisibilityBitmasks)
								{
									EditorGUILayout.HelpBox("Consider switching the Tracing Mode to Visibility Bitmasks for better precision with a large World Space Radius.", MessageType.Info);
								}
								PropertyField(p_GTAOScreenSpaceRadius);
								if (!isAfterOpaque)
									PropertyField(p_DirectLightingOcclusion);
#if UNITY_2023_3_OR_NEWER
								if (!isBeforeOpaque)
								{
									using (new EditorGUI.DisabledScope(!supportsRenderingLayerMasks))
									{
										PropertyField(p_GTAOExcludeCasting);
										PropertyField(p_GTAOExcludeReceiving);
										if (p_GTAOExcludeReceiving.value.FindPropertyRelative("m_Bits").uintValue != 0)
										{
											using (new IndentLevelScope())
											{
												PropertyField(p_GTAOExcludedIntensity);
												if (Mathf.Approximately(p_GTAOExcludedIntensity.value.floatValue, 0.0f))
													PropertyField(p_GTAOEarlyOutExcludedReceivers);
											}
										}
									}
								}
#endif
							}

						}
						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						_showQualityArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showQualityArea, "Quality");
						if (_showQualityArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_GTAOResolution);
								if((GTAODenoisingMode)p_GTAODenoising.value.enumValueIndex == GTAODenoisingMode.None && (GTAOResolutionMode)p_GTAOResolution.value.enumValueIndex != GTAOResolutionMode.Full)
									EditorGUILayout.HelpBox(HEditorStyles.Info_ResolutionForcedToFull, MessageType.Info);
								PropertyField(p_GTAOTracingMode);
								//PropertyField(p_GTAODepthFormat);

								PropertyField(p_GTAOSliceCount);
								PropertyField(p_GTAOStepCount);

								if ((GTAOTracingMode)p_GTAOTracingMode.value.intValue == GTAOTracingMode.VisibilityBitmasks)
								{
									EditorGUILayout.Space(3f);
									PropertyField(p_GTAOThicknessMode);
									PropertyField(p_GTAOThickness);
									PropertyField(p_GTAOUseAttenuation);
								}
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();

					}

					// -------------------------------------------- RTAO --------------------------------------------

					if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.RTAO)
					{
						_showVisualsArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualsArea, "Visuals");
						if (_showVisualsArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_Fadeout);
								PropertyField(p_Intensity);
								PropertyField(p_RTAOMaxRayBias);
								PropertyField(p_RTAOWorldSpaceRadius);
								if (!isAfterOpaque)
									PropertyField(p_DirectLightingOcclusion);
								if (!isBeforeOpaque)
								{
									using (new EditorGUI.DisabledScope(!supportsRenderingLayerMasks))
									{
										PropertyField(p_RTAOExcludeReceiving);
										if (p_RTAOExcludeReceiving.value.FindPropertyRelative("m_Bits").uintValue != 0)
										{
											using (new IndentLevelScope())
											{
												PropertyField(p_RTAOExcludedIntensity);
												if (Mathf.Approximately(p_RTAOExcludedIntensity.value.floatValue, 0.0f))
													PropertyField(p_RTAOEarlyOutExcludedReceivers);
											}
										}
									}
								}
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						_showQualityArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showQualityArea, "Quality");
						if (_showQualityArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_RTAOResolution);
								if((RTAODenoisingMode)p_RTAODenoising.value.enumValueIndex == RTAODenoisingMode.None && (ResolutionMode)p_RTAOResolution.value.enumValueIndex != ResolutionMode.Full)
									EditorGUILayout.HelpBox(HEditorStyles.Info_ResolutionForcedToFull, MessageType.Info);
								PropertyField(p_RTAORayCount);

								// Show Sample Rebalancing only when Resolution is Adaptive
								bool isAdaptive = (ResolutionMode)p_RTAOResolution.value.intValue == ResolutionMode.Adaptive;
								if (isAdaptive)
								{
									PropertyField(p_RTAOSampleRebalancing);
								}

								PropertyField(p_RTAOScreenSpacePretrace);
								if (p_RTAOScreenSpacePretrace.value.boolValue)
								{
									using (new IndentLevelScope())
									{
#if UNITY_2023_3_OR_NEWER
										PropertyField(p_RTAOPretraceMask);
#endif
										PropertyField(p_RTAOPretraceSamples);
										PropertyField(p_RTAOPretraceDistance);
										PropertyField(p_RTAOPretraceThickness);
									}
								}
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						// Ray Tracing Culling section
						_showRayTracingCullingArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showRayTracingCullingArea, "Ray Tracing Culling");
						if (_showRayTracingCullingArea)
						{
							using (new IndentLevelScope())
							{
								PropertyField(p_RTAOCullAlphaCutout);
								PropertyField(p_RTAOCullBackfaces);
								PropertyField(p_RTAOCullingMask);

								PropertyField(p_RTAOCullingMode);
								if ((CullingMode)p_RTAOCullingMode.value.intValue == CullingMode.Sphere)
								{
									using (new IndentLevelScope())
									{
										PropertyField(p_RTAOCullingDistance);
									}
								}
								else if ((CullingMode)p_RTAOCullingMode.value.intValue == CullingMode.SolidAngle)
								{
									using (new IndentLevelScope())
									{
										PropertyField(p_RTAOMinSolidAngle);
									}
								}
							}
						}
						EditorGUILayout.EndFoldoutHeaderGroup();
					}

				}
			}

			// ------------------------------------- DENOISING ----------------------------------------------------------

			if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.GTAO)
			{
				using (new HEditorUtils.FoldoutScope(AnimBoolDenoisingTab, out var shouldDrawDenoising, HEditorStyles.GTAO_DenoisingTabContent.text))
				{
					if (shouldDrawDenoising)
					{
						EditorGUILayout.Space(3f);
						PropertyField(p_GTAODenoising);
						if ((GTAODenoisingMode)p_GTAODenoising.value.enumValueIndex == GTAODenoisingMode.None)
						{
							EditorGUILayout.HelpBox(HEditorStyles.Info_ResolutionForcedToFull, MessageType.Info);
						}

						EditorGUILayout.Space(3f);

						var gtaoDenoisingMode = (GTAODenoisingMode)p_GTAODenoising.value.enumValueIndex;
						bool showTemporal = gtaoDenoisingMode == GTAODenoisingMode.TemporalOnly || gtaoDenoisingMode == GTAODenoisingMode.SpatioTemporal;
						bool showSpatial  = gtaoDenoisingMode == GTAODenoisingMode.SpatialOnly  || gtaoDenoisingMode == GTAODenoisingMode.SpatioTemporal;

						if (showTemporal)
						{
							_showTemporalArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showTemporalArea, "Temporal");
							if (_showTemporalArea)
							{
								using (new IndentLevelScope())
								{
									PropertyField(p_GTAOReprojectionFilter);

									PropertyField(p_GTAOTemporalRejection);
									p_GTAOTemporalRejection.value.floatValue = Mathf.Round(p_GTAOTemporalRejection.value.floatValue * 100f) / 100f;

									PropertyField(p_GTAOUseSimpleRejection);

									if ((ReprojectionFilter)p_GTAOReprojectionFilter.value.enumValueIndex == ReprojectionFilter.Linear)
									{
										PropertyField(p_GTAOUseNormalWeightingTemporal);
									}
								}

							}
							EditorGUILayout.EndFoldoutHeaderGroup();
							EditorGUILayout.Space(3f);
						}

						if (showSpatial)
						{
							_showSpatialArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showSpatialArea, "Spatial");
							if (_showSpatialArea)
							{
								using (new IndentLevelScope())
								{
									PropertyField(p_GTAOSpatialFilterType);

									if ((SpatialFilterType)p_GTAOSpatialFilterType.value.enumValueIndex == SpatialFilterType.Disk)
									{
										PropertyField(p_GTAOFilterRadius);
										p_GTAOFilterRadius.value.floatValue = Mathf.Round(p_GTAOFilterRadius.value.floatValue * 10f) / 10f;
										PropertyField(p_GTAOFilterAdaptivity);
										p_GTAOFilterAdaptivity.value.floatValue = Mathf.Round(p_GTAOFilterAdaptivity.value.floatValue * 10f) / 10f;
									}
									else
									{
										PropertyField(p_GTAOBoxPassCount);
									}

									PropertyField(p_GTAODoubleSampleCount);
								}
							}
							EditorGUILayout.EndFoldoutHeaderGroup();
						}

					}
				}
			}

			if ((AmbientOcclusionMode)p_AmbientOcclusionMode.value.enumValueIndex == AmbientOcclusionMode.RTAO)
			{
				using (new HEditorUtils.FoldoutScope(AnimBoolDenoisingTab, out var shouldDrawDenoising, HEditorStyles.RTAO_DenoisingTabContent.text))
				{
					if (shouldDrawDenoising)
					{
						PropertyField(p_RTAODenoising);

						if ((RTAODenoisingMode)p_RTAODenoising.value.enumValueIndex == RTAODenoisingMode.None)
						{
							EditorGUILayout.HelpBox(HEditorStyles.Info_ResolutionForcedToFull, MessageType.Info);
						}

						EditorGUILayout.Space(3f);

						// Hide all denoising options when Denoising Mode is None
						if ((RTAODenoisingMode)p_RTAODenoising.value.intValue != RTAODenoisingMode.None)
						{
							_showTemporalArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showTemporalArea, "Temporal");
							if (_showTemporalArea)
							{
								using (new IndentLevelScope())
								{
									PropertyField(p_RTAOReprojectionFilter);

									PropertyField(p_RTAOSampleCountTemporal);

									bool hide = p_RTAOSampleCountTemporal.value.intValue > 0;
									GUI.enabled = hide;

									PropertyField(p_RTAOStaticRejection);
									p_RTAOStaticRejection.value.floatValue = Mathf.Round(p_RTAOStaticRejection.value.floatValue * 100f) / 100f;
									PropertyField(p_RTAODynamicRejection);
									p_RTAODynamicRejection.value.floatValue = Mathf.Round(p_RTAODynamicRejection.value.floatValue * 100f) / 100f;

									if (p_RTAORayCount.value.intValue < 4)
										PropertyField(p_RTAODisocclusionBoost);

									GUI.enabled = true;

									if ((ReprojectionFilter)p_RTAOReprojectionFilter.value.enumValueIndex != ReprojectionFilter.Bicubic)
									{
										PropertyField(p_RTAOUseNormalWeightingTemporal);
									}
								}
							}
							EditorGUILayout.EndFoldoutHeaderGroup();
							EditorGUILayout.Space(3f);

							_showSpatialArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showSpatialArea, "Spatial");
							if (_showSpatialArea)
							{
								using (new IndentLevelScope())
								{
									PropertyField(p_RTAOFilterRadius);
									p_RTAOFilterRadius.value.floatValue = Mathf.Round(p_RTAOFilterRadius.value.floatValue * 10f) / 10f;

									PropertyField(p_RTAOContactOcclusion);

									// Show ContactOcclusionDenoise only when ContactOcclusion > 0, Denoising is DoublePass, and Resolution is Full or Adaptive
									bool isFullOrAdaptive = (ResolutionMode)p_RTAOResolution.value.intValue == ResolutionMode.Full ||
									                        (ResolutionMode)p_RTAOResolution.value.intValue == ResolutionMode.Adaptive;
									if (p_RTAOContactOcclusion.value.floatValue > 0f && (RTAODenoisingMode)p_RTAODenoising.value.intValue == RTAODenoisingMode.DoublePass && isFullOrAdaptive)
									{
										using (new IndentLevelScope())
										{
											PropertyField(p_RTAOContactOcclusionDenoise);
										}
									}
								}
							}
							EditorGUILayout.EndFoldoutHeaderGroup();
						}
					}
				}
			}


			HEditorUtils.HorizontalLine(1f);
			EditorGUILayout.Space(3f);

			//HEditorUtils.DrawClickableLink($"HTrace AO Version: {HNames.HTRACE_AO_VERSION}", HNames.HTRACE_AO_DOCUMENTATION_LINK, true);
			HEditorUtils.DrawLinkRow(
				($"Documentation (v." + HNames.HTRACE_AO_VERSION + ")", () => Application.OpenURL(HNames.HTRACE_AO_DOCUMENTATION_LINK)),
				("Discord", () => Application.OpenURL(HNames.HTRACE_DISCORD_LINK)),
				("Bug report", () => Application.OpenURL(HNames.HTRACE_DISCORD_BUGS_AO_LINK))
			);
		}
	}
}

#endif
