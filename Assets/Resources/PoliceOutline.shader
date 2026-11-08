// Officers' outline: the mesh again, pushed out along its normals and drawn back faces only, so a thin rim shows round the body.
Shader "Shitshow/Police Outline"
{
    Properties
    {
        _Color("Color", Color) = (1, 0.1, 0.1, 0.3)
        _Width("Width (metres)", Float) = 0.006
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 p = TransformObjectToWorld(v.positionOS.xyz) + normalize(TransformObjectToWorldNormal(v.normalOS)) * _Width;
                return TransformWorldToHClip(p);
            }
            half4 frag() : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
