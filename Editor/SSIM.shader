Shader "d4rkpl4y3r/TextureAnalyzer/SSIM"
{
    Properties
    {
        _Reference ("Reference Texture", 2D) = "white" {}
        _Target ("Target Texture", 2D) = "white" {}
        [ToggleUI] _sRGB ("sRGB", Float) = 1
        [ToggleUI] _NormalMap ("Normal Map", Float) = 0
        [IntRange] _KernelSize ("Kernel Size", Range(2, 11)) = 8
        [IntRange] _TargetMipBias ("Target Mip Bias", Range(0, 2)) = 0
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
            bool _NormalMap;
            float _KernelSize;
            float _TargetMipBias;

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

            float4 SampleGrad(Texture2D tex, float2 uv, float2 dx, float2 dy)
            {
                return AdjustColorSpace(tex.SampleGrad(linear_clamp_sampler, uv, dx, dy));
            }

            float frag (v2f i) : SV_Target
            {
                _KernelSize = clamp(_KernelSize, 2, 11);
                float2 uv = i.uv;
                float2 dx = ddx(uv);
                float2 dy = ddy(uv);
                float2 pixelSize = 1 / _ScreenParams.xy;
                float4 meanA = 0;
                float4 meanB = 0;
                float2 offset = 0;
                for (offset.y = 0; offset.y < _KernelSize; offset.y++)
                for (offset.x = 0; offset.x < _KernelSize; offset.x++)
                {
                    float2 sampleUV = uv + (offset - floor(_KernelSize / 2)) * pixelSize;
                    meanA += SampleGrad(_Reference, sampleUV, dx, dy);
                    meanB += SampleGrad(_Target, sampleUV, dx * exp2(_TargetMipBias), dy * exp2(_TargetMipBias));
                }
                meanA /= _KernelSize * _KernelSize;
                meanB /= _KernelSize * _KernelSize;
                float4 varA = 0;
                float4 varB = 0;
                float4 covAB = 0;
                for (offset.y = 0; offset.y < _KernelSize; offset.y++)
                for (offset.x = 0; offset.x < _KernelSize; offset.x++)
                {
                    float2 sampleUV = uv + (offset - floor(_KernelSize / 2)) * pixelSize;
                    float4 a = SampleGrad(_Reference, sampleUV, dx, dy);
                    float4 b = SampleGrad(_Target, sampleUV, dx * exp2(_TargetMipBias), dy * exp2(_TargetMipBias));
                    varA += (a - meanA) * (a - meanA);
                    varB += (b - meanB) * (b - meanB);
                    covAB += (a - meanA) * (b - meanB);
                }
                varA /= _KernelSize * _KernelSize - 1;
                varB /= _KernelSize * _KernelSize - 1;
                covAB /= _KernelSize * _KernelSize - 1;
                float c1 = 0.01 * 0.01;
                float c2 = 0.03 * 0.03;
                float4 ssim = (2 * meanA * meanB + c1) * (2 * covAB + c2) / ((meanA * meanA + meanB * meanB + c1) * (varA + varB + c2));
                return min(min(ssim.r, ssim.g), min(ssim.b, ssim.a));
            }
            ENDCG
        }
    }
}
