#if H_BIRP 
using HTraceAO.Rendering.BIRP; 
#endif 
using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEngine;
using UnityEngine.Rendering;

namespace IPGames.HTraceAO.Runtime.Passes
{
	internal static class SSAO
	{	
		// Kernels
		private enum HDepthPyramidSSAOKernel
		{
			DepthDownsample_1 = 0,
			DepthDownsample_2 = 1,
		}

		private enum HRenderSSAOKernel
		{
			RenderOcclusion = 0,
		}

		private enum HDenoiseSSAOKernel
		{
			DenoiseOcclusion_A = 0,
			DenoiseOcclusion_B = 1,
			OutputComposition = 2,
		}
		
		// Keywords
		private const string HIGH_SAMPLE_COUNT = "HIGH_SAMPLE_COUNT";
		
		// Computes
		internal static ComputeShader HRenderSSAO       = null;
		internal static ComputeShader HDenoiseSSAO      = null;
		internal static ComputeShader HDepthPyramidSSAO = null;
		internal static ComputeShader HDebugSSAO        = null;

		// Materials
		public static Material OutputCompositionSSAO = null;

		// Profiler Samplers
		internal static ProfilingSampler DepthPyramidGenerationSampler = new ProfilingSampler(Globals.HNames.DEPTH_PYRAMID_GENERATION_SAMPLER);
		internal static ProfilingSampler RenderOcclusionSampler        = new ProfilingSampler(Globals.HNames.RENDER_OCCLUSION_SAMPLER);
		internal static ProfilingSampler InterpolationSampler          = new ProfilingSampler(Globals.HNames.INTERPOLATION_SAMPLER);
		internal static ProfilingSampler OutputCompositionSampler      = new ProfilingSampler(Globals.HNames.OUTPUT_COMPOSITION_SAMPLER);
		internal static ProfilingSampler DebugSampler                  = new ProfilingSampler(Globals.HNames.DEBUG_SAMPLER);

		// Textures
		internal static Wrappers.RTWrapper DepthTiledSSAO               = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper DepthPyramidSSAO             = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper DepthIntermediatePyramidSSAO = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionSSAO_1              = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionSSAO_2              = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionSSAO_3              = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionSSAO_4              = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionCombinedSSAO_0      = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionCombinedSSAO_1      = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionCombinedSSAO_2      = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionCombinedSSAO_3      = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper DebugSSAO                    = new Wrappers.RTWrapper();
		internal static Wrappers.RTWrapper OcclusionShaderGraphSSAO    = new Wrappers.RTWrapper();
		
		
		internal static Wrappers.RTWrapper[] Occlusion_SSAO_Array    = new Wrappers.RTWrapper[4];
		internal static Wrappers.RTWrapper[] Occlusion_LowRes_Array  = new Wrappers.RTWrapper[4];
		internal static Wrappers.RTWrapper[] Occlusion_HighRes_Array = new Wrappers.RTWrapper[4];
		internal static Wrappers.RTWrapper[] Occlusion_Output_Array  = new Wrappers.RTWrapper[4];
		
		// Variables
		static readonly float [] SampleThickness = {
			Mathf.Sqrt(1 - 0.2f * 0.2f),
			Mathf.Sqrt(1 - 0.4f * 0.4f),
			Mathf.Sqrt(1 - 0.6f * 0.6f),
			Mathf.Sqrt(1 - 0.8f * 0.8f),
			Mathf.Sqrt(1 - 0.2f * 0.2f - 0.2f * 0.2f),
			Mathf.Sqrt(1 - 0.2f * 0.2f - 0.4f * 0.4f),
			Mathf.Sqrt(1 - 0.2f * 0.2f - 0.6f * 0.6f),
			Mathf.Sqrt(1 - 0.2f * 0.2f - 0.8f * 0.8f),
			Mathf.Sqrt(1 - 0.4f * 0.4f - 0.4f * 0.4f),
			Mathf.Sqrt(1 - 0.4f * 0.4f - 0.6f * 0.6f),
			Mathf.Sqrt(1 - 0.4f * 0.4f - 0.8f * 0.8f),
			Mathf.Sqrt(1 - 0.6f * 0.6f - 0.6f * 0.6f)
		};
		
