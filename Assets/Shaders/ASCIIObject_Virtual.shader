Shader "Custom/URP/ASCIIObject_Virtual"
{
    Properties
    {
        _CharTex("Character Atlas", 2D) = "white" {}
        _CharCount("Char Count", Float) = 8
        _FillCharCount("Fill Char Count", Float) = 4

        _CellPixelSizeX("Cell Pixel Size X", Float) = 10
        _CellPixelSizeY("Cell Pixel Size Y", Float) = 14

        _Tint("Tint", Color) = (0.85, 1.0, 0.9, 1)

        _GlowColor("Glow Color", Color) = (0.65, 1.0, 0.85, 1)
        _GlowStrength("Glow Strength", Range(0,4)) = 1.0

        _JitterStrength("Jitter Strength", Range(0,0.5)) = 0.08
        _FlickerStrength("Flicker Strength", Range(0,0.5)) = 0.03

        _EdgeThreshold("Edge Threshold", Range(0,1)) = 0.08
        _EdgeStrength("Edge Strength", Range(0,4)) = 1.2

        _AlphaClip("Alpha Clip", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
            };

            TEXTURE2D(_LitAsciiSourceTex);
            SAMPLER(sampler_LitAsciiSourceTex);

            TEXTURE2D(_CharTex);
            SAMPLER(sampler_CharTex);

            CBUFFER_START(UnityPerMaterial)
                float _CharCount;
                float _FillCharCount;

                float _CellPixelSizeX;
                float _CellPixelSizeY;

                float4 _Tint;

                float4 _GlowColor;
                float _GlowStrength;

                float _JitterStrength;
                float _FlickerStrength;

                float _EdgeThreshold;
                float _EdgeStrength;

                float _AlphaClip;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.screenPos = ComputeScreenPos(posInputs.positionCS);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Luma(half3 c)
            {
                return dot(c, float3(0.299, 0.587, 0.114));
            }

            float GetFillGlyphIndex(float gray, float fillCount)
            {
                fillCount = max(fillCount, 1.0);
                return round(saturate(gray) * (fillCount - 1.0));
            }

            // 将方向映射到边缘字符区间 [_FillCharCount, _CharCount - 1]
            float GetEdgeGlyphIndexFlexible(float2 grad, float edgeStart, float charCount)
            {
                float edgeCount = max(charCount - edgeStart, 1.0);

                if (edgeCount <= 1.0)
                    return edgeStart;

                float2 d = normalize(grad + 1e-6);
                float ax = abs(d.x);
                float ay = abs(d.y);

                float dir01 = 0.0;

                // 4 类方向：
                // 0 = '-'
                // 1 = '|'
                // 2 = '/'
                // 3 = '\'
                if (ax > ay * 1.5)
                {
                    dir01 = 1.0 / 3.0; // '|'
                }
                else if (ay > ax * 1.5)
                {
                    dir01 = 0.0;       // '-'
                }
                else
                {
                    dir01 = (d.x * d.y > 0.0) ? (2.0 / 3.0) : 1.0; // '/' or '\'
                }

                float localIndex = round(dir01 * (edgeCount - 1.0));
                return edgeStart + localIndex;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float2 screenPixel = screenUV * _ScaledScreenParams.xy;

                float2 cellSize = float2(
                    max(_CellPixelSizeX, 1.0),
                    max(_CellPixelSizeY, 1.0)
                );

                // 用“字符单元像素尺寸”来划分 tile
                float2 tileCoord = floor(screenPixel / cellSize);
                float2 localUV   = frac(screenPixel / cellSize);

                float2 tileCenterPixel = tileCoord * cellSize + cellSize * 0.5;
                float2 tileCenterUV = tileCenterPixel / _ScaledScreenParams.xy;

                // 邻域采样间隔也按 cellSize 来
                float2 texel = cellSize / _ScaledScreenParams.xy;

                half4 c = SAMPLE_TEXTURE2D(_LitAsciiSourceTex, sampler_LitAsciiSourceTex, tileCenterUV);
                half4 l = SAMPLE_TEXTURE2D(_LitAsciiSourceTex, sampler_LitAsciiSourceTex, tileCenterUV + float2(-texel.x, 0));
                half4 r = SAMPLE_TEXTURE2D(_LitAsciiSourceTex, sampler_LitAsciiSourceTex, tileCenterUV + float2( texel.x, 0));
                half4 u = SAMPLE_TEXTURE2D(_LitAsciiSourceTex, sampler_LitAsciiSourceTex, tileCenterUV + float2(0,  texel.y));
                half4 d = SAMPLE_TEXTURE2D(_LitAsciiSourceTex, sampler_LitAsciiSourceTex, tileCenterUV + float2(0, -texel.y));

                float gray = saturate(Luma(c.rgb));

                float gx = Luma(r.rgb) - Luma(l.rgb);
                float gy = Luma(u.rgb) - Luma(d.rgb);
                float2 grad = float2(gx, gy);
                float edgeMag = length(grad);

                float n = Hash21(tileCoord);
                float jitter = (n - 0.5) * _JitterStrength;
                float jitteredGray = saturate(gray + jitter);

                float fillCount = clamp(_FillCharCount, 1.0, _CharCount - 1.0);
                float edgeStart = fillCount;

                float fillIndex = GetFillGlyphIndex(jitteredGray, fillCount);
                float edgeIndex = GetEdgeGlyphIndexFlexible(grad, edgeStart, _CharCount);

                float useEdge = step(_EdgeThreshold, edgeMag);
                float charIndex = lerp(fillIndex, edgeIndex, useEdge);

                float2 charUV = float2(
                    (charIndex + localUV.x) / _CharCount,
                    1.0 - localUV.y
                );

                half charMask = SAMPLE_TEXTURE2D(_CharTex, sampler_CharTex, charUV).r;
                half glyph = step(_AlphaClip, charMask);

                clip(glyph - 0.01);

                half3 baseColor = c.rgb * _Tint.rgb;

                float flicker = 1.0 + (_FlickerStrength * sin(_Time.y * 18.0 + tileCoord.x * 0.37 + tileCoord.y * 0.21));

                float glowFactor = gray * glyph;
                float edgeGlow = useEdge * _EdgeStrength;

                half3 glow = _GlowColor.rgb * _GlowStrength * (glowFactor + edgeGlow * 0.35);
                half3 finalRGB = baseColor * flicker + glow;

                return half4(finalRGB, 1.0);
            }
            ENDHLSL
        }
    }
}