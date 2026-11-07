#if UNITY_EDITOR
using IPGames.HTraceAO.Runtime.URP;
using IPGames.HTraceAO.Runtime.URP.Infrastructure;
using UnityEngine.Rendering.Universal;
using System;
using IPGames.HTraceAO.Runtime.Data.Profile;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.Rendering;
using UnityEngine;

namespace IPGames.HTraceAO.Editor
{
	[CustomEditor(typeof(HTraceAOProfile))]
	internal class HTraceAOProfileEditor : UnityEditor.Editor
	{
		// -- Foldout state (plain bools, not persisted) --
		private bool _globalSettingsTabOpen     = true;
		private bool _denoisingTabOpen          = true;
		private bool _ssaoTabOpen               = true;
		private bool _rtaoTabOpen               = true;
		private bool _debugTabOpen              = true;

#pragma warning disable CS0414
		private bool _showVisualsArea           = true;
		private bool _showQualityArea           = true;
		private bool _showTemporalArea          = true;
		private bool _showSpatialArea           = true;
		private bool _showRayTracingCullingArea = true;
		private bool _showRenderingArea         = true;
#pragma warning restore CS0414

		private AnimBool AnimBoolGeneralTab;
		private AnimBool AnimBoolDenoisingTab;
		private AnimBool AnimBoolSSAOTab;
		private AnimBool AnimBoolRTAOTab;
		private AnimBool AnimBoolDebugTab;
		private AnimBool AnimBoolEMPTY;

		// -- Serialized root groups --
		SerializedProperty GeneralSettings;
		SerializedProperty SSAOSettings;
		SerializedProperty GTAOSettings;
		SerializedProperty RTAOSettings;
		SerializedProperty DebugSettings;

		// General
		SerializedProperty HBuffer;
		SerializedProperty AmbientOcclusionMode;
		SerializedProperty Intensity;
		SerializedProperty Fadeout;
		SerializedProperty DirectLightingOcclusion;
		SerializedProperty OutputDithering;
		SerializedProperty OutputDitheringAnimate;
		SerializedProperty InjectionPoint;
		SerializedProperty ReconstructNormals;

		// SSAO
		SerializedProperty SSAO_DebugMode;
		SerializedProperty SSAO_Thickness;
		SerializedProperty SSAO_Radius;
		SerializedProperty SSAO_ExcludeCasting;
		SerializedProperty SSAO_ExcludeReceiving;
		SerializedProperty SSAO_ExcludedIntensity;

		// GTAO
		SerializedProperty GTAO_DebugMode;
		SerializedProperty GTAO_Thickness;
		SerializedProperty GTAO_WorldSpaceRadius;
		SerializedProperty GTAO_ScreenSpaceRadius;
		SerializedProperty GTAO_SliceCount;
		SerializedProperty GTAO_StepCount;
		SerializedProperty GTAO_Resolution;
		SerializedProperty GTAO_TracingMode;
		//SerializedProperty GTAO_DepthFormat;
		SerializedProperty GTAO_ThicknessMode;
		SerializedProperty GTAO_UseAttenuation;
		SerializedProperty GTAO_UseSimpleRejection;
		SerializedProperty GTAO_TemporalRejection;
		SerializedProperty GTAO_SampleCountTemporal;
		SerializedProperty GTAO_ReprojectionFilter;
		SerializedProperty GTAO_UseNormalWeightingTemporal;
		SerializedProperty GTAO_SpatialFilterType;
		SerializedProperty GTAO_FilterRadius;
		SerializedProperty GTAO_FilterAdaptivity;
		SerializedProperty GTAO_BoxPassCount;
		SerializedProperty GTAO_DoubleSampleCount;
		SerializedProperty GTAO_Denoising;
		SerializedProperty GTAO_ExcludeCasting;
		SerializedProperty GTAO_ExcludeReceiving;
		SerializedProperty GTAO_ExcludedIntensity;
		SerializedProperty GTAO_EarlyOutExcludedReceivers;

		// RTAO
		SerializedProperty RTAO_DebugMode;
		SerializedProperty RTAO_DebugSceneMismatch;
		SerializedProperty RTAO_WorldSpaceRadius;
		SerializedProperty RTAO_MaxRayBias;
		SerializedProperty RTAO_SpecularOcclusion;
		SerializedProperty RTAO_TracingMode;
		SerializedProperty RTAO_CullAlphaCutout;
		SerializedProperty RTAO_CullingMask;
		SerializedProperty RTAO_RayCount;
		SerializedProperty RTAO_SampleRebalancing;
		SerializedProperty RTAO_CullBackfaces;
		SerializedProperty RTAO_Resolution;
		SerializedProperty RTAO_Denoising;
		SerializedProperty RTAO_SampleCountTemporal;
		SerializedProperty RTAO_StaticRejection;
		SerializedProperty RTAO_DynamicRejection;
		SerializedProperty RTAO_ReprojectionFilter;
		SerializedProperty RTAO_UseNormalWeightingTemporal;
		SerializedProperty RTAO_DisocclusionBoost;
		SerializedProperty RTAO_FilterRadius;
		SerializedProperty RTAO_ContactOcclusion;
		SerializedProperty RTAO_ContactOcclusionDenoise;
		SerializedProperty RTAO_ScreenSpacePretrace;
#if UNITY_2023_3_OR_NEWER
		SerializedProperty RTAO_PretraceMask;
#endif
		SerializedProperty RTAO_PretraceSamples;
		SerializedProperty RTAO_PretraceDistance;
		SerializedProperty RTAO_CullingMode;
		SerializedProperty RTAO_CullingDistance;
		SerializedProperty RTAO_MinSolidAngle;
		SerializedProperty RTAO_PretraceThickness;
		SerializedProperty RTAO_ExcludeReceiving;
		SerializedProperty RTAO_ExcludedIntensity;
		SerializedProperty RTAO_EarlyOutExcludedReceivers;

