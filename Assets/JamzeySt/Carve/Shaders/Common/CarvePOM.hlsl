#ifndef CARVE_POM_INCLUDED
#define CARVE_POM_INCLUDED

#define CARVE_MAX_STEPS 128

struct CarveSurfaceFrame
{
    float3 normalWS;
    float3 tangentWS;
    float3 bitangentWS;
    float2 uvPerMeter;
    float2 dUVdx;
    float2 dUVdy;
    float2 hUVdx;   // derivatives used for height sampling (coarser with stability)
    float2 hUVdy;
    float3 perpX;   // w -> screen-x share of a surface vector w (dot(w, perpX)), from the derivative basis
    float3 perpY;
    float3 uAxis;   // world gradient of u along the surface (UV per meter of tangential displacement)
    float3 vAxis;
    float hexCell;      // anti-tiling cell size in tiles (0 = off)
    float hexRot;       // anti-tiling rotation strength 0..1
    float4 hexFixed;    // per texture (Carve Lit: x; layers: x..w): 1 = every sample of the current ray lies in one anti-tiling cell
    float4 hexFix[4];   // that cell's transform: cos, sin, offset (cell uv = mul(uv, rotation) + offset), no lattice work per step
    float detailMix;    // detail relief's share of the total depth (0 = no detail)
    float detailTiling; // detail tiles per base tile
    float4 layerMask;   // layered: painted coverage from vertex colors (or the mask map at the pixel), times the live mask map
    float layerMaskLive;// layered: 1 = sample the mask map along the ray
    float4 layerLive;   // layered: 1 = the layer is painted somewhere along this pixel's rays (the others are skipped)
    float layerSingle;  // layered: 1 = exactly one layer is live (no mask reads or blending in the march)
};

struct CarvePOMResult
{
    float2 uv;
    float height;    // 1 = top, 0 = deepest
    float rayDist;   // meters along the view ray below the mesh surface
    float steps;
    bool clipped;    // the ray left the relief without a hit (silhouette)
};

// Direction from the smooth mesh tangent frame; UV-per-meter scale from screen derivatives,
// so depth is in meters whatever the mesh UV scale or tiling.
CarveSurfaceFrame CarveBuildFrame(float3 positionWS, float3 normalWS, float3 tangentWS, float3 bitangentWS, float2 uv)
{
    CarveSurfaceFrame f;
    float3 dp1 = ddx(positionWS);
    float3 dp2 = ddy(positionWS);
    f.dUVdx = ddx(uv);
    f.dUVdy = ddy(uv);
    f.hUVdx = f.dUVdx;
    f.hUVdy = f.dUVdy;
    f.normalWS = normalWS;
    f.tangentWS = normalize(tangentWS - normalWS * dot(tangentWS, normalWS));
    f.bitangentWS = normalize(bitangentWS - normalWS * dot(bitangentWS, normalWS));
    float det = dot(dp1, cross(dp2, normalWS));
    float inv = abs(det) > 1e-20 ? rcp(det) : 0.0;
    f.perpX = cross(dp2, normalWS) * inv;
    f.perpY = cross(normalWS, dp1) * inv;
    float3 gradU = f.perpX * f.dUVdx.x + f.perpY * f.dUVdy.x;
    float3 gradV = f.perpX * f.dUVdx.y + f.perpY * f.dUVdy.y;
    f.uvPerMeter = float2(length(gradU), length(gradV));
    f.uAxis = f.tangentWS * f.uvPerMeter.x;
    f.vAxis = f.bitangentWS * f.uvPerMeter.y;
    f.hexCell = 0.0;
    f.hexRot = 0.0;
    f.hexFixed = 0.0;
    f.hexFix[0] = float4(1, 0, 0, 0);
    f.hexFix[1] = float4(1, 0, 0, 0);
    f.hexFix[2] = float4(1, 0, 0, 0);
    f.hexFix[3] = float4(1, 0, 0, 0);
    f.detailMix = 0.0;
    f.detailTiling = 1.0;
    f.layerMask = 1.0;
    f.layerMaskLive = 0.0;
    f.layerLive = 1.0;
    f.layerSingle = 0.0;
    return f;
}

// ---------------- Triplanar: three planar projections in object (or world) space, POM in the dominant one.

struct CarveTriplanar
{
    float3x3 rot;     // projection space -> world (rotation only)
    float3 pos;       // surface position in projection space (meters)
    float3 nrm;       // geometric normal in projection space
    float3 weights;   // blend weights, sum 1
    int dom;          // dominant projection: 0 X, 1 Y, 2 Z
};

// Planar axes of projection a, mirrored on the negative side so U x V = W points out of the surface (normal maps stay right-handed).
void CarveTriAxes(int a, float3 nrm, out float3 U, out float3 V, out float3 W)
{
    if (a == 0)      { float s = nrm.x >= 0.0 ? 1.0 : -1.0; U = float3(0, 0, -s); V = float3(0, 1, 0); W = float3(s, 0, 0); }
    else if (a == 1) { float s = nrm.y >= 0.0 ? 1.0 : -1.0; U = float3(1, 0, 0); V = float3(0, 0, -s); W = float3(0, s, 0); }
    else             { float s = nrm.z >= 0.0 ? 1.0 : -1.0; U = float3(s, 0, 0); V = float3(0, 1, 0); W = float3(0, 0, s); }
}

