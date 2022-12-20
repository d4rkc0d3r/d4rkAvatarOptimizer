Shader "d4rkpl4y3r/TextureAnalyzer/MeanSquaredError"
{
    Properties
    {
        _Reference ("Reference Texture", 2D) = "white" {}
        _Target ("Target Texture", 2D) = "white" {}
        [ToggleUI] _sRGB ("sRGB", Float) = 1
        [ToggleUI] _Derivative ("Derivative", Float) = 0
        [ToggleUI] _NormalMap ("Normal Map", Float) = 0
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
            Texture2D _Target;
            SamplerState linear_clamp_sampler;
            bool _sRGB;
            bool _Derivative;
            bool _NormalMap;

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

            float4 AdjustColorSpace(float4 color)
            {
                if (_NormalMap)
                {
                    return float4(UnpackNormal(color), 1);
                }
                float4 srgb;
                srgb.r = LinearToGammaSpaceExact(color.r);
                srgb.g = LinearToGammaSpaceExact(color.g);
                srgb.b = LinearToGammaSpaceExact(color.b);
                srgb.a = color.a;
                return _sRGB ? srgb : color;
            }

            float4 Sample(Texture2D tex, float2 uv)
            {
                float2 texSize;
                tex.GetDimensions(texSize.x, texSize.y);
                float scale = max(texSize.x, texSize.y) / max(_ScreenParams.x, _ScreenParams.y);
                float texMip = round(log2(scale));
                return AdjustColorSpace(tex.SampleLevel(linear_clamp_sampler, uv, texMip));
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 reference = Sample(_Reference, i.uv);
                float4 target = Sample(_Target, i.uv);
                float4 error = reference - target;
                float4 mse = error * error;
                mse.rgb *= reference.a;
                if (_Derivative)
                {
                    float4 refX = Sample(_Reference, i.uv + ddx(i.uv));
                    float4 refY = Sample(_Reference, i.uv + ddy(i.uv));
                    float4 tarX = Sample(_Target, i.uv + ddx(i.uv));
                    float4 tarY = Sample(_Target, i.uv + ddy(i.uv));
                    float4 refdX = refX - reference;
                    float4 refdY = refY - reference;
                    float4 tardX = tarX - target;
                    float4 tardY = tarY - target;
                    float4 errorX = refdX - tardX;
                    float4 errorY = refdY - tardY;
                    float4 mseX = errorX * errorX;
                    float4 mseY = errorY * errorY;
                    mseX.rgb *= reference.a;
                    mseY.rgb *= reference.a;
                    return mseX * 0.5 + mseY * 0.5;
                }
                return mse;
            }
            ENDCG
        }
    }
}
