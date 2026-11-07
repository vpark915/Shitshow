#ifndef H_MAIN
#define H_MAIN

#include "HPipelineDefineAO.hlsl"

// --------------------------------- INCLUDE FILES ----------------------------- //


#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/Sampling.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"


// --------------------------------------- VARIABLES ----------------------------------- //
float4 _HRenderScale;
float4 _HRenderScalePrevious;
uint _JitteringIsEnabled;
uint _ForceNonOctahedralNormals;



#define IsJitteringEnabled _JitteringIsEnabled > 0
#define HRenderScale float4(1,1,1,1)
#define HRenderScalePrevious float4(1,1,1,1)
int _FrameCount;


// --------------------------------------- CONSTANTS -------------------------------------- //
#define H_TWO_PI (6.28318530718f)
#define H_PI (3.1415926535897932384626433832795)
#define H_PI_HALF (1.5707963267948966192313216916398)


// ----------------------------------- TEXTURE SAMPLERS ----------------------------------- //


SamplerState sampler_point_clamp;
SamplerState sampler_linear_clamp;
#define H_SAMPLER_POINT_CLAMP                     sampler_point_clamp
#define H_SAMPLER_LINEAR_CLAMP                    sampler_linear_clamp


// --------------------------------- TEXTURE READ / WRITE HELPERS ----------------------------- //


#define H_COORD(pixelCoord)     uint2(pixelCoord)
#define H_INDEX_ARRAY(slot)     (slot)


// ----------------------------- TEXTURE PROPERTY DECLARATIONS ----------------------------- //


#define H_TEXTURE(textureName)                      TEXTURE2D_X(textureName) 
#define H_TEXTURE_ARRAY(textureName)                TEXTURE2D_ARRAY(textureName)
#define H_TEXTURE_UINT2(textureName)                Texture2D<uint2> textureName

#define H_RW_TEXTURE(type, textureName)             RW_TEXTURE2D(type, textureName)
#define H_RW_TEXTURE_ARRAY(type, textureName)       RW_TEXTURE2D_ARRAY(type, textureName)


// -------------------------------- TEXTURE FETCH OPERATIONS -------------------------------- //


#define H_LOAD(textureName, unCoord2)                                           LOAD_TEXTURE2D_X(textureName, unCoord2)
#define H_LOAD_LOD(textureName, unCoord2, lod)                                  LOAD_TEXTURE2D_X_LOD(textureName, unCoord2, lod)
#define H_LOAD_ARRAY(textureName, unCoord2, index)                              LOAD_TEXTURE2D_ARRAY(textureName, unCoord2, index)
#define H_LOAD_ARRAY_LOD(textureName, unCoord2, index, lod)                     LOAD_TEXTURE2D_ARRAY_LOD(textureName, unCoord2, index, lod)

#define H_SAMPLE(textureName, samplerName, coord2)                              SAMPLE_TEXTURE2D_X(textureName, samplerName, coord2)
#define H_SAMPLE_LOD(textureName, samplerName, coord2, lod)                     SAMPLE_TEXTURE2D_X_LOD(textureName, samplerName, coord2, lod)
#define H_SAMPLE_ARRAY_LOD(textureName, samplerName, coord2, index, lod)        SAMPLE_TEXTURE2D_ARRAY_LOD(textureName, samplerName, coord2, index, lod)

#define H_GATHER_RED(textureName, samplerName, coord2, offset)                  GATHER_RED_TEXTURE2D_X(textureName, samplerName, coord2)
#define H_GATHER_GREEN(textureName, samplerName, coord2, offset)                GATHER_GREEN_TEXTURE2D_X(textureName, samplerName, coord2)


// ---------------------------------- MATRICES ----------------------------- //


float4x4 _H_MATRIX_PREV_V;
float4x4 _H_MATRIX_PREV_VP;
float4x4 _H_MATRIX_PREV_I_VP;
float4x4 _H_MATRIX_I_VP;

