Shader "d4rkpl4y3r/Optimizer/DisabledMaterial"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "VRCFallback" = "Hidden" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode" = "Always" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                int2 i2 = i.uv * 32;
                return ((i2.x ^ i2.y) & 1) == 0 ? float4(1,0,1,1) : float4(0,0,0,1);
            }
            ENDCG
        }
    }
}
