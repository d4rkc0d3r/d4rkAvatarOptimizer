Shader "d4rkpl4y3r/TextureAnalyzer/Write Coverage Mask"
{
    Properties
    {
        [ToggleUI] _WriteIslandID("Write Island ID", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Conservative True
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            float _WriteIslandID;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                nointerpolation float islandID : ISLAND_ID;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.uv.xy * 2 - 1, 1, 1);
                #if UNITY_UV_STARTS_AT_TOP
                    o.vertex.y *= -1;
                #endif
                o.islandID = v.uv.z;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                return _WriteIslandID > 0.5 ? i.islandID : 1;
            }
            ENDCG
        }
    }
}
