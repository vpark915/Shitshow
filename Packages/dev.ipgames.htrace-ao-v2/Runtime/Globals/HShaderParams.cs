using UnityEngine;

namespace IPGames.HTraceAO.Runtime.Globals
{
    public static class HShaderParams
    {
        // Shared Keywords
        public static readonly string _SCREEN_SPACE_OCCLUSION = "_SCREEN_SPACE_OCCLUSION";

        // Global Textures
        public static readonly int g_HTraceGBuffer0          = Shader.PropertyToID("g_HTraceGBuffer0");
        public static readonly int g_HTraceGBuffer1          = Shader.PropertyToID("g_HTraceGBuffer1");
        public static readonly int g_HTraceGBuffer2          = Shader.PropertyToID("g_HTraceGBuffer2");
        public static readonly int g_HTraceGBuffer3          = Shader.PropertyToID("g_HTraceGBuffer3");
        public static readonly int g_HTraceRenderLayerMask   = Shader.PropertyToID("g_HTraceRenderLayerMask");
        public static readonly int g_HTraceColor             = Shader.PropertyToID("g_HTraceColor");
        public static readonly int g_HTraceDepth             = Shader.PropertyToID("g_HTraceDepth");
        public static readonly int g_HTraceMotionMaskStencil = Shader.PropertyToID("g_HTraceMotionMaskStencil");
        public static readonly int g_HTraceMotionDelta       = Shader.PropertyToID("g_HTraceMotionDelta");
        public static readonly int g_HTraceMotionVectors     = Shader.PropertyToID("g_HTraceMotionVectors");
        public static readonly int g_HTraceMotionMask        = Shader.PropertyToID("g_HTraceMotionMask");

        // URP Textures
        public static readonly int g_CameraDepthTexture   = Shader.PropertyToID("_CameraDepthTexture");
        public static readonly int g_CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");

        // Output Textures
        public static readonly int g_HTraceBufferAO              = Shader.PropertyToID("_HTraceBufferAO");
        public static readonly int g_ScreenSpaceOcclusionTexture = Shader.PropertyToID("_ScreenSpaceOcclusionTexture");

        // Global Scalars — Dithering (g_ prefix)
        public static readonly int g_HUseDithering               = Shader.PropertyToID("g_HUseDithering");
        public static readonly int g_HAnimateDithering           = Shader.PropertyToID("g_HAnimateDithering");
        public static readonly int HScaleFactorAO                = Shader.PropertyToID("g_HScaleFactorAO");
        public static readonly int g_HDepthToViewParamsAO        = Shader.PropertyToID("g_HDepthToViewParamsAO");
        public static readonly int g_HTemporalDynamicRejectionAO = Shader.PropertyToID("g_HTemporalDynamicRejectionAO");
        public static readonly int g_HTemporalStaticRejectionAO  = Shader.PropertyToID("g_HTemporalStaticRejectionAO");
        public static readonly int g_HIntensityAO                = Shader.PropertyToID("g_HIntensityAO");
        public static readonly int g_HPixelSpreadMultiplier      = Shader.PropertyToID("g_HPixelSpreadMultiplier");
        public static readonly int g_HTemporalSamplecountAO      = Shader.PropertyToID("g_HTemporalSamplecountAO");

        // Matrices
        public static readonly int H_MATRIX_VP        = Shader.PropertyToID("_H_MATRIX_VP");
        public static readonly int H_MATRIX_I_VP      = Shader.PropertyToID("_H_MATRIX_I_VP");
        public static readonly int H_MATRIX_I_P       = Shader.PropertyToID("_H_MATRIX_I_P");
        public static readonly int H_MATRIX_PREV_V    = Shader.PropertyToID("_H_MATRIX_PREV_V");
        public static readonly int H_MATRIX_PREV_VP   = Shader.PropertyToID("_H_MATRIX_PREV_VP");
        public static readonly int H_MATRIX_PREV_I_VP = Shader.PropertyToID("_H_MATRIX_PREV_I_VP");

