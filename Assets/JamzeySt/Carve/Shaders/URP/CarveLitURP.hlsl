#ifndef CARVE_LIT_URP_INCLUDED
#define CARVE_LIT_URP_INCLUDED

// URP passes for Carve Lit and Carve Layered Lit. Each pass defines CARVE_URP_PASS before including this file. Every
// pass marches the relief and writes the depth where the ray hit it, so props, shadows, SSAO, decals and depth priming
// all see the carved surface. In shadow maps the march runs along the light.
#define CARVE_URP_FORWARD 1
#define CARVE_URP_GBUFFER 2
#define CARVE_URP_SHADOW 3
#define CARVE_URP_DEPTH 4
#define CARVE_URP_DEPTH_NORMALS 5

// One keyword set covers URP 6.0 (_FORWARD_PLUS) and 6.1+ (_CLUSTER_LIGHT_LOOP); 6.1+ warns about any shader merely
// declaring _FORWARD_PLUS, which it never enables, so the declaration marker is dropped.
#undef _FORWARD_PLUS_KEYWORD_DECLARED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#if CARVE_URP_PASS == CARVE_URP_FORWARD || CARVE_URP_PASS == CARVE_URP_GBUFFER
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif
#if CARVE_URP_PASS == CARVE_URP_GBUFFER
    // URP 6.1 replaced UnityGBuffer.hlsl with GBufferOutput.hlsl (6.1+ Core includes the Forward+ deprecation header).
    #if defined(UNIVERSAL_FORWARD_PLUS_KEYWORD_DEPRECATED_INCLUDED)
        #define CARVE_URP_6_1 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
        #define CarveGBufferOutput GBufferFragOutput
    #else
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
        #define CarveGBufferOutput FragmentOutput
    #endif
#endif
#if CARVE_URP_PASS == CARVE_URP_SHADOW
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
    float3 _LightDirection;
    float3 _LightPosition;
#endif
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif
#include "CarveInputURP.hlsl"

#if defined(CARVE_LAYERED)
    #if defined(_CARVE_LAYERS_L2)
        #define CARVE_LAYER_COUNT 2
    #elif defined(_CARVE_LAYERS_L3)
        #define CARVE_LAYER_COUNT 3
    #elif defined(_CARVE_LAYERS_L4)
        #define CARVE_LAYER_COUNT 4
    #endif
#endif

// Hooks for the shared core: URP positions are absolute; the depth passes march in full, because with depth priming the
// color pass tests Equal against them.
float3 CarveViewDirURP(float3 positionWS)
{
#if CARVE_URP_PASS == CARVE_URP_SHADOW
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        return normalize(_LightPosition - positionWS);
    #else
        return _LightDirection;
    #endif
#else
    return GetWorldSpaceNormalizeViewDir(positionWS);
#endif
}
#define CARVE_CAMERA_DISTANCE(p) length(_WorldSpaceCameraPos - (p))
#define CARVE_VIEW_DIR(p) CarveViewDirURP(p)
#define CARVE_SCREEN_SIZE _ScaledScreenParams.xy
#define CARVE_PASS_SHADOWS (CARVE_URP_PASS == CARVE_URP_SHADOW)
#define CARVE_PASS_DEPTH_ONLY 0
#define CARVE_PASS_LIT (CARVE_URP_PASS == CARVE_URP_FORWARD || CARVE_URP_PASS == CARVE_URP_GBUFFER)
#include "../Common/CarveLitCore.hlsl"

// The hit is only ever farther than the mesh surface, so the depth write stays conservative (early depth test kept).
// D3D needs the pixel position interpolated at the centroid for that (as HDRP's conservative depth offset).
#if defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_WEBGPU)
    #define CARVE_DEPTH_SEMANTIC SV_Depth
    #define CARVE_POSITION_QUALIFIERS
#elif defined(UNITY_REVERSED_Z)
    #define CARVE_DEPTH_SEMANTIC SV_DepthLessEqual
    #define CARVE_POSITION_QUALIFIERS linear noperspective centroid
#else
    #define CARVE_DEPTH_SEMANTIC SV_DepthGreaterEqual
    #define CARVE_POSITION_QUALIFIERS linear noperspective centroid
#endif

struct CarveAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float4 uv0 : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
    float4 uv2 : TEXCOORD2;
    float4 uv3 : TEXCOORD3;
