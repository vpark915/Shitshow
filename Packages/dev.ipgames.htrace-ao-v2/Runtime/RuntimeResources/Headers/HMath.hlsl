#ifndef H_MATH
#define H_MATH

#define FLT_MIN  1.175494351e-38
#define FLT_MAX  3.402823466e+38

#define HFastSqrt(x) asfloat(0x1FBD1DF5 + (asint(x) >> 1))

float3 HSafeNormalize(float3 inVec)
{
    float dp3 = max(FLT_MIN, dot(inVec, inVec));
    return inVec * rsqrt(dp3);
}

float HSinFromCos(float cosX)
{
    return sqrt(saturate(1 - cosX * cosX));
}

float HFastACos(float Input)
{	
    float X = abs(Input); 
    float Result = -0.156583 * X + H_PI_HALF;
    Result *= HFastSqrt(1.0 - X);
    return Input >= 0 ? Result : H_PI - Result;
}

float2 HFastACos(float2 Input)                      
{
    float2 X = abs(Input); 
    float2 Result = -0.156583 * X + H_PI_HALF;
    Result *= HFastSqrt(1.0 - X);              
    return Input >= 0 ? Result : H_PI - Result;                   
}

float4 HFastACos(float4 Input)                      
{
    float4 X = abs(Input); 
    float4 Result = -0.156583 * X + H_PI_HALF;
    Result *= HFastSqrt(1.0 - X);              
    return Input >= 0 ? Result : H_PI - Result;                   
}

float3x3 HGetLocalFrame(float3 localZ)
{
    float x  = localZ.x;
    float y  = localZ.y;
    float z  = localZ.z;
    

    float sz = FastSign(z);
    float a  = 1 / (sz + z);
    float ya = y * a;
    float b  = x * ya;
    float c  = x * sz;

    float3 localX = float3(c * x * a - 1, sz * b, c);
    float3 localY = float3(b, y * ya - sz, y);

    return float3x3(localX, localY, localZ);
}

float2 HSampleDiskCubic(float u1, float u2)
{
    float r   = u1;
    float phi = 6.28318530718f * u2;

    float sinPhi, cosPhi;
    sincos(phi, sinPhi, cosPhi);

    return r * float2(cosPhi, sinPhi);
}

float3 HSphericalToCartesian(float cosPhi, float sinPhi, float cosTheta)
{
    float sinTheta = HSinFromCos(cosTheta);
    return float3(float2(cosPhi, sinPhi) * sinTheta, cosTheta);
}

float3 HSphericalToCartesian(float phi, float cosTheta)
{
    float sinPhi, cosPhi;
    sincos(phi, sinPhi, cosPhi);
    return HSphericalToCartesian(cosPhi, sinPhi, cosTheta);
}

float3 HSampleSphereUniform(float u1, float u2)
{
    float phi = 6.28318530718f * u2;
    float cosTheta = 1.0 - 2.0 * u1;
    return HSphericalToCartesian(phi, cosTheta);
}

float3 HSampleHemisphereCosine(float u1, float u2, float3 normal)
{
    float3 pointOnSphere = HSampleSphereUniform(u1, u2);
    return HSafeNormalize(normal + pointOnSphere);
}
#endif
