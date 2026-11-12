#ifndef CARVE_LAYERS_INCLUDED
#define CARVE_LAYERS_INCLUDED

// Height-blended layers (Carve Layered Lit). Layer 0 is the base; the mask's R, G, B paint layers 1, 2, 3 over the ones
// below, and near a boundary the taller relief wins, so the march, shadows and shading all see one combined relief.
// The march runs in the mesh's UV0 (triplanar: the projection in meters); every layer has its own tiling.
// One sampler per texture kind keeps the shader inside the 16-sampler limit.

// Layer count: compiled in when the material's keyword says so (fewer registers, faster), else read from _LayerCount.
#if defined(CARVE_LAYER_COUNT)
    #define CARVE_HAS_LAYER2 (CARVE_LAYER_COUNT > 2)
    #define CARVE_HAS_LAYER3 (CARVE_LAYER_COUNT > 3)
#else
    #define CARVE_HAS_LAYER2 (_LayerCount > 2.5)
    #define CARVE_HAS_LAYER3 (_LayerCount > 3.5)
#endif

#ifndef CARVE_LAYER_SAMPLER
    #define CARVE_LAYER_SAMPLER sampler_HeightMap0
#endif
#ifndef CARVE_SURFACE_SAMPLER
    #define CARVE_SURFACE_SAMPLER sampler_BaseColorMap0
#endif

// Layers are evaluated in a loop: only the texture reads differ per layer, so the anti-tiling code below is compiled once
// (HLSL inlines every call; unrolled per layer it made the shader several times larger and very slow to compile).
#define CARVE_LAYER_LOOP (2 + (CARVE_HAS_LAYER2 ? 1 : 0) + (CARVE_HAS_LAYER3 ? 1 : 0))

float4 CarveLayerST(int l) { return l == 0 ? _BaseColorMap0_ST : (l == 1 ? _BaseColorMap1_ST : (l == 2 ? _BaseColorMap2_ST : _BaseColorMap3_ST)); }
float4 CarveLayerOneHot(int l) { return float4(l == 0, l == 1, l == 2, l == 3); }
float4 CarveLayerFix(CarveSurfaceFrame f, int l) { return l == 0 ? f.hexFix[0] : (l == 1 ? f.hexFix[1] : (l == 2 ? f.hexFix[2] : f.hexFix[3])); }
float4 CarveLayerHeightTexel(int l) { return l == 0 ? _HeightMap0_TexelSize : (l == 1 ? _HeightMap1_TexelSize : (l == 2 ? _HeightMap2_TexelSize : _HeightMap3_TexelSize)); }

float CarveLayerSample(int l, float2 u, float2 dx, float2 dy)
{
    float h;
    [branch] switch (l)
    {
        case 0: h = SAMPLE_TEXTURE2D_GRAD(_HeightMap0, CARVE_LAYER_SAMPLER, u, dx, dy).r; break;
        case 1: h = SAMPLE_TEXTURE2D_GRAD(_HeightMap1, CARVE_LAYER_SAMPLER, u, dx, dy).r; break;
        case 2: h = SAMPLE_TEXTURE2D_GRAD(_HeightMap2, CARVE_LAYER_SAMPLER, u, dx, dy).r; break;
        default: h = SAMPLE_TEXTURE2D_GRAD(_HeightMap3, CARVE_LAYER_SAMPLER, u, dx, dy).r; break;
    }
    return h;
}

// Layer l's cells at u: one cell through the fixed transform (f.hexFixed) or without anti-tiling, else its lattice cells.
CarveHex CarveLayerCells(int l, float2 u, CarveSurfaceFrame f, float4 one)
{
    CarveHex hx = (CarveHex)0;
    if (f.hexCell > 0.0 && dot(f.hexFixed, one) <= 0.5)
        hx = CarveHexSetup(u, f.hexCell, f.hexRot);
    else
    {
        hx.dom = 0;
        hx.live = float3(1, 0, 0);
        float4 fix = f.hexCell > 0.0 ? CarveLayerFix(f, l) : float4(1, 0, 0, 0);
        hx.rot[0] = CarveFixRotation(fix);
        hx.uv[0] = mul(u, hx.rot[0]) + fix.zw;
    }
    return hx;
}

float2 CarveCellUV(CarveHex hx, int i) { return i == 0 ? hx.uv[0] : (i == 1 ? hx.uv[1] : hx.uv[2]); }
float2x2 CarveCellRot(CarveHex hx, int i) { return i == 0 ? hx.rot[0] : (i == 1 ? hx.rot[1] : hx.rot[2]); }

