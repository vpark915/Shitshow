#ifndef CARVE_NODES_INCLUDED
#define CARVE_NODES_INCLUDED

// Carve POM sub-graph (your own Shader Graphs, HDRP and URP): Carve's march, silhouettes, self-shadow, occlusion and
// cliff shading on any height map. Sample your textures at ParallaxUV with Mip Sampling Mode = Gradient (DDX, DDY).
#include "../Common/CarvePOM.hlsl"

void CarvePOMNode_float(UnityTexture2D HeightMap, UnityTexture2D HeightPyramid, float2 UV, float2 MeshUV0, float4 UV2, float4 UV3,
                        float3 PositionWS, float3 NormalWS, float3 TangentWS, float3 BitangentWS,
                        float Depth, float MinSteps, float MaxSteps, float RefineSteps, float AutoLOD, float FadeStart, float FadeEnd,
                        float Silhouette, float Curvature, float CurvatureScale,
                        float3 LightDirection, float SelfShadow, float ShadowSoftness, float ShadowSteps,
                        float Occlusion, float OcclusionRadius, float CliffShading, float CliffSlope,
                        out float2 ParallaxUV, out float2 DDX, out float2 DDY, out float Height, out float DepthOffset, out float Alpha,
                        out float Shadow, out float ReliefOcclusion, out float Cliff, out float3 CliffNormal)
{
    float3 N = normalize(NormalWS);
    CarveSurfaceFrame f = CarveBuildFrame(PositionWS, N, TangentWS, BitangentWS, UV);
    float3 V = GetWorldSpaceNormalizeViewDir(PositionWS);
    float depth = max(Depth, 0.0) * CarveGlobalDepthScale() * CarveDistanceFade(length(GetWorldSpaceViewDir(PositionWS)), FadeStart, FadeEnd);
    float stepScale = CarveGlobalStepScale();
#if defined(SHADERPASS) && ((defined(SHADERPASS_SHADOWS) && SHADERPASS == SHADERPASS_SHADOWS) || (defined(SHADERPASS_SHADOWCASTER) && SHADERPASS == SHADERPASS_SHADOWCASTER))
    stepScale *= 0.5;
    // Relief finer than a shadow texel can't change the shadow map: cast the flat bottom of the relief, no march.
    float casterTexel = max(length(ddx(PositionWS)), length(ddy(PositionWS)));
    if (casterTexel > depth * 0.5)
    {
        ParallaxUV = UV;
        DDX = f.dUVdx;
        DDY = f.dUVdy;
        Height = 1.0;
        DepthOffset = depth / max(dot(N, V), 0.25);
        Alpha = 1.0;
        Shadow = 1.0;
        ReliefOcclusion = 1.0;
        Cliff = 0.0;
        CliffNormal = float3(0, 0, 1);
        return;
    }
#endif

    int curveMode = (int)round(Curvature);
    float3 curvature = 0;
    if (curveMode == 1)
        curvature = CarveAutoCurvature(N, f);
    else if (curveMode == 2)
    {
        float3x3 w2o = (float3x3)GetWorldToObjectMatrix();
        float sN = rcp(max(length(mul(w2o, N)), 1e-6));
        float sT = rcp(max(length(mul(w2o, f.tangentWS)), 1e-6));
        float sB = rcp(max(length(mul(w2o, f.bitangentWS)), 1e-6));
        curvature = UV3.xyz * sN / float3(sT * sT, sB * sB, sT * sB);
    }
    curvature *= CurvatureScale;

    // Baked islands are in mesh UV0; carry them into the node's UV (tiling and offset, measured from the derivatives).
    int silMode = (int)round(Silhouette);
    float4 bounds = float4(floor(UV), floor(UV) + 1.0);
    if (silMode == 2)
    {
        float2 m0 = ddx(MeshUV0), m1 = ddy(MeshUV0), u0 = ddx(UV), u1 = ddy(UV);
        bool2 useX = abs(m0) >= abs(m1);
        float2 scale = (useX ? u0 : u1) / max(abs(useX ? m0 : m1), 1e-12) * sign(useX ? m0 : m1);
        float2 offset = UV - MeshUV0 * scale;
        bounds = float4(UV2.xy * scale + offset, UV2.zw * scale + offset);
    }
    bounds = float4(min(bounds.xy, bounds.zw), max(bounds.xy, bounds.zw));
    bool useBounds = silMode != 0 && all(bounds.zw > bounds.xy);

    bool usePyramid = HeightPyramid.texelSize.z >= 16.0;
    CarvePOMResult pom = CarvePOM(TEXTURE2D_ARGS(HeightMap.tex, HeightMap.samplerstate), HeightPyramid.tex, HeightPyramid.texelSize, usePyramid, UV, V, f,
                                  depth, MinSteps * stepScale, MaxSteps * stepScale, (int)RefineSteps, AutoLOD * stepScale,
                                  curvature, curveMode != 0, bounds, useBounds);

    // Extras range (CarveQualityManager): cliff, self-shadow and occlusion fade out with distance.
    float extras = CarveExtrasFade(length(GetWorldSpaceViewDir(PositionWS)));
    CliffShading *= extras;
    SelfShadow *= extras;
    Occlusion *= extras;
    float3 cliffN = float3(0, 0, 1);
    Cliff = CliffShading > 0.0 ? CliffShading * CarveCliff(TEXTURE2D_ARGS(HeightMap.tex, HeightMap.samplerstate), HeightMap.texelSize, pom, f,
                                                           depth, CliffSlope, CliffSlope * 2.5, cliffN) : 0.0;
    CliffNormal = cliffN;
    float blur = 1.0 + Cliff * 6.0;
    ParallaxUV = pom.uv;
    DDX = f.dUVdx * blur;
    DDY = f.dUVdy * blur;
    Height = pom.height;
    DepthOffset = pom.rayDist;
    Alpha = pom.clipped ? 0.0 : 1.0;

    // Light-facing extras only where they are seen (not in depth or shadow passes).
    Shadow = 1.0;
    ReliefOcclusion = 1.0;
#if defined(SHADERGRAPH_PREVIEW) || (defined(SHADERPASS) && ((defined(SHADERPASS_GBUFFER) && SHADERPASS == SHADERPASS_GBUFFER) || (defined(SHADERPASS_FORWARD) && SHADERPASS == SHADERPASS_FORWARD) || (defined(SHADERPASS_FORWARD_UNLIT) && SHADERPASS == SHADERPASS_FORWARD_UNLIT) || (defined(SHADERPASS_UNLIT) && SHADERPASS == SHADERPASS_UNLIT)))
    bool customLight = dot(LightDirection, LightDirection) > 1e-8;
    float3 L = customLight ? LightDirection * rsqrt(max(dot(LightDirection, LightDirection), 1e-8)) : _CarveSunDirWS.xyz;
    if ((customLight || _CarveSunDirWS.w > 0.5) && SelfShadow > 0.0)
    {
        float s = CarveSelfShadow(TEXTURE2D_ARGS(HeightMap.tex, HeightMap.samplerstate), HeightPyramid.tex, HeightPyramid.texelSize, usePyramid, pom, L, f, depth, (int)ShadowSteps, radians(ShadowSoftness),
                                  CarveRayMax(HeightPyramid.tex, HeightPyramid.texelSize, usePyramid, pom, L, f, depth), AutoLOD);
        Shadow = lerp(1.0, s, SelfShadow * saturate(dot(N, L) * 3.0));
    }
    if (Occlusion > 0.0)
    {
        float3 bent;
        ReliefOcclusion = lerp(1.0, CarveReliefOcclusion(TEXTURE2D_ARGS(HeightMap.tex, HeightMap.samplerstate), pom, f, depth, OcclusionRadius, 8, 0.0, bent), Occlusion);
    }
#endif
}

#endif