		// Debug Dev
		SerializedProperty ShowBowels;
		SerializedProperty ShowFullDebugLog;
		SerializedProperty TestCheckBox1;
		SerializedProperty TestCheckBox2;
		SerializedProperty TestCheckBox3;
		SerializedProperty HInjectionPoint;
		SerializedProperty HTraceLayer;
		
#pragma warning disable CS0414
		private const string NO_RENDERER_FEATURE_MESSAGE  = "HTrace Ambient Occlusion feature is missing in the active URP renderer.";
		private const string RENDERER_FEATURE_OFF_MESSAGE = "HTrace Ambient Occlusion is disabled in the active URP renderer.";
		private const string RT_IS_NOT_SUPPORTED_MESSAGE  = "Realtime Raytracing is not supported!";
		private const string IS_RT_DISABLED_MESSAGE       = "Realtime Raytracing is disabled!\nYou can enable it in HDRP Asset:\nProject Settings -> Quality -> HDRP -> Rendering.";
		private const string ADAPTIVE_RES_NOT_SUPPORTED_IN_INLINE_RT_MESSAGE  = "Adaptive Resolution Mode with Inline Ray Tracing requires Unity 6000 or above.";
#pragma warning restore CS0414

		private void OnEnable()
		{
			if (target == null)
				return;
			
			PropertiesRelative();

			AnimBoolGeneralTab = new AnimBool(_globalSettingsTabOpen);
			AnimBoolGeneralTab.valueChanged.RemoveAllListeners();
			AnimBoolGeneralTab.valueChanged.AddListener(Repaint);

			AnimBoolDenoisingTab = new AnimBool(_denoisingTabOpen);
			AnimBoolDenoisingTab.valueChanged.RemoveAllListeners();
			AnimBoolDenoisingTab.valueChanged.AddListener(Repaint);

			AnimBoolSSAOTab = new AnimBool(_ssaoTabOpen);
			AnimBoolSSAOTab.valueChanged.RemoveAllListeners();
			AnimBoolSSAOTab.valueChanged.AddListener(Repaint);

			AnimBoolRTAOTab = new AnimBool(_rtaoTabOpen);
			AnimBoolRTAOTab.valueChanged.RemoveAllListeners();
			AnimBoolRTAOTab.valueChanged.AddListener(Repaint);

			AnimBoolDebugTab = new AnimBool(_debugTabOpen);
			AnimBoolDebugTab.valueChanged.RemoveAllListeners();
			AnimBoolDebugTab.valueChanged.AddListener(Repaint);

			AnimBoolEMPTY = new AnimBool(false);
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			UpdateStandartStyles();

			AnimBoolEMPTY = new AnimBool(false);

#pragma warning disable CS0219
			bool isBeforeOpaque = false;
			bool isAfterOpaque  = false;
			bool supportsRenderingLayerMasks = false;
			bool shouldDisableRenderingLayerMasks = false;
#pragma warning restore CS0219
			isBeforeOpaque = (URPInjectionPoint)InjectionPoint.enumValueIndex == URPInjectionPoint.BeforeOpaque;
			isAfterOpaque  = (URPInjectionPoint)InjectionPoint.enumValueIndex == URPInjectionPoint.AfterOpaque;

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
			shouldDisableRenderingLayerMasks = !supportsRenderingLayerMasks;

			Color standartBackgroundColor = GUI.backgroundColor;
			Color standartColor           = GUI.color;

			WarningsHandle();

// ------------------------------------- Global settings ----------------------------------------------------------

			using (new HEditorUtils.FoldoutScope(AnimBoolGeneralTab, out var shouldDraw, HEditorStyles.GlobalSettingsContent.text))
			{
				_globalSettingsTabOpen = shouldDraw;
				if (shouldDraw)
				{
					EditorGUILayout.Space(3f);
					EditorGUILayout.PropertyField(AmbientOcclusionMode, HEditorStyles.AOMode);
					switch ((AmbientOcclusionMode)AmbientOcclusionMode.enumValueIndex)
					{
						case IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.SSAO:
							EditorGUILayout.PropertyField(SSAO_DebugMode, HEditorStyles.DebugModeContent);
							GTAO_DebugMode.enumValueIndex = SSAO_DebugMode.enumValueIndex;
							RTAO_DebugMode.enumValueIndex = SSAO_DebugMode.enumValueIndex;
							break;
						case IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.GTAO:
							EditorGUILayout.PropertyField(GTAO_DebugMode, HEditorStyles.DebugModeContent);
							SSAO_DebugMode.enumValueIndex = GTAO_DebugMode.enumValueIndex < 2 ? GTAO_DebugMode.enumValueIndex : 0;
							RTAO_DebugMode.enumValueIndex = GTAO_DebugMode.enumValueIndex;
							if((DebugModeGTAO)GTAO_DebugMode.enumValueIndex == DebugModeGTAO.MainBuffers)
								EditorGUILayout.PropertyField(HBuffer, HEditorStyles.HBuffer);

							if((DebugModeGTAO)GTAO_DebugMode.enumValueIndex == DebugModeGTAO.TemporalDisocclusion && GTAO_SampleCountTemporal.intValue == 0)
								EditorGUILayout.HelpBox("Temporal Denoiser disabled", MessageType.Info);

							break;
						case IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.RTAO:
							EditorGUILayout.PropertyField(RTAO_DebugMode, HEditorStyles.DebugModeContent);
							SSAO_DebugMode.enumValueIndex = RTAO_DebugMode.enumValueIndex < 2 ? RTAO_DebugMode.enumValueIndex : 0;
							GTAO_DebugMode.enumValueIndex = RTAO_DebugMode.enumValueIndex < 3 ? RTAO_DebugMode.enumValueIndex : 0;
							if((DebugModeRTAO)RTAO_DebugMode.enumValueIndex == DebugModeRTAO.MainBuffers)
								EditorGUILayout.PropertyField(HBuffer, HEditorStyles.HBuffer);

							if((DebugModeRTAO)RTAO_DebugMode.enumValueIndex == DebugModeRTAO.TemporalDisocclusion && RTAO_SampleCountTemporal.intValue == 0)
								EditorGUILayout.HelpBox("Temporal Denoiser disabled", MessageType.Info);

							if((DebugModeRTAO)RTAO_DebugMode.enumValueIndex == DebugModeRTAO.HardwareRayTracingScene)
								EditorGUILayout.PropertyField(RTAO_DebugSceneMismatch, new GUIContent("Scene Mismatch"));

							break;
					}

					EditorGUILayout.Space(3f);

					_showRenderingArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showRenderingArea, "Pipeline Integration");
					if (_showRenderingArea)
					{
						EditorGUI.indentLevel++;
						EditorGUILayout.PropertyField(InjectionPoint, HEditorStyles.InjectionPoint);
						if ((AmbientOcclusionMode)AmbientOcclusionMode.enumValueIndex != IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.SSAO)
							ReconstructNormals.boolValue = EditorGUILayout.Toggle(HEditorStyles.ReconstructNormals, ReconstructNormals.boolValue);
						EditorGUI.indentLevel--;
					}

					EditorGUILayout.EndFoldoutHeaderGroup();
					EditorGUILayout.Space(3f);

					if ((AmbientOcclusionMode)AmbientOcclusionMode.enumValueIndex == IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.SSAO)
					{
						_showVisualsArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualsArea, "Visuals");
						if (_showVisualsArea)
						{
							EditorGUI.indentLevel++;

							Fadeout.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(HEditorStyles.Fadeout, Fadeout.floatValue));
							EditorGUILayout.Slider(Intensity, 0.0f, 4.0f, HEditorStyles.Intensity);
							SSAO_Radius.intValue = EditorGUILayout.IntSlider(HEditorStyles.SSAO_Radius, SSAO_Radius.intValue, 1, 4);
							EditorGUILayout.Slider(SSAO_Thickness, 0.0f, 1.0f, HEditorStyles.SSAO_Thickness);
							if (!isAfterOpaque)
								EditorGUILayout.Slider(DirectLightingOcclusion, 0.0f, 1.0f, HEditorStyles.DirectLightingOcclusion);
#if UNITY_2023_3_OR_NEWER
							if (!isBeforeOpaque)
							{
								using (new EditorGUI.DisabledScope(shouldDisableRenderingLayerMasks))
								{
									EditorGUILayout.PropertyField(SSAO_ExcludeCasting,   HEditorStyles.GTAO_ExcludeCasting);
									EditorGUILayout.PropertyField(SSAO_ExcludeReceiving, HEditorStyles.GTAO_ExcludeReceiving);
									if (SSAO_ExcludeReceiving.FindPropertyRelative("m_Bits").uintValue != 0)
									{
										EditorGUI.indentLevel++;
										EditorGUILayout.Slider(SSAO_ExcludedIntensity, 0.0f, 1.0f, HEditorStyles.ExcludedIntensity);
										EditorGUI.indentLevel--;
									}
								}
							}
#endif

							EditorGUI.indentLevel--;
						}

						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);
					}