// Heights of layer l's live cells (one texture read site for every case).
float3 CarveLayerCellHeights(int l, CarveHex hx, float2 ddx, float2 ddy)
{
    float3 c = 0;
    [loop] for (int i = 0; i < 3; i++)
    {
        float3 onec = float3(i == 0, i == 1, i == 2);
        if (dot(hx.live, onec) <= 0.0)
            continue;
        float2x2 r = CarveCellRot(hx, i);
        c += CarveLayerSample(l, CarveCellUV(hx, i), mul(ddx, r), mul(ddy, r)) * onec;
    }
    return c;
}

// Each layer's relief in the shared depth (1 = top, 0 = deepest), after its Height Scale and Offset.
// Layers not painted anywhere along this pixel's rays (f.layerLive) are skipped. Anti-tiling: each layer has its own
// lattice in its own tiles; where one cell owns the whole pixel (f.hexFixed) it samples through that cell alone.
float4 CarveLayerHeights(float2 uv, float2 dx, float2 dy, CarveSurfaceFrame f)
{
    float4 h = 0.0;
    [loop] for (int l = 0; l < CARVE_LAYER_LOOP; l++)
    {
        float4 one = CarveLayerOneHot(l);
        if (dot(f.layerLive, one) <= 0.0)
            continue;
        float4 st = CarveLayerST(l);
        CarveHex hx = CarveLayerCells(l, uv * st.xy + st.zw, f, one);
        float3 c = CarveLayerCellHeights(l, hx, dx * st.xy, dy * st.xy);
        h += (hx.dom >= 0 ? c.x : dot(CarveHexWeights(hx.bary, c), c)) * one;
    }
    return saturate(float4(_CarveLayerOffset0, _CarveLayerOffset1, _CarveLayerOffset2, _CarveLayerOffset3)
                    + h * float4(_CarveLayerHeight0, _CarveLayerHeight1, _CarveLayerHeight2, _CarveLayerHeight3));
}

// Fixed anti-tiling cells per live layer for the box uv0..uv1 (see CarveHexOwnerBox).
void CarveLayersFixBox(inout CarveSurfaceFrame f, float2 uv0, float2 uv1)
{
    [loop] for (int l = 0; l < CARVE_LAYER_LOOP; l++)
    {
        float4 one = CarveLayerOneHot(l);
        if (dot(f.layerLive, one) <= 0.0)
            continue;
        float4 st = CarveLayerST(l), fix;
        if (!CarveHexOwnerBox(uv0 * st.xy + st.zw, uv1 * st.xy + st.zw, f.hexCell, f.hexRot, fix))
            continue;
        f.hexFixed += one;
        [branch] switch (l)
        {
            case 0: f.hexFix[0] = fix; break;
            case 1: f.hexFix[1] = fix; break;
            case 2: f.hexFix[2] = fix; break;
            default: f.hexFix[3] = fix; break;
        }
    }
}

// Shading rays (sun shadow, relief occlusion, reflection) read a simplified relief: the dominant layer at the hit, through
// its nearest anti-tiling cell, one read per step. The march and the colors stay exact; only shading detail right at layer
// borders and cell seams changes.
void CarveLayersSecondary(inout CarveSurfaceFrame f, float2 uv, float4 w)
{
    int l = w.x >= max(w.y, max(w.z, w.w)) ? 0 : (w.y >= max(w.z, w.w) ? 1 : (w.z >= w.w ? 2 : 3));
    float4 one = CarveLayerOneHot(l);
    f.layerLive = one;
    f.layerSingle = 1.0;
    f.hexFixed = 0.0;
    if (f.hexCell <= 0.0)
        return;
    float4 st = CarveLayerST(l);
    float4 fix = CarveHexNearestFix(uv * st.xy + st.zw, f.hexCell, f.hexRot);
    f.hexFixed = one;
    [branch] switch (l)
    {
        case 0: f.hexFix[0] = fix; break;
        case 1: f.hexFix[1] = fix; break;
        case 2: f.hexFix[2] = fix; break;
        default: f.hexFix[3] = fix; break;
    }
}

