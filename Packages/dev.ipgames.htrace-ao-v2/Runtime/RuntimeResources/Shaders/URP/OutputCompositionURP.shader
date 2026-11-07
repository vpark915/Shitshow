Shader "Hidden/HTraceAO/OutputCompositionURP"
{
    SubShader
    {
        HLSLINCLUDE
        
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
        
        struct Attributes
        {
            uint VertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 PositionCS : POSITION;
            float2 TexCoord   : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        
        Varyings SharedVertexStage(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            
            output.PositionCS = GetFullScreenTriangleVertexPosition(input.VertexID);
            output.TexCoord = GetFullScreenTriangleTexCoord(input.VertexID);

            return output;
        }
        
        ENDHLSL

        Pass
        {
            Name "Output Composition"
                
            ZTest Off
            ZWrite Off
            Cull Off
            
            Blend One SrcAlpha, Zero One
            BlendOp Add, Add

            HLSLPROGRAM

            #pragma vertex SharedVertexStage
            #pragma fragment FragmentStage
            
            #include "../../Headers/HCommonAO.hlsl"

            float _Fadeout;
            float _ExcludedIntensity;
            uint _ExcludeReceivingMask;

            H_TEXTURE(_Occlusion_Output);
            
            float4 FragmentStage(Varyings input) : SV_Target
            {
                uint2 pixCoord = input.TexCoord * _ScreenSize.xy;

                float Depth = HBUFFER_DEPTH(pixCoord);
                 float DepthLinear = H_LINEAR_EYE_DEPTH(Depth);
          
                float Occlusion = H_LOAD(_Occlusion_Output, pixCoord.xy).x;
                Occlusion = pow(abs(Occlusion), g_HIntensityAO);

                if (_Fadeout > 0)
                {
                    float FadeoutStart = _Fadeout * 0.75;
                    float FadeoutFactor = smoothstep(FadeoutStart, _Fadeout, DepthLinear);
                    Occlusion = lerp(Occlusion, 1.0, FadeoutFactor);  
                }

                if (HBUFFER_RENDER_LAYER_MASK(pixCoord.xy) & _ExcludeReceivingMask)
                {
                    Occlusion = lerp(1.0f, Occlusion, _ExcludedIntensity);
                }
                
                return float4(0,0,0,Occlusion);
            }

            ENDHLSL
        }
    }
}