					if ((AmbientOcclusionMode)AmbientOcclusionMode.enumValueIndex == IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.GTAO)
					{
						_showVisualsArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualsArea, "Visuals");
						if (_showVisualsArea)
						{
							EditorGUI.indentLevel++;

							Fadeout.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(HEditorStyles.Fadeout, Fadeout.floatValue));
							EditorGUILayout.Slider(Intensity,                    0.0f,  4.0f, HEditorStyles.Intensity);
							EditorGUILayout.Slider(GTAO_WorldSpaceRadius,        0.1f, 5.0f, HEditorStyles.GTAO_WorldSpaceRadius);
							if (GTAO_WorldSpaceRadius.floatValue > 2.5f && (GTAOTracingMode)GTAO_TracingMode.enumValueIndex != GTAOTracingMode.VisibilityBitmasks)
							{
								EditorGUILayout.HelpBox("Consider switching the Tracing Mode to Visibility Bitmasks for better precision with a large World Space Radius.", MessageType.Info);
							}
							GTAO_ScreenSpaceRadius.intValue = EditorGUILayout.IntSlider(HEditorStyles.GTAO_ScreenSpaceRadius, GTAO_ScreenSpaceRadius.intValue, 1, 256);
							if (!isAfterOpaque)
								EditorGUILayout.Slider(DirectLightingOcclusion, 0.0f, 1.0f, HEditorStyles.DirectLightingOcclusion);
#if UNITY_2023_3_OR_NEWER
							if (!isBeforeOpaque)
							{
								using (new EditorGUI.DisabledScope(shouldDisableRenderingLayerMasks))
								{
									EditorGUILayout.PropertyField(GTAO_ExcludeCasting,   HEditorStyles.GTAO_ExcludeCasting);
									EditorGUILayout.PropertyField(GTAO_ExcludeReceiving, HEditorStyles.GTAO_ExcludeReceiving);
									if (GTAO_ExcludeReceiving.FindPropertyRelative("m_Bits").uintValue != 0)
									{
										EditorGUI.indentLevel++;
										EditorGUILayout.Slider(GTAO_ExcludedIntensity, 0.0f, 1.0f, HEditorStyles.ExcludedIntensity);
										if (Mathf.Approximately(GTAO_ExcludedIntensity.floatValue, 0.0f))
											EditorGUILayout.PropertyField(GTAO_EarlyOutExcludedReceivers, HEditorStyles.EarlyOutExcludedReceivers);
										EditorGUI.indentLevel--;
									}
								}
							}
#endif