// Painted coverage -> layer weights (sum 1): each layer covers the ones below it.
float4 CarveLayerCoverage(float2 uv, float2 dx, float2 dy, CarveSurfaceFrame f)
{
    float3 m = f.layerMask.rgb;
    if (f.layerMaskLive > 0.5)
    {
        float2 st = _LayerMaskMap_ST.xy;
        m *= SAMPLE_TEXTURE2D_GRAD(_LayerMaskMap, sampler_LayerMaskMap, uv * st + _LayerMaskMap_ST.zw, dx * st, dy * st).rgb;
    }
    m *= float3(1.0, CARVE_HAS_LAYER2 ? 1.0 : 0.0, CARVE_HAS_LAYER3 ? 1.0 : 0.0);
    return float4((1.0 - m.x) * (1.0 - m.y) * (1.0 - m.z), m.x * (1.0 - m.y) * (1.0 - m.z), m.y * (1.0 - m.z), m.z);
}

// Height blend: within Blend Softness of the tallest (coverage + height), layers share; the rest drop out.
// Height Blend 0 = plain cross-fade by coverage.
float4 CarveLayerWeights(float4 coverage, float4 H)
{
    float4 a = lerp(-2.0, coverage + H, step(1e-5, coverage));
    float top = max(max(a.x, a.y), max(a.z, a.w)) - max(_CarveLayerBlendSoftness, 1e-3);
    float4 b = max(a - top, 0.0);
    return lerp(coverage, b / max(dot(b, 1.0), 1e-6), _CarveHeightBlend);
}

// Coverage anywhere within radius (uv units) of center: the mask read once at a mip whose texels cover that area.
float4 CarveLayerCoverageAround(float2 center, float radius, CarveSurfaceFrame f)
{
    float3 m = f.layerMask.rgb;
    if (f.layerMaskLive > 0.5)
    {
        float2 st = _LayerMaskMap_ST.xy, res = _LayerMaskMap_TexelSize.zw;
        float texels = max(2.0 * radius * max(abs(st.x) * res.x, abs(st.y) * res.y),
                           max(length(f.dUVdx * st * res), length(f.dUVdy * st * res)));
        m *= SAMPLE_TEXTURE2D_LOD(_LayerMaskMap, sampler_LayerMaskMap, center * st + _LayerMaskMap_ST.zw, log2(max(texels, 1.0)) + 1.0).rgb;
    }
    m *= float3(1.0, CARVE_HAS_LAYER2 ? 1.0 : 0.0, CARVE_HAS_LAYER3 ? 1.0 : 0.0);
    return float4((1.0 - m.x) * (1.0 - m.y) * (1.0 - m.z), m.x * (1.0 - m.y) * (1.0 - m.z), m.y * (1.0 - m.z), m.z);
}

// Marks the layers painted anywhere along the view ray (uv -> uv + viewSpan) and the sun ray leaving it (sunSpan):
// most of a large ground shows one or two layers, and the march then reads only those (one, without the mask).
void CarveLayersPrepare(inout CarveSurfaceFrame f, float2 uv, float2 viewSpan, float2 sunSpan)
{
    float4 c = max(CarveLayerCoverageAround(uv + viewSpan * 0.5, length(viewSpan) * 0.5, f),
                   CarveLayerCoverageAround(uv + viewSpan + sunSpan * 0.5, length(sunSpan) * 0.5, f));
    f.layerLive = step(1.0 / 512.0, c);
    f.layerSingle = dot(f.layerLive, 1.0) < 1.5 ? 1.0 : 0.0;
}

float CarveLayersHeight(float2 uv, CarveSurfaceFrame f)
{
    float4 H = CarveLayerHeights(uv, f.hUVdx, f.hUVdy, f);
    if (f.layerSingle > 0.5)
        return dot(H, f.layerLive);
    return dot(CarveLayerWeights(CarveLayerCoverage(uv, f.hUVdx, f.hUVdy, f), H), H);
}

float2 CarveLayerPyramidRange(int l, float2 u, float2 span, float2 dx, float2 dy)
{
    float2 r;
    [branch] switch (l)
    {
        case 0: r = CarvePyramidRange(_CarvePyramid0, _CarvePyramid0_TexelSize, u, span, dx, dy); break;
        case 1: r = CarvePyramidRange(_CarvePyramid1, _CarvePyramid1_TexelSize, u, span, dx, dy); break;
        case 2: r = CarvePyramidRange(_CarvePyramid2, _CarvePyramid2_TexelSize, u, span, dx, dy); break;
        default: r = CarvePyramidRange(_CarvePyramid3, _CarvePyramid3_TexelSize, u, span, dx, dy); break;
    }
    return r;
}

