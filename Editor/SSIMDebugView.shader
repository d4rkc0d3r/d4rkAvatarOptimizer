Shader "d4rkpl4y3r/TextureAnalyzer/SSIM Debug View"
{
    Properties
    {
        _Mip1SSIM("Mip 1 SSIM", 2D) = "black" {}
        _Mip2SSIM("Mip 2 SSIM", 2D) = "black" {}
        _FlipSSIM("Flip SSIM", 2D) = "black" {}
        _CoverageMask("Coverage Mask", 2D) = "white" {}
        _QualityThreshold("Quality Threshold", Range(0, 1)) = 0.9
        [ToggleUI] _ShowCoverageMaskAsColors("Show Coverage Mask As Colors", Float) = 0
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
                centroid float2 uv : TEXCOORD0;
            };

            Texture2D<float> _Mip1SSIM;
            float4 _Mip1SSIM_TexelSize;
            SamplerState sampler_Mip1SSIM;
            Texture2D<float> _Mip2SSIM;
            float4 _Mip2SSIM_TexelSize;
            Texture2D<float> _FlipSSIM;
            float4 _FlipSSIM_TexelSize;
            float _QualityThreshold;
            Texture2D<float> _CoverageMask;
            float4 _CoverageMask_TexelSize;
            SamplerState sampler_CoverageMask;
            float _ShowCoverageMaskAsColors;

            uint pcg_hash(uint seed)
			{
				uint state = seed * 747796405u + 2891336453u;
				uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
				return (word >> 22u) ^ word;
			}

            float4 RandomColorFromID(float id)
            {
                if (id == 0)
                    return float4(0, 0, 0, 1);
                uint hash = pcg_hash(asuint(id));
                float r = (hash & 0x3FF) / 1023.0;
                float g = (hash >> 10 & 0x3FF) / 1023.0;
                float b = (hash >> 20 & 0x3FF) / 1023.0;
                return float4(r, g, b, 1);
            }

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
                if (_ShowCoverageMaskAsColors > 0.5) {
                    float islandID = _CoverageMask.Sample(sampler_CoverageMask, i.uv).r;
                    return float4(GammaToLinearSpace(RandomColorFromID(islandID).rgb), 1);
                }
                if (_CoverageMask.Sample(sampler_CoverageMask, i.uv).r == 0)
                    return float4(0, 0, 0, 1);
                float ssim1 = _Mip1SSIM.Sample(sampler_Mip1SSIM, i.uv).r;
                float ssim2 = _Mip2SSIM.Sample(sampler_Mip1SSIM, i.uv).r;
                if (_QualityThreshold == 0) {
                    return 1 - float4(ssim1.xxx, ssim2);
                }
                float ssimFlip = _FlipSSIM.Sample(sampler_Mip1SSIM, i.uv).r;
                if (ssimFlip >= _QualityThreshold && i.uv.x > 0.5) {
                    return float4(0, 0, 1, 1);
                }
                if (ssim2 >= _QualityThreshold)
                    return float4(1, 0, 0, 1);
                else if (ssim1 >= _QualityThreshold)
                    return float4(1, 1, 0, 1);
                else if (ssim1 > 0 && ssim2 > 0)
                    return float4(0, 1, 0, 1);
                else
                    return float4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