		static readonly float [] InvThicknessTable = new float [12];
		static readonly float [] SampleWeightTable = new float [12];
		
		// Methods
		private static void UpdateTables(Vector2 depthRes, float tanHalfFovH, float screenspaceDiameter)
		{
			var thicknessMultiplier = 2.0f * tanHalfFovH * screenspaceDiameter / depthRes.x;
			var inverseRangeFactor  = 1.0f / thicknessMultiplier;
			
			for (var i = 0; i < 12; i++)
				InvThicknessTable[i] = inverseRangeFactor / SampleThickness[i];

			SampleWeightTable[0]  = 4 * SampleThickness[0];  // Axial
			SampleWeightTable[1]  = 4 * SampleThickness[1];  // Axial
			SampleWeightTable[2]  = 4 * SampleThickness[2];  // Axial
			SampleWeightTable[3]  = 4 * SampleThickness[3];  // Axial
			SampleWeightTable[4]  = 4 * SampleThickness[4];  // Diagonal
			SampleWeightTable[5]  = 8 * SampleThickness[5];  // L-shaped
			SampleWeightTable[6]  = 8 * SampleThickness[6];  // L-shaped
			SampleWeightTable[7]  = 8 * SampleThickness[7];  // L-shaped
			SampleWeightTable[8]  = 4 * SampleThickness[8];  // Diagonal
			SampleWeightTable[9]  = 8 * SampleThickness[9];  // L-shaped
			SampleWeightTable[10] = 8 * SampleThickness[10]; // L-shaped
			SampleWeightTable[11] = 4 * SampleThickness[11]; // Diagonal

			// if (HTraceAOSettings.ActiveProfile.SSAOSettings.HighSampleCount == false)
			// {
			// 	// Zero out the unused samples.
			// 	SampleWeightTable[0] = 0;
			// 	SampleWeightTable[2] = 0;
			// 	SampleWeightTable[5] = 0;
			// 	SampleWeightTable[7] = 0;
			// 	SampleWeightTable[9] = 0;	
			// }
	

			var WeightTotal = 0.0f;

			foreach (var w in SampleWeightTable)
				WeightTotal += w;

			for (var i = 0; i < SampleWeightTable.Length; i++)
				SampleWeightTable[i] /= WeightTotal;
		}
		
