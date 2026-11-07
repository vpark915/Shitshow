#include "../Headers/HMain.hlsl"
#include "../Headers/HMath.hlsl"
#include "../Headers/RandomSampling.hlsl"
#include "../Headers/HSpaceTransforms.hlsl"

#define ENABLE_DISOCCLUSION_BOOST_FOR_FRAME_BORDERS 0
#define TEMPORAL_STABILIZATION_SAMPLECOUNT 6

#define ENABLE_TEMPORAL_STABILIZATION 1
#define ENABLE_TEMPORAL_DENOISING 1
#define ENABLE_SPATIAL_DENOISING 1

#define MOTION_DELTA_THRESHOLD 0.4
#define TEMPORAL_REJECTION_INTENSITY 10
#define TEMPORAL_STABILIZATION_HISTORY_CLAMP 1

// Shading rate classification
#define SHADING_RATE_FULL 0
#define SHADING_RATE_HALF 0x01
#define SHADING_RATE_QUARTER 0x02

// Unused
#define SHADING_DIRECTION_HORZ 0x04
#define SHADING_DIRECTION_VERT 0x08
#define SHADING_DIRECTION_DIAG 0x10 


// ------------------------ GLOBAL PROPERTIES -------------------------
float4 g_HDepthToViewParamsAO;
float2 g_HScaleFactorAO;
float2 g_HPreviousScaleFactorAO;
float g_HTemporalDynamicRejectionAO;
float g_HTemporalStaticRejectionAO;
float g_HPixelSpreadMultiplier;
float g_HIntensityAO;
uint g_HTemporalSamplecountAO;


// ------------------------ SCALING FUNCTIONS -------------------------
uint2 GetUnscaledCoords(uint2 pixCoord)
{
    uint2 UnscaledCoord = floor(pixCoord.xy * g_HScaleFactorAO.xy);
    return (g_HScaleFactorAO.x + g_HScaleFactorAO.y) == 3 ? UnscaledCoord + int2((pixCoord.y & 1), 0) : UnscaledCoord;
}


// ------------------------ TRANSFORMATION FUNCTIONS -------------------------
float3 ComputeViewSpacePosition(float2 pixCoordNDC, float Depth, float DepthLinear)
{
    #ifdef VR_COMPATIBILITY
    return ComputeViewSpacePosition(pixCoordNDC, Depth, UNITY_MATRIX_I_P) * float3(1, 1, -1);
    #endif
    float3 PositionVS = float3((pixCoordNDC * g_HDepthToViewParamsAO.xy + g_HDepthToViewParamsAO.zw) * DepthLinear, DepthLinear);
    return float3(PositionVS.x, PositionVS.y, -PositionVS.z);
}


// ------------------------ EDGE STOPPING FUNCTIONS -------------------------
float PlaneWeighting(float4 Plane, float3 WorldPosSample, float DepthLinearCenter, float Multiplier)
{
    float PlaneDistance = abs(dot(float4(WorldPosSample, -1), Plane));
    float DepthDifference = PlaneDistance / DepthLinearCenter;
    float PlaneWeight = exp2(-100.0f * Multiplier * (DepthDifference * DepthDifference));
    return PlaneWeight; 
}

float GaussianWeighting(float Radius )
{
    // Assuming Radius is normalized to 1
    return exp(-0.66 * Radius * Radius); 
}


// ------------------------ SAMPLING FUNCTIONS -------------------------
float HInterleavedGradientNoise(float2 pixCoord, int FrameCount)
{
    // Version A
    // const float3 Magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
    // float2 FrameMagicScale = float2(2.083f, 4.867f);
    // pixCoord += FrameCount * FrameMagicScale;
    // return frac(Magic.z * frac(dot(pixCoord, Magic.xy)));

    // Version B
    pixCoord += FrameCount * (float2(47, 17) * 0.695f);
    const float3 Magic = float3( 0.06711056f, 0.00583715f, 52.9829189f );
    return frac(Magic.z * frac(dot(pixCoord, Magic.xy)));
}

float HRadicalInverseVdC(uint bits)
{
    bits = (bits << 16) | (bits >> 16);
    bits = ((bits & 0x55555555) << 1) | ((bits & 0xAAAAAAAA) >> 1);
    bits = ((bits & 0x33333333) << 2) | ((bits & 0xCCCCCCCC) >> 2);
    bits = ((bits & 0x0F0F0F0F) << 4) | ((bits & 0xF0F0F0F0) >> 4);
    bits = ((bits & 0x00FF00FF) << 8) | ((bits & 0xFF00FF00) >> 8);
    return float(bits) * 2.3283064365386963e-10;
}