// Range under a ray across layer l's anti-tiling seams: the cells around both ends hold every cell a ray shorter than half
// a cell crosses (see CarveHexPyramidRange); longer rays get the full range.
float2 CarveLayerHexRange(int l, float2 u, float2 sp, float2 dx, float2 dy, CarveSurfaceFrame f)
{
    float len = length(sp);
    if (len > 0.5 * f.hexCell)
        return float2(0.0, 1.0);
    float2 foot = float2(max(length(dx), length(dy)), 0.0);
    float2 r = float2(1.0, 0.0);
    [loop] for (int e = 0; e < 2; e++)
    {
        CarveHex hx = CarveHexSetupEx(e == 0 ? u : u + sp, f.hexCell, f.hexRot, 1.0, false);
        [loop] for (int i = 0; i < 3; i++)
        {
            float2 q = CarveLayerPyramidRange(l, CarveCellUV(hx, i), len, foot, foot);
            r = float2(min(r.x, q.x), max(r.y, q.y));
        }
    }
    return r;
}

// Union of the live layers' ranges (the blend is a convex mix of them). A layer without a height map is flat at its top.
float2 CarveLayersRange(float2 uv, float2 span, CarveSurfaceFrame f, bool seams)
{
    float4 offs = float4(_CarveLayerOffset0, _CarveLayerOffset1, _CarveLayerOffset2, _CarveLayerOffset3);
    float4 scales = float4(_CarveLayerHeight0, _CarveLayerHeight1, _CarveLayerHeight2, _CarveLayerHeight3);
    float2 r = float2(1.0, 0.0);
    [loop] for (int l = 0; l < CARVE_LAYER_LOOP; l++)
    {
        float4 one = CarveLayerOneHot(l);
        if (dot(f.layerLive, one) <= 0.0)
            continue;
        float4 st = CarveLayerST(l);
        float2 u = uv * st.xy + st.zw, sp = span * st.xy, ddx = f.hUVdx * st.xy, ddy = f.hUVdy * st.xy;
        bool fixedCell = dot(f.hexFixed, one) > 0.5;
        float2 q;
        if (CarveLayerHeightTexel(l).z < 8.0)
            q = 1.0;
        else if (!fixedCell && f.hexCell > 0.0)
            q = seams ? CarveLayerHexRange(l, u, sp, ddx, ddy, f) : float2(0.0, 1.0);
        else
        {
            float4 fix = fixedCell ? CarveLayerFix(f, l) : float4(1, 0, 0, 0);
            float2x2 R = CarveFixRotation(fix);
            q = CarveLayerPyramidRange(l, mul(u, R) + fix.zw, mul(sp, R), mul(ddx, R), mul(ddy, R));
        }
        q = saturate(dot(offs, one) + q * dot(scales, one));
        r = float2(min(r.x, q.x), max(r.y, q.y));
    }
    return r.x <= r.y ? r : float2(0.0, 1.0);
}

// A layer without a height map reads Unity's 4x4 white default: flat at its top.
bool CarveLayerIsFlat(float4 heightTexel) { return heightTexel.z < 8.0; }

#define CARVE_LAYER_READY(i) (CarveLayerIsFlat(_HeightMap##i##_TexelSize) || _CarvePyramid##i##_TexelSize.z >= 16.0)

bool CarveLayersHavePyramids()
{
    bool ok = CARVE_LAYER_READY(0) && CARVE_LAYER_READY(1);
    if (CARVE_HAS_LAYER2)
        ok = ok && CARVE_LAYER_READY(2);
    if (CARVE_HAS_LAYER3)
        ok = ok && CARVE_LAYER_READY(3);
    return ok;
}

