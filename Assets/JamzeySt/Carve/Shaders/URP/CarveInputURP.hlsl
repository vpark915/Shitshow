#ifndef CARVE_INPUT_URP_INCLUDED
#define CARVE_INPUT_URP_INCLUDED

// Carve's material properties in URP, named as in HDRP so the same tools, samples and presets drive both pipelines.
// Every per-material value sits in UnityPerMaterial (SRP Batcher). Textures share samplers, leaving room for URP's own
// (shadows, lightmaps, reflections, SSAO, decals); a shared sampler is named after a texture every pass using it reads.
CBUFFER_START(UnityPerMaterial)
#ifdef CARVE_LAYERED
    float4 _LayerMaskMap_ST, _LayerMaskMap_TexelSize;
    float4 _BaseColorMap0_ST, _BaseColorMap1_ST, _BaseColorMap2_ST, _BaseColorMap3_ST;
    float4 _BaseColor0, _BaseColor1, _BaseColor2, _BaseColor3;
    float4 _HeightMap0_TexelSize, _HeightMap1_TexelSize, _HeightMap2_TexelSize, _HeightMap3_TexelSize;
    float4 _CarvePyramid0_TexelSize, _CarvePyramid1_TexelSize, _CarvePyramid2_TexelSize, _CarvePyramid3_TexelSize;
    float _NormalScale0, _NormalScale1, _NormalScale2, _NormalScale3;
    float _Metallic0, _Metallic1, _Metallic2, _Metallic3;
    float _Smoothness0, _Smoothness1, _Smoothness2, _Smoothness3;
    float _CarveLayerHeight0, _CarveLayerHeight1, _CarveLayerHeight2, _CarveLayerHeight3;
    float _CarveLayerOffset0, _CarveLayerOffset1, _CarveLayerOffset2, _CarveLayerOffset3;
    float _LayerCount, _CarveLayerMaskSource, _CarveLayerMaskUV, _CarveHeightBlend, _CarveLayerBlendSoftness;
#else
    float4 _BaseColorMap_ST, _BaseColor, _HeightMap_TexelSize, _CarvePyramid_TexelSize;
    float _NormalScale, _Metallic, _Smoothness;
    float _CarveDetail, _CarveDetailNormalScale, _CarveDetailTiling, _CarveDetailDepth;
#endif
    float _CarveMapping, _CarveBlendSharpness;
    float _CarveDepth, _CarveMinSteps, _CarveMaxSteps, _CarveRefineSteps, _CarveStability, _CarveTracing, _CarveAutoLOD;
    float _CarveAntiTiling, _CarveAntiTilingCell, _CarveFadeStart, _CarveFadeEnd;
    float _CarveSilhouette, _CarveCurvature, _CarveCurvatureScale;
    float _CarveSelfShadow, _CarveShadowSoftness, _CarveShadowSteps, _CarveCasterSteps;
    float _CarveOcclusion, _CarveOcclusionRadius, _CarveSpecOcclusion, _CarveCliff, _CarveCliffSlope;
    float _CarveDebug;
CBUFFER_END

#ifdef CARVE_LAYERED
    TEXTURE2D(_LayerMaskMap);
    TEXTURE2D(_BaseColorMap0); TEXTURE2D(_BaseColorMap1); TEXTURE2D(_BaseColorMap2); TEXTURE2D(_BaseColorMap3);
    TEXTURE2D(_NormalMap0); TEXTURE2D(_NormalMap1); TEXTURE2D(_NormalMap2); TEXTURE2D(_NormalMap3);
    TEXTURE2D(_MaskMap0); TEXTURE2D(_MaskMap1); TEXTURE2D(_MaskMap2); TEXTURE2D(_MaskMap3);
    TEXTURE2D(_HeightMap0); TEXTURE2D(_HeightMap1); TEXTURE2D(_HeightMap2); TEXTURE2D(_HeightMap3);
    TEXTURE2D(_CarvePyramid0); TEXTURE2D(_CarvePyramid1); TEXTURE2D(_CarvePyramid2); TEXTURE2D(_CarvePyramid3);
    SAMPLER(sampler_BaseColorMap0);
    SAMPLER(sampler_NormalMap0);
    SAMPLER(sampler_HeightMap0);
    #define CARVE_SURFACE_SAMPLER sampler_NormalMap0
    #define sampler_LayerMaskMap sampler_HeightMap0
#else
    TEXTURE2D(_BaseColorMap); TEXTURE2D(_NormalMap); TEXTURE2D(_MaskMap);
    TEXTURE2D(_HeightMap); TEXTURE2D(_CarvePyramid);
    TEXTURE2D(_CarveDetailHeight); TEXTURE2D(_CarveDetailNormal);
    SAMPLER(sampler_BaseColorMap);
    SAMPLER(sampler_NormalMap);
    SAMPLER(sampler_HeightMap);
    #define sampler_MaskMap sampler_BaseColorMap
    #define sampler_CarveDetailNormal sampler_NormalMap
    #define sampler_CarveDetailHeight sampler_HeightMap
#endif

#endif
