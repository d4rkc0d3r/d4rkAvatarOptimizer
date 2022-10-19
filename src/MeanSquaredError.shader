Shader "d4rkpl4y3r/TextureAnalyzer/MeanSquaredError"
{
    Properties
    {
        _Reference ("Reference Texture", 2D) = "white" {}
        _Target ("Target Texture", 2D) = "white" {}
        [ToggleUI] _sRGB ("sRGB", Float) = 1
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

            Texture2D _Reference;
            float4 _Reference_TexelSize;
            Texture2D _Target;
            float4 _Target_TexelSize;
            SamplerState linear_clamp_sampler;
            bool _sRGB;

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
                float4 reference = _Reference.SampleLevel(linear_clamp_sampler, i.uv, 0);
                float4 target = _Target.SampleLevel(linear_clamp_sampler, i.uv, 0);
                if (_sRGB)
                {
                    reference.r = LinearToGammaSpaceExact(reference.r);
                    reference.g = LinearToGammaSpaceExact(reference.g);
                    reference.b = LinearToGammaSpaceExact(reference.b);
                    target.r = LinearToGammaSpaceExact(target.r);
                    target.g = LinearToGammaSpaceExact(target.g);
                    target.b = LinearToGammaSpaceExact(target.b);
                }
                float4 error = reference - target;
                float4 mse = error * error;
                mse.rgb *= reference.a;
                return mse;
            }
            ENDCG
        }
    }
}