// rot = object rotation (columns = object axes in world) or identity for world-space projection.
CarveTriplanar CarveTriSetup(float3 posAbsWS, float3 originAbsWS, float3 normalWS, float3x3 rot, float sharpness)
{
    CarveTriplanar t;
    t.rot = rot;
    t.pos = mul(transpose(rot), posAbsWS - originAbsWS);
    t.nrm = mul(transpose(rot), normalWS);
    float3 w = pow(abs(t.nrm), sharpness);
    t.weights = w / max(w.x + w.y + w.z, 1e-6);
    t.dom = t.weights.x >= t.weights.y && t.weights.x >= t.weights.z ? 0 : (t.weights.y >= t.weights.z ? 1 : 2);
    return t;
}

float2 CarveTriUV(CarveTriplanar t, int a, float3 p, float4 st)
{
    float3 U, V, W;
    CarveTriAxes(a, t.nrm, U, V, W);
    return float2(dot(p, U), dot(p, V)) * st.xy + st.zw;
}

// World gradients of projection a's u and v (unprojected), for analytic texture derivatives.
void CarveTriGradients(CarveTriplanar t, int a, float4 st, out float3 gu, out float3 gv)
{
    float3 U, V, W;
    CarveTriAxes(a, t.nrm, U, V, W);
    gu = mul(t.rot, U) * st.x;
    gv = mul(t.rot, V) * st.y;
}

// Frame for the dominant projection: its UV axes projected onto the surface; derivatives computed analytically,
// so they stay smooth where the dominant projection changes between neighbouring pixels.
CarveSurfaceFrame CarveTriFrame(float3 positionWS, float3 normalWS, CarveTriplanar t, float4 st, out float2 uv)
{
    float3 gu, gv;
    CarveTriGradients(t, t.dom, st, gu, gv);
    float3 uT = gu - normalWS * dot(gu, normalWS);
    float3 vT = gv - normalWS * dot(gv, normalWS);
    float3 T = normalize(uT);
    float3 B = normalize(vT - T * dot(vT, T));
    uv = CarveTriUV(t, t.dom, t.pos, st);
    CarveSurfaceFrame f = CarveBuildFrame(positionWS, normalWS, T, B, uv);
    float3 dp1 = ddx(positionWS), dp2 = ddy(positionWS);
    f.dUVdx = float2(dot(dp1, gu), dot(dp1, gv));
    f.dUVdy = float2(dot(dp2, gu), dot(dp2, gv));
    f.hUVdx = f.dUVdx;
    f.hUVdy = f.dUVdy;
    f.uAxis = uT;
    f.vAxis = vT;
    f.uvPerMeter = float2(length(uT), length(vT));
    return f;
}

float3 CarveFrameToWorld(float3 ts, CarveSurfaceFrame f)
{
    return normalize(ts.x * f.tangentWS + ts.y * f.bitangentWS + ts.z * f.normalWS);
}

// Normal curvature (kT, kB, kTB) in 1/m from the screen derivatives of the interpolated normal. Convex > 0.
float3 CarveAutoCurvature(float3 normalWS, CarveSurfaceFrame f)
{
    float3 dN1 = ddx(normalWS);
    float3 dN2 = ddy(normalWS);
    float3 dNT = dN1 * dot(f.tangentWS, f.perpX) + dN2 * dot(f.tangentWS, f.perpY);
    float3 dNB = dN1 * dot(f.bitangentWS, f.perpX) + dN2 * dot(f.bitangentWS, f.perpY);
    return float3(dot(dNT, f.tangentWS), dot(dNB, f.bitangentWS), 0.5 * (dot(dNT, f.bitangentWS) + dot(dNB, f.tangentWS)));
}

// Stability marches a coarser height mip so sub-pixel relief stops popping without TAA (0..1 = up to +1.25 mips).
void CarveApplyStability(inout CarveSurfaceFrame f, float stability)
{
    float k = exp2(saturate(stability) * 1.25);
    f.hUVdx = f.dUVdx * k;
    f.hUVdy = f.dUVdy * k;
}

// ---------------- Anti-tiling: hex tiling (Mikkelsen 2022). Each hex cell gets its own offset and rotation; the three
// nearest cells are blended by height, so the relief, albedo and normals stay consistent and seams fall into the gaps.

struct CarveHex
{
    float2 uv[3];
    float2x2 rot[3];
    float3 bary;
    int dom;    // 0 = one cell owns the point outright and only slot 0 is set up; -1 = blend
    int2 id;    // the owning cell when dom = 0
    float3 live;// blend: 1 = the cell can win somewhere at this point; the others get no setup and no sample
};

float2x2 CarveFixRotation(float4 fix) { return float2x2(fix.x, -fix.y, fix.y, fix.x); }

float2 CarveHexHash(float2 p)
{
    return frac(sin(mul(float2x2(127.1, 311.7, 269.5, 183.3), p)) * 43758.5453);
}

int CarveHexDominant(float3 bary);
float3 CarveHexCandidates(float3 bary);

// One cell's transform: cell uv = mul(uv, rot) + t (rotated about the cell centre, randomly offset).
void CarveHexCellAffine(int2 v, float cell, float rotStrength, float hashScale, out float2x2 rot, out float2 t)
{
    float a = abs(v.x * v.y) + abs(v.x + v.y) + PI;
    a = fmod(a, 2.0 * PI);
    if (a > PI)
        a -= 2.0 * PI;
    float sn, cs;
    sincos(a * rotStrength, sn, cs);
    rot = float2x2(cs, -sn, sn, cs);
    float2 c = float2(v.x + 0.5 * v.y, 0.8660254 * v.y) * cell;
    t = c - mul(c, rot) + CarveHexHash((float2)v) * hashScale;
}

