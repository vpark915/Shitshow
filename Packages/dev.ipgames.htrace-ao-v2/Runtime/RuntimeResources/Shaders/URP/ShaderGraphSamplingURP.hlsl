TEXTURE2D(_HTraceBufferAO);
SAMPLER(_HTraceBufferAO_Sampler_LinearClamp);

void SampleHTraceAO_float(float2 ScreenCoord, out float HTraceAO)
{
    HTraceAO = SAMPLE_TEXTURE2D(_HTraceBufferAO, _HTraceBufferAO_Sampler_LinearClamp, ScreenCoord).x;
}