							EditorGUI.indentLevel--;
						}

						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						_showQualityArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showQualityArea, "Quality");
						if (_showQualityArea)
						{
							EditorGUI.indentLevel++;

							EditorGUILayout.PropertyField(GTAO_Resolution, HEditorStyles.GTAO_Resolution);
							if((GTAODenoisingMode)GTAO_Denoising.enumValueIndex == GTAODenoisingMode.None && (GTAOResolutionMode)GTAO_Resolution.enumValueIndex != GTAOResolutionMode.Full)
								EditorGUILayout.HelpBox(HEditorStyles.Info_ResolutionForcedToFull, MessageType.Info);
							EditorGUILayout.PropertyField(GTAO_TracingMode, HEditorStyles.GTAO_TracingMode);
							//EditorGUILayout.PropertyField(GTAO_DepthFormat, HEditorStyles.GTAO_DepthFormat);

							GTAO_SliceCount.intValue = EditorGUILayout.IntSlider(HEditorStyles.GTAO_SliceCount, GTAO_SliceCount.intValue, 1, 4);
							GTAO_StepCount.intValue  = EditorGUILayout.IntSlider(HEditorStyles.GTAO_StepCount,  GTAO_StepCount.intValue,  8, 32);

							if ((GTAOTracingMode)GTAO_TracingMode.enumValueIndex == GTAOTracingMode.VisibilityBitmasks)
							{
								EditorGUILayout.Space(3f);
								EditorGUILayout.PropertyField(GTAO_ThicknessMode, HEditorStyles.GTAO_ThicknessMode);
								EditorGUILayout.Slider(GTAO_Thickness, 0.05f, 1.0f, HEditorStyles.GTAO_Thickness);
								GTAO_UseAttenuation.boolValue = EditorGUILayout.Toggle(HEditorStyles.GTAO_UseAttenuation, GTAO_UseAttenuation.boolValue);
							}

							EditorGUI.indentLevel--;
						}

						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);
					}

					if ((AmbientOcclusionMode)AmbientOcclusionMode.enumValueIndex == IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.RTAO)
					{
						_showVisualsArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showVisualsArea, "Visuals");
						if (_showVisualsArea)
						{
							EditorGUI.indentLevel++;

							Fadeout.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(HEditorStyles.Fadeout, Fadeout.floatValue));
							EditorGUILayout.Slider(Intensity, 0.0f, 4.0f, HEditorStyles.Intensity);
							EditorGUILayout.Slider(RTAO_MaxRayBias, 0.001f, 0.05f, HEditorStyles.RTAO_MaxRayBias);
							EditorGUILayout.Slider(RTAO_WorldSpaceRadius, 0.1f, 5.0f, HEditorStyles.RTAO_WorldSpaceRadius);
							if (!isAfterOpaque)
								EditorGUILayout.Slider(DirectLightingOcclusion, 0.0f, 1.0f, HEditorStyles.DirectLightingOcclusion);
#if UNITY_2023_2_OR_NEWER
							if (!isBeforeOpaque)
							{
								using (new EditorGUI.DisabledScope(shouldDisableRenderingLayerMasks))
								{
									EditorGUILayout.PropertyField(RTAO_ExcludeReceiving, HEditorStyles.GTAO_ExcludeReceiving);
									if (RTAO_ExcludeReceiving.FindPropertyRelative("m_Bits").uintValue != 0)
									{
										EditorGUI.indentLevel++;
										EditorGUILayout.Slider(RTAO_ExcludedIntensity, 0.0f, 1.0f, HEditorStyles.ExcludedIntensity);
										if (Mathf.Approximately(RTAO_ExcludedIntensity.floatValue, 0.0f))
											EditorGUILayout.PropertyField(RTAO_EarlyOutExcludedReceivers, HEditorStyles.EarlyOutExcludedReceivers);
										EditorGUI.indentLevel--;
									}
								}
							}
