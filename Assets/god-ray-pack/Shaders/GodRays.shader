// VolumetricLightScattering's passes, as render-graph blits (Blit.hlsl's full-screen triangle, _BlitTexture in).
// Replaces the pack's UnlitColor / RadialBlur / GaussianBlur, which were built for the old Graphics.Blit path.
Shader "Hidden/Volumetric/GodRays"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        float4 _Center; // the sun on screen, 0..1
        float _Intensity, _BlurWidth, _SunRadius;
        ENDHLSL

        Pass // 0: the sky (wherever nothing was drawn) round the sun; everything else black
        {
            Name "Sky"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float depth = SampleSceneDepth(i.texcoord);
                #if UNITY_REVERSED_Z
                bool sky = depth <= 1e-5;
                #else
                bool sky = depth >= 1 - 1e-5;
                #endif
                float2 off = (i.texcoord - _Center.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1);
                float near = saturate(1 - length(off) / _SunRadius);
                half3 c = min(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.texcoord).rgb, 8); // the sun disc is huge in HDR
                return half4(sky ? c * near : 0, 1);
            }
            ENDHLSL
        }

        Pass // 1: smeared out from the sun (the pack's radial blur)
        {
            Name "Smear"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #define SAMPLES 64
            half4 Frag(Varyings i) : SV_Target
            {
                float2 ray = i.texcoord - _Center.xy;
                half3 c = 0;
                for (int s = 0; s < SAMPLES; s++)
                    c += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, ray * (1 - _BlurWidth * s / (SAMPLES - 1.0)) + _Center.xy, 0).rgb;
                return half4(c / SAMPLES, 1);
            }
            ENDHLSL
        }

        Pass // 2: added onto the picture
        {
            Name "Add"
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                return half4(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.texcoord).rgb * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
