Shader "Hidden/Debug/ProjectionRTPreview"
{
    Properties
    {
        _MainTex("Source", 2D) = "black" {}
        _Mode("Mode", Float) = 0
        _MaskColor("Mask Color", Color) = (0,1,0,1)
        _ZeroColor("Zero Color", Color) = (0,0,0,1)
        _NonZeroTint("Non Zero Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Overlay"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "Preview"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                float4 _MaskColor;
                float4 _ZeroColor;
                float4 _NonZeroTint;
            CBUFFER_END

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float3 HashColorFromId(float idValue)
            {
                uint x = (uint)round(max(idValue, 0.0));

                x ^= x >> 17;
                x *= 0xed5ad4bbU;
                x ^= x >> 11;
                x *= 0xac4c1b51U;
                x ^= x >> 15;
                x *= 0x31848babU;
                x ^= x >> 14;

                float hue = (x % 360u) / 360.0;
                float sat = 0.75;
                float val = 1.0;

                float3 rgb = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                rgb = rgb * rgb * (3.0 - 2.0 * rgb);

                float3 hsvColor = lerp(float3(1.0, 1.0, 1.0), rgb, sat) * val;
                return hsvColor;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float raw = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;

                // Mode 0 = mask
                if (_Mode < 0.5)
                {
                    return raw > 0.5
                        ? half4(_MaskColor.rgb, 1)
                        : half4(_ZeroColor.rgb, 1);
                }

                // Mode 1 = id pseudo color
                if (raw < 0.5)
                    return half4(_ZeroColor.rgb, 1);

                float3 idColor = HashColorFromId(raw);
                idColor *= _NonZeroTint.rgb;

                return half4(idColor, 1);
            }
            ENDHLSL
        }
    }
}