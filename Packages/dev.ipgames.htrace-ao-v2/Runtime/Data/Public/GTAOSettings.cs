using System;
using UnityEngine;

namespace IPGames.HTraceAO.Runtime.Data.Public
{
	[Serializable]
	public class GTAOSettings
	{
		public global::IPGames.HTraceAO.Runtime.Globals.DebugModeGTAO DebugMode = global::IPGames.HTraceAO.Runtime.Globals.DebugModeGTAO.None;
		
		// ----------------------------------------------- Visuals -----------------------------------------------

		[SerializeField]
		private float _thickness = 0.2f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.05;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.05f,1.0f)]
		public float Thickness
		{
			get => _thickness;    
			set
			{
				if (Mathf.Abs(value - _thickness) < Mathf.Epsilon)
					return;

				_thickness = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.Thickness));
			}
		}
		
		[SerializeField]
		private float _worldSpaceRadius = 1f;
		/// <summary>
		///
		/// </summary>
		/// <value>[0.25;5.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.1f,5.0f)]
		public float WorldSpaceRadius
		{
			get => _worldSpaceRadius;
			set
			{
				if (Mathf.Abs(value - _worldSpaceRadius) < Mathf.Epsilon)
					return;

				_worldSpaceRadius = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.WorldSpaceRadius));
			}
		}

		[SerializeField]
		private int _screenSpaceRadius = 25;
		/// <summary>
		///
		/// </summary>
		/// <value>[1;256]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(1,256)]
		public int ScreenSpaceRadius
		{
			get => _screenSpaceRadius;
			set
			{
				if (value == _screenSpaceRadius)
					return;

				_screenSpaceRadius = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.ScreenSpaceRadius));
			}
		}

		// ----------------------------------------------- Quality -----------------------------------------------
		
		[SerializeField]
		private int _sliceCount = 2;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[1;4]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(1,4)]
		public int SliceCount
		{
			get
			{
				return _sliceCount;
			}
			set
			{
				if (value == _sliceCount)
					return;

				_sliceCount = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.SliceCount));
			}
		}
		
		[SerializeField]
		private int _stepCount = 16;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[8;32]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(8,32)]
		public int StepCount
		{
			get => _stepCount;    
			set
			{
				if (value == _stepCount)
					return;

				_stepCount = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.StepCount));
			}
		}

		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.GTAOResolutionMode Resolution = global::IPGames.HTraceAO.Runtime.Globals.GTAOResolutionMode.Full;

		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.GTAOTracingMode TracingMode = global::IPGames.HTraceAO.Runtime.Globals.GTAOTracingMode.HorizonSearch;

		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.GTAODepthFormat DepthFormat = global::IPGames.HTraceAO.Runtime.Globals.GTAODepthFormat.R16;

		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.GTAOThicknessMode ThicknessMode = global::IPGames.HTraceAO.Runtime.Globals.GTAOThicknessMode.Linear;

		[SerializeField]
		public bool UseAttenuation = true;

		public bool VisibilityBitmasks => TracingMode == global::IPGames.HTraceAO.Runtime.Globals.GTAOTracingMode.VisibilityBitmasks;

		// ----------------------------------------------- DENOISING -----------------------------------------------

		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.GTAODenoisingMode Denoising = global::IPGames.HTraceAO.Runtime.Globals.GTAODenoisingMode.SpatioTemporal;

		// ----------------------------------------------- Temporal -----------------------------------------------

		[SerializeField]
		public bool UseSimpleRejection = false;

		[SerializeField]
		private float _temporalRejection = 0.7f;
		/// <summary>
		/// Controls the strictness of temporal history rejection.
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float TemporalRejection
		{
			get => _temporalRejection;
			set
			{
				if (Mathf.Abs(value - _temporalRejection) < Mathf.Epsilon)
					return;

				_temporalRejection = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.TemporalRejection));
			}
		}

		[SerializeField]
		private int _sampleCountTemporal = 8;
		/// <summary>
		///
		/// </summary>
		/// <value>[0;12]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0,12)]
		public int SampleCountTemporal
		{
			get => _sampleCountTemporal;
			set
			{
				if (value == _sampleCountTemporal)
					return;

				_sampleCountTemporal = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.SampleCountTemporal));
			}
		}
		
		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.ReprojectionFilter ReprojectionFilter = global::IPGames.HTraceAO.Runtime.Globals.ReprojectionFilter.Linear;

		[SerializeField]
		public bool UseNormalWeightingTemporal = true;

		// ----------------------------------------------- Spatial -----------------------------------------------

		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.SpatialFilterType SpatialFilterType = global::IPGames.HTraceAO.Runtime.Globals.SpatialFilterType.Box;

		[SerializeField]
		private float _filterRadius = 0.4f;
		/// <summary>
		///
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float FilterRadius
		{
			get => global::IPGames.HTraceAO.Runtime.Globals.HMath.Remap(_filterRadius, 0f, 1f, 0.00f, 0.12f);
			set
			{
				if (Mathf.Abs(value - _filterRadius) < Mathf.Epsilon)
					return;

				_filterRadius = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.FilterRadius));
			}
		}

		[SerializeField]
		private float _filterAdaptivity = 0.1f;
		/// <summary>
		/// Controls how much the spatial filter adapts to local contrast.
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float FilterAdaptivity
		{
			get => _filterAdaptivity;
			set
			{
				if (Mathf.Abs(value - _filterAdaptivity) < Mathf.Epsilon)
					return;

				_filterAdaptivity = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.FilterAdaptivity));
			}
		}

		[SerializeField]
		private int _boxPassCount = 2;
		/// <summary>
		/// Number of box filter passes.
		/// </summary>
		/// <value>[1;3]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(1,3)]
		public int BoxPassCount
		{
			get => _boxPassCount;
			set
			{
				if (value == _boxPassCount)
					return;

				_boxPassCount = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.BoxPassCount));
			}
		}

		[SerializeField]
		public bool DoubleSampleCount = false;

		// ----------------------------------------------- Rendering Layer Masks -----------------------------------------------

		
#if UNITY_2023_3_OR_NEWER
		[SerializeField]
		public RenderingLayerMask ExcludeCasting = 0;

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

				_excludedIntensity = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GTAOSettings), nameof(GTAOSettings.ExcludedIntensity));
			}
		}
	}
}