#ifdef CARVE_LAYERED
    float4 color : COLOR;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct CarveVaryings
{
    CARVE_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;    // w: bitangent sign
    float4 uv01 : TEXCOORD3;         // xy: UV0, zw: UV1
    float4 uv2 : TEXCOORD4;          // baked silhouette data (face UV rect)
    float4 uv3 : TEXCOORD5;          // baked curvature
#ifdef CARVE_LAYERED
    float4 color : TEXCOORD6;
#endif
#if CARVE_URP_PASS == CARVE_URP_FORWARD || CARVE_URP_PASS == CARVE_URP_GBUFFER
    half4 fogFactorAndVertexLight : TEXCOORD7;
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
    #ifdef DYNAMICLIGHTMAP_ON
        float2 dynamicLightmapUV : TEXCOORD9;
    #endif
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

CarveVaryings CarveVertex(CarveAttributes input)
{
    CarveVaryings output = (CarveVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs vp = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs vn = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    output.positionWS = vp.positionWS;
    output.normalWS = vn.normalWS;
    output.tangentWS = float4(vn.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.uv01 = float4(input.uv0.xy, input.uv1.xy);
    output.uv2 = input.uv2;
    output.uv3 = input.uv3;
#ifdef CARVE_LAYERED
    output.color = input.color;
#endif
#if CARVE_URP_PASS == CARVE_URP_SHADOW
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        float3 lightDir = normalize(_LightPosition - vp.positionWS);
    #else
        float3 lightDir = _LightDirection;
    #endif
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(vp.positionWS, vn.normalWS, lightDir)));
#else
    output.positionCS = vp.positionCS;
#endif
#if CARVE_URP_PASS == CARVE_URP_FORWARD || CARVE_URP_PASS == CARVE_URP_GBUFFER
    half fog = 0;
    #if !defined(_FOG_FRAGMENT)
        fog = ComputeFogFactor(vp.positionCS.z);
    #endif
    output.fogFactorAndVertexLight = half4(fog, VertexLighting(vp.positionWS, vn.normalWS));
    OUTPUT_LIGHTMAP_UV(input.uv1.xy, unity_LightmapST, output.staticLightmapUV);
    #ifdef DYNAMICLIGHTMAP_ON
        output.dynamicLightmapUV = input.uv2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
    #endif
    OUTPUT_SH(vn.normalWS, output.vertexSH);
#endif
    return output;
}

struct CarveSurface
{
    float3 baseColor;
    float3 normalWS;
    float3 bentNormalWS;
    float metallic;
    float smoothness;
    float occlusion;
    float specularOcclusion;
    float3 hitWS;      // where the ray met the relief
};

CarveSurface CarveEvaluate(CarveVaryings input)
{
    CarveSurface s;
    float3 N = normalize(input.normalWS);
    float3 T = input.tangentWS.xyz;
    float3 B = cross(N, T) * input.tangentWS.w;
    float depthOffset, alpha;
    CarveLit_float(float4(input.uv01.xy, 0, 0), input.uv2, input.uv3, input.positionWS, N, T, B,
#ifdef CARVE_LAYERED
                   input.color, float4(input.uv01.zw, 0, 0),
#endif
                   s.baseColor, s.normalWS, s.metallic, s.smoothness, s.occlusion, depthOffset, alpha, s.bentNormalWS, s.specularOcclusion);
    s.hitWS = input.positionWS - CarveViewDirURP(input.positionWS) * depthOffset;
    return s;
}

float CarveDepth(float4 positionCS)
{
    return positionCS.z / positionCS.w;
}

#if CARVE_URP_PASS == CARVE_URP_FORWARD || CARVE_URP_PASS == CARVE_URP_GBUFFER
void CarveLightingInputs(CarveVaryings input, CarveSurface s, out InputData d, out SurfaceData sd)
{
    d = (InputData)0;
    d.positionWS = s.hitWS;
    d.positionCS = input.positionCS;
    d.normalWS = normalize(s.normalWS);
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
    d.shadowCoord = ComputeScreenPos(TransformWorldToHClip(s.hitWS));
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    d.shadowCoord = TransformWorldToShadowCoord(s.hitWS);
#endif
#if CARVE_URP_PASS == CARVE_URP_FORWARD
    d.fogCoord = InitializeInputDataFog(float4(s.hitWS, 1.0), input.fogFactorAndVertexLight.x);
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    d.vertexLighting = input.fogFactorAndVertexLight.yzw;
#endif
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    // Ambient light comes from where the relief leaves it open (bent normal).
#if defined(DYNAMICLIGHTMAP_ON)
    d.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, normalize(s.bentNormalWS));
#else
    d.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalize(s.bentNormalWS));
