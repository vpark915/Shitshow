// Shared body of the relief bake compute shaders: one thread per vertex evaluates the Carve material's relief height
// with the same code the shaders march (anti-tiling, detail relief, layers, triplanar).
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

StructuredBuffer<float4> _BakeUV;      // uv0.xy, uv1.xy
StructuredBuffer<float4> _BakePos;     // world position
StructuredBuffer<float4> _BakeNrm;     // world normal
StructuredBuffer<float4> _BakeCol;     // vertex color
RWStructuredBuffer<float> _BakeOut;    // relief height, 1 = top
uint _BakeCount;
float4x4 _BakeObjRot;
float4 _BakeObjOrigin;

float _CarveDepth, _CarveMapping, _CarveBlendSharpness, _CarveAntiTiling, _CarveAntiTilingCell;

#ifdef CARVE_LAYERED
    #define DECLARE_LAYER(i) \
        TEXTURE2D(_BaseColorMap##i); SAMPLER(sampler_BaseColorMap##i); TEXTURE2D(_NormalMap##i); TEXTURE2D(_MaskMap##i); \
        TEXTURE2D(_HeightMap##i); SAMPLER(sampler_HeightMap##i); TEXTURE2D(_CarvePyramid##i); \
        float4 _BaseColorMap##i##_ST, _BaseColor##i, _HeightMap##i##_TexelSize, _CarvePyramid##i##_TexelSize; \
        float _NormalScale##i, _Metallic##i, _Smoothness##i, _CarveLayerHeight##i, _CarveLayerOffset##i;
    DECLARE_LAYER(0)
    DECLARE_LAYER(1)
    DECLARE_LAYER(2)
    DECLARE_LAYER(3)
    TEXTURE2D(_LayerMaskMap); SAMPLER(sampler_LayerMaskMap);
    float4 _LayerMaskMap_ST, _LayerMaskMap_TexelSize;
    float _LayerCount, _CarveLayerMaskSource, _CarveLayerMaskUV, _CarveHeightBlend, _CarveLayerBlendSoftness;
    #include "../../Shaders/Common/CarvePOM.hlsl"
    #define BAKE_HEIGHT_ARGS TEXTURE2D_ARGS(_HeightMap0, sampler_HeightMap0)
    #define BAKE_ST float4(1.0, 1.0, 0.0, 0.0)
#else
    TEXTURE2D(_HeightMap); SAMPLER(sampler_HeightMap);
    TEXTURE2D(_CarveDetailHeight); SAMPLER(sampler_CarveDetailHeight);
    float4 _BaseColorMap_ST;
    float _CarveDetail, _CarveDetailDepth, _CarveDetailTiling;
    #define CARVE_DETAIL_HEIGHT _CarveDetailHeight
    #define CARVE_DETAIL_SAMPLER sampler_CarveDetailHeight
    #include "../../Shaders/Common/CarvePOM.hlsl"
    #define BAKE_HEIGHT_ARGS TEXTURE2D_ARGS(_HeightMap, sampler_HeightMap)
    #define BAKE_ST _BaseColorMap_ST
#endif

[numthreads(64, 1, 1)]
void Bake(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _BakeCount)
        return;
    float4 uvs = _BakeUV[id.x];
    float3 p = _BakePos[id.x].xyz;
    float3 n = normalize(_BakeNrm[id.x].xyz);

    CarveSurfaceFrame f = (CarveSurfaceFrame)0;
    f.normalWS = n;
    f.layerMask = 1.0;
    f.layerLive = 1.0;
    f.detailTiling = 1.0;
    f.hexCell = _CarveAntiTiling > 0.0 ? _CarveAntiTilingCell : 0.0;
    f.hexRot = _CarveAntiTiling;
    bool tri = _CarveMapping > 0.5;
#ifdef CARVE_LAYERED
    int maskSource = (int)_CarveLayerMaskSource;
    f.layerMask = maskSource >= 1 ? _BakeCol[id.x] : 1.0;
    if (maskSource != 1)
    {
        if (tri || _CarveLayerMaskUV > 0.5)
        {
            float2 muv = _CarveLayerMaskUV > 0.5 ? uvs.zw : uvs.xy;
            f.layerMask *= SAMPLE_TEXTURE2D_LOD(_LayerMaskMap, sampler_LayerMaskMap, muv * _LayerMaskMap_ST.xy + _LayerMaskMap_ST.zw, 0);
        }
        else
            f.layerMaskLive = 1.0;
    }
#else
    if (_CarveDetail > 0.5)
    {
        float total = _CarveDepth + _CarveDetailDepth;
        f.detailMix = total > 0.0 ? _CarveDetailDepth / total : 0.0;
        f.detailTiling = _CarveDetailTiling;
    }
#endif
    float h;
    if (!tri)
        h = CarveHeight(BAKE_HEIGHT_ARGS, uvs.xy * BAKE_ST.xy + BAKE_ST.zw, f);
    else
    {
        // The shader marches the dominant projection; blending the three here avoids seams in the mesh.
        CarveTriplanar tp = CarveTriSetup(p, _BakeObjOrigin.xyz, n, (float3x3)_BakeObjRot, _CarveBlendSharpness);
        h = 0.0;
        [unroll] for (int a = 0; a < 3; a++)
        {
            float w = a == 0 ? tp.weights.x : (a == 1 ? tp.weights.y : tp.weights.z);
            if (w > 0.001)
                h += CarveHeight(BAKE_HEIGHT_ARGS, CarveTriUV(tp, a, tp.pos, BAKE_ST), f) * w;
        }
    }
    _BakeOut[id.x] = h;
}
