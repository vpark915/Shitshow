#ifndef CARVE_LIT_CORE_INCLUDED
#define CARVE_LIT_CORE_INCLUDED

// Carve Lit and Carve Layered Lit (CARVE_LAYERED), shared by HDRP (Shader Graph custom function) and URP (hand-written
// passes). The hooks below default to HDRP, where world positions are camera-relative; URP defines its own first.
#ifndef CARVE_CAMERA_DISTANCE
    #define CARVE_CAMERA_DISTANCE(p) length(p)
#endif
#ifndef CARVE_VIEW_DIR
    #define CARVE_VIEW_DIR(p) GetWorldSpaceNormalizeViewDir(p)
#endif
#ifndef CARVE_SCREEN_SIZE
    #define CARVE_SCREEN_SIZE _ScreenSize.xy
#endif
#ifndef CARVE_PASS_SHADOWS
    #if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_SHADOWS)
        #define CARVE_PASS_SHADOWS 1
    #else
        #define CARVE_PASS_SHADOWS 0
    #endif
#endif
#ifndef CARVE_PASS_DEPTH_ONLY
    #if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_DEPTH_ONLY)
        #define CARVE_PASS_DEPTH_ONLY 1
    #else
        #define CARVE_PASS_DEPTH_ONLY 0
    #endif
#endif
#ifndef CARVE_PASS_LIT
    #if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_GBUFFER || SHADERPASS == SHADERPASS_FORWARD)
        #define CARVE_PASS_LIT 1
    #else
        #define CARVE_PASS_LIT 0
    #endif
#endif

#ifdef CARVE_LAYERED
    #include "CarvePOM.hlsl"
    #define CARVE_HEIGHT_ARGS TEXTURE2D_ARGS(_HeightMap0, sampler_HeightMap0)
    #define CARVE_PYRAMID_ARGS _CarvePyramid0, _CarvePyramid0_TexelSize
    #define CARVE_MAIN_ST float4(1.0, 1.0, 0.0, 0.0)
    #define CARVE_HEIGHT_TEXEL float4(_HeightMap0_TexelSize.xy / max(abs(_BaseColorMap0_ST.xy), 1e-3), 0.0, 0.0)
#else
    #define CARVE_DETAIL_HEIGHT _CarveDetailHeight
    #define CARVE_DETAIL_SAMPLER sampler_CarveDetailHeight
    #include "CarvePOM.hlsl"
    #define CARVE_HEIGHT_ARGS TEXTURE2D_ARGS(_HeightMap, sampler_HeightMap)
    #define CARVE_PYRAMID_ARGS _CarvePyramid, _CarvePyramid_TexelSize
    #define CARVE_MAIN_ST _BaseColorMap_ST
    #define CARVE_HEIGHT_TEXEL _HeightMap_TexelSize
#endif

// Albedo (tinted), mask (metallic and smoothness applied) and tangent-space normal at uv; the detail normal is layered on top.
void CarveSampleSurface(float2 uv, float2 dx, float2 dy, float blur, CarveSurfaceFrame f, bool useFix, out float4 base, out float4 mask, out float3 nTS,
                        out float4 layerWeights)
{
    layerWeights = 0;
#ifdef CARVE_LAYERED
    CarveLayersSurface(uv, dx, dy, blur, f, useFix, base, mask, nTS, layerWeights);
#else
    CarveSampleMaps(TEXTURE2D_ARGS(_BaseColorMap, sampler_BaseColorMap), TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), TEXTURE2D_ARGS(_MaskMap, sampler_MaskMap),
                    CARVE_HEIGHT_ARGS, _NormalScale, uv, dx, dy, blur, f, useFix ? f.hexFixed.x : 0.0, f.hexFix[0], base, mask, nTS);
    base *= _BaseColor;
    mask *= float4(_Metallic, 1.0, 1.0, _Smoothness);
    if (_CarveDetail > 0.5)
    {
        float k = _CarveDetailTiling;
        float3 d = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_CarveDetailNormal, sampler_CarveDetailNormal, uv * k, dx * k, dy * k), _CarveDetailNormalScale);
        nTS = normalize(float3(nTS.xy + d.xy, nTS.z * d.z));
    }