float2 HSampleUnitDisk(uint Index)
{
    float Angle = HRadicalInverseVdC(Index) * 2.0 * H_PI;
    float Radius = sqrt(frac(float(Index) * 0.61803398875)); // Golden ratio for decorrelation
    return float2(cos(Angle), sin(Angle)) * Radius;
}

bool StochasticMotionRejection(float Velocity, int2 pixCoord)
{
    float MotionRejectionThreshold = saturate(pow(1 - Velocity, TEMPORAL_REJECTION_INTENSITY));
    float MotionRejectionRandom = HInterleavedGradientNoise(pixCoord.yx, _FrameCount % g_HTemporalSamplecountAO) - 0.01f;
    return (MotionRejectionThreshold < MotionRejectionRandom);
}


// ------------------------ PACKING FUNCTIONS -------------------------
uint PackTemporalDataGTAO(float Velocity, float Occlusion, float SampleCount)
{
    uint SampleCountPacked = uint(clamp(SampleCount, 0.0f, 15.0f) + 0.5f) & 0xF;    // 4 bits  (0-15)
    uint VelocityPacked = uint(Velocity * 4095.0f + 0.5f) & 0xFFF;                  // 12 bits (0-4095)
    uint OcclusionPacked = uint(Occlusion * 65535.0f + 0.5f) & 0xFFFF;              // 16 bits (0-65535)

    return (SampleCountPacked << 28) | (VelocityPacked << 16) | OcclusionPacked;
}

float4 UnpackTemporalDataGTAO(uint PackedData)
{
    float SampleCount = float((PackedData >> 28) & 0xF);
    float Velocity = float((PackedData >> 16) & 0xFFF) / 4095.0f;
    float Occlusion = float(PackedData & 0xFFFF) / 65535.0f;
    
    return float4(Velocity, Occlusion, SampleCount, 1.0f);
}


uint PackTemporalDataRTAO(float Velocity, float Occlusion, float SampleCount, bool IsInsideFrame)
{
    uint SampleCountPacked = uint(clamp(SampleCount, 0.0f, 15.0f) + 0.5f) & 0xF;     // 4 bits  (0-15)
    uint VelocityPacked = uint(Velocity * 4095.0f + 0.5f) & 0xFFF;                   // 12 bits (0-4095)
    uint OcclusionPacked = uint(Occlusion * 32767.0f + 0.5f) & 0x7FFF;               // 15 bits (0-32767)
    uint IsInsideFramePacked = IsInsideFrame ? 1u : 0u;                              // 1 bit

    return (IsInsideFramePacked << 31) | (SampleCountPacked << 27) | (VelocityPacked << 15) | OcclusionPacked;
}

float4 UnpackTemporalDataRTAO(uint PackedData)
{
    bool IsInsideFrame = (PackedData >> 31) & 1u;
    float SampleCount = float((PackedData >> 27) & 0xF);
    float Velocity = float((PackedData >> 15) & 0xFFF) / 4095.0f;
    float Occlusion = float(PackedData & 0x7FFF) / 32767.0f;

    return float4(Velocity, Occlusion, SampleCount, float(IsInsideFrame));
}

float2 HPackNormalOctQuadEncode(float3 Normal)
{
    Normal *= rcp(max(dot(abs(Normal), 1.0), 1e-6));
    float T = saturate(-Normal.z);
    return Normal.xy + float2(Normal.x >= 0.0 ? T : -T, Normal.y >= 0.0 ? T : -T);
}

float3 HUnpackNormalOctQuadEncode(float2 Coord)
{
    // NOTE: Do NOT use abs() in this line. It causes miscompilations. (UUM-62216, UUM-70600)
    float3 Normal = float3(Coord.x, Coord.y, 1.0 - (Coord.x < 0 ? -Coord.x : Coord.x) - (Coord.y < 0 ? -Coord.y : Coord.y));
    
    float T = max(-Normal.z, 0.0);
    Normal.xy += float2(Normal.x >= 0.0 ? -T : T, Normal.y >= 0.0 ? -T : T);

    return normalize(Normal);
}

// From Color.hlsl in Core module (SRP)
// Hue, Saturation, Value
// Ranges:
//  Hue [0.0, 1.0]
//  Sat [0.0, 1.0]
//  Lum [0.0, HALF_MAX]
float3 HRgbToHsv(float3 c)
{
    const float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    const float e = 1.0e-4;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 HHsvToRgb(float3 c)
{
    const float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}