// The cell nearest to uv (largest barycentric) as a fixed transform: cos, sin, offset (see CarveFixRotation).
float4 CarveHexNearestFix(float2 uv, float cell, float rotStrength)
{
    float2 st = uv / max(cell, 1e-3);
    float2 skew = float2(st.x - 0.57735027 * st.y, 1.15470054 * st.y);
    int2 b = (int2)floor(skew);
    float3 t = float3(frac(skew), 0.0);
    t.z = 1.0 - t.x - t.y;
    float s = step(0.0, -t.z), s2 = 2.0 * s - 1.0;
    float3 bary = float3(-t.z * s2, s - t.y * s2, s - t.x * s2);
    int2 v = bary.x >= max(bary.y, bary.z) ? b + int2(s, s) : (bary.y >= bary.z ? b + int2(s, 1 - s) : b + int2(1 - s, s));
    float2x2 rot;
    float2 off;
    CarveHexCellAffine(v, cell, rotStrength, 1.0, rot, off);
    return float4(rot[0][0], rot[1][0], off);
}

void CarveHexCell(int2 v, float2 uv, float cell, float rotStrength, float hashScale, out float2x2 rot, out float2 cellUV)
{
    float2 t;
    CarveHexCellAffine(v, cell, rotStrength, hashScale, rot, t);
    cellUV = mul(uv, rot) + t;
}

// hashScale: range of each cell's random offset in uv units (1 = one tile). collapse: when one cell owns the point
// outright, set up only that one (slot 0); away from the seams that is almost everywhere.
CarveHex CarveHexSetupEx(float2 uv, float cell, float rotStrength, float hashScale, bool collapse)
{
    CarveHex hx = (CarveHex)0;
    float2 st = uv / max(cell, 1e-3);
    float2 skew = float2(st.x - 0.57735027 * st.y, 1.15470054 * st.y);
    int2 b = (int2)floor(skew);
    float3 t = float3(frac(skew), 0.0);
    t.z = 1.0 - t.x - t.y;
    float s = step(0.0, -t.z), s2 = 2.0 * s - 1.0;
    hx.bary = float3(-t.z * s2, s - t.y * s2, s - t.x * s2);
    int2 v0 = b + int2(s, s), v1 = b + int2(s, 1 - s), v2 = b + int2(1 - s, s);
    int d = collapse ? CarveHexDominant(hx.bary) : -1;
    hx.dom = d >= 0 ? 0 : -1;
    hx.id = d == 0 ? v0 : (d == 1 ? v1 : v2);
    // The owning cell goes to slot 0. One CarveHexCell site in a loop (HLSL inlines every call).
    if (d >= 0)
    {
        v0 = hx.id;
        hx.live = float3(1, 0, 0);
    }
    else
        hx.live = collapse ? CarveHexCandidates(hx.bary) : 1.0;
    hx.rot[0] = hx.rot[1] = hx.rot[2] = float2x2(1, 0, 0, 1);
    hx.uv[0] = hx.uv[1] = hx.uv[2] = uv;
    [loop] for (int i = 0; i < 3; i++)
    {
        if (dot(hx.live, float3(i == 0, i == 1, i == 2)) <= 0.0)
            continue;
        float2x2 r;
        float2 c;
        CarveHexCell(i == 0 ? v0 : (i == 1 ? v1 : v2), uv, cell, rotStrength, hashScale, r, c);
        [branch] switch (i)
        {
            case 0: hx.rot[0] = r; hx.uv[0] = c; break;
            case 1: hx.rot[1] = r; hx.uv[1] = c; break;
            default: hx.rot[2] = r; hx.uv[2] = c; break;
        }
    }
    return hx;
}

CarveHex CarveHexSetup(float2 uv, float cell, float rotStrength) { return CarveHexSetupEx(uv, cell, rotStrength, 1.0, true); }

// Seam blend: each cell scores closeness to its centre plus its height there, and the top score wins (soft edge).
// Near a seam a stone from one cell covers a gap of the other, so seams run around whole stones instead of
// averaging them into a sunken band.
#define CARVE_HEX_BIAS 4.0     // closeness weight (barycentric)
#define CARVE_HEX_HEIGHT 1.2   // height weight: how far into a neighbour a tall feature can reach
#define CARVE_HEX_SOFT 0.06    // edge softness, in score units

float3 CarveHexWeights(float3 bary, float3 h)
{
    float3 a = max(bary, 0.0) * CARVE_HEX_BIAS + h * CARVE_HEX_HEIGHT;
    float3 w = max(a - (max(a.x, max(a.y, a.z)) - CARVE_HEX_SOFT), 0.0);
    return w / max(w.x + w.y + w.z, 1e-6);
}

// Cells that can win somewhere at this point: within height range + softness of the closest one (the rest score lower
// than the leader whatever the heights, so their blend weight is exactly 0).
float3 CarveHexCandidates(float3 bary)
{
    float3 b = max(bary, 0.0) * CARVE_HEX_BIAS;
    return step(max(b.x, max(b.y, b.z)) - (CARVE_HEX_HEIGHT + CARVE_HEX_SOFT), b);
}

// Index of the cell that owns this point outright, else -1: its closeness lead is larger than any height difference plus
// the softness, so no height can bring another cell into the blend. The region is a hexagon (convex) around the cell centre.
int CarveHexDominant(float3 bary)
{
    float3 b = max(bary, 0.0) * CARVE_HEX_BIAS;
    float k = CARVE_HEX_HEIGHT + CARVE_HEX_SOFT;
    return b.x - max(b.y, b.z) >= k ? 0 : (b.y - max(b.x, b.z) >= k ? 1 : (b.z - max(b.x, b.y) >= k ? 2 : -1));
}

