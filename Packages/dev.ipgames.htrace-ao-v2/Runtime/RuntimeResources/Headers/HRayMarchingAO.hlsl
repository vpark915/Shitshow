
// ------------------------ MARCHING VARIABLES -------------------------
int _RayMarchingSamplecount;
float _RayMarchingDistance;
float2 _RayMarchingThicknessParams;


// ------------------------ MARCHING FUNCTIONS -------------------------
float4 RayMarchAO(H_TEXTURE(_TracingDepth), float3 RayStartPositionNDC, float3 RayEndPositionNDC, float StepJitter)
{
    float CurrentDepth = 0;
    float3 CurrentPositionNDC = RayStartPositionNDC;
    float3 PreviousPositionNDC = RayStartPositionNDC;

    const static uint StepCount = _RayMarchingSamplecount;
    
    bool PotentialHitFound = false;
    
    for (int i = 0; i < StepCount; i++)
    {
        float StepExponent = pow(float(i + StepJitter) / float(StepCount), 2);
        CurrentPositionNDC = lerp(RayStartPositionNDC, RayEndPositionNDC, StepExponent);
        
        CurrentDepth = H_SAMPLE_LOD(_TracingDepth, H_SAMPLER_POINT_CLAMP, CurrentPositionNDC.xy * HRenderScale.xy, 0).x * 0.9995f;
    
        if (CurrentPositionNDC.z < CurrentDepth)
        {
            if (RAYMARCH_BEHIND_OBJECTS)
            {
                if (CurrentPositionNDC.z > CurrentDepth * _RayMarchingThicknessParams.x + _RayMarchingThicknessParams.y) 
                {
                    return float4(CurrentPositionNDC.xyz, 1);
                    break; 
                }  
            }
            else
            {
                PotentialHitFound = true;
                break;   
            }
        }
        
        PreviousPositionNDC = CurrentPositionNDC;
    }

    if (!RAYMARCH_BEHIND_OBJECTS)
    {
        if (PotentialHitFound)
        {
            if (CurrentPositionNDC.z > CurrentDepth * _RayMarchingThicknessParams.x + _RayMarchingThicknessParams.y) 
            {
                return float4(CurrentPositionNDC.xyz, 1);
            }
        }
    }
    
    return float4(PreviousPositionNDC.xyz, 0);
}


// ------------------------ RAY FUNCTIONS -------------------------
float2 LineBoxIntersect(float3 RayOrigin, float3 RayEnd, float3 BoxMin, float3 BoxMax)
{
    float3 InvRayDir = 1.0f / (RayEnd - RayOrigin);
    
    float3 FirstPlaneIntersections = (BoxMin - RayOrigin) * InvRayDir;
    float3 SecondPlaneIntersections = (BoxMax - RayOrigin) * InvRayDir;
    float3 ClosestPlaneIntersections = min(FirstPlaneIntersections, SecondPlaneIntersections);
    float3 FurthestPlaneIntersections = max(FirstPlaneIntersections, SecondPlaneIntersections);

    float2 BoxIntersections;
    BoxIntersections.x = max(ClosestPlaneIntersections.x, max(ClosestPlaneIntersections.y, ClosestPlaneIntersections.z));
    BoxIntersections.y = min(FurthestPlaneIntersections.x, min(FurthestPlaneIntersections.y, FurthestPlaneIntersections.z));
    return saturate(BoxIntersections);
}

float3 GetRayStartPositionNDC(float2 pixCoordNDC, float Depth, float3 PositionWS, float3 NormalWS, inout float3 RayOriginWS)
{
    // Calculate normal bias
    float2 CornerCoordNDC = pixCoordNDC + 0.5f * _ScreenSize.zw;
    float3 CornerPositionWS = H_COMPUTE_POSITION_WS(CornerCoordNDC.xy, Depth, H_MATRIX_I_VP);
    float NormalBias = abs(dot(CornerPositionWS - RayOriginWS, NormalWS)) * 2.0f;

    // This can push the ray origin off-screen causing black pixels on the border
    RayOriginWS += NormalWS * NormalBias;
    
    float3 RayStartPositionNDC = H_COMPUTE_NDC_Z(RayOriginWS, H_MATRIX_VP).xyz;
    return RayStartPositionNDC;
}

float3 GetRayEndPositionNDC(float DepthLinear, float RayLength, float3 RayOriginWS, float3 RayDirectionWS, float3 RayStartPositionNDC)
{
    // Calculate clipped ray distance in world space
    float3 RayDirectionVS = H_TRANSFORM_WORLD_TO_VIEW_DIR(-RayDirectionWS, true);
    float RayClippedDistanceWS = RayDirectionVS.z < 0.0 ? min(-0.99f * DepthLinear / RayDirectionVS.z, RayLength) : RayLength;
        
    // Calculate ray end position in screen space
    float3 RayEndPositionNDC = H_COMPUTE_NDC_Z(RayOriginWS + RayDirectionWS * RayClippedDistanceWS, H_MATRIX_VP).xyz;

    // Recalculate ray end position where it leaves the screen
    float2 ScreenEdgeIntersections = LineBoxIntersect(RayStartPositionNDC, RayEndPositionNDC, 0, 1);
    RayEndPositionNDC = RayStartPositionNDC + (RayEndPositionNDC - RayStartPositionNDC) * ScreenEdgeIntersections.y;
    
    return RayEndPositionNDC;
}