        // Frame & Camera
        public static          int HRenderScale         = Shader.PropertyToID("_HRenderScale");
        public static          int HRenderScalePrevious = Shader.PropertyToID("_HRenderScalePrevious");
        public static          int FrameCount           = Shader.PropertyToID("_FrameCount");
        public static          int ScreenSize           = Shader.PropertyToID("_ScreenSize");
        public static          int JitteringIsEnabled   = Shader.PropertyToID("_JitteringIsEnabled");
        public static readonly int SliceXR              = Shader.PropertyToID("_SliceXR");
        public static readonly int IndexXR              = Shader.PropertyToID("_IndexXR");

        // Depth Pyramid
        public static readonly int DepthPyramid_OutputMIP0  = Shader.PropertyToID("_DepthPyramid_OutputMIP0");
        public static readonly int DepthPyramid_OutputMIP1  = Shader.PropertyToID("_DepthPyramid_OutputMIP1");
        public static readonly int DepthPyramid_OutputMIP2  = Shader.PropertyToID("_DepthPyramid_OutputMIP2");
        public static readonly int DepthPyramid_OutputMIP3  = Shader.PropertyToID("_DepthPyramid_OutputMIP3");
        public static readonly int DepthIntermediate        = Shader.PropertyToID("_DepthIntermediate");
        public static readonly int DepthIntermediate_Output = Shader.PropertyToID("_DepthIntermediate_Output");

        // Shared AO Parameters
        public static readonly int Intensity                 = Shader.PropertyToID("_Intensity");
        public static readonly int FilterRadius              = Shader.PropertyToID("_FilterRadius");
        public static readonly int ForceNonOctahedralNormals = Shader.PropertyToID("_ForceNonOctahedralNormals");
        public static readonly int ApplyIntensity            = Shader.PropertyToID("_ApplyIntensity");
        public static readonly int IsGTAO                    = Shader.PropertyToID("_IsGTAO");
        public static readonly int IsFinalPass               = Shader.PropertyToID("_IsFinalPass");
        public static readonly int Fadeout                   = Shader.PropertyToID("_Fadeout");
        public static readonly int ExcludeReceivingMask      = Shader.PropertyToID("_ExcludeReceivingMask");
        public static readonly int EarlyOutExcludedReceivers = Shader.PropertyToID("_EarlyOutExcludedReceivers");
        public static readonly int ExcludeObjectsFromDepth   = Shader.PropertyToID("_ExcludeObjectsFromDepth");
        public static readonly int ExcludedIntensity         = Shader.PropertyToID("_ExcludedIntensity");
        public static readonly int ZBufferParamsPrev         = Shader.PropertyToID("_ZBufferParamsPrev");
        public static readonly int CameraPositionsPrevious   = Shader.PropertyToID("_CameraPositionsPrevious");

        // Shared AO Textures
        public static readonly int Occlusion                       = Shader.PropertyToID("_Occlusion");
        public static readonly int Occlusion_Output                = Shader.PropertyToID("_Occlusion_Output");
        public static readonly int Occlusion_ShaderGraphOutput     = Shader.PropertyToID("_Occlusion_ShaderGraphOutput");
        public static readonly int OcclusionReprojected            = Shader.PropertyToID("_OcclusionReprojected");
        public static readonly int OcclusionNormal                 = Shader.PropertyToID("_OcclusionNormal");
        public static readonly int OcclusionNormal_Output          = Shader.PropertyToID("_OcclusionNormal_Output");
        public static readonly int NormalHistory_Output            = Shader.PropertyToID("_NormalHistory_Output");
        public static readonly int GeometryNormals_Output          = Shader.PropertyToID("_GeometryNormals_Output");
        public static readonly int Depth                           = Shader.PropertyToID("_Depth");
        public static readonly int Depth_Output                    = Shader.PropertyToID("_Depth_Output");
        public static readonly int DepthHistory                    = Shader.PropertyToID("_DepthHistory");
        public static readonly int SamplecountNormalHistory        = Shader.PropertyToID("_SamplecountNormalHistory");
        public static readonly int SamplecountNormalHistory_Output = Shader.PropertyToID("_SamplecountNormalHistory_Output");
        public static readonly int TemporalDataReprojected         = Shader.PropertyToID("_TemporalDataReprojected");