// st maps the cells' uv into this texture's tiles (layers share one lattice; float4(1, 1, 0, 0) otherwise).
float CarveHexHeightST(TEXTURE2D_PARAM(heightMap, samp), CarveHex hx, float4 st, float2 dx, float2 dy, out float3 w)
{
    w = float3(1, 0, 0);
    float3 h = 0;
    if (hx.dom >= 0)
        h.x = SAMPLE_TEXTURE2D_GRAD(heightMap, samp, hx.uv[0] * st.xy + st.zw, mul(dx, hx.rot[0]) * st.xy, mul(dy, hx.rot[0]) * st.xy).r;
    else
    {
        [unroll] for (int i = 0; i < 3; i++)
            if (hx.live[i] > 0.0)
                h[i] = SAMPLE_TEXTURE2D_GRAD(heightMap, samp, hx.uv[i] * st.xy + st.zw, mul(dx, hx.rot[i]) * st.xy, mul(dy, hx.rot[i]) * st.xy).r;
        w = CarveHexWeights(hx.bary, h);
    }
    return dot(w, h);
}

float CarveHexHeight(TEXTURE2D_PARAM(heightMap, samp), CarveHex hx, float2 dx, float2 dy, out float3 w)
{
    return CarveHexHeightST(TEXTURE2D_ARGS(heightMap, samp), hx, float4(1, 1, 0, 0), dx, dy, w);
}

// Albedo, mask and tangent-space normal through the anti-tiling cells hx (same height-aware weights as the relief).
void CarveSampleMapsHex(TEXTURE2D_PARAM(baseMap, bs), TEXTURE2D_PARAM(normalMap, ns), TEXTURE2D_PARAM(maskMap, ms), TEXTURE2D_PARAM(heightMap, hs),
                        float normalScale, CarveHex hx, float4 st, float2 dx, float2 dy, float blur, out float4 base, out float4 mask, out float3 nTS)
{
    float3 w;
    CarveHexHeightST(TEXTURE2D_ARGS(heightMap, hs), hx, st, dx, dy, w);
    base = 0;
    mask = 0;
    float3 n = 0;
    [unroll] for (int i = 0; i < 3; i++)
    {
        if (w[i] <= 0.0)
            continue;
        float2 u = hx.uv[i] * st.xy + st.zw, rdx = mul(dx, hx.rot[i]) * st.xy, rdy = mul(dy, hx.rot[i]) * st.xy;
        base += SAMPLE_TEXTURE2D_GRAD(baseMap, bs, u, rdx * blur, rdy * blur) * w[i];
        mask += SAMPLE_TEXTURE2D_GRAD(maskMap, ms, u, rdx * blur, rdy * blur) * w[i];
        float3 t = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap, ns, u, rdx, rdy), normalScale);
        n += float3(mul(hx.rot[i], t.xy), t.z) * w[i];
    }
    nTS = normalize(n);
}

// Albedo, mask and tangent-space normal of one texture set at uv: through anti-tiling (same height-aware weights as the relief).
// fixed/fix: this texture's anti-tiling cell when one cell owns the whole pixel (CarveHexFixBox): sampled directly.
void CarveSampleMaps(TEXTURE2D_PARAM(baseMap, bs), TEXTURE2D_PARAM(normalMap, ns), TEXTURE2D_PARAM(maskMap, ms), TEXTURE2D_PARAM(heightMap, hs),
                     float normalScale, float2 uv, float2 dx, float2 dy, float blur, CarveSurfaceFrame f, float fixed, float4 fix,
                     out float4 base, out float4 mask, out float3 nTS)
{
    if (fixed > 0.5)
    {
        float2x2 r = CarveFixRotation(fix);
        float2 u = mul(uv, r) + fix.zw, rdx = mul(dx, r), rdy = mul(dy, r);
        base = SAMPLE_TEXTURE2D_GRAD(baseMap, bs, u, rdx * blur, rdy * blur);
        mask = SAMPLE_TEXTURE2D_GRAD(maskMap, ms, u, rdx * blur, rdy * blur);
        float3 t = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap, ns, u, rdx, rdy), normalScale);
        nTS = float3(mul(r, t.xy), t.z);
    }
    else if (f.hexCell > 0.0)
        CarveSampleMapsHex(TEXTURE2D_ARGS(baseMap, bs), TEXTURE2D_ARGS(normalMap, ns), TEXTURE2D_ARGS(maskMap, ms), TEXTURE2D_ARGS(heightMap, hs),
                           normalScale, CarveHexSetup(uv, f.hexCell, f.hexRot), float4(1, 1, 0, 0), dx, dy, blur, base, mask, nTS);
    else
    {
        base = SAMPLE_TEXTURE2D_GRAD(baseMap, bs, uv, dx * blur, dy * blur);
        mask = SAMPLE_TEXTURE2D_GRAD(maskMap, ms, uv, dx * blur, dy * blur);
        nTS = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap, ns, uv, dx, dy), normalScale);
    }
}

// (min, max) of a min/max pyramid texel covering every sample a ray can take while moving uvSpan from uv.
// Mip l texel = range of that cell and its 8 neighbours, so one texel at a level whose cell covers the span plus
// the filter footprint (dx, dy) bounds every sample conservatively.
float2 CarvePyramidRange(TEXTURE2D(pyramid), float4 pyramidSize, float2 uv, float2 uvSpan, float2 dx, float2 dy)
{
    float2 res = pyramidSize.zw;
    float span = max(abs(uvSpan.x) * res.x, abs(uvSpan.y) * res.y);
    float foot = max(length(dx * res), length(dy * res));
    float l = clamp(ceil(log2(max(span + 2.0 * foot, 1.0))), 0.0, log2(max(res.x, res.y)));
    float2 cells = max(res * exp2(-l), 1.0);
    int2 ci = (int2)floor(frac(uv) * cells) & ((int2)cells - 1);
    return LOAD_TEXTURE2D_LOD(pyramid, ci, (int)l).gr;
}

