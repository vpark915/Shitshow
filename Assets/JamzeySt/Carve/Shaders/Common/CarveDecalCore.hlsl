#ifndef CARVE_DECAL_CORE_INCLUDED
#define CARVE_DECAL_CORE_INCLUDED

// Shared by HDRP and URP decal graphs (both use HDRP's decal space: Y is the projection axis). Hooks default to HDRP.
#ifndef CARVE_CAMERA_DISTANCE
    #define CARVE_CAMERA_DISTANCE(p) length(p)
#endif
#ifndef CARVE_DECAL_LIT_PASS
    #if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_DBUFFER_PROJECTOR || SHADERPASS == SHADERPASS_FORWARD_PREVIEW)
        #define CARVE_DECAL_LIT_PASS 1
    #else
        #define CARVE_DECAL_LIT_PASS 0
    #endif
#endif

#include "CarvePOM.hlsl"

// Carve Decal (HDRP decal projector): the relief is marched inside the decal in the receiving surface's own frame,
// rebuilt from screen derivatives, so cracks, holes and grates read as cut into any surface (Carve or not).
// Where the ray stays on the top of the relief the surface below shows through, unless Surface Coverage paints the
// decal's own top (plates, manhole covers). Decals can't write depth, so this is parallax depth only.
void CarveDecal_float(float4 UV0, float3 PositionWS,
                      out float3 BaseColor, out float Alpha, out float3 NormalTS, out float NormalAlpha,
                      out float Metallic, out float Occlusion, out float Smoothness, out float MAOSAlpha)
{
    float2 uv = UV0.xy;
    float3 dp1 = ddx(PositionWS), dp2 = ddy(PositionWS);
    float2 du1 = ddx(uv), du2 = ddy(uv);
    float3 V = GetWorldSpaceNormalizeViewDir(PositionWS);

    // Receiving surface normal from the depth buffer, and its UV axes (cotangent frame).
    float3 N = normalize(cross(dp2, dp1));
    N = dot(N, V) < 0.0 ? -N : N;
    float det = du1.x * du2.y - du1.y * du2.x;
    float sgn = det < 0.0 ? -1.0 : 1.0;
    float3 T = (dp1 * du2.y - dp2 * du1.y) * sgn;
    float3 B = (dp2 * du1.x - dp1 * du2.x) * sgn;
    CarveSurfaceFrame f = CarveBuildFrame(PositionWS, N, normalize(T), normalize(B), uv);

    float depth = _CarveDepth * CarveGlobalDepthScale() * CarveDistanceFade(CARVE_CAMERA_DISTANCE(PositionWS), _CarveFadeStart, _CarveFadeEnd);
    float stepScale = CarveGlobalStepScale();
    CarvePOMResult pom = CarvePOM(TEXTURE2D_ARGS(_HeightMap, sampler_HeightMap), _HeightMap, _HeightMap_TexelSize, false, uv, V, f,
                                  depth, _CarveMinSteps * stepScale, _CarveMaxSteps * stepScale, (int)_CarveRefineSteps, _CarveAutoLOD * stepScale,
                                  0, false, float4(0, 0, 1, 1), true);

    float4 base = SAMPLE_TEXTURE2D_GRAD(_BaseColorMap, sampler_BaseColorMap, pom.uv, f.dUVdx, f.dUVdy) * _BaseColor;
    float4 mask = SAMPLE_TEXTURE2D_GRAD(_MaskMap, sampler_MaskMap, pom.uv, f.dUVdx, f.dUVdy);
    float3 nMap = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_NormalMap, sampler_NormalMap, pom.uv, f.dUVdx, f.dUVdy), _NormalScale);

    // How far into the cut the ray went: 0 on the untouched top, 1 once it is deeper than the rim.
    float carved = saturate((1.0 - pom.height) / max(_CarveDecalRim, 1e-3));
    Alpha = pom.clipped ? 0.0 : lerp(carved, 1.0, _CarveDecalCoverage) * base.a;

    // Relief normal at the hit (walls of the cut), with the normal map's detail on top.
    float3 reliefN;
    CarveCliff(TEXTURE2D_ARGS(_HeightMap, sampler_HeightMap), _HeightMap_TexelSize, pom, f, depth, 0.0, 1.0, reliefN);
    float3 nTS = normalize(float3(reliefN.xy + nMap.xy, reliefN.z * nMap.z));
    float3 nW = CarveFrameToWorld(nTS, f);

    // Darken with depth (light rarely reaches the bottom of a hole) and with self-shadow from the sun.
    float cavity = lerp(1.0 - _CarveDecalCavity, 1.0, pom.height);
    float shadow = 1.0;
#if CARVE_DECAL_LIT_PASS
    float3 L = _CarveSunDirWS.xyz;
    if (_CarveSunDirWS.w > 0.5 && _CarveSelfShadow > 0.0)
        shadow = lerp(1.0, CarveSelfShadow(TEXTURE2D_ARGS(_HeightMap, sampler_HeightMap), _HeightMap, _HeightMap_TexelSize, false, pom, L, f, depth, (int)_CarveShadowSteps,
                                           radians(_CarveShadowSoftness), 1.0, _CarveAutoLOD), _CarveSelfShadow * CarveExtrasFade(CARVE_CAMERA_DISTANCE(PositionWS)) * saturate(dot(N, L) * 3.0));
#endif
    BaseColor = base.rgb * cavity * shadow;
    Metallic = mask.r * _Metallic;
    Smoothness = mask.a * _Smoothness * lerp(0.6, 1.0, pom.height);
    Occlusion = mask.g * cavity;

    // Decal tangent space: x = decal X, y = decal Z, z = decal Y (the projection axis).
    float4x4 o2w = GetObjectToWorldMatrix();
    float3 dx = normalize(o2w._m00_m10_m20), dy = normalize(o2w._m01_m11_m21), dz = normalize(o2w._m02_m12_m22);
    NormalTS = normalize(float3(dot(nW, dx), dot(nW, dz), dot(nW, dy)));
    NormalAlpha = Alpha;
    MAOSAlpha = Alpha;

    int debugView = max((int)_CarveDebug, CarveGlobalDebugView());
    if (debugView == 2)
        BaseColor = CarveHeatmap(pom.steps / max(_CarveMaxSteps + _CarveRefineSteps + 1.0, 1.0));
    else if (debugView == 3)
        BaseColor = pom.height.xxx;
}

#endif