#endif

							EditorGUI.indentLevel--;
						}

						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						_showQualityArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showQualityArea, "Quality");
						if (_showQualityArea)
						{
							EditorGUI.indentLevel++;

							EditorGUILayout.PropertyField(RTAO_Resolution, HEditorStyles.RTAO_Resolution);

							if((RTAODenoisingMode)RTAO_Denoising.enumValueIndex == RTAODenoisingMode.None && (ResolutionMode)RTAO_Resolution.enumValueIndex != ResolutionMode.Full)
								EditorGUILayout.HelpBox(HEditorStyles.Info_ResolutionForcedToFull, MessageType.Info);
							RTAO_RayCount.intValue = EditorGUILayout.IntSlider(HEditorStyles.RTAO_RayCount, RTAO_RayCount.intValue, 1, 8);

							bool isInlineRayTracing = HRenderer.CurrentHRenderPipeline != HRenderPipeline.HDRP ||
							                          (HRenderer.CurrentHRenderPipeline == HRenderPipeline.HDRP && (TracingMode)RTAO_TracingMode.enumValueIndex == TracingMode.InlineRayTracing);
							bool isAdaptive = (ResolutionMode)RTAO_Resolution.enumValueIndex == ResolutionMode.Adaptive;
							if (isInlineRayTracing && isAdaptive)
							{
								RTAO_SampleRebalancing.boolValue = EditorGUILayout.Toggle(HEditorStyles.RTAO_SampleRebalancing, RTAO_SampleRebalancing.boolValue);
							}

							if (HRenderer.CurrentHRenderPipeline != HRenderPipeline.HDRP ||
							    (HRenderer.CurrentHRenderPipeline == HRenderPipeline.HDRP && (TracingMode)RTAO_TracingMode.enumValueIndex == TracingMode.InlineRayTracing))
							{
								RTAO_ScreenSpacePretrace.boolValue = EditorGUILayout.Toggle(HEditorStyles.RTAO_ScreenSpacePretrace, RTAO_ScreenSpacePretrace.boolValue);
								if (RTAO_ScreenSpacePretrace.boolValue)
								{
									EditorGUI.indentLevel++;
#if UNITY_2023_3_OR_NEWER
									EditorGUILayout.PropertyField(RTAO_PretraceMask, HEditorStyles.RTAO_PretraceMask);
#endif
									RTAO_PretraceSamples.intValue = EditorGUILayout.IntSlider(HEditorStyles.RTAO_PretraceSamples, RTAO_PretraceSamples.intValue, 8, 24);
									EditorGUILayout.Slider(RTAO_PretraceDistance, 0.1f, 1.0f, HEditorStyles.RTAO_PretraceDistance);
									EditorGUILayout.Slider(RTAO_PretraceThickness, 0.05f, 1.0f, HEditorStyles.RTAO_PretraceThickness);
									EditorGUI.indentLevel--;
								}
							}

							EditorGUI.indentLevel--;
						}

						EditorGUILayout.EndFoldoutHeaderGroup();
						EditorGUILayout.Space(3f);

						if (HRenderer.CurrentHRenderPipeline != HRenderPipeline.HDRP ||
						    (HRenderer.CurrentHRenderPipeline == HRenderPipeline.HDRP && (TracingMode)RTAO_TracingMode.enumValueIndex == TracingMode.InlineRayTracing))
						{
							_showRayTracingCullingArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showRayTracingCullingArea, "Ray Tracing Culling");
							if (_showRayTracingCullingArea)
							{
								EditorGUI.indentLevel++;
								RTAO_CullAlphaCutout.boolValue = EditorGUILayout.Toggle(HEditorStyles.RTAO_CullAlphaCutout, RTAO_CullAlphaCutout.boolValue);
								RTAO_CullBackfaces.boolValue = EditorGUILayout.Toggle(HEditorStyles.RTAO_CullBackfaces, RTAO_CullBackfaces.boolValue);
								EditorGUILayout.PropertyField(RTAO_CullingMask, HEditorStyles.RTAO_CullingMask);

								EditorGUILayout.PropertyField(RTAO_CullingMode, HEditorStyles.RTAO_CullingMode);
								if ((CullingMode)RTAO_CullingMode.enumValueIndex == CullingMode.Sphere)
								{
									EditorGUI.indentLevel++;
									RTAO_CullingDistance.floatValue = EditorGUILayout.FloatField(new GUIContent("Culling Distance"), RTAO_CullingDistance.floatValue);
									EditorGUI.indentLevel--;
								}
								else if ((CullingMode)RTAO_CullingMode.enumValueIndex == CullingMode.SolidAngle)
								{
									EditorGUI.indentLevel++;
									EditorGUILayout.Slider(RTAO_MinSolidAngle, 0.01f, 180f, new GUIContent("Min Solid Angle"));
									EditorGUI.indentLevel--;
								}
								EditorGUI.indentLevel--;
							}

							EditorGUILayout.EndFoldoutHeaderGroup();
							EditorGUILayout.Space(3f);
						}
					}

				}
			}

