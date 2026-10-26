Shader "Hidden/Universal Render Pipeline/GroundTruthAmbientOcclusion"
{
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Shitshow: procedural fullscreen triangle (render graph has no fullscreenMesh); far-plane z keeps pass 4's ZTest NotEqual off the sky
        struct Attributes
        {
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4  positionCS  : SV_POSITION;
            float2  uv          : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings VertDefault(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
            // Add a small epsilon to avoid artifacts when reconstructing the normals
            output.uv = GetFullScreenTriangleTexCoord(input.vertexID) + 1.0e-6;
            return output;
        }

    ENDHLSL

    SubShader
    {
        Tags{ "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        Cull Off ZWrite Off ZTest Always

        // ------------------------------------------------------------------
        // Depth only passes
        // ------------------------------------------------------------------

        // 0 - Occlusion estimation with CameraDepthTexture
        Pass
        {
            Name "SSAO_Occlusion"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment SSAO
                #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
                #pragma multi_compile_local _SOURCE_DEPTH _SOURCE_DEPTH_NORMALS
                #pragma multi_compile_local _RECONSTRUCT_NORMAL_LOW _RECONSTRUCT_NORMAL_MEDIUM _RECONSTRUCT_NORMAL_HIGH
                #pragma multi_compile_local _ _ORTHOGRAPHIC
                #include "GTAO.hlsl"
            ENDHLSL
        }

        // 1 - Horizontal Blur
        Pass
        {
            Name "SSAO_HorizontalBlur"

            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment HorizontalBlur
                #define BLUR_SAMPLE_CENTER_NORMAL
                #pragma multi_compile_local _ _ORTHOGRAPHIC
                #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
                #pragma multi_compile_local _SOURCE_DEPTH _SOURCE_DEPTH_NORMALS
                #include "GTAO.hlsl"
            ENDHLSL
        }

        // 2 - Vertical Blur
        Pass
        {
            Name "SSAO_VerticalBlur"

            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment VerticalBlur
                #include "GTAO.hlsl"
            ENDHLSL
        }

        // 3 - Final Blur
        Pass
        {
            Name "SSAO_FinalBlur"

            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FinalBlur
                #include "GTAO.hlsl"
            ENDHLSL
        }

        // 4 - After Opaque
        Pass
        {
            Name "SSAO_AfterOpaque"

            ZTest NotEqual
            ZWrite Off
            Cull Off
            Blend One SrcAlpha, Zero One
            BlendOp Add, Add

            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragAfterOpaque
                #define _SCREEN_SPACE_OCCLUSION

                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

                half4 FragAfterOpaque(Varyings input) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(input.uv);
                    half occlusion = aoFactor.indirectAmbientOcclusion;
                    return half4(0.0, 0.0, 0.0, occlusion);
                }

            ENDHLSL
        }
    }
}
