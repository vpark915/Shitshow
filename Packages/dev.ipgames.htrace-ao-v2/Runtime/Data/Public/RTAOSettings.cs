using System;
using UnityEngine;

namespace IPGames.HTraceAO.Runtime.Data.Public
{
	[Serializable]
	public class RTAOSettings
	{
#if UNITY_EDITOR
		[Extensions.HExtensions.FilteredEnum(nameof(ShouldShowDebugMode))]
#endif
		public global::IPGames.HTraceAO.Runtime.Globals.DebugModeRTAO DebugMode = global::IPGames.HTraceAO.Runtime.Globals.DebugModeRTAO.None;

		[SerializeField]
		public bool DebugSceneMismatch = true;
		
		// ----------------------------------------------- Visuals -----------------------------------------------
		
		[SerializeField]
		private float _worldSpaceRadius = 1f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.1;5.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.1f,5.0f)]
		public float WorldSpaceRadius
		{
			get => _worldSpaceRadius;    
			set
			{
				if (Mathf.Abs(value - _worldSpaceRadius) < Mathf.Epsilon)
					return;

				_worldSpaceRadius = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.WorldSpaceRadius));
			}
		}
		
		[SerializeField]
		private float _maxRayBias = 0.001f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.001;0.05]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.001f,0.05f)] 
		public float MaxRayBias
		{
			get => _maxRayBias;    
			set
			{
				if (Mathf.Abs(value - _maxRayBias) < Mathf.Epsilon)
					return;

				_maxRayBias = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.MaxRayBias));
			}
		}
		
		
		[SerializeField]
		private float _specularOcclusion = 0.5f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float SpecularOcclusion
		{
			get => _specularOcclusion;    
			set
			{
				if (Mathf.Abs(value - _specularOcclusion) < Mathf.Epsilon)
					return;

				_specularOcclusion = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.SpecularOcclusion));
			}
		}
		
		// ----------------------------------------------- Quality -----------------------------------------------

		/// <summary>
		/// Only HDRP
		/// </summary>
		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.TracingMode TracingMode

		= global::IPGames.HTraceAO.Runtime.Globals.TracingMode.InlineRayTracing;

		[SerializeField]
		private int _rayCount = 4;
		/// <summary>
		///
		/// </summary>
		/// <value>[1;8]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(1,8)]
		public int RayCount
		{
			get
			{
				return _rayCount;
			}
			set
			{
				if (value == _rayCount)
					return;

				_rayCount = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.RayCount));
			}
		}

		[SerializeField]
		public bool SampleRebalancing = false;

		[SerializeField]
		public bool CullBackfaces = false;

		[SerializeField]
		public bool CullAlphaCutout = false;

		/// <summary>
		/// Use this option to exclude objects from the Ray Tracing Acceleration Structure on a per-layer basis.
		/// </summary>
		[SerializeField]
		public LayerMask CullingMask = ~0;

		// ----------------------------------------------- Screen Space Pretrace -----------------------------------------------

		[SerializeField]
		public bool ScreenSpacePretrace = false;

#if UNITY_2023_3_OR_NEWER
		[SerializeField]
		public RenderingLayerMask PretraceMask = RenderingLayerMask.defaultRenderingLayerMask;