		private static void KeywordSwitch(ComputeShader compute, bool state, string keyword)
		{
			if (state) compute.EnableKeyword(keyword);
			else compute.DisableKeyword(keyword);
		}

		
		public static void Execute(CommandBuffer cmd, Camera camera, int cameraWidth, int cameraHeight, RenderTexture cameraColorBuffer = null)
		{
			const float screenSpaceDiameter = 10;
			float tanHalfFovH = 1 / camera.projectionMatrix[0, 0];
			
			var upsampleTolerance = Mathf.Pow(10, -12.0f);
			var noiseFilterWeight = 1 / (Mathf.Pow(10, 0) + upsampleTolerance);

			bool requiresOutputComposition = false;
			bool requiresAfterOpaqueWriting = false;
			bool requiresShaderGraphOutput = false;

			requiresAfterOpaqueWriting = HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint == URPInjectionPoint.AfterOpaque;
			requiresShaderGraphOutput  = HTraceAOSettings.ActiveProfile.GeneralSettings.InjectionPoint == URPInjectionPoint.BeforeOpaque;
			requiresOutputComposition  = HTraceAOSettings.ActiveProfile.SSAOSettings.DebugModeSSAO == DebugModeSSAO.None;
			
			
			var dispatchResolutionFull8_X = Mathf.CeilToInt(cameraWidth / 8.0f);
			var dispatchResolutionFull8_Y = Mathf.CeilToInt(cameraHeight / 8.0f);
			
			
			// ---------------------------------------- PARAMETERS SET ---------------------------------------- //
			
			// Global parameters
			cmd.SetGlobalFloat(Globals.HShaderParams.g_HIntensityAO, HTraceAOSettings.ActiveProfile.GeneralSettings.Intensity);
			
			// SSAO Render parameters
			cmd.SetComputeFloatParam(HRenderSSAO, Globals.HShaderParams.RejectFadeoff, -1.0f / (Mathf.Clamp(HTraceAOSettings.ActiveProfile.SSAOSettings.Thickness, 0.1f, 1.0f) * 10));
			cmd.SetComputeIntParam(HRenderSSAO, Globals.HShaderParams.SliceXR, Globals.HRenderer.TextureXrSlices > 1 ? 1 : 0);
			
			// SSAO Denoise parameters
			cmd.SetComputeFloatParam(HDenoiseSSAO, Globals.HShaderParams.NoiseFilterStrength, noiseFilterWeight);
			cmd.SetComputeFloatParam(HDenoiseSSAO, Globals.HShaderParams.UpsampleTolerance, upsampleTolerance);
			cmd.SetComputeIntParam(HDenoiseSSAO, Globals.HShaderParams.ApplyIntensity, requiresOutputComposition ? 1 : 0);
			cmd.SetComputeFloatParam(HDenoiseSSAO, Globals.HShaderParams.Fadeout, HTraceAOSettings.ActiveProfile.GeneralSettings.Fadeout);
			
			// Render Layer Mask parameters
#if UNITY_2023_3_OR_NEWER			
			cmd.SetComputeIntParam(HDepthPyramidSSAO, Globals.HShaderParams.ExcludeObjectsFromDepth, HTraceAOSettings.ActiveProfile.SSAOSettings.ExcludeCasting);
			cmd.SetComputeIntParam(HDebugSSAO, Globals.HShaderParams.ExcludeObjectsFromDepth, HTraceAOSettings.ActiveProfile.SSAOSettings.ExcludeCasting);
			cmd.SetComputeIntParam(HDenoiseSSAO, Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.SSAOSettings.ExcludeReceiving);
			cmd.SetComputeFloatParam(HDenoiseSSAO, Globals.HShaderParams.ExcludedIntensity, HTraceAOSettings.ActiveProfile.SSAOSettings.ExcludedIntensity);
#endif	
			
			
			// ---------------------------------------- DEPTH PYRAMID GENERATION ---------------------------------------- //
			using (new ProfilingScope(cmd, DepthPyramidGenerationSampler))
			{	
				Vector2Int depthPyramidResolution     = Globals.HMath.CalculateDepthPyramidResolution(new Vector2Int((int)cameraWidth, (int)cameraHeight), 5);
				var        dispatchResolutionDepth8_X = Mathf.CeilToInt(depthPyramidResolution.x / 2.0f / 8.0f);
				var        dispatchResolutionDepth8_Y = Mathf.CeilToInt(depthPyramidResolution.y / 2.0f / 8.0f);

				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, Globals.HShaderParams.DepthTiled_OutputMIP0, DepthTiledSSAO.rt, 0);
				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, Globals.HShaderParams.DepthTiled_OutputMIP1, DepthTiledSSAO.rt, 1);
				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, Globals.HShaderParams.DepthPyramid_OutputMIP0, DepthPyramidSSAO.rt, 0);
				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, Globals.HShaderParams.DepthPyramid_OutputMIP1, DepthPyramidSSAO.rt, 1);
				cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, Globals.HShaderParams.DepthIntermediate_Output, DepthIntermediatePyramidSSAO.rt);
				cmd.DispatchCompute(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_1, dispatchResolutionDepth8_X, dispatchResolutionDepth8_Y, Globals.HRenderer.TextureXrSlices);

				if (HTraceAOSettings.ActiveProfile.SSAOSettings.Radius > 2)
				{	
					dispatchResolutionDepth8_X = Mathf.CeilToInt(depthPyramidResolution.x / 8.0f / 8.0f);
					dispatchResolutionDepth8_Y = Mathf.CeilToInt(depthPyramidResolution.y / 8.0f / 8.0f);

					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, Globals.HShaderParams.DepthTiled_OutputMIP2, DepthTiledSSAO.rt, 2);
					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, Globals.HShaderParams.DepthTiled_OutputMIP3, DepthTiledSSAO.rt, 3);
					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, Globals.HShaderParams.DepthPyramid_OutputMIP2, DepthPyramidSSAO.rt, 2);
					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, Globals.HShaderParams.DepthPyramid_OutputMIP3, DepthPyramidSSAO.rt, 3);
					cmd.SetComputeTextureParam(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, Globals.HShaderParams.DepthIntermediate, DepthIntermediatePyramidSSAO.rt);
					cmd.DispatchCompute(HDepthPyramidSSAO, (int)HDepthPyramidSSAOKernel.DepthDownsample_2, dispatchResolutionDepth8_X, dispatchResolutionDepth8_Y, Globals.HRenderer.TextureXrSlices);
				}
			}
			
			
			// ---------------------------------------- OCCLUSION RENDER ---------------------------------------- //
			using (new ProfilingScope(cmd, RenderOcclusionSampler))
			{	
				// KeywordSwitch(HRenderSSAO, HTraceAOSettings.ActiveProfile.SSAOSettings.HighSampleCount, HIGH_SAMPLE_COUNT);
				
				cmd.SetComputeTextureParam(HRenderSSAO, (int)HRenderSSAOKernel.RenderOcclusion, Globals.HShaderParams.DepthTiled, DepthTiledSSAO.rt);
				
				Occlusion_SSAO_Array[0] = OcclusionSSAO_1;
				Occlusion_SSAO_Array[1] = OcclusionSSAO_2;
				Occlusion_SSAO_Array[2] = OcclusionSSAO_3;
				Occlusion_SSAO_Array[3] = OcclusionSSAO_4;

				for (int passIndex = 0; passIndex < HTraceAOSettings.ActiveProfile.SSAOSettings.Radius; passIndex++)
				{
					float scaleFactor = 8.0f * Mathf.Pow(2, passIndex);
					Vector2 depthRes = new Vector2(cameraWidth / scaleFactor, cameraHeight / scaleFactor);
					UpdateTables(depthRes, tanHalfFovH, screenSpaceDiameter);
					
					var dispatchResolutionOcclusion8_X = Mathf.CeilToInt(cameraWidth / scaleFactor / 8.0f);
					var dispatchResolutionOcclusion8_Y = Mathf.CeilToInt(cameraHeight / scaleFactor / 8.0f);
				
					cmd.SetComputeIntParam(HRenderSSAO, Globals.HShaderParams.PassNumber, passIndex);
					cmd.SetComputeFloatParams(HRenderSSAO, Globals.HShaderParams.InvThicknessTable, InvThicknessTable);
					cmd.SetComputeFloatParams(HRenderSSAO, Globals.HShaderParams.SampleWeightTable, SampleWeightTable);
					cmd.SetComputeVectorParam(HRenderSSAO, Globals.HShaderParams.DepthScale, new Vector2(depthRes.x, depthRes.y));
					cmd.SetComputeTextureParam(HRenderSSAO, (int)HRenderSSAOKernel.RenderOcclusion, Globals.HShaderParams.Occlusion_Output, Occlusion_SSAO_Array[passIndex].rt);
					cmd.DispatchCompute(HRenderSSAO, (int)HRenderSSAOKernel.RenderOcclusion, dispatchResolutionOcclusion8_X, dispatchResolutionOcclusion8_Y, Globals.HRenderer.TextureXrSlices * 16);
				}
			}
			
				
			// ---------------------------------------- INTERPOLATION ---------------------------------------- //
			using (new ProfilingScope(cmd, InterpolationSampler))
			{
				int kernel = (int)HDenoiseSSAOKernel.DenoiseOcclusion_A;
			
				Occlusion_LowRes_Array[0] = OcclusionSSAO_1;
				Occlusion_LowRes_Array[1] = OcclusionSSAO_2;
				Occlusion_LowRes_Array[2] = OcclusionSSAO_3;
				Occlusion_LowRes_Array[3] = OcclusionSSAO_4;
				if (HTraceAOSettings.ActiveProfile.SSAOSettings.Radius == 2)
				{
					Occlusion_LowRes_Array[0] = OcclusionCombinedSSAO_1;
				}

				if (HTraceAOSettings.ActiveProfile.SSAOSettings.Radius == 3)
				{
					Occlusion_LowRes_Array[0] = OcclusionCombinedSSAO_1;
					Occlusion_LowRes_Array[1] = OcclusionCombinedSSAO_2;
				}

				if (HTraceAOSettings.ActiveProfile.SSAOSettings.Radius == 4)
				{
					Occlusion_LowRes_Array[0] = OcclusionCombinedSSAO_1;
					Occlusion_LowRes_Array[1] = OcclusionCombinedSSAO_2;
					Occlusion_LowRes_Array[2] = OcclusionCombinedSSAO_3;
				}
				
				Occlusion_HighRes_Array[0] = OcclusionSSAO_1 /* null */;
				Occlusion_HighRes_Array[1] = OcclusionSSAO_1;
				Occlusion_HighRes_Array[2] = OcclusionSSAO_2;
				Occlusion_HighRes_Array[3] = OcclusionSSAO_3;
				Occlusion_Output_Array[0]  = OcclusionCombinedSSAO_0;
				Occlusion_Output_Array[1]  = OcclusionCombinedSSAO_1;
				Occlusion_Output_Array[2]  = OcclusionCombinedSSAO_2;
				Occlusion_Output_Array[3]  = OcclusionCombinedSSAO_3;
				
				for (int passIndex = (HTraceAOSettings.ActiveProfile.SSAOSettings.Radius - 1); passIndex >= 0; passIndex--)
				{
					if (passIndex == 0)
					{	
						kernel = (int)HDenoiseSSAOKernel.DenoiseOcclusion_B;
					}
					
					var stepSize = Mathf.Pow(2, passIndex + 1);
					var blurTolerance = 1 - Mathf.Pow(10, -4.6f) * stepSize;
					blurTolerance *= blurTolerance;
					
					var dispatchResolutionInterpolation8_X = ((int)cameraWidth * 2 / (int)stepSize + 17) / 16;
					var dispatchResolutionInterpolation8_Y = ((int)cameraHeight * 2 / (int)stepSize + 17) / 16;
					
					cmd.SetComputeIntParam(HDenoiseSSAO, Globals.HShaderParams.PassNumber, passIndex);
					cmd.SetComputeFloatParam(HDenoiseSSAO, Globals.HShaderParams.StepSize, stepSize);
					cmd.SetComputeFloatParam(HDenoiseSSAO, Globals.HShaderParams.BlurTolerance, blurTolerance);
					cmd.SetComputeTextureParam(HDenoiseSSAO, kernel, Globals.HShaderParams.DepthPyramid, DepthPyramidSSAO.rt);
					cmd.SetComputeTextureParam(HDenoiseSSAO, kernel, Globals.HShaderParams.OcclusionLowRes, Occlusion_LowRes_Array[passIndex].rt);		// inverted textures order
					cmd.SetComputeTextureParam(HDenoiseSSAO, kernel, Globals.HShaderParams.OcclusionHighRes, Occlusion_HighRes_Array[passIndex].rt);	// inverted textures order
					cmd.SetComputeTextureParam(HDenoiseSSAO, kernel, Globals.HShaderParams.Occlusion_Output, Occlusion_Output_Array[passIndex].rt);		// inverted textures order
					cmd.DispatchCompute(HDenoiseSSAO, kernel, dispatchResolutionInterpolation8_X, dispatchResolutionInterpolation8_Y, Globals.HRenderer.TextureXrSlices);
				}
			}
			
			
			// ---------------------------------------- OUTPUT COMPOSITION ---------------------------------------- //
			using (new ProfilingScope(cmd, OutputCompositionSampler))
			{
				if (requiresAfterOpaqueWriting)
				{
					CoreUtils.SetRenderTarget(cmd, cameraColorBuffer);
					OutputCompositionSSAO.SetTexture(Globals.HShaderParams.Occlusion_Output, OcclusionCombinedSSAO_0.rt);
					OutputCompositionSSAO.SetFloat(Globals.HShaderParams.Fadeout, HTraceAOSettings.ActiveProfile.GeneralSettings.Fadeout);
					OutputCompositionSSAO.SetFloat(Globals.HShaderParams.ExcludedIntensity, HTraceAOSettings.ActiveProfile.SSAOSettings.ExcludedIntensity);
				#if UNITY_2023_3_OR_NEWER
					OutputCompositionSSAO.SetInt(Globals.HShaderParams.ExcludeReceivingMask, HTraceAOSettings.ActiveProfile.SSAOSettings.ExcludeReceiving);
				#endif
					cmd.DrawProcedural(Matrix4x4.identity, OutputCompositionSSAO, 0, MeshTopology.Triangles, 3, 1);
				}
				else
				{	
					cmd.SetComputeTextureParam(HDenoiseSSAO, (int)HDenoiseSSAOKernel.OutputComposition, Globals.HShaderParams.Occlusion_Output, OcclusionCombinedSSAO_0.rt);
					cmd.SetComputeTextureParam(HDenoiseSSAO, (int)HDenoiseSSAOKernel.OutputComposition, Globals.HShaderParams.Occlusion_ShaderGraphOutput, OcclusionShaderGraphSSAO.rt);
					cmd.DispatchCompute(HDenoiseSSAO, (int)HDenoiseSSAOKernel.OutputComposition, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);
				}
			}
			
			
			// -------------------------------------------- DEBUG ------------------------------------------ //
			if (HTraceAOSettings.ActiveProfile.SSAOSettings.DebugModeSSAO != Globals.DebugModeSSAO.None)
			{
				using (new ProfilingScope(cmd, DebugSampler))
				{	
					cmd.SetComputeIntParam(HDebugSSAO, Globals.HShaderParams.DebugSwitch, (int)HTraceAOSettings.ActiveProfile.SSAOSettings.DebugModeSSAO);
					cmd.SetComputeTextureParam(HDebugSSAO, 1, Globals.HShaderParams.Debug_Output, DebugSSAO.rt);
					cmd.SetComputeTextureParam(HDebugSSAO, 1, Globals.HShaderParams.g_HTraceBufferAO, OcclusionCombinedSSAO_0.rt);
					cmd.DispatchCompute(HDebugSSAO, 1, dispatchResolutionFull8_X, dispatchResolutionFull8_Y, Globals.HRenderer.TextureXrSlices);
					
					cmd.SetGlobalTexture(Globals.HShaderParams.Debug_Output, DebugSSAO.rt);
				}
			}
			
			cmd.SetGlobalTexture(Globals.HShaderParams.g_HTraceBufferAO, requiresShaderGraphOutput ? OcclusionShaderGraphSSAO.rt : OcclusionCombinedSSAO_0.rt);
			cmd.SetGlobalTexture(Globals.HShaderParams.g_ScreenSpaceOcclusionTexture, OcclusionCombinedSSAO_0.rt);
		}
	}
}
