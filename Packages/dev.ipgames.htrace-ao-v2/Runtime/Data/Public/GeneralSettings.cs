using System;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEngine;

namespace IPGames.HTraceAO.Runtime.Data.Public
{
	[Serializable]
	public class GeneralSettings
	{
		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.HBuffer HBuffer = global::IPGames.HTraceAO.Runtime.Globals.HBuffer.Multi;
		
#if UNITY_EDITOR
		[Extensions.HExtensions.FilteredEnum(nameof(ShouldShowRTAO))]
#endif
		[SerializeField]
		public global::IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode AmbientOcclusionMode = global::IPGames.HTraceAO.Runtime.Globals.AmbientOcclusionMode.GTAO;

		[SerializeField]
		private float _intensity = 2f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.1;4.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,4.0f)]
		public float Intensity
		{
			get => _intensity;    
			set
			{
				if (Mathf.Abs(value - _intensity) < Mathf.Epsilon)
					return;

				_intensity = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GeneralSettings), nameof(GeneralSettings.Intensity));
			}
		}
		
		[SerializeField]
		public bool OutputDithering = false;

		[SerializeField]
		public bool OutputDitheringAnimate = false;

		[SerializeField]
		private float _directLightOcclusion = 0f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float DirectLightOcclusion
		{
			get => _directLightOcclusion;    
			set
			{
				if (Mathf.Abs(value - _directLightOcclusion) < Mathf.Epsilon)
					return;

				_directLightOcclusion = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(GeneralSettings), nameof(GeneralSettings.DirectLightOcclusion));
			}
		}

		[SerializeField]
		public float Fadeout = 0f;

		[SerializeField]
		public URPInjectionPoint InjectionPoint = URPInjectionPoint.BeforeLighting;

		[SerializeField]
		public bool ReconstructNormals = false;


#if UNITY_EDITOR
		public bool ShouldShowRTAO(AmbientOcclusionMode value)
		{
			if (value == AmbientOcclusionMode.RTAO)
			{
#if UNITY_2023_2_OR_NEWER
				return true;
#else
				return false;
#endif
			}
			
			return true;
		}
#endif
	}
}