// ------------------------------------- Denoising ----------------------------------------------------------

			if ((AmbientOcclusionMode)AmbientOcclusionMode.enumValueIndex == IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.GTAO)
			{
				using (new HEditorUtils.FoldoutScope(AnimBoolDenoisingTab, out var shouldDraw, HEditorStyles.GTAO_DenoisingTabContent.text))
				{
					_denoisingTabOpen = shouldDraw;
					if (shouldDraw)
					{
						EditorGUILayout.Space(3f);
						EditorGUILayout.PropertyField(GTAO_Denoising, HEditorStyles.GTAO_Denoising);

						if ((GTAODenoisingMode)GTAO_Denoising.enumValueIndex != GTAODenoisingMode.None)
						{
						}
						else
						{
							EditorGUILayout.HelpBox(HEditorStyles.Info_ResolutionForcedToFull, MessageType.Info);
						}

						EditorGUILayout.Space(3f);

						var gtaoDenoisingMode = (GTAODenoisingMode)GTAO_Denoising.enumValueIndex;
						bool showTemporal = gtaoDenoisingMode == GTAODenoisingMode.TemporalOnly || gtaoDenoisingMode == GTAODenoisingMode.SpatioTemporal;
						bool showSpatial  = gtaoDenoisingMode == GTAODenoisingMode.SpatialOnly  || gtaoDenoisingMode == GTAODenoisingMode.SpatioTemporal;

						if (showTemporal)
						{
							_showTemporalArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showTemporalArea, "Temporal");
							if (_showTemporalArea)
							{
								EditorGUI.indentLevel++;

								EditorGUILayout.PropertyField(GTAO_ReprojectionFilter, HEditorStyles.GTAO_ReprojectionFilter);

								EditorGUILayout.Slider(GTAO_TemporalRejection, 0.0f, 1.0f, HEditorStyles.GTAO_TemporalRejection);
								GTAO_TemporalRejection.floatValue = Mathf.Round(GTAO_TemporalRejection.floatValue * 100f) / 100f;

								GTAO_UseSimpleRejection.boolValue = EditorGUILayout.Toggle(HEditorStyles.GTAO_UseSimpleRejection, GTAO_UseSimpleRejection.boolValue);

								if ((ReprojectionFilter)GTAO_ReprojectionFilter.enumValueIndex == ReprojectionFilter.Linear)
								{
									GTAO_UseNormalWeightingTemporal.boolValue = EditorGUILayout.Toggle(HEditorStyles.GTAO_UseNormalWeightingTemporal, GTAO_UseNormalWeightingTemporal.boolValue);
								}

								EditorGUI.indentLevel--;
							}

							EditorGUILayout.EndFoldoutHeaderGroup();
							EditorGUILayout.Space(3f);
						}

						if (showSpatial)
						{
							_showSpatialArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showSpatialArea, "Spatial");
							if (_showSpatialArea)
							{
								EditorGUI.indentLevel++;

								EditorGUILayout.PropertyField(GTAO_SpatialFilterType, HEditorStyles.GTAO_SpatialFilterType);

								if ((SpatialFilterType)GTAO_SpatialFilterType.enumValueIndex == SpatialFilterType.Disk)
								{
									EditorGUILayout.Slider(GTAO_FilterRadius, 0.0f, 1.0f, HEditorStyles.GTAO_FilterRadius);
									GTAO_FilterRadius.floatValue = Mathf.Round(GTAO_FilterRadius.floatValue * 10f) / 10f;
									EditorGUILayout.Slider(GTAO_FilterAdaptivity, 0.0f, 1.0f, HEditorStyles.GTAO_FilterAdaptivity);
									GTAO_FilterAdaptivity.floatValue = Mathf.Round(GTAO_FilterAdaptivity.floatValue * 10f) / 10f;
								}
								else
								{
									GTAO_BoxPassCount.intValue = EditorGUILayout.IntSlider(HEditorStyles.GTAO_BoxPassCount, GTAO_BoxPassCount.intValue, 1, 3);
								}

								GTAO_DoubleSampleCount.boolValue = EditorGUILayout.Toggle(HEditorStyles.GTAO_DoubleSampleCount, GTAO_DoubleSampleCount.boolValue);

								EditorGUI.indentLevel--;
							}

							EditorGUILayout.EndFoldoutHeaderGroup();
							EditorGUILayout.Space(3f);
						}
					}
				}
			}

			if ((AmbientOcclusionMode)AmbientOcclusionMode.enumValueIndex == IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.RTAO)
			{
				using (new HEditorUtils.FoldoutScope(AnimBoolDenoisingTab, out var shouldDraw, HEditorStyles.RTAO_DenoisingTabContent.text))
				{
					_denoisingTabOpen = shouldDraw;
					if (shouldDraw)
					{
						EditorGUILayout.Space(3f);
						EditorGUILayout.PropertyField(RTAO_Denoising, HEditorStyles.RTAO_Denoising);

						if ((RTAODenoisingMode)RTAO_Denoising.enumValueIndex != RTAODenoisingMode.None)
						{
						}
						else
						{
							EditorGUILayout.HelpBox(HEditorStyles.Info_ResolutionForcedToFull, MessageType.Info);
						}

						EditorGUILayout.Space(3f);

						if ((RTAODenoisingMode)RTAO_Denoising.enumValueIndex != RTAODenoisingMode.None)
						{
							_showTemporalArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showTemporalArea, "Temporal");
							if (_showTemporalArea)
							{
								EditorGUI.indentLevel++;

								EditorGUILayout.PropertyField(RTAO_ReprojectionFilter, HEditorStyles.RTAO_ReprojectionFilter);

								RTAO_SampleCountTemporal.intValue = EditorGUILayout.IntSlider(HEditorStyles.RTAO_SampleCountTemporal, RTAO_SampleCountTemporal.intValue, 8, 16);

								bool hide = RTAO_SampleCountTemporal.intValue > 0;
								GUI.enabled = hide;

								EditorGUILayout.Slider(RTAO_StaticRejection,  0.0f, 1.0f, HEditorStyles.RTAO_StaticRejection);
								RTAO_StaticRejection.floatValue = Mathf.Round(RTAO_StaticRejection.floatValue * 100f) / 100f;
								EditorGUILayout.Slider(RTAO_DynamicRejection, 0.0f, 1.0f, HEditorStyles.RTAO_DynamicRejection);
								RTAO_DynamicRejection.floatValue = Mathf.Round(RTAO_DynamicRejection.floatValue * 100f) / 100f;

								if (RTAO_RayCount.intValue < 4)
									RTAO_DisocclusionBoost.boolValue = EditorGUILayout.Toggle(HEditorStyles.RTAO_DisocclusionBoost, RTAO_DisocclusionBoost.boolValue);

								GUI.enabled = true;

								if ((ReprojectionFilter)RTAO_ReprojectionFilter.enumValueIndex != ReprojectionFilter.Bicubic)
								{
									RTAO_UseNormalWeightingTemporal.boolValue = EditorGUILayout.Toggle(HEditorStyles.RTAO_UseNormalWeightingTemporal, RTAO_UseNormalWeightingTemporal.boolValue);
								}

								EditorGUI.indentLevel--;
							}

							EditorGUILayout.EndFoldoutHeaderGroup();
							EditorGUILayout.Space(3f);

							_showSpatialArea = EditorGUILayout.BeginFoldoutHeaderGroup(_showSpatialArea, "Spatial");
							if (_showSpatialArea)
							{
								EditorGUI.indentLevel++;

								EditorGUILayout.Slider(RTAO_FilterRadius, 0.0f, 1.0f, HEditorStyles.RTAO_FilterRadius);
								RTAO_FilterRadius.floatValue = Mathf.Round(RTAO_FilterRadius.floatValue * 10f) / 10f;

								EditorGUILayout.Slider(RTAO_ContactOcclusion, 0.0f, 1.0f, HEditorStyles.RTAO_ContactOcclusion);

								bool isFullOrAdaptive = (ResolutionMode)RTAO_Resolution.enumValueIndex == ResolutionMode.Full ||
								                        (ResolutionMode)RTAO_Resolution.enumValueIndex == ResolutionMode.Adaptive;
								if (RTAO_ContactOcclusion.floatValue > 0f && (RTAODenoisingMode)RTAO_Denoising.enumValueIndex == RTAODenoisingMode.DoublePass && isFullOrAdaptive)
								{
									EditorGUI.indentLevel++;
									RTAO_ContactOcclusionDenoise.boolValue = EditorGUILayout.Toggle(HEditorStyles.RTAO_ContactOcclusionDenoise, RTAO_ContactOcclusionDenoise.boolValue);
									EditorGUI.indentLevel--;
								}

								EditorGUI.indentLevel--;
							}

							EditorGUILayout.EndFoldoutHeaderGroup();
							EditorGUILayout.Space(3f);
						}
					}
				}
			}

