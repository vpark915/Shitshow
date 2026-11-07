using System;
using IPGames.HTraceAO.Runtime.Extensions;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor.PackageManager;
#endif
#if UNITY_2022
using UnityEngine.Experimental.Rendering; //using UnityEngine.Experimental.Rendering; for 2022 UNITY_2022
#endif

namespace IPGames.HTraceAO.Runtime.RuntimeResources
{
	[Serializable]
	internal struct HTraceAOResources
	{
		[SerializeField] internal ComputeResources Computes;
		[SerializeField] internal SharedResources  Shared;
		[SerializeField] internal URPResources     URP;
		[SerializeField] internal SRPResources     SRP;

		public bool IsValid =>
			Computes.IsValid &&
			Shared.IsValid
			&& URP.IsValid && SRP.IsValid
			;

		public bool Initialize()
		{
			bool changed = false;
			changed |= Computes.Initialize();
			changed |= Shared.Initialize();
			changed |= URP.Initialize();
			changed |= SRP.Initialize();
			return changed;
		}

#if UNITY_EDITOR
		public static HTraceAOResources CreateEditorResources()
		{
			var resources = default(HTraceAOResources);
			resources.Initialize();
			return resources;
		}
#endif

#if UNITY_EDITOR
		private static string RuntimeAssetPath(string relativePath)
		{
			var packageInfo = PackageInfo.FindForAssembly(typeof(HTraceAOResources).Assembly);
			string packageRoot = packageInfo != null ? packageInfo.assetPath : $"Assets/{HNames.ASSET_NAME}";
			return $"{packageRoot}/Runtime/RuntimeResources/{relativePath}";
		}
#endif

		[Serializable]
		internal struct ComputeResources
		{
			[SerializeField] internal ComputeShader    HContactOcclusionRTAO;
			[SerializeField] internal ComputeShader    HDebugAO;
			[SerializeField] internal ComputeShader    HDenoiseSSAO;
			[SerializeField] internal ComputeShader    HDepthPyramidAO;
			[SerializeField] internal ComputeShader    HDepthPyramidSSAO;
			[SerializeField] internal ComputeShader    HRenderGTAO;
			[SerializeField] internal ComputeShader    HRenderRTAO;
			[SerializeField] internal ComputeShader    HRenderSSAO;
			[SerializeField] internal ComputeShader    HSpatialFilterGTAO;
			[SerializeField] internal ComputeShader    HSpatialFilterRTAO;
			[SerializeField] internal ComputeShader    HTemporalFilterGTAO;
			[SerializeField] internal ComputeShader    HTemporalFilterRTAO;
			[SerializeField] internal ComputeShader    HThreadCompactionRTAO;

			public bool IsValid =>
				HContactOcclusionRTAO != null &&
				HDebugAO != null &&
				HDenoiseSSAO != null &&
				HDepthPyramidAO != null &&
				HDepthPyramidSSAO != null &&
				HRenderGTAO != null &&
				(HRenderer.SupportsInlineRayTracing == false || HRenderRTAO != null) &&
				HRenderSSAO != null &&
				HSpatialFilterGTAO != null &&
				HSpatialFilterRTAO != null &&
				HTemporalFilterGTAO != null &&
				HTemporalFilterRTAO != null &&
				HThreadCompactionRTAO != null;

			public bool Initialize()
			{
				bool changed = false;
#if UNITY_EDITOR
				changed |= HExtensions.Load(ref HContactOcclusionRTAO, RuntimeAssetPath("Computes/HContactOcclusionRTAO.compute"));
				changed |= HExtensions.Load(ref HDebugAO, RuntimeAssetPath("Computes/HDebugAO.compute"));
				changed |= HExtensions.Load(ref HDenoiseSSAO, RuntimeAssetPath("Computes/HDenoiseSSAO.compute"));
				changed |= HExtensions.Load(ref HDepthPyramidAO, RuntimeAssetPath("Computes/HDepthPyramidAO.compute"));
				changed |= HExtensions.Load(ref HDepthPyramidSSAO, RuntimeAssetPath("Computes/HDepthPyramidSSAO.compute"));
				changed |= HExtensions.Load(ref HRenderGTAO, RuntimeAssetPath("Computes/HRenderGTAO.compute"));
				if (HRenderer.SupportsInlineRayTracing)
					changed |= HExtensions.Load(ref HRenderRTAO, RuntimeAssetPath("Computes/HRenderRTAO.compute"));
				changed |= HExtensions.Load(ref HRenderSSAO, RuntimeAssetPath("Computes/HRenderSSAO.compute"));
				changed |= HExtensions.Load(ref HSpatialFilterGTAO, RuntimeAssetPath("Computes/HSpatialFilterGTAO.compute"));
				changed |= HExtensions.Load(ref HSpatialFilterRTAO, RuntimeAssetPath("Computes/HSpatialFilterRTAO.compute"));
				changed |= HExtensions.Load(ref HTemporalFilterGTAO, RuntimeAssetPath("Computes/HTemporalFilterGTAO.compute"));
				changed |= HExtensions.Load(ref HTemporalFilterRTAO, RuntimeAssetPath("Computes/HTemporalFilterRTAO.compute"));
				changed |= HExtensions.Load(ref HThreadCompactionRTAO, RuntimeAssetPath("Computes/HThreadCompactionRTAO.compute"));
#endif
				return changed;
			}
		}

