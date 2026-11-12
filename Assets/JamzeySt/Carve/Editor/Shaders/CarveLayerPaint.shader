// Editor-only: paints Carve Layered Lit layer masks. The mesh is drawn flattened into its mask UVs, and every texel
// measures its own world position against the brush, so the brush stays round across UV seams and stretching.
Shader "Hidden/Carve/LayerPaint"
{
    Properties
    {
        _Prev ("Previous", 2D) = "black" {}
        _Coverage ("Coverage", 2D) = "black" {}
    }

    HLSLINCLUDE
    #include "UnityCG.cginc"

    sampler2D _Prev;
    sampler2D _Coverage;
    float4 _Prev_TexelSize;
    float4x4 _PaintObjectToWorld;
    float4 _Brush;          // xyz center (world), w radius (m)
    float4 _BrushNormal;    // xyz surface normal at the brush, w = 1 to skip faces turned away
    float4 _Keep;           // per-channel: keep the previous value (layer k keeps the masks of the layers below it)
    float4 _Set;            // per-channel target value
    float _Strength;
    float _Hardness;
    float _Mode;            // 0 paint, 1 smooth
    float _UVChannel;
    float4 _MaskST;         // the material's mask tiling/offset (fits tiled UVs into the mask)
    float _FlipY;

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv0 : TEXCOORD0;
        float2 uv1 : TEXCOORD1;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 world : TEXCOORD1;
        float3 normal : TEXCOORD2;
    };

    v2f VertUV(appdata v)
    {
        v2f o;
        o.uv = (_UVChannel > 0.5 ? v.uv1 : v.uv0) * _MaskST.xy + _MaskST.zw;
        o.pos = float4(o.uv * 2.0 - 1.0, 0.5, 1.0);
        o.pos.y *= _FlipY;
        o.world = mul(_PaintObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
        o.normal = normalize(mul((float3x3)_PaintObjectToWorld, v.normal));
        return o;
    }

    float4 Smoothed(float2 uv)
    {
        float2 t = _Prev_TexelSize.xy * 2.0;
        float4 s = tex2Dlod(_Prev, float4(uv, 0, 0)) * 4.0;
        s += tex2Dlod(_Prev, float4(uv + float2(t.x, 0), 0, 0)) + tex2Dlod(_Prev, float4(uv - float2(t.x, 0), 0, 0));
        s += tex2Dlod(_Prev, float4(uv + float2(0, t.y), 0, 0)) + tex2Dlod(_Prev, float4(uv - float2(0, t.y), 0, 0));
        return s / 8.0;
    }

    float4 FragStamp(v2f i) : SV_Target
    {
        float4 prev = tex2Dlod(_Prev, float4(i.uv, 0, 0));
        float d = distance(i.world, _Brush.xyz);
        float fall = 1.0 - smoothstep(_Brush.w * min(_Hardness, 0.98), _Brush.w, d);
        if (_BrushNormal.w > 0.5)
            fall *= saturate(dot(normalize(i.normal), _BrushNormal.xyz) * 4.0 + 0.4);
        float w = saturate(fall * _Strength);
        clip(w - 1e-4);
        // Blended, not written: faces that share these mask texels (boxes, mirrored UVs) but sit outside the brush add nothing.
        float4 target = _Mode > 0.5 ? Smoothed(i.uv) : prev * _Keep + _Set;
        return float4(target.rgb, w);
    }

    float4 FragCoverage(v2f i) : SV_Target
    {
        return 1.0;
    }

    struct v2fq
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    v2fq VertQuad(appdata_img v)
    {
        v2fq o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    // Texels outside every UV island take the nearest painted island texel, so bilinear filtering and mips never pull in
    // unpainted gutter values (no seam lines).
    float4 FragDilate(v2fq i) : SV_Target
    {
        float4 c = tex2Dlod(_Prev, float4(i.uv, 0, 0));
        if (tex2Dlod(_Coverage, float4(i.uv, 0, 0)).r > 0.5)
            return c;
        float best = 1e9;
        float4 result = c;
        [loop] for (int y = -4; y <= 4; y++)
        {
            [loop] for (int x = -4; x <= 4; x++)
            {
                float2 uv = i.uv + float2(x, y) * _Prev_TexelSize.xy;
                float dd = x * x + y * y;
                if (dd < best && tex2Dlod(_Coverage, float4(uv, 0, 0)).r > 0.5)
                {
                    best = dd;
                    result = tex2Dlod(_Prev, float4(uv, 0, 0));
                }
            }
        }
        return result;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass // 0: brush stamp
        {
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma vertex VertUV
            #pragma fragment FragStamp
            ENDHLSL
        }

        Pass // 1: UV island coverage
        {
            HLSLPROGRAM
            #pragma vertex VertUV
            #pragma fragment FragCoverage
            ENDHLSL
        }

        Pass // 2: dilate into the gutters
        {
            HLSLPROGRAM
            #pragma vertex VertQuad
            #pragma fragment FragDilate
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass // 0: brush stamp
        {
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma vertex VertUV
            #pragma fragment FragStamp
            ENDHLSL
        }

        Pass // 1: UV island coverage
        {
            HLSLPROGRAM
            #pragma vertex VertUV
            #pragma fragment FragCoverage
            ENDHLSL
        }

        Pass // 2: dilate into the gutters
        {
            HLSLPROGRAM
            #pragma vertex VertQuad
            #pragma fragment FragDilate
            ENDHLSL
        }
    }
}