#endif

		[SerializeField]
		private int _pretraceSamples = 12;
		/// <summary>
		///
		/// </summary>
		/// <value>[8;24]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(8,24)]
		public int PretraceSamples
		{
			get => _pretraceSamples;
			set
			{
				if (value == _pretraceSamples)
					return;

				_pretraceSamples = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.PretraceSamples));
			}
		}

		[SerializeField]
		private float _pretraceDistance = 0.5f;
		/// <summary>
		///
		/// </summary>
		/// <value>[0.1;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.1f,1.0f)]
		public float PretraceDistance
		{
			get => _pretraceDistance;
			set
			{
				if (Mathf.Abs(value - _pretraceDistance) < Mathf.Epsilon)
					return;

				_pretraceDistance = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.PretraceDistance));
			}
		}

		[SerializeField]
		private float _pretraceThickness = 0.1f;
		/// <summary>
		///
		/// </summary>
		/// <value>[0.05;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.05f,1.0f)]
		public float PretraceThickness
		{
			get => _pretraceThickness;
			set
			{
				if (Mathf.Abs(value - _pretraceThickness) < Mathf.Epsilon)
					return;

				_pretraceThickness = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.PretraceThickness));
			}
		}

		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.CullingMode CullingMode = global::IPGames.HTraceAO.Runtime.Globals.CullingMode.None;

		[SerializeField]
		private float _cullingDistance = 1000f;
		/// <summary>
		///
		/// </summary>
		/// <value>[1;10000]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(1f, 10000f)]
		public float CullingDistance
		{
			get => _cullingDistance;
			set
			{
				if (Mathf.Abs(value - _cullingDistance) < Mathf.Epsilon)
					return;

				_cullingDistance = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.CullingDistance));
			}
		}

		[SerializeField]
		private float _minSolidAngle = 4f;
		/// <summary>
		///
		/// </summary>
		/// <value>[0.01;180]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.01f, 180f)]
		public float MinSolidAngle
		{
			get => _minSolidAngle;
			set
			{
				if (Mathf.Abs(value - _minSolidAngle) < Mathf.Epsilon)
					return;

				_minSolidAngle = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.MinSolidAngle));
			}
		}

		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.ResolutionMode Resolution = global::IPGames.HTraceAO.Runtime.Globals.ResolutionMode.Adaptive;

		// ----------------------------------------------- DENOISING -----------------------------------------------

		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.RTAODenoisingMode Denoising = global::IPGames.HTraceAO.Runtime.Globals.RTAODenoisingMode.DoublePass;

		// ----------------------------------------------- Temporal -----------------------------------------------

		[SerializeField]
		private int _sampleCountTemporal = 16;
		/// <summary>
		///
		/// </summary>
		/// <value>[8;16]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(8,16)]
		public int SampleCountTemporal
		{
			get => _sampleCountTemporal;    
			set
			{
				if (value == _sampleCountTemporal)
					return;

				_sampleCountTemporal = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.SampleCountTemporal));
			}
		}
		
		[SerializeField]
		private float _staticRejection = 0.15f;
		/// <summary>
		///
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float StaticRejection
		{
			get => _staticRejection;
			set
			{
				if (Mathf.Abs(value - _staticRejection) < Mathf.Epsilon)
					return;

				_staticRejection = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.StaticRejection));
			}
		}

		[SerializeField]
		private float _dynamicRejection = 0.4f;
		/// <summary>
		///
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float DynamicRejection
		{
			get => _dynamicRejection;
			set
			{
				if (Mathf.Abs(value - _dynamicRejection) < Mathf.Epsilon)
					return;

				_dynamicRejection = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.DynamicRejection));
			}
		}
		
		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.ReprojectionFilter ReprojectionFilter = global::IPGames.HTraceAO.Runtime.Globals.ReprojectionFilter.Linear;

		[SerializeField]
		public bool UseNormalWeightingTemporal = true;

		[SerializeField]
		public bool DisocclusionBoost = true;

		// ----------------------------------------------- Spatial -----------------------------------------------

		[SerializeField]
		private float _filterRadius = 0.5f;
		/// <summary>
		///
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float FilterRadius
		{
			get => global::IPGames.HTraceAO.Runtime.Globals.HMath.Remap(_filterRadius, 0f, 1f, 0f, 0.08f);
			set
			{
				if (Mathf.Abs(value - _filterRadius) < Mathf.Epsilon)
					return;

				_filterRadius = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.FilterRadius));
			}
		}

		[SerializeField]
		private float _contactOcclusion = 0f;
		/// <summary>
		///
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float ContactOcclusion
		{
			get => _contactOcclusion;
			set
			{
				if (Mathf.Abs(value - _contactOcclusion) < Mathf.Epsilon)
					return;

				_contactOcclusion = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.ContactOcclusion));
			}
		}

		[SerializeField]
		public bool ContactOcclusionDenoise = true;

#if UNITY_2023_2_OR_NEWER
		[SerializeField]
		public RenderingLayerMask ExcludeReceiving = 0;

		[SerializeField]
		public bool EarlyOutExcludedReceivers = false;
#endif

		[SerializeField]
		private float _excludedIntensity = 0f;
		/// <summary>
		/// Controls the AO intensity on excluded receiving surfaces. 0 = no AO, 1 = full AO.
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float ExcludedIntensity
		{
			get => _excludedIntensity;
			set
			{
				if (Mathf.Abs(value - _excludedIntensity) < Mathf.Epsilon)
					return;

				_excludedIntensity = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(RTAOSettings), nameof(RTAOSettings.ExcludedIntensity));
			}
		}

#if UNITY_EDITOR
		public bool ShouldShowDebugMode(Globals.DebugModeRTAO value)
		{
			// Hide ShadingRateVisualization if Resolution is Full\Half\Quarter
			if (value == Globals.DebugModeRTAO.ShadingRateVisualization)
				return Resolution == Globals.ResolutionMode.Adaptive;

			// Hide HardwareRayTracingScene if TracingMode is Native
			// (only show for Inline which uses RTAS directly)
			if (value == Globals.DebugModeRTAO.HardwareRayTracingScene)
				return TracingMode == Globals.TracingMode.InlineRayTracing;

			return true;
		}
#endif
	}
}