		[Serializable]
		internal struct SharedResources
		{
			[SerializeField] internal Texture2D OwenScrambledNoise256;
			[SerializeField] internal Texture2D ScramblingTile8Spp;
			[SerializeField] internal Texture2D RankingTile8Spp;
			[SerializeField] internal Texture2D ScrambleNoise;
			[SerializeField] internal Texture3D Stbn1D128X128X16;
			[SerializeField] internal Texture3D Stbn2D128X128X16;

			public bool IsValid =>
				OwenScrambledNoise256 != null &&
				ScramblingTile8Spp != null &&
				RankingTile8Spp != null &&
				ScrambleNoise != null &&
				Stbn1D128X128X16 != null &&
				Stbn2D128X128X16 != null;

			public bool Initialize()
			{
				bool changed = false;
#if UNITY_EDITOR
				changed |= HExtensions.Load(ref OwenScrambledNoise256, RuntimeAssetPath("BlueNoise/OwenScrambledNoise256.png"));
				changed |= HExtensions.Load(ref ScramblingTile8Spp, RuntimeAssetPath("BlueNoise/ScramblingTile8SPP.png"));
				changed |= HExtensions.Load(ref RankingTile8Spp, RuntimeAssetPath("BlueNoise/RankingTile8SPP.png"));
				changed |= HExtensions.Load(ref ScrambleNoise, RuntimeAssetPath("BlueNoise/ScrambleNoise.png"));
				changed |= HExtensions.Load(ref Stbn1D128X128X16, RuntimeAssetPath("BlueNoise/STBN_1D_128x128x16.png"));
				changed |= HExtensions.Load(ref Stbn2D128X128X16, RuntimeAssetPath("BlueNoise/STBN_2D_128x128x16.png"));
#endif
				return changed;
			}
		}

		[Serializable]
		internal struct HDRPResources
		{
			[SerializeField] internal VolumeProfile VolumeProfile;

			public bool IsValid => VolumeProfile != null;

			public bool Initialize()
			{
				bool changed = false;
#if UNITY_EDITOR
				changed |= HExtensions.Load(ref VolumeProfile, RuntimeAssetPath("HTraceAO Volume Profile HDRP.asset"));
#endif
				return changed;
			}
		}

		[Serializable]
		internal struct URPResources
		{
			[SerializeField] internal Shader MotionVectorsUrpShader;
			[SerializeField] internal Shader OutputCompositionUrpShader;

			public bool IsValid =>
				MotionVectorsUrpShader != null &&
				OutputCompositionUrpShader != null;

			public bool Initialize()
			{
				bool changed = false;
#if UNITY_EDITOR
				changed |= HExtensions.Load(ref MotionVectorsUrpShader, RuntimeAssetPath("Shaders/URP/MotionVectorsURP.shader"));
				changed |= HExtensions.Load(ref OutputCompositionUrpShader, RuntimeAssetPath("Shaders/URP/OutputCompositionURP.shader"));
#endif
				return changed;
			}
		}

		[Serializable]
		internal struct SRPResources
		{
			[SerializeField] internal Shader ObjectMotionDeltaSrpShader;

			public bool IsValid =>
				ObjectMotionDeltaSrpShader != null;

			public bool Initialize()
			{
				bool changed = false;
#if UNITY_EDITOR
				changed |= HExtensions.Load(ref ObjectMotionDeltaSrpShader, RuntimeAssetPath("Shaders/ObjectMotionDeltaSRP.shader"));
#endif
				return changed;
			}
		}

		[Serializable]
		internal struct BIRPResources
		{
			[SerializeField] internal Shader FinalComposeBirpShader;
			[SerializeField] internal Shader MotionVectorsBirpShader;
			[SerializeField] internal Shader ObjectMotionDeltaBirpShader;

			public bool IsValid =>
				FinalComposeBirpShader != null &&
				MotionVectorsBirpShader != null &&
				ObjectMotionDeltaBirpShader != null;

			public bool Initialize()
			{
				bool changed = false;
#if UNITY_EDITOR
				changed |= HExtensions.Load(ref FinalComposeBirpShader, RuntimeAssetPath("Shaders/BIRP/FinalComposeBIRP.shader"));
				changed |= HExtensions.Load(ref MotionVectorsBirpShader, RuntimeAssetPath("Shaders/BIRP/MotionVectorsBIRP.shader"));
				changed |= HExtensions.Load(ref ObjectMotionDeltaBirpShader, RuntimeAssetPath("Shaders/BIRP/ObjectMotionDeltaBIRP.shader"));
#endif
				return changed;
			}
		}
	}
}
