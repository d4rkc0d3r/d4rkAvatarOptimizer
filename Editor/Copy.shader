Shader "d4rkpl4y3r/TextureAnalyzer/Copy"
{
    Properties
    {
        _MainTex ("Main Tex", 2D) = "white" {}
        _MipLevel ("MipLevel", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Texture2D _MainTex;
            SamplerState linear_clamp_sampler;
            float _MipLevel;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.uv * 2 - 1, 1, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.vertex.y *= -1;
                #endif
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                if (_MipLevel == -1)
                    return _MainTex.Sample(linear_clamp_sampler, i.uv);
                else
                    return _MainTex.SampleLevel(linear_clamp_sampler, i.uv, _MipLevel);
            }
            ENDCG
        }
    }
}
