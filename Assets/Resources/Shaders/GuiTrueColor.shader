// Draws a picture in an OnGUI (Loading) at its true colours, through Graphics.DrawTexture with this plain material. In
// this project (linear colour space) GUI.DrawTexture comes out brightened twice: the art's purple head (78, 69, 128)
// showed as (150, 142, 188). This way it's exact. _Alpha fades it.
Shader "Hidden/GuiTrueColor"
{
    Properties { _MainTex ("Picture", 2D) = "white" {} _Alpha ("Alpha", Float) = 1 }
    SubShader
    {
        Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Alpha;
            fixed4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                c.a *= _Alpha;
                return c;
            }
            ENDCG
        }
    }
}