#define H_MATRIX_PREV_V                     _H_MATRIX_PREV_V
#define H_MATRIX_PREV_VP                    _H_MATRIX_PREV_VP
#define H_MATRIX_PREV_I_VP                  _H_MATRIX_PREV_I_VP
#define H_MATRIX_I_VP                       _H_MATRIX_I_VP
#define H_MATRIX_I_P                        UNITY_MATRIX_I_P
#define H_MATRIX_VP                         UNITY_MATRIX_VP
#define H_MATRIX_V                          UNITY_MATRIX_V
#define H_MATRIX_I_V                        UNITY_MATRIX_I_V


// ------------------------------------- MAIN BUFFERS DECLARATION ----------------------------- //
#if UNITY_VERSION < 600000
//URP NRG 2022 multi window support
#define g_HTraceDepth _CameraDepthTexture
#define g_HTraceGBuffer2 _CameraNormalsTexture
#else
H_TEXTURE(g_HTraceDepth);
H_TEXTURE(g_HTraceGBuffer2);
#endif


H_TEXTURE(g_HTraceMotionMask);
H_TEXTURE(g_HTraceMotionDelta);
H_TEXTURE(g_HTraceMotionVectors);
H_TEXTURE(g_HTraceRenderLayerMask);

// ----------------------------------- MAIN BUFFERS FETCH ----------------------------------- //
#define HBUFFER_NORMAL_WS(pixCoord)             GetNormalWS(pixCoord)
#define HBUFFER_DEPTH(pixCoord)                 GetDepth(pixCoord)
#define HBUFFER_MOTION_VECTOR(pixCoord)         GetMotionVector(pixCoord)
#define HBUFFER_MOTION_MASK(pixCoord)           GetMotionMask(pixCoord)
#define HBUFFER_MOTION_DELTA(pixCoord)          GetMotionDelta(pixCoord)
#define HBUFFER_MOTION_DIRECTION(pixCoord)      GetMotionDirection(pixCoord)
#define HBUFFER_RENDER_LAYER_MASK(pixCoord)     GetRenderLayerMask(pixCoord)

float3 GetNormalWS(uint2 pixCoord)
{
    
    
    float3 Normal = H_LOAD(g_HTraceGBuffer2, pixCoord).xyz;
    #if _GBUFFER_NORMALS_OCT
    if (!_ForceNonOctahedralNormals)
    {
        float2 RemappedOctNormalWS = Unpack888ToFloat2(Normal);
        float2 OctNormalWS = RemappedOctNormalWS.xy * 2.0 - 1.0;
        Normal = UnpackNormalOctQuadEncode(OctNormalWS); 
    }
    #endif
    return Normal;
}


float GetDepth(uint2 pixCoord)
{
    
    
    return H_LOAD(g_HTraceDepth, pixCoord).x;
}

float2 GetMotionVector(uint2 pixCoord)
{
    
    
    return H_LOAD(g_HTraceMotionVectors, pixCoord).xy;
}

float GetMotionMask(uint2 pixCoord)
{
    
    
    return any(H_LOAD(g_HTraceMotionMask, pixCoord).xy != 0);
}

float3 GetMotionDelta(uint2 pixCoord)
{
    
    
    float2 MotionDelta = H_LOAD(g_HTraceMotionDelta, pixCoord).xy;
    return float3(MotionDelta.x, abs(MotionDelta.y), MotionDelta.y < 0);
}

float4 GetMotionDirection(uint2 pixCoord, out bool Disoccluded)
{
    float4 MotionDirection = H_LOAD(g_HTraceMotionDelta, pixCoord).xyzw;
    Disoccluded = MotionDirection.z < 0;
    MotionDirection.z = abs(MotionDirection.z);

    
    
    return float4(UnpackNormalOctQuadEncode(MotionDirection.zw * 2.0f - 1.0f), abs(MotionDirection.y));
}

float4 GetMotionDirection(uint2 pixCoord)
{
    bool Disoccluded;
    return GetMotionDirection(pixCoord, Disoccluded);
}


uint GetRenderLayerMask(uint2 pixCoord)
{
    #if UNITY_VERSION >= 600000
    

    #if UNITY_VERSION >= 60020000
    return asuint(H_LOAD(g_HTraceRenderLayerMask, pixCoord.xy).x);
    #else
    float4 RenderLayers = H_LOAD(g_HTraceRenderLayerMask, pixCoord.xy);
    return DecodeMeshRenderingLayer(RenderLayers.x);
    #endif
    
    #else
    return 0;
    #endif
}
#endif