#endif
}

// Normals are returned in world space (the graph's fragment normal space is World), so triplanar needs no mesh tangents.
void CarveLit_float(float4 UV0, float4 UV2, float4 UV3, float3 PositionWS, float3 NormalWS, float3 TangentWS, float3 BitangentWS,
#ifdef CARVE_LAYERED
                    float4 VertexColor, float4 UV1,
#endif
                    out float3 BaseColor, out float3 NormalOutWS, out float Metallic, out float Smoothness,
                    out float Occlusion, out float DepthOffset, out float Alpha, out float3 BentNormalWS, out float SpecularOcclusion)
{
    float3 N = normalize(NormalWS);
    BaseColor = 0;
    NormalOutWS = N;
    Metallic = 0;
    Smoothness = 0;
    Occlusion = 1;
    DepthOffset = 0;
    Alpha = 1;
    BentNormalWS = N;
    SpecularOcclusion = 1;
    bool tri = _CarveMapping > 0.5;
    float2 uv;
    CarveSurfaceFrame f;
    CarveTriplanar tp;
    if (tri)
    {
        float3x3 rot = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
        float3 origin = 0;
        if (_CarveMapping < 1.5)
        {
            float4x4 o2w = GetObjectToWorldMatrix();
            rot = transpose(float3x3(normalize(o2w._m00_m10_m20), normalize(o2w._m01_m11_m21), normalize(o2w._m02_m12_m22)));
            origin = GetAbsolutePositionWS(o2w._m03_m13_m23);
        }
        tp = CarveTriSetup(GetAbsolutePositionWS(PositionWS), origin, N, rot, _CarveBlendSharpness);
        f = CarveTriFrame(PositionWS, N, tp, CARVE_MAIN_ST, uv);
    }
    else
    {
        tp = (CarveTriplanar)0;
        uv = UV0.xy * CARVE_MAIN_ST.xy + CARVE_MAIN_ST.zw;
        f = CarveBuildFrame(PositionWS, N, TangentWS, BitangentWS, uv);
    }
    CarveApplyStability(f, _CarveStability);
#ifdef CARVE_LAYERED
    // Mask source: 0 mask map, 1 vertex color, 2 both. The march runs in UV0, so a UV1 mask (or any mask under triplanar)
    // is read once at the pixel instead of along the ray.
    int maskSource = (int)_CarveLayerMaskSource;
    f.layerMask = maskSource >= 1 ? VertexColor : 1.0;
    if (maskSource != 1)
    {
        if (tri || _CarveLayerMaskUV > 0.5)
        {
            float2 maskUV = _CarveLayerMaskUV > 0.5 ? UV1.xy : UV0.xy;
            f.layerMask *= SAMPLE_TEXTURE2D(_LayerMaskMap, sampler_LayerMaskMap, maskUV * _LayerMaskMap_ST.xy + _LayerMaskMap_ST.zw);
        }
        else
            f.layerMaskLive = 1.0;
    }
#endif
    float3 V = CARVE_VIEW_DIR(PositionWS);
    // Material strengths times the scene-wide feature switches (CarveGlobals: quality and profiling).
    float4 features = CarveGlobalFeatures() * float4(CarveExtrasFade(CARVE_CAMERA_DISTANCE(PositionWS)).xxx, 1.0);
    float selfShadowAmt = _CarveSelfShadow * features.x;
    float occlusionAmt = _CarveOcclusion * features.y;
    float cliffAmt = _CarveCliff * features.z;
    float specAmt = _CarveSpecOcclusion * features.z;
    float antiTiling = _CarveAntiTiling * features.w;
    float depthScale = CarveGlobalDepthScale() * CarveDistanceFade(CARVE_CAMERA_DISTANCE(PositionWS), _CarveFadeStart, _CarveFadeEnd);
#ifdef CARVE_LAYERED
    float detailDepth = 0.0;
#else
    float detailDepth = _CarveDetail > 0.5 ? _CarveDetailDepth * depthScale : 0.0;
#endif
    float depth = _CarveDepth * depthScale + detailDepth;
    f.hexCell = antiTiling > 0.0 ? _CarveAntiTilingCell : 0.0;
    f.hexRot = antiTiling;
    f.detailMix = depth > 0.0 ? detailDepth / depth : 0.0;
#ifndef CARVE_LAYERED
    f.detailTiling = _CarveDetailTiling;
#else
    {
        float2 viewSpan = -float2(dot(f.uAxis, V), dot(f.vAxis, V)) * (depth / max(dot(N, V), 0.05));
        float3 L = _CarveSunDirWS.xyz;
        float2 sunSpan = _CarveSunDirWS.w > 0.5 ? float2(dot(f.uAxis, L), dot(f.vAxis, L)) * (depth / max(dot(N, L), 0.1)) : 0.0;
        CarveLayersPrepare(f, uv, viewSpan, sunSpan);
    }
#endif
    float stepScale = CarveGlobalStepScale();
#if CARVE_PASS_SHADOWS
    stepScale *= _CarveCasterSteps;
    // Relief finer than a shadow texel can't change the shadow map (far cascades): cast the flat bottom of the relief, no march.
    // The bottom, not the top, so the relief never shadows itself; the color pass still self-shadows it.
    float casterTexel = max(length(ddx(PositionWS)), length(ddy(PositionWS)));
    if (stepScale <= 0.0 || casterTexel > depth * 0.5)
    {
        BaseColor = 0;
        NormalOutWS = N;
        Metallic = 0;
        Smoothness = 0;
        Occlusion = 1;
        DepthOffset = depth / max(dot(N, V), 0.25);
        Alpha = 1.0;
        BentNormalWS = N;
        SpecularOcclusion = 1.0;
        return;
    }
#endif

    // Anti-tiling: a box around what the march reads (the view ray, plus the cliff stencil). When one cell owns all of it,
    // every relief read of the march goes through that cell alone (see CarveHexFixBox). Shading rays use the nearest cell.
    if (f.hexCell > 0.0)
    {
        float2 viewSpan = -float2(dot(f.uAxis, V), dot(f.vAxis, V)) * (depth / max(dot(N, V), 0.05));
        float2 pad = abs(CARVE_HEIGHT_TEXEL.xy) * 2.0;
        CarveHexFixBox(f, min(uv, uv + viewSpan) - pad, max(uv, uv + viewSpan) + pad);
    }

    // Baked curvature lives in the mesh's UV frame, so triplanar always measures it (Auto).
    int curveMode = (int)_CarveCurvature;
    float3 curvature = 0;
    if (curveMode == 1 || (curveMode == 2 && tri))
        curvature = CarveAutoCurvature(N, f);
    else if (curveMode == 2)
    {
        float3x3 w2o = (float3x3)GetWorldToObjectMatrix();
        float sN = rcp(max(length(mul(w2o, N)), 1e-6));
        float sT = rcp(max(length(mul(w2o, f.tangentWS)), 1e-6));
        float sB = rcp(max(length(mul(w2o, f.bitangentWS)), 1e-6));
        curvature = UV3.xyz * sN / float3(sT * sT, sB * sB, sT * sB);
    }
    curvature *= _CarveCurvatureScale;

    int silMode = tri ? 0 : (int)_CarveSilhouette;
    float4 bounds = silMode == 1 ? float4(floor(UV0.xy), floor(UV0.xy) + 1.0) : UV2;
    bounds = float4(bounds.xy * CARVE_MAIN_ST.xy + CARVE_MAIN_ST.zw, bounds.zw * CARVE_MAIN_ST.xy + CARVE_MAIN_ST.zw);
    bounds = float4(min(bounds.xy, bounds.zw), max(bounds.xy, bounds.zw));
    bool useBounds = silMode != 0 && all(bounds.zw > bounds.xy);

#ifdef CARVE_LAYERED
    bool usePyramid = _CarveTracing > 0.5 && CarveLayersHavePyramids();
#else
    bool usePyramid = _CarveTracing > 0.5 && _CarvePyramid_TexelSize.z >= 16.0;
#endif

#if CARVE_PASS_DEPTH_ONLY && !defined(_ALPHATEST_ON)
    // Depth prepass (HDRP draws one for alpha clipping and decals). Where no outline can be cut, skip the march and write a
    // safe depth: the deepest point the ray can reach. The GBuffer pass tests LessEqual and writes the exact depth.
    // (An alpha-clipped material's GBuffer tests Equal, so it keeps the exact prepass.)
    if (depth > 0.0 && dot(N, V) > 0.0)
    {
        float nv = max(dot(N, V), 0.05);
        float2 vt = float2(dot(f.tangentWS, V), dot(f.bitangentWS, V));
        float K = curveMode != 0 ? curvature.x * vt.x * vt.x + 2.0 * curvature.z * vt.x * vt.y + curvature.y * vt.y * vt.y : 0.0;
        float disc = nv * nv - 2.0 * K * depth;
        float2 uvPerS = -float2(dot(f.uAxis, V), dot(f.vAxis, V));
        float sMax = disc >= 0.0 ? 2.0 * depth / (nv + sqrt(disc)) : 0.0;
        // Curved outlines need disc < 0; edge outlines need the ray to leave the UV bounds (both ends inside = all inside).
        if (disc >= 0.0 && (!useBounds || (CarveInBounds(uv, bounds) && CarveInBounds(uv + uvPerS * sMax, bounds))))
        {
            float sFar = sMax;
            if (usePyramid)
            {
                float sLow = CarveRayDistance((1.0 - CarveHeightRange(CARVE_PYRAMID_ARGS, uv, uvPerS * sMax, f).x) * depth, nv, K);
                if (sLow >= 0.0)
                    sFar = min(sFar, sLow);
            }
            BaseColor = 0;
            NormalOutWS = N;
            Metallic = 0;
            Smoothness = 0;
            Occlusion = 1;
            DepthOffset = sFar;
            Alpha = 1.0;
            BentNormalWS = N;
            SpecularOcclusion = 1.0;
            return;
        }
    }
#endif

    CarvePOMResult pom = CarvePOM(CARVE_HEIGHT_ARGS, CARVE_PYRAMID_ARGS, usePyramid, uv, V, f,
                                  depth, _CarveMinSteps * stepScale, _CarveMaxSteps * stepScale, (int)_CarveRefineSteps, _CarveAutoLOD * stepScale,
                                  curvature, curveMode != 0, bounds, useBounds);

    // Screen-size LOD for the extras: the relief's depth and the occlusion ring measured in pixels.
    float pixelsPerMeter = f.uvPerMeter.x * CarvePixelsPerUV(f);
    float reliefPx = depth * pixelsPerMeter;
    float3 cliffN = float3(0, 0, 1);
    // Cliff walls narrower than ~2 px can't show stretched texels: fade the cliff shading out (4 height reads saved).
    float cliffLOD = saturate(reliefPx * 0.5 - 1.0);
    float cliff = cliffAmt * cliffLOD > 0.0 ? cliffAmt * cliffLOD * CarveCliff(CARVE_HEIGHT_ARGS, CARVE_HEIGHT_TEXEL, pom, f,
                                                                depth, _CarveCliffSlope, _CarveCliffSlope * 2.5, cliffN) : 0.0;
    float blur = 1.0 + cliff * 6.0;
    float4 base, mask, layerW = 0;
    float3 nW;
    if (tri)
    {
        // Every projection is sampled where the dominant one's ray landed: the surface point moved along the surface by the parallax.
        float3 hitP = tp.pos + mul(transpose(tp.rot), -(V - N * dot(N, V)) * pom.rayDist);
        float3 dp1 = ddx(PositionWS), dp2 = ddy(PositionWS);
        base = 0;
        mask = 0;
        float3 nP = 0;
        [unroll] for (int a = 0; a < 3; a++)
        {
            float w = a == 0 ? tp.weights.x : (a == 1 ? tp.weights.y : tp.weights.z);
            if (w < 0.003)
                continue;
            float3 U, Vp, Wp, gu, gv;
            CarveTriAxes(a, tp.nrm, U, Vp, Wp);
            CarveTriGradients(tp, a, CARVE_MAIN_ST, gu, gv);
            bool isDom = a == tp.dom;
            float2 uvA = isDom ? pom.uv : CarveTriUV(tp, a, hitP, CARVE_MAIN_ST);
            float2 ddxA = float2(dot(dp1, gu), dot(dp1, gv));
            float2 ddyA = float2(dot(dp2, gu), dot(dp2, gv));
            float4 baseA, maskA;
            float3 tn;
            float4 lwA;
            CarveSampleSurface(uvA, ddxA, ddyA, isDom ? blur : 1.0, f, isDom, baseA, maskA, tn, lwA);
            if (isDom)
                layerW = lwA;
            base += baseA * w;
            mask += maskA * w;
            // Whiteout blend with the surface normal, in this projection's frame.
            float3 nl = float3(dot(tp.nrm, U), dot(tp.nrm, Vp), dot(tp.nrm, Wp));
            float3 bl = float3(tn.xy + nl.xy, abs(tn.z) * nl.z);
            nP += (bl.x * U + bl.y * Vp + bl.z * Wp) * w;
        }
        nW = normalize(mul(tp.rot, nP));
        nW = normalize(lerp(nW, CarveFrameToWorld(cliffN, f), cliff));
    }
    else
    {
        float3 tn;
        CarveSampleSurface(pom.uv, f.dUVdx, f.dUVdy, blur, f, true, base, mask, tn, layerW);
        nW = CarveFrameToWorld(normalize(lerp(tn, cliffN, cliff)), f);
    }
    BaseColor = base.rgb;
    NormalOutWS = nW;
    Metallic = mask.r;
    Occlusion = mask.g;
    Smoothness = mask.a;
    if (_CarveStability > 0.0)
    {
        float3 dndx = ddx(nW), dndy = ddy(nW);
        float kernel = min(0.5 * _CarveStability * (dot(dndx, dndx) + dot(dndy, dndy)), 0.04 + 0.16 * _CarveStability);
        float alpha = PerceptualRoughnessToRoughness(PerceptualSmoothnessToPerceptualRoughness(Smoothness));
        Smoothness = PerceptualRoughnessToPerceptualSmoothness(RoughnessToPerceptualRoughness(sqrt(alpha * alpha + kernel)));
    }
    DepthOffset = pom.rayDist;
    Alpha = pom.clipped ? 0.0 : 1.0;

    // From here on the shading rays read a simplified relief: the dominant layer, through its nearest anti-tiling cell.
#ifdef CARVE_LAYERED
    CarveLayersSecondary(f, pom.uv, layerW);
#else
    if (f.hexCell > 0.0 && f.hexFixed.x < 0.5)
    {
        f.hexFix[0] = CarveHexNearestFix(pom.uv, f.hexCell, f.hexRot);
        f.hexFixed.x = 1.0;
    }
#endif

    float shadow = 1.0, reliefAO = 1.0, reflVis = 1.0;
    BentNormalWS = nW;
    SpecularOcclusion = 1.0;
#if CARVE_PASS_LIT
    float3 L = _CarveSunDirWS.xyz;
    bool sunOn = _CarveSunDirWS.w > 0.5 && selfShadowAmt > 0.0;
    float roughness = PerceptualRoughnessToRoughness(PerceptualSmoothnessToPerceptualRoughness(Smoothness));
    // Rough surfaces scatter reflections too widely for relief to occlude them: the reflection ray fades out below 0.3 smoothness.
    float specWeight = specAmt * saturate((Smoothness - 0.25) / 0.15);
    // Sun shadow and reflection visibility in one loop: one copy of the ray march (HLSL inlines every call).
    [loop] for (int ray = 0; ray < 2; ray++)
    {
        bool isSun = ray == 0;
        if (isSun ? !sunOn : specWeight <= 0.0)
            continue;
        float3 dir = isSun ? L : reflect(-V, N);
        float vis = CarveSelfShadow(CARVE_HEIGHT_ARGS, CARVE_PYRAMID_ARGS, usePyramid, pom, dir, f, depth,
                                    isSun ? (int)_CarveShadowSteps : max((int)(_CarveShadowSteps * 0.5), 4),
                                    isSun ? radians(_CarveShadowSoftness) * (1.0 + 2.0 * _CarveStability) : atan(max(roughness, 0.05)),
                                    CarveRayMax(CARVE_PYRAMID_ARGS, usePyramid, pom, dir, f, depth), _CarveAutoLOD);
        if (isSun)
            shadow = vis;
        else
            reflVis = vis;
    }
    if (sunOn)
        BaseColor *= lerp(1.0, shadow, selfShadowAmt * saturate(dot(N, L) * 3.0));
    if (occlusionAmt > 0.0)
    {
        // Rings under 8 px: 4 directions per pixel, rotated 45 degrees on alternate pixels (TAA blends them into 8).
        float4 hclip = TransformWorldToHClip(PositionWS);
        float2 screenPx = (hclip.xy / hclip.w * 0.5 + 0.5) * CARVE_SCREEN_SIZE;
        int occMode = (int)CarveGlobalQuality().w;
        bool full = occMode == 2 || (occMode == 0 && (_CarveOcclusionRadius * pixelsPerMeter >= 8.0 || _CarveStability > 0.0));
        float rotate = full ? 0.0 : fmod(floor(screenPx.x) + floor(screenPx.y), 2.0) * (PI / 4.0);
        float3 bent;
        reliefAO = CarveReliefOcclusion(CARVE_HEIGHT_ARGS, pom, f, depth, _CarveOcclusionRadius, full ? 8 : 4, rotate, bent);
        Occlusion *= lerp(1.0, reliefAO, occlusionAmt);
        BentNormalWS = normalize(nW + (bent.x * f.tangentWS + bent.y * f.bitangentWS) * occlusionAmt);
    }
    SpecularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(ClampNdotV(dot(N, V)), Occlusion, roughness) * lerp(1.0, reflVis, specWeight);
#endif

    int debugView = max((int)_CarveDebug, CarveGlobalDebugView());
    if (debugView != 0)
    {
        Metallic = 0;
        Smoothness = 0.2;
        NormalOutWS = N;
        BentNormalWS = N;
        if (debugView == 1)
            BaseColor = float3(saturate(curvature.x * 0.5), 0.15, saturate(curvature.y * 0.5)) + float3(saturate(-curvature.x), saturate(-curvature.y), 0) * 0.5;
        else if (debugView == 2)
            BaseColor = CarveHeatmap(pom.steps / max(_CarveMaxSteps + _CarveRefineSteps + 1.0, 1.0));
        else if (debugView == 3)
        {
            BaseColor = pom.clipped ? float3(1, 0, 1) : pom.height.xxx;
            Alpha = 1.0;
        }
        else if (debugView == 4)
            BaseColor = shadow.xxx;
        else if (debugView == 5)
            BaseColor = reliefAO.xxx;
        else if (debugView == 6)
            BaseColor = lerp(float3(0.2, 0.2, 0.2), float3(1, 0.5, 0), cliff);
        else if (debugView == 7)
            BaseColor = SpecularOcclusion.xxx;
        else if (debugView == 8)
            BaseColor = tri ? tp.weights : float3(0.5, 0.5, 0.5);
        else if (debugView == 9)
        {
#ifdef CARVE_LAYERED
            float4 lw = layerW;
            BaseColor = lw.x * float3(0.45, 0.45, 0.45) + lw.y * float3(1.0, 0.25, 0.2) + lw.z * float3(0.25, 0.9, 0.2) + lw.w * float3(0.2, 0.45, 1.0);
#else
            BaseColor = float3(0.45, 0.45, 0.45);
#endif
        }
    }
    // Carve cuts its outlines itself: the material needs no HDRP Alpha Clipping, so HDRP treats it as opaque (no forced
    // depth prepass, GBuffer tests LessEqual) and the cheap prepass above stays exact.
    clip(Alpha - 0.5);
}

#endif
