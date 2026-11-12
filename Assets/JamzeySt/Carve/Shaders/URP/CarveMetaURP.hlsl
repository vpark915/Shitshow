#ifndef CARVE_META_URP_INCLUDED
#define CARVE_META_URP_INCLUDED

// Lightmapper input: the base color at the mesh UV, without the relief march.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
#include "CarveInputURP.hlsl"

struct CarveMetaAttributes
{
    float4 positionOS : POSITION;
    float2 uv0 : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
};

struct CarveMetaVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

CarveMetaVaryings CarveMetaVertex(CarveMetaAttributes input)
{
    CarveMetaVaryings output;
    output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
#ifdef CARVE_LAYERED
    output.uv = input.uv0 * _BaseColorMap0_ST.xy + _BaseColorMap0_ST.zw;
#else
    output.uv = input.uv0 * _BaseColorMap_ST.xy + _BaseColorMap_ST.zw;
#endif
    return output;
}

half4 CarveMetaFragment(CarveMetaVaryings input) : SV_Target
{
#ifdef CARVE_LAYERED
    float3 albedo = SAMPLE_TEXTURE2D(_BaseColorMap0, sampler_BaseColorMap0, input.uv).rgb * _BaseColor0.rgb;
    float metallic = SAMPLE_TEXTURE2D(_MaskMap0, sampler_BaseColorMap0, input.uv).r * _Metallic0;
#else
    float3 albedo = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, input.uv).rgb * _BaseColor.rgb;
    float metallic = SAMPLE_TEXTURE2D(_MaskMap, sampler_BaseColorMap, input.uv).r * _Metallic;
#endif
    MetaInput meta = (MetaInput)0;
    meta.Albedo = albedo * (1.0 - metallic * 0.5);
    meta.Emission = 0;
    return UnityMetaFragment(meta);
}

#endif