// Height range under a short ray through anti-tiled relief. The anti-tiled height is a convex blend of three cells, and the
// cells around the two ends of a ray shorter than half a cell include every cell it crosses; longer rays get the full range.
float2 CarveHexPyramidRange(TEXTURE2D(pyramid), float4 pyramidSize, CarveHex a, CarveHex b, float4 st, float spanLen, float2 dx, float2 dy)
{
    float2 span = spanLen * abs(st.xy);
    float2 foot = float2(max(length(dx * st.xy), length(dy * st.xy)), 0.0);
    float2 r = float2(1.0, 0.0);
    [unroll] for (int i = 0; i < 3; i++)
    {
        float2 q = CarvePyramidRange(pyramid, pyramidSize, a.uv[i] * st.xy + st.zw, span, foot, foot);
        float2 p = CarvePyramidRange(pyramid, pyramidSize, b.uv[i] * st.xy + st.zw, span, foot, foot);
        r = float2(min(r.x, min(q.x, p.x)), max(r.y, max(q.y, p.y)));
    }
    return r;
}

// A cell owns a convex region around its centre (where its blend weight is over 99%). Every point of the box spanned by
// uv0 and uv1 lies in it when all four corners do; the whole box then samples through that one cell, with no lattice,
// hash or blend work per step. Returns false when no single cell owns the box (those rays keep the full blend).
bool CarveHexOwnerBox(float2 uv0, float2 uv1, float cell, float rotStrength, out float4 fix)
{
    fix = float4(1, 0, 0, 0);
    CarveHex a = CarveHexSetupEx(uv0, cell, rotStrength, 1.0, true);
    if (a.dom < 0)
        return false;
    float2 corners[3] = { uv1, float2(uv0.x, uv1.y), float2(uv1.x, uv0.y) };
    [unroll] for (int i = 0; i < 3; i++)
    {
        // Ownership needs only the lattice part of the setup (no hash or rotation): compare the owning cell ids.
        float2 st = corners[i] / max(cell, 1e-3);
        float2 skew = float2(st.x - 0.57735027 * st.y, 1.15470054 * st.y);
        int2 b = (int2)floor(skew);
        float3 t = float3(frac(skew), 0.0);
        t.z = 1.0 - t.x - t.y;
        float s = step(0.0, -t.z), s2 = 2.0 * s - 1.0;
        float3 bary = float3(-t.z * s2, s - t.y * s2, s - t.x * s2);
        int d = CarveHexDominant(bary);
        int2 id = d == 0 ? b + int2(s, s) : (d == 1 ? b + int2(s, 1 - s) : b + int2(1 - s, s));
        if (d < 0 || any(id != a.id))
            return false;
    }
    fix = float4(a.rot[0][0][0], a.rot[0][1][0], a.uv[0] - mul(uv0, a.rot[0]));
    return true;
}

#ifdef CARVE_LAYERED
#include "CarveLayers.hlsl"
#endif

// Fix the anti-tiling cell of every texture whose cell owns the whole box uv0..uv1 (see CarveHexOwnerBox).
void CarveHexFixBox(inout CarveSurfaceFrame f, float2 uv0, float2 uv1)
{
    f.hexFixed = 0.0;
    if (f.hexCell <= 0.0)
        return;
#ifdef CARVE_LAYERED
    CarveLayersFixBox(f, uv0, uv1);
#else
    f.hexFixed.x = CarveHexOwnerBox(uv0, uv1, f.hexCell, f.hexRot, f.hexFix[0]) ? 1.0 : 0.0;
#endif
}

// Height of the whole relief: base map (anti-tiled when on) plus the detail relief, normalized to the total depth.
float CarveHeight(TEXTURE2D_PARAM(heightMap, samp), float2 uv, CarveSurfaceFrame f)
{
    float h;
#ifdef CARVE_LAYERED
    h = CarveLayersHeight(uv, f);
#else
    if (f.hexFixed.x > 0.5)
    {
        float2x2 r = CarveFixRotation(f.hexFix[0]);
        h = SAMPLE_TEXTURE2D_GRAD(heightMap, samp, mul(uv, r) + f.hexFix[0].zw, mul(f.hUVdx, r), mul(f.hUVdy, r)).r;
    }
    else if (f.hexCell > 0.0)
    {
        float3 w;
        h = CarveHexHeight(TEXTURE2D_ARGS(heightMap, samp), CarveHexSetup(uv, f.hexCell, f.hexRot), f.hUVdx, f.hUVdy, w);
    }
    else
        h = SAMPLE_TEXTURE2D_GRAD(heightMap, samp, uv, f.hUVdx, f.hUVdy).r;
#endif
#ifdef CARVE_DETAIL_HEIGHT
    if (f.detailMix > 0.0)
    {
        float k = f.detailTiling;
        h = lerp(h, SAMPLE_TEXTURE2D_GRAD(CARVE_DETAIL_HEIGHT, CARVE_DETAIL_SAMPLER, uv * k, f.hUVdx * k, f.hUVdy * k).r, f.detailMix);
    }
#endif
    return h;
}

bool CarveInBounds(float2 uv, float4 bounds)
{
    return all(uv >= bounds.xy) && all(uv <= bounds.zw);
}

float CarveRayDepth(float s, float NdotV, float K)
{
    return s * NdotV - 0.5 * K * s * s;
}