#endif
    d.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

    sd = (SurfaceData)0;
    sd.albedo = s.baseColor;
    sd.metallic = s.metallic;
    sd.smoothness = s.smoothness;
    sd.normalTS = float3(0, 0, 1);
    // URP has one occlusion for ambient and reflections: the reflection trace (no glowing cracks) is folded in.
    sd.occlusion = min(s.occlusion, s.specularOcclusion);
    sd.alpha = 1.0;
#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, sd, d);
#endif
}
#endif

#if CARVE_URP_PASS == CARVE_URP_FORWARD
void CarveForwardFragment(CarveVaryings input, out half4 outColor : SV_Target0, out float outDepth : CARVE_DEPTH_SEMANTIC
#ifdef _WRITE_RENDERING_LAYERS
                          , out float4 outRenderingLayers : SV_Target1
#endif
                          )
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif
    CarveSurface s = CarveEvaluate(input);
    outDepth = CarveDepth(TransformWorldToHClip(s.hitWS));
    InputData d;
    SurfaceData sd;
    CarveLightingInputs(input, s, d, sd);
    half4 color = UniversalFragmentPBR(d, sd);
    color.rgb = MixFog(color.rgb, d.fogCoord);
    outColor = half4(color.rgb, 1.0);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = float4(EncodeMeshRenderingLayer(GetMeshRenderingLayer()), 0, 0, 0);
#endif
}
#endif

#if CARVE_URP_PASS == CARVE_URP_GBUFFER
CarveGBufferOutput CarveGBufferFragment(CarveVaryings input, out float outDepth : CARVE_DEPTH_SEMANTIC)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif
    CarveSurface s = CarveEvaluate(input);
    outDepth = CarveDepth(TransformWorldToHClip(s.hitWS));
    InputData d;
    SurfaceData sd;
    CarveLightingInputs(input, s, d, sd);
    BRDFData brdf;
    InitializeBRDFData(sd.albedo, sd.metallic, sd.specular, sd.smoothness, sd.alpha, brdf);
    Light mainLight = GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask);
    MixRealtimeAndBakedGI(mainLight, d.normalWS, d.bakedGI, d.shadowMask);
    half3 gi = GlobalIllumination(brdf, d.bakedGI, sd.occlusion, d.positionWS, d.normalWS, d.viewDirectionWS);
#if defined(CARVE_URP_6_1)
    d.positionCS.z = outDepth;   // URP 6.1+ also writes depth to a GBuffer target
    return PackGBuffersBRDFData(brdf, d, sd.smoothness, gi, sd.occlusion);
#else
    return BRDFDataToGbuffer(brdf, d, sd.smoothness, gi, sd.occlusion);
#endif
}
#endif

#if CARVE_URP_PASS == CARVE_URP_SHADOW
half4 CarveShadowFragment(CarveVaryings input, out float outDepth : CARVE_DEPTH_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif
    CarveSurface s = CarveEvaluate(input);
    float3 lightDir = CarveViewDirURP(input.positionWS);
    outDepth = CarveDepth(ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(s.hitWS, normalize(input.normalWS), lightDir))));
    return 0;
}
#endif

#if CARVE_URP_PASS == CARVE_URP_DEPTH
half CarveDepthFragment(CarveVaryings input, out float outDepth : CARVE_DEPTH_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif
    CarveSurface s = CarveEvaluate(input);
    outDepth = CarveDepth(TransformWorldToHClip(s.hitWS));
    return outDepth;
}
#endif

#if CARVE_URP_PASS == CARVE_URP_DEPTH_NORMALS
void CarveDepthNormalsFragment(CarveVaryings input, out half4 outNormalWS : SV_Target0, out float outDepth : CARVE_DEPTH_SEMANTIC
#ifdef _WRITE_RENDERING_LAYERS
                               , out float4 outRenderingLayers : SV_Target1
#endif
                               )
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif
    CarveSurface s = CarveEvaluate(input);
    outDepth = CarveDepth(TransformWorldToHClip(s.hitWS));
    float3 n = normalize(s.normalWS);
#if defined(_GBUFFER_NORMALS_OCT)
    outNormalWS = half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5)), 0.0);
#else
    outNormalWS = half4(n, 0.0);
#endif
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = float4(EncodeMeshRenderingLayer(GetMeshRenderingLayer()), 0, 0, 0);
#endif
}
#endif

#endif