// ------------------------------------- Debug settings ----------------------------------------------------------


			HEditorUtils.HorizontalLine(1f);
			EditorGUILayout.Space(3f);

			HEditorUtils.DrawLinkRow(
				($"Documentation (v. " + HNames.HTRACE_AO_VERSION + ")", () => Application.OpenURL(HNames.HTRACE_AO_DOCUMENTATION_LINK)),
				("Discord", () => Application.OpenURL(HNames.HTRACE_DISCORD_LINK)),
				("Bug report", () => Application.OpenURL(HNames.HTRACE_DISCORD_BUGS_AO_LINK))
			);

			GUI.backgroundColor = standartBackgroundColor;
			GUI.color           = standartColor;

			serializedObject.ApplyModifiedProperties();
		}
		
		private void WarningsHandle()
		{
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

			if ((AmbientOcclusionMode)AmbientOcclusionMode.enumValueIndex == IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.RTAO)
			{
				if (HRenderer.SupportsInlineRayTracing == false) // URP has only Inline Raytracing, but we output realtime RT error to avoid confusing users
					EditorGUILayout.HelpBox(RT_IS_NOT_SUPPORTED_MESSAGE, MessageType.Error);
			}
			
		}

		private void DebugPart()
		{
			using (new HEditorUtils.FoldoutScope(AnimBoolDebugTab, out var shouldDraw, HEditorStyles.DebugContent.text, toggle: null))
			{
				_debugTabOpen = shouldDraw;
				if (shouldDraw)
				{
					EditorGUILayout.PropertyField(HTraceLayer, HEditorStyles.hTraceLayerContent);

					EditorGUILayout.PropertyField(ShowBowels, new GUIContent("Show Bowels"));
					ShowFullDebugLog.boolValue = EditorGUILayout.Toggle(new GUIContent("Show Full Debug Log"), ShowFullDebugLog.boolValue);

					TestCheckBox1.boolValue = EditorGUILayout.Toggle(new GUIContent("TestCheckBox1"), TestCheckBox1.boolValue);
					TestCheckBox2.boolValue = EditorGUILayout.Toggle(new GUIContent("TestCheckBox2"), TestCheckBox2.boolValue);
					TestCheckBox3.boolValue = EditorGUILayout.Toggle(new GUIContent("TestCheckBox3"), TestCheckBox3.boolValue);

					EditorGUILayout.Space(3);
				}
			}
		}

		private void UpdateStandartStyles()
		{
			HEditorStyles.foldout.fontStyle = FontStyle.Bold;
		}

		private void PropertiesRelative()
		{
			GeneralSettings = serializedObject.FindProperty("GeneralSettings");
			SSAOSettings    = serializedObject.FindProperty("SSAOSettings");
			GTAOSettings    = serializedObject.FindProperty("GTAOSettings");
			RTAOSettings    = serializedObject.FindProperty("RTAOSettings");
			DebugSettings   = serializedObject.FindProperty("DebugSettings");

			// Debug Settings
			HInjectionPoint  = DebugSettings.FindPropertyRelative("HInjectionPoint");
			HTraceLayer      = DebugSettings.FindPropertyRelative("HTraceLayer");
			ShowBowels       = DebugSettings.FindPropertyRelative("ShowBowels");
			ShowFullDebugLog = DebugSettings.FindPropertyRelative("ShowFullDebugLog");
			TestCheckBox1    = DebugSettings.FindPropertyRelative("TestCheckBox1");
			TestCheckBox2    = DebugSettings.FindPropertyRelative("TestCheckBox2");
			TestCheckBox3    = DebugSettings.FindPropertyRelative("TestCheckBox3");

			// Global Tab
			HBuffer                 = GeneralSettings.FindPropertyRelative("HBuffer");
			AmbientOcclusionMode    = GeneralSettings.FindPropertyRelative("AmbientOcclusionMode");
			Intensity               = GeneralSettings.FindPropertyRelative("_intensity");
			Fadeout                 = GeneralSettings.FindPropertyRelative("Fadeout");
			DirectLightingOcclusion = GeneralSettings.FindPropertyRelative("_directLightOcclusion");
			OutputDithering         = GeneralSettings.FindPropertyRelative("OutputDithering");
			OutputDitheringAnimate  = GeneralSettings.FindPropertyRelative("OutputDitheringAnimate");
			InjectionPoint          = GeneralSettings.FindPropertyRelative("InjectionPoint");
			ReconstructNormals      = GeneralSettings.FindPropertyRelative("ReconstructNormals");

			// SSAO Tab
			SSAO_DebugMode          = SSAOSettings.FindPropertyRelative("DebugModeSSAO");
			SSAO_Thickness          = SSAOSettings.FindPropertyRelative("_thickness");
			SSAO_Radius             = SSAOSettings.FindPropertyRelative("_radius");
#if UNITY_2023_3_OR_NEWER
			SSAO_ExcludeCasting    = SSAOSettings.FindPropertyRelative("ExcludeCasting");
			SSAO_ExcludeReceiving  = SSAOSettings.FindPropertyRelative("ExcludeReceiving");
			SSAO_ExcludedIntensity = SSAOSettings.FindPropertyRelative("_excludedIntensity");
#endif

			// GTAO Tab
			GTAO_DebugMode                 = GTAOSettings.FindPropertyRelative("DebugMode");
			GTAO_Thickness                 = GTAOSettings.FindPropertyRelative("_thickness");
			GTAO_WorldSpaceRadius          = GTAOSettings.FindPropertyRelative("_worldSpaceRadius");
			GTAO_ScreenSpaceRadius         = GTAOSettings.FindPropertyRelative("_screenSpaceRadius");
			GTAO_SliceCount                = GTAOSettings.FindPropertyRelative("_sliceCount");
			GTAO_StepCount                 = GTAOSettings.FindPropertyRelative("_stepCount");
			GTAO_Resolution                = GTAOSettings.FindPropertyRelative("Resolution");
			GTAO_TracingMode               = GTAOSettings.FindPropertyRelative("TracingMode");
			//GTAO_DepthFormat               = GTAOSettings.FindPropertyRelative("DepthFormat");
			GTAO_ThicknessMode             = GTAOSettings.FindPropertyRelative("ThicknessMode");
			GTAO_UseAttenuation            = GTAOSettings.FindPropertyRelative("UseAttenuation");
			GTAO_UseSimpleRejection        = GTAOSettings.FindPropertyRelative("UseSimpleRejection");
			GTAO_TemporalRejection         = GTAOSettings.FindPropertyRelative("_temporalRejection");
			GTAO_SampleCountTemporal       = GTAOSettings.FindPropertyRelative("_sampleCountTemporal");
			GTAO_ReprojectionFilter        = GTAOSettings.FindPropertyRelative("ReprojectionFilter");
			GTAO_UseNormalWeightingTemporal = GTAOSettings.FindPropertyRelative("UseNormalWeightingTemporal");
			GTAO_SpatialFilterType         = GTAOSettings.FindPropertyRelative("SpatialFilterType");
			GTAO_FilterRadius              = GTAOSettings.FindPropertyRelative("_filterRadius");
			GTAO_FilterAdaptivity          = GTAOSettings.FindPropertyRelative("_filterAdaptivity");
			GTAO_BoxPassCount              = GTAOSettings.FindPropertyRelative("_boxPassCount");
			GTAO_DoubleSampleCount         = GTAOSettings.FindPropertyRelative("DoubleSampleCount");
			GTAO_Denoising                 = GTAOSettings.FindPropertyRelative("Denoising");
#if UNITY_2023_3_OR_NEWER
			GTAO_ExcludeCasting            = GTAOSettings.FindPropertyRelative("ExcludeCasting");
			GTAO_ExcludeReceiving          = GTAOSettings.FindPropertyRelative("ExcludeReceiving");
			GTAO_ExcludedIntensity         = GTAOSettings.FindPropertyRelative("_excludedIntensity");
			GTAO_EarlyOutExcludedReceivers = GTAOSettings.FindPropertyRelative("EarlyOutExcludedReceivers");
#endif

			// RTAO Tab
			RTAO_DebugMode                 = RTAOSettings.FindPropertyRelative("DebugMode");
			RTAO_DebugSceneMismatch        = RTAOSettings.FindPropertyRelative("DebugSceneMismatch");
			RTAO_WorldSpaceRadius          = RTAOSettings.FindPropertyRelative("_worldSpaceRadius");
			RTAO_MaxRayBias                = RTAOSettings.FindPropertyRelative("_maxRayBias");
			RTAO_SpecularOcclusion         = RTAOSettings.FindPropertyRelative("_specularOcclusion");
			RTAO_TracingMode               = RTAOSettings.FindPropertyRelative("TracingMode");
			RTAO_CullAlphaCutout           = RTAOSettings.FindPropertyRelative("CullAlphaCutout");
			RTAO_CullingMask               = RTAOSettings.FindPropertyRelative("CullingMask");
			RTAO_RayCount                  = RTAOSettings.FindPropertyRelative("_rayCount");
			RTAO_SampleRebalancing         = RTAOSettings.FindPropertyRelative("SampleRebalancing");
			RTAO_CullBackfaces             = RTAOSettings.FindPropertyRelative("CullBackfaces");
			RTAO_Resolution                = RTAOSettings.FindPropertyRelative("Resolution");
			RTAO_Denoising                 = RTAOSettings.FindPropertyRelative("Denoising");
			RTAO_SampleCountTemporal       = RTAOSettings.FindPropertyRelative("_sampleCountTemporal");
			RTAO_StaticRejection           = RTAOSettings.FindPropertyRelative("_staticRejection");
			RTAO_DynamicRejection          = RTAOSettings.FindPropertyRelative("_dynamicRejection");
			RTAO_ReprojectionFilter        = RTAOSettings.FindPropertyRelative("ReprojectionFilter");
			RTAO_UseNormalWeightingTemporal = RTAOSettings.FindPropertyRelative("UseNormalWeightingTemporal");
			RTAO_DisocclusionBoost         = RTAOSettings.FindPropertyRelative("DisocclusionBoost");
			RTAO_FilterRadius              = RTAOSettings.FindPropertyRelative("_filterRadius");
			RTAO_ContactOcclusion          = RTAOSettings.FindPropertyRelative("_contactOcclusion");
			RTAO_ContactOcclusionDenoise   = RTAOSettings.FindPropertyRelative("ContactOcclusionDenoise");
			RTAO_ScreenSpacePretrace       = RTAOSettings.FindPropertyRelative("ScreenSpacePretrace");
#if UNITY_2023_3_OR_NEWER
			RTAO_PretraceMask              = RTAOSettings.FindPropertyRelative("PretraceMask");
#endif
			RTAO_PretraceSamples           = RTAOSettings.FindPropertyRelative("_pretraceSamples");
			RTAO_PretraceDistance          = RTAOSettings.FindPropertyRelative("_pretraceDistance");
			RTAO_PretraceThickness         = RTAOSettings.FindPropertyRelative("_pretraceThickness");
			RTAO_CullingMode               = RTAOSettings.FindPropertyRelative("CullingMode");
			RTAO_CullingDistance           = RTAOSettings.FindPropertyRelative("_cullingDistance");
			RTAO_MinSolidAngle             = RTAOSettings.FindPropertyRelative("_minSolidAngle");
			RTAO_ExcludeReceiving          = RTAOSettings.FindPropertyRelative("ExcludeReceiving");
			RTAO_ExcludedIntensity         = RTAOSettings.FindPropertyRelative("_excludedIntensity");
			RTAO_EarlyOutExcludedReceivers = RTAOSettings.FindPropertyRelative("EarlyOutExcludedReceivers");
		}
	}
}
#endif