// Lowest and highest relief (x = min, y = max) anywhere a ray can sample while moving uvSpan from uv.
// seams = false: rays through anti-tiling that isn't fixed get the full range (no seam code compiled in at that call).
float2 CarveHeightRangeEx(TEXTURE2D(pyramid), float4 pyramidSize, float2 uv, float2 uvSpan, CarveSurfaceFrame f, bool seams)
{
#ifdef CARVE_LAYERED
    return CarveLayersRange(uv, uvSpan, f, seams);
#else
    float2 r;
    if (f.hexFixed.x > 0.5)
    {
        float2x2 fr = CarveFixRotation(f.hexFix[0]);
        r = CarvePyramidRange(pyramid, pyramidSize, mul(uv, fr) + f.hexFix[0].zw, mul(uvSpan, fr), mul(f.hUVdx, fr), mul(f.hUVdy, fr));
    }
    else if (f.hexCell > 0.0)
    {
        float len = length(uvSpan);
        if (!seams || len > 0.5 * f.hexCell)
            r = float2(0.0, 1.0);
        else
            r = CarveHexPyramidRange(pyramid, pyramidSize, CarveHexSetupEx(uv, f.hexCell, f.hexRot, 1.0, false),
                                     CarveHexSetupEx(uv + uvSpan, f.hexCell, f.hexRot, 1.0, false), float4(1, 1, 0, 0), len, f.hUVdx, f.hUVdy);
    }
    else
        r = CarvePyramidRange(pyramid, pyramidSize, uv, uvSpan, f.hUVdx, f.hUVdy);
    // The detail relief can add anywhere from nothing to its full share on top of the base range.
    return float2(r.x * (1.0 - f.detailMix), r.y * (1.0 - f.detailMix) + f.detailMix);
#endif
}

float2 CarveHeightRange(TEXTURE2D(pyramid), float4 pyramidSize, float2 uv, float2 uvSpan, CarveSurfaceFrame f)
{
    return CarveHeightRangeEx(pyramid, pyramidSize, uv, uvSpan, f, true);
}

// How many screen pixels a UV offset covers (from the pixel's UV derivatives).
float CarveScreenPixels(float2 uvSpan, CarveSurfaceFrame f)
{
    float det = f.dUVdx.x * f.dUVdy.y - f.dUVdx.y * f.dUVdy.x;
    if (abs(det) < 1e-20)
        return 1e6;
    float a = (uvSpan.x * f.dUVdy.y - uvSpan.y * f.dUVdy.x) / det;
    float b = (f.dUVdx.x * uvSpan.y - f.dUVdx.y * uvSpan.x) / det;
    return length(float2(a, b));
}

// Auto LOD: steps beyond one per screen pixel of travel can't add visible detail.
int CarveLODSteps(int steps, float2 uvSpan, CarveSurfaceFrame f, float stepsPerPixel)
{
    return stepsPerPixel > 0.0 ? min(steps, max((int)ceil(CarveScreenPixels(uvSpan, f) * stepsPerPixel), 2)) : steps;
}

// Distance along the ray where its depth below the top reaches target (meters); -1 if a convex top never gets there.
float CarveRayDistance(float target, float NdotV, float K)
{
    float disc = NdotV * NdotV - 2.0 * K * target;
    return disc >= 0.0 ? 2.0 * target / (NdotV + sqrt(disc)) : -1.0;
}

