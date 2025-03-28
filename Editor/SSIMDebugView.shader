Shader "d4rkpl4y3r/TextureAnalyzer/SSIM Debug View"
{
    Properties
    {
        _Mip1SSIM("Mip 1 SSIM", 2D) = "black" {}
        _Mip2SSIM("Mip 2 SSIM", 2D) = "black" {}
        _QualityThreshold("Quality Threshold", Range(0, 1)) = 0.9
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

            Texture2D<float> _Mip1SSIM;
            float4 _Mip1SSIM_TexelSize;
            SamplerState sampler_Mip1SSIM;
            Texture2D<float> _Mip2SSIM;
            float4 _Mip2SSIM_TexelSize;
            float _QualityThreshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 AdjustColorSpace(float4 color)
            {
                float ssim = color.r;
                if (_QualityThreshold == 0)
                    return float4(ssim, ssim, ssim, 1);
                else if (ssim >= _QualityThreshold)
                    return float4(0, 1, 0, 1);
                else
                    return float4(1, 0, 0, 1);
                return float4(1 - color.rrr, 1);
            }

            float4 Sample(Texture2D tex, float2 uv)
            {
                return AdjustColorSpace(tex.Sample(sampler_Mip1SSIM, uv));
            }

            float4 frag (v2f i) : SV_Target
            {
                float ssim1 = _Mip1SSIM.Sample(sampler_Mip1SSIM, i.uv).r;
                float ssim2 = _Mip2SSIM.Sample(sampler_Mip1SSIM, i.uv).r;
                if (_QualityThreshold == 0) {
                    return 1 - float4(ssim1.xxx, ssim2);
                }
                if (ssim2 >= _QualityThreshold)
                    return float4(1, 0, 0, 1);
                else if (ssim1 >= _QualityThreshold)
                    return float4(1, 1, 0, 1);
                else
                    return float4(0, 1, 0, 1);
            }
            ENDCG
        }
    }
}