        // SSAO Parameters
        public static readonly int RejectFadeoff         = Shader.PropertyToID("_RejectFadeoff");
        public static readonly int DepthScale            = Shader.PropertyToID("_DepthScale");
        public static readonly int InvThicknessTable     = Shader.PropertyToID("_InvThicknessTable");
        public static readonly int SampleWeightTable     = Shader.PropertyToID("_SampleWeightTable");
        public static readonly int PassNumber            = Shader.PropertyToID("_PassNumber");
        public static readonly int NoiseFilterStrength   = Shader.PropertyToID("_NoiseFilterStrength");
        public static readonly int UpsampleTolerance     = Shader.PropertyToID("_UpsampleTolerance");
        public static readonly int StepSize              = Shader.PropertyToID("_StepSize");
        public static readonly int BlurTolerance         = Shader.PropertyToID("_BlurTolerance");
        public static readonly int DepthTiled_OutputMIP0 = Shader.PropertyToID("_DepthTiled_OutputMIP0");
        public static readonly int DepthTiled_OutputMIP1 = Shader.PropertyToID("_DepthTiled_OutputMIP1");
        public static readonly int DepthTiled_OutputMIP2 = Shader.PropertyToID("_DepthTiled_OutputMIP2");
        public static readonly int DepthTiled_OutputMIP3 = Shader.PropertyToID("_DepthTiled_OutputMIP3");
        public static readonly int DepthTiled            = Shader.PropertyToID("_DepthTiled");
        public static readonly int DepthPyramid          = Shader.PropertyToID("_DepthPyramid");
        public static readonly int OcclusionLowRes       = Shader.PropertyToID("_OcclusionLowRes");
        public static readonly int OcclusionHighRes      = Shader.PropertyToID("_OcclusionHighRes");

        // GTAO Parameters
        public static readonly int HPreviousScaleFactorAO       = Shader.PropertyToID("g_HPreviousScaleFactorAO");
        public static readonly int Radius                       = Shader.PropertyToID("_Radius");
        public static readonly int ScreenSpaceRadiusScale       = Shader.PropertyToID("_ScreenSpaceRadiusScale");
        public static readonly int WorldSpaceRadius             = Shader.PropertyToID("_WorldSpaceRadius");
        public static readonly int ScreenSpaceRadius            = Shader.PropertyToID("_ScreenSpaceRadius");
        public static readonly int Thickness                    = Shader.PropertyToID("_Thickness");
        public static readonly int StepCount                    = Shader.PropertyToID("_StepCount");
        public static readonly int SliceCount                   = Shader.PropertyToID("_SliceCount");
        public static readonly int FilterAdaptivity             = Shader.PropertyToID("_FilterAdaptivity");
        public static readonly int FilterStep                   = Shader.PropertyToID("_FilterStep");
        public static readonly int TemporalRejection            = Shader.PropertyToID("_TemporalRejection");
        public static readonly int UseAccurateTemporalRejection = Shader.PropertyToID("_UseAccurateTemporalRejection");
        public static readonly int SimpleTemporalRejection      = Shader.PropertyToID("_SimpleTemporalRejection");
        public static readonly int UseAttenuation               = Shader.PropertyToID("_UseAttenuation");
        public static readonly int UseLinearThickness           = Shader.PropertyToID("_UseLinearThickness");
        public static readonly int UseTemporalNoise             = Shader.PropertyToID("_UseTemporalNoise");

        // GTAO Textures
        public static readonly int OcclusionVelocityHistory        = Shader.PropertyToID("_OcclusionVelocityHistory");
        public static readonly int RayMarchingDepthPyramid         = Shader.PropertyToID("_RayMarchingDepthPyramid");
        public static readonly int OcclusionVelocityHistory_Output = Shader.PropertyToID("_OcclusionVelocityHistory_Output");