// viewWS points from the surface to the viewer. depth = meters at height 0.
// curvature = (kT, kB, kTB) in 1/m; the top of the relief bends away from the tangent plane by k*s^2/2.
// bounds = UV rect (xy min, zw max); rays whose hit leaves it are clipped when useBounds.
// usePyramid: march only the band between the lowest and highest relief under the ray (baked min/max pyramid).
// lod = steps per screen pixel of parallax (0 = off).
CarvePOMResult CarvePOM(TEXTURE2D_PARAM(heightMap, samp), TEXTURE2D(pyramid), float4 pyramidSize, bool usePyramid,
                        float2 uv, float3 viewWS, CarveSurfaceFrame f,
                        float depth, float minSteps, float maxSteps, int refineSteps, float lod,
                        float3 curvature, bool curved, float4 bounds, bool useBounds)
{
    CarvePOMResult r;
    r.uv = uv;
    r.height = 1.0;
    r.rayDist = 0.0;
    r.steps = 0.0;
    r.clipped = false;

    float NdotV = dot(f.normalWS, viewWS);
    if (depth <= 0.0)
        return r;
    if (NdotV <= 0.0)
    {
        r.clipped = curved;
        return r;
    }
    NdotV = max(NdotV, 0.05);

    float2 vt = float2(dot(f.tangentWS, viewWS), dot(f.bitangentWS, viewWS));
    float K = curved ? curvature.x * vt.x * vt.x + 2.0 * curvature.z * vt.x * vt.y + curvature.y * vt.y * vt.y : 0.0;
    float disc = NdotV * NdotV - 2.0 * K * depth;
    bool reachesBottom = disc >= 0.0;
    float sMax = reachesBottom ? 2.0 * depth / (NdotV + sqrt(disc)) : 2.0 * NdotV / max(K, 1e-6);

    float2 uvPerS = -float2(dot(f.uAxis, viewWS), dot(f.vAxis, viewWS));
    int n = (int)clamp(round(lerp(maxSteps, minSteps, NdotV)), 1.0, (float)CARVE_MAX_STEPS);
    n = CarveLODSteps(n, uvPerS * sMax, f, lod);
    float invDepth = 1.0 / max(depth, 1e-6);

    // Min/max: the ray can only hit between the heights of the relief under it, so march just that band.
    // Same step length as the full march (never coarser), fewer steps.
    float sStart = 0.0, sEnd = sMax;
    bool endHits = reachesBottom;
    if (usePyramid)
    {
        float2 range = CarveHeightRange(pyramid, pyramidSize, uv, uvPerS * sMax, f);
        sStart = CarveRayDistance((1.0 - range.y) * depth, NdotV, K);
        if (sStart < 0.0 || sStart >= sMax)
        {
            r.rayDist = reachesBottom ? sMax : 0.0;
            r.uv = uv + uvPerS * r.rayDist;
            r.height = reachesBottom ? 0.0 : 1.0;
            r.clipped = !reachesBottom || (useBounds && !CarveInBounds(r.uv, bounds));
            r.steps = 1.0;
            return r;
        }
        float sBottom = CarveRayDistance((1.0 - range.x) * depth, NdotV, K);
        if (sBottom >= 0.0 && sBottom < sMax)
        {
            sEnd = sBottom;
            endHits = true;
        }
        n = clamp((int)ceil(n * (sEnd - sStart) / sMax) + 1, 2, n);
    }
    float ds = (sEnd - sStart) / n;

    // The first sample is the loop's step 0 (one copy of the height read: HLSL inlines it at every call).
    float steps = 0.0;
    float sA = sStart, dA = -1e-5, sB = sEnd, dB = 0.0;
    bool hit = false;
    [loop] for (int i = 0; i <= n && !hit; i++)
    {
        float s = sStart + ds * i;
        float d = CarveHeight(TEXTURE2D_ARGS(heightMap, samp), uv + uvPerS * s, f) - (1.0 - CarveRayDepth(s, NdotV, K) * invDepth);
        steps += 1.0;
        if (d >= 0.0) { sB = s; dB = d; hit = true; }
        else { sA = s; dA = d; }
    }
    if (hit && sB <= 0.0)
    {
        r.steps = steps;
        return r;
    }
    if (!hit)
    {
        // A ray that reaches the lowest relief under it always lands; one that leaves the top of a curved relief is a silhouette.
        if (!endHits)
        {
            r.clipped = true;
            r.steps = steps;
            return r;
        }
        sB = sEnd;
        dB = 0.0;
    }

    // Secant refinement between the last sample above the relief (A) and the first below it (B).
    [loop] for (int k = 0; k < refineSteps; k++)
    {
        float sM = lerp(sB, sA, dB / max(dB - dA, 1e-5));
        float dM = CarveHeight(TEXTURE2D_ARGS(heightMap, samp), uv + uvPerS * sM, f) - (1.0 - CarveRayDepth(sM, NdotV, K) * invDepth);
        if (dM < 0.0) { sA = sM; dA = dM; }
        else          { sB = sM; dB = dM; }
    }
    float sHit = lerp(sB, sA, dB / max(dB - dA, 1e-5));
    r.uv = uv + uvPerS * sHit;
    r.height = 1.0 - CarveRayDepth(sHit, NdotV, K) * invDepth;
    r.rayDist = sHit;
    r.steps = steps + refineSteps;
    r.clipped = useBounds && !CarveInBounds(r.uv, bounds);
    return r;
}

float4 _CarveSunDirWS;   // xyz = direction toward the sun, w = 1 when a sun was found
float4 _CarveGlobals;    // x depth scale, y step scale, z debug view, w = 1 once published
float4 _CarveGlobalFeatures; // scene-wide switches 0..1: x self-shadow, y relief occlusion, z cliff + specular occlusion, w anti-tiling
float4 _CarveGlobalQuality;  // x relief distance scale, y shadow step scale, z extras range in meters (0 = no limit), w occlusion: 0 auto, 1 four, 2 eight

float CarveGlobalDepthScale() { return _CarveGlobals.w > 0.5 ? _CarveGlobals.x : 1.0; }
float CarveGlobalStepScale() { return _CarveGlobals.w > 0.5 ? _CarveGlobals.y : 1.0; }
int CarveGlobalDebugView() { return _CarveGlobals.w > 0.5 ? (int)_CarveGlobals.z : 0; }
float4 CarveGlobalFeatures() { return _CarveGlobals.w > 0.5 ? _CarveGlobalFeatures : 1.0; }
float4 CarveGlobalQuality() { return _CarveGlobals.w > 0.5 ? _CarveGlobalQuality : float4(1, 1, 0, 0); }

// Self-shadow, relief occlusion and cliff shading fade out over the last fifth of the extras range (CarveQualityManager).
float CarveExtrasFade(float distance)
{
    float r = CarveGlobalQuality().z;
    return r > 0.0 ? saturate((r - distance) / (0.2 * r)) : 1.0;
}

