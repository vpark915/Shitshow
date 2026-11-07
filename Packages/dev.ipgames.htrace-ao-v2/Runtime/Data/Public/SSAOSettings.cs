using System;
using UnityEngine;

namespace IPGames.HTraceAO.Runtime.Data.Public
{
	[Serializable]
	public class SSAOSettings
	{
		public global::IPGames.HTraceAO.Runtime.Globals.DebugModeSSAO DebugModeSSAO = global::IPGames.HTraceAO.Runtime.Globals.DebugModeSSAO.None;

#if UNITY_2023_3_OR_NEWER
		[SerializeField]
		public RenderingLayerMask ExcludeCasting = 0;

		[SerializeField]
		public RenderingLayerMask ExcludeReceiving = 0;
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

				_excludedIntensity = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(SSAOSettings), nameof(SSAOSettings.ExcludedIntensity));
			}
		}

		[SerializeField]
		private float _thickness = 0.15f;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[0.0;1.0]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(0.0f,1.0f)]
		public float Thickness
		{
			get => _thickness;    
			set
			{
				if (Mathf.Abs(value - _thickness) < Mathf.Epsilon)
					return;

				_thickness = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(SSAOSettings), nameof(SSAOSettings.Thickness));
			}
		}
		
		[SerializeField]
		private int _radius = 2;
		/// <summary>
		/// 
		/// </summary>
		/// <value>[1;4]</value>
		[global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.HRangeAttribute(1,4)]
		public int Radius
		{
			get
			{
				return _radius;
			}
			set
			{
				if (value == _radius)
					return;

				_radius = global::IPGames.HTraceAO.Runtime.Extensions.HExtensions.Clamp(value, typeof(SSAOSettings), nameof(SSAOSettings.Radius));
			}
		}
	}
}
