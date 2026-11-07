Shader "Hidden/HTraceAO/ObjectMotionDeltaSRP"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

         Pass
        {
            Name "ObjectMotionDelta"
            Tags{ "LightMode" = "MotionVectors" } // Caution, this need to be call like this to setup the correct parameters by C++ (legacy Unity)
            
            ZWrite Off
            ZTest Equal
            Cull[_CullMode]

            HLSLPROGRAM
            
            #pragma vertex VertexStage
            #pragma fragment FragmentStage

            // These are required to work with GPU Resident Drawer
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            
            #include  "../Headers/HMain.hlsl"
           // #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
           // #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
           // #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        
            struct VertexInput
            {
                float3 PositionOS                : POSITION;
                float3 PositionSkinnedPreviousOS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
   
            struct VertexToFragment
            {
                float4 PositionCS               : SV_POSITION;
                float2 CurrentPreviousViewZ     : TEXCOORD0;
                float3 PositionCurrentWS        : TEXCOORD1;
                float3 PositionPreviousWS       : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            

            VertexToFragment VertexStage(VertexInput Input)
            {
                VertexToFragment Output;
                UNITY_SETUP_INSTANCE_ID(Input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(Output);

                // Get Object Space positions
                float3 PositionCurrentOS = Input.PositionOS;
                float3 PositionPreviousOS = unity_MotionVectorsParams.x ? Input.PositionSkinnedPreviousOS : Input.PositionOS;

                // Convert to World Space positions
                Output.PositionCurrentWS = mul(UNITY_MATRIX_M, float4(PositionCurrentOS, 1.0)).xyz;
                Output.PositionPreviousWS =  mul(UNITY_PREV_MATRIX_M, float4(PositionPreviousOS, 1.0)).xyz;
                
                // Convert to Clip Space position
                Output.PositionCS = mul(UNITY_MATRIX_VP, float4(Output.PositionCurrentWS, 1.0)).xyzw;
           
                // Convert to View Space positions
                float3 PositionCurrentVS = mul(UNITY_MATRIX_V, float4(Output.PositionCurrentWS, 1.0)).xyz;
                float3 PositionPreviousVS = mul(UNITY_MATRIX_V, float4(Output.PositionPreviousWS, 1.0)).xyz;

                // Pass ViewZs to the fragment stage
                Output.CurrentPreviousViewZ.x = -PositionCurrentVS.z;
                Output.CurrentPreviousViewZ.y = -PositionPreviousVS.z;
                
                return Output;
            }

            float4 FragmentStage(VertexToFragment Input) : SV_Target0
            {
                float NormalVisibilityDisocclusion = 1;
                
                float3 ViewDirection = normalize(_WorldSpaceCameraPos - GetAbsolutePositionWS(Input.PositionCurrentWS));

                // Calculate geometry (flat) normals
                float3 NormalCurrent = -normalize(cross(ddx(Input.PositionCurrentWS), ddy(Input.PositionCurrentWS)));
                float3 NormalPrevious = -normalize(cross(ddx(Input.PositionPreviousWS), ddy(Input.PositionPreviousWS)));
               
                float AngleDivergence = (dot(ViewDirection, NormalCurrent) - dot(ViewDirection, NormalPrevious));
                
                if (dot(ViewDirection, NormalPrevious) <= 0.05 && AngleDivergence >= 0.0001)
                    NormalVisibilityDisocclusion = -10000;

                float SkinnedMeshSign = unity_MotionVectorsParams.x ? -1 : 1;

                // We add small epsilon to the PositionDelta to not confuse it with pixels where this shader wasn't able to write data at all 
                float PositionDelta = (length(Input.PositionCurrentWS - Input.PositionPreviousWS) + 0.0001) * SkinnedMeshSign;
                float ViewDepthDelta = (Input.CurrentPreviousViewZ.y - Input.CurrentPreviousViewZ.x) * NormalVisibilityDisocclusion;
                
                return float4(ViewDepthDelta, PositionDelta, PackNormalOctQuadEncode(Input.PositionCurrentWS - Input.PositionPreviousWS) * 0.5 + 0.5);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