// Soft self-shadow: march from the hit toward the light until the ray leaves the top of the relief.
// Penumbra widens with distance from the occluder, scaled by the light's angular radius (radians).
// hMax = highest relief along the way (1 without a pyramid): the march stops once the ray clears it. With a pyramid the ray
// goes 4 steps at a time: a stretch whose highest relief stays under the ray can't shadow, so its reads are skipped.
float CarveSelfShadow(TEXTURE2D_PARAM(heightMap, samp), TEXTURE2D(pyramid), float4 pyramidSize, bool usePyramid, CarvePOMResult hit,
                      float3 lightWS, CarveSurfaceFrame f, float depth, int steps, float angularRadius, float hMax, float lod)
{
    float NdotL = dot(f.normalWS, lightWS);
    if (depth <= 0.0 || hit.clipped || hit.height >= 0.999)
        return 1.0;
    if (NdotL <= 0.0)
        return 0.0;
    NdotL = max(NdotL, 0.05);
    float2 uvPerT = float2(dot(f.uAxis, lightWS), dot(f.vAxis, lightWS));
    float tMax = (min(hMax, 1.0) - hit.height) * depth / NdotL;
    if (tMax <= 0.0)
        return 1.0;
    steps = max((int)ceil(steps * CarveGlobalQuality().y * (min(hMax, 1.0) - hit.height) / (1.0 - hit.height)), 2);
    steps = CarveLODSteps(steps, uvPerT * tMax, f, lod);
    float dt = tMax / steps;
    float penumbra = max(tan(angularRadius), 1e-3);
    float shadow = 0.0;
    int segs = (steps + 3) >> 2;
    [loop] for (int sg = 0; sg < segs && shadow < 1.0; sg++)
    {
        int i0 = sg * 4 + 1, i1 = min(i0 + 3, steps);
        float t0 = dt * (i0 - 0.5);
        if (usePyramid && CarveHeightRangeEx(pyramid, pyramidSize, hit.uv + uvPerT * t0, uvPerT * (dt * (i1 - i0)), f, false).y <= hit.height + t0 * NdotL / depth)
            continue;
        [loop] for (int i = i0; i <= i1; i++)
        {
            float t = dt * (i - 0.5);
            float rayH = hit.height + t * NdotL / depth;
            float h = CarveHeight(TEXTURE2D_ARGS(heightMap, samp), hit.uv + uvPerT * t, f);
            shadow = max(shadow, (h - rayH) * depth / (t * penumbra));
        }
    }
    return 1.0 - saturate(shadow);
}

// Relief occlusion: how far the surrounding relief rises above the hit, sampled on a ring (radius in meters).
// bentTS tilts away from the occluding side (tangent space), for directional indirect light.
// Highest relief along a shadow/reflection ray leaving the hit (1 without a pyramid).
float CarveRayMax(TEXTURE2D(pyramid), float4 pyramidSize, bool usePyramid, CarvePOMResult hit, float3 dirWS, CarveSurfaceFrame f, float depth)
{
    float NdotL = max(dot(f.normalWS, dirWS), 0.05);
    if (!usePyramid || depth <= 0.0)
        return 1.0;
    float tMax = (1.0 - hit.height) * depth / NdotL;
    float2 uvSpan = float2(dot(f.uAxis, dirWS), dot(f.vAxis, dirWS)) * tMax;
    return CarveHeightRangeEx(pyramid, pyramidSize, hit.uv, uvSpan, f, false).y;
}

// samples = 8, or 4 with rotate = 0 or PI/4 alternating per pixel: TAA blends neighbours into the 8-direction ring.
float CarveReliefOcclusion(TEXTURE2D_PARAM(heightMap, samp), CarvePOMResult hit, CarveSurfaceFrame f, float depth, float radius,
                           int samples, float rotate, out float3 bentTS)
{
    bentTS = float3(0, 0, 1);
    if (depth <= 0.0 || hit.clipped)
        return 1.0;
    float occ = 0.0;
    float2 lean = 0.0;
    float da = 2.0 * PI / samples;
    [loop] for (int i = 0; i < samples; i++)
    {
        float a = i * da + 0.3927 + rotate;
        float2 dir = float2(cos(a), sin(a));
        float2 uvOff = dir * f.uvPerMeter * radius;
        float rise = (CarveHeight(TEXTURE2D_ARGS(heightMap, samp), hit.uv + uvOff, f) - hit.height) * depth;
        float o = saturate(rise / radius);
        occ += o;
        lean -= dir * o;
    }
    bentTS = normalize(float3(lean / samples, 1.0));
    return 1.0 - occ / samples;
}

// Screen pixels per uv unit at this pixel (from the uv derivatives).
float CarvePixelsPerUV(CarveSurfaceFrame f)
{
    return rcp(max(max(length(f.dUVdx), length(f.dUVdy)), 1e-8));
}

// Steep walls of the height field get stretched texels: returns the height field's own tangent-space normal
// and a 0..1 cliff factor (slope in meters per meter mapped between slope0 and slope1).
float CarveCliff(TEXTURE2D_PARAM(heightMap, samp), float4 texelSize, CarvePOMResult hit, CarveSurfaceFrame f,
                 float depth, float slope0, float slope1, out float3 cliffNormalTS)
{
    cliffNormalTS = float3(0, 0, 1);
    if (depth <= 0.0 || hit.clipped)
        return 0.0;
    float2 e = texelSize.xy * 1.5;
    float hu = 0.0, hv = 0.0;
    [loop] for (int k = 0; k < 4; k++)
    {
        float2 o = k == 0 ? float2(e.x, 0) : (k == 1 ? float2(-e.x, 0) : (k == 2 ? float2(0, e.y) : float2(0, -e.y)));
        float h = CarveHeight(TEXTURE2D_ARGS(heightMap, samp), hit.uv + o, f);
        if (k < 2)
            hu += k == 0 ? h : -h;
        else
            hv += k == 2 ? h : -h;
    }
    float2 slope = float2(hu / (2.0 * e.x), hv / (2.0 * e.y)) * f.uvPerMeter * depth;
    cliffNormalTS = normalize(float3(-slope, 1.0));
    return saturate((length(slope) - slope0) / max(slope1 - slope0, 1e-3));
}

float CarveDistanceFade(float distance, float fadeStart, float fadeEnd)
{
    float k = CarveGlobalQuality().x;
    return 1.0 - saturate((distance - fadeStart * k) / max((fadeEnd - fadeStart) * k, 1e-3));
}

float3 CarveHeatmap(float t)
{
    t = saturate(t);
    return saturate(float3(1.5 - abs(4.0 * t - 3.0), 1.5 - abs(4.0 * t - 2.0), 1.5 - abs(4.0 * t - 1.0)));
}

#endif