void CarveLayerSampleMaps(int l, float2 u, float2 dx, float2 dy, float blur, out float4 base, out float4 mask, out float3 nTS)
{
    float4 n;
    float scale;
    [branch] switch (l)
    {
        case 0:
            base = SAMPLE_TEXTURE2D_GRAD(_BaseColorMap0, CARVE_SURFACE_SAMPLER, u, dx * blur, dy * blur);
            mask = SAMPLE_TEXTURE2D_GRAD(_MaskMap0, CARVE_SURFACE_SAMPLER, u, dx * blur, dy * blur);
            n = SAMPLE_TEXTURE2D_GRAD(_NormalMap0, CARVE_SURFACE_SAMPLER, u, dx, dy);
            scale = _NormalScale0;
            break;
        case 1:
            base = SAMPLE_TEXTURE2D_GRAD(_BaseColorMap1, CARVE_SURFACE_SAMPLER, u, dx * blur, dy * blur);
            mask = SAMPLE_TEXTURE2D_GRAD(_MaskMap1, CARVE_SURFACE_SAMPLER, u, dx * blur, dy * blur);
            n = SAMPLE_TEXTURE2D_GRAD(_NormalMap1, CARVE_SURFACE_SAMPLER, u, dx, dy);
            scale = _NormalScale1;
            break;
        case 2:
            base = SAMPLE_TEXTURE2D_GRAD(_BaseColorMap2, CARVE_SURFACE_SAMPLER, u, dx * blur, dy * blur);
            mask = SAMPLE_TEXTURE2D_GRAD(_MaskMap2, CARVE_SURFACE_SAMPLER, u, dx * blur, dy * blur);
            n = SAMPLE_TEXTURE2D_GRAD(_NormalMap2, CARVE_SURFACE_SAMPLER, u, dx, dy);
            scale = _NormalScale2;
            break;
        default:
            base = SAMPLE_TEXTURE2D_GRAD(_BaseColorMap3, CARVE_SURFACE_SAMPLER, u, dx * blur, dy * blur);
            mask = SAMPLE_TEXTURE2D_GRAD(_MaskMap3, CARVE_SURFACE_SAMPLER, u, dx * blur, dy * blur);
            n = SAMPLE_TEXTURE2D_GRAD(_NormalMap3, CARVE_SURFACE_SAMPLER, u, dx, dy);
            scale = _NormalScale3;
            break;
    }
    nTS = UnpackNormalScale(n, scale);
}

// Albedo (tinted), mask (metallic and smoothness applied) and tangent normal of the blended layers at uv.
// useFix = false when uv is not the space the fixed cells were found in (a non-dominant triplanar projection).
void CarveLayersSurface(float2 uv, float2 dx, float2 dy, float blur, CarveSurfaceFrame f, bool useFix,
                        out float4 base, out float4 mask, out float3 nTS, out float4 weights)
{
    if (!useFix)
        f.hexFixed = 0.0;
    // The real mask at the hit (not the live-layer shortcut), so the colors always follow the painting exactly.
    f.layerLive = 1.0;
    float4 w = CarveLayerWeights(CarveLayerCoverage(uv, dx, dy, f), CarveLayerHeights(uv, dx, dy, f));
    float4 tints[4] = { _BaseColor0, _BaseColor1, _BaseColor2, _BaseColor3 };
    float4 metal = float4(_Metallic0, _Metallic1, _Metallic2, _Metallic3);
    float4 smoothL = float4(_Smoothness0, _Smoothness1, _Smoothness2, _Smoothness3);
    base = 0;
    mask = 0;
    float3 n = 0;
    [loop] for (int l = 0; l < CARVE_LAYER_LOOP; l++)
    {
        float4 one = CarveLayerOneHot(l);
        float wl = dot(w, one);
        if (wl <= 0.003)
            continue;
        float4 st = CarveLayerST(l);
        float2 ddx = dx * st.xy, ddy = dy * st.xy;
        CarveHex hx = CarveLayerCells(l, uv * st.xy + st.zw, f, one);
        float3 cw = float3(1, 0, 0);
        if (hx.dom < 0)
            cw = CarveHexWeights(hx.bary, CarveLayerCellHeights(l, hx, ddx, ddy));
        float4 lb = 0, lm = 0;
        float3 lt = 0;
        [loop] for (int i = 0; i < 3; i++)
        {
            float wi = dot(cw, float3(i == 0, i == 1, i == 2));
            if (wi <= 0.0)
                continue;
            float2x2 r = CarveCellRot(hx, i);
            float4 b, m;
            float3 t;
            CarveLayerSampleMaps(l, CarveCellUV(hx, i), mul(ddx, r), mul(ddy, r), blur, b, m, t);
            lb += b * wi;
            lm += m * wi;
            lt += float3(mul(r, t.xy), t.z) * wi;
        }
        float4 tint = l == 0 ? tints[0] : (l == 1 ? tints[1] : (l == 2 ? tints[2] : tints[3]));
        base += lb * tint * wl;
        mask += lm * float4(dot(metal, one), 1.0, 1.0, dot(smoothL, one)) * wl;
        n += normalize(lt) * wl;
    }
    nTS = normalize(n);
    weights = w;
}

#endif