        // RTAO Parameters
        public static readonly int MaxRayDistance             = Shader.PropertyToID("_MaxRayDistance");
        public static readonly int MaxRayBias                 = Shader.PropertyToID("_MaxRayBias");
        public static readonly int RaySampleCount             = Shader.PropertyToID("_RaySampleCount");
        public static readonly int VelocityEvaluationXR       = Shader.PropertyToID("_VelocityEvaluationXR");
        public static readonly int ContactOcclusionIntensity  = Shader.PropertyToID("_ContactOcclusionIntensity");
        public static readonly int IsSinglePass               = Shader.PropertyToID("_IsSinglePass");
        public static readonly int RayMarchingLayerMask       = Shader.PropertyToID("_RayMarchingLayerMask");
        public static readonly int UseDisocclusionBoost       = Shader.PropertyToID("_UseDisocclusionBoost");
        public static readonly int UseAdaptiveResolution      = Shader.PropertyToID("_UseAdaptiveResolution");
        public static readonly int RayMarchingThicknessParams = Shader.PropertyToID("_RayMarchingThicknessParams");
        public static readonly int RayMarchingDistance        = Shader.PropertyToID("_RayMarchingDistance");
        public static readonly int RayMarchingSamplecount     = Shader.PropertyToID("_RayMarchingSamplecount");
        public static readonly int JitterCoordForHalfRes      = Shader.PropertyToID("_JitterCoordForHalfRes");
        public static readonly int IsBeforeInterpolationPass  = Shader.PropertyToID("_IsBeforeInterpolationPass");
        public static readonly int RayTracedCounter           = Shader.PropertyToID("_RayTracedCounter");
        public static readonly int DebugSceneMismatchRTAS     = Shader.PropertyToID("_DebugSceneMismatchRTAS");

        // RTAO Indirection Parameters
        public static readonly int RayCounter               = Shader.PropertyToID("_RayCounter");
        public static readonly int RayCounter_Output        = Shader.PropertyToID("_RayCounter_Output");
        public static readonly int IndirectCoords_Output    = Shader.PropertyToID("_IndirectCoords_Output");
        public static readonly int IndirectArguments_Output = Shader.PropertyToID("_IndirectArguments_Output");
        public static readonly int TracingCoords            = Shader.PropertyToID("_TracingCoords");

        // RTAO Textures
        public static readonly int TemporalData_Output      = Shader.PropertyToID("_TemporalData_Output");
        public static readonly int ShadingRate              = Shader.PropertyToID("_ShadingRate");
        public static readonly int ShadingRate_Output       = Shader.PropertyToID("_ShadingRate_Output");
        public static readonly int RayMarchingDepth         = Shader.PropertyToID("_RayMarchingDepth");
        public static readonly int RayMarchingDepth_Output  = Shader.PropertyToID("_RayMarchingDepth_Output");
        public static readonly int OcclusionHalfRate        = Shader.PropertyToID("_OcclusionHalfRate");
        public static readonly int OcclusionHalfRate_Output = Shader.PropertyToID("_OcclusionHalfRate_Output");
        public static readonly int Occlusion_History        = Shader.PropertyToID("_Occlusion_History");
        public static readonly int OcclusionHistory_Output  = Shader.PropertyToID("_OcclusionHistory_Output");
        public static readonly int VelocityHistory          = Shader.PropertyToID("_VelocityHistory");
        public static readonly int VelocityHistory_Output   = Shader.PropertyToID("_VelocityHistory_Output");
        public static readonly int VelocityReprojected      = Shader.PropertyToID("_VelocityReprojected");

        // RTAO RTAS Parameters
        public static readonly int RTAS = Shader.PropertyToID("_RTAS");

        // RTAO Buffers
        public static readonly int DisocclusionCounter      = Shader.PropertyToID("_DisocclusionCounter");
        public static readonly int PointDistribution        = Shader.PropertyToID("_PointDistribution");
        public static readonly int PointDistribution_Output = Shader.PropertyToID("_PointDistribution_Output");

        // Debug
        public static readonly int DebugDepth            = Shader.PropertyToID("_DebugDepth");
        public static readonly int AmbientOcclusionParam = Shader.PropertyToID("_AmbientOcclusionParam");
        public static readonly int DebugSwitch           = Shader.PropertyToID("_DebugSwitch");
        public static readonly int BuffersSwitch         = Shader.PropertyToID("_BuffersSwitch");
        public static readonly int Debug_Output          = Shader.PropertyToID("_Debug_Output");
    }
}
