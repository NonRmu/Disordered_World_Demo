Shader "Custom/URP/ASCIIObject_Projection_Opaque"
{
    Properties
    {
        _CharTex("Character Atlas", 2D) = "white" {}
        _CharCount("Char Count", Float) = 12
        _FillCharCount("Fill Char Count", Float) = 8

        _CellPixelSizeX("Cell Pixel Size X", Float) = 10
        _CellPixelSizeY("Cell Pixel Size Y", Float) = 14

        _Tint("Tint", Color) = (0.85, 1.0, 0.9, 1)
        _BackgroundColor("Background Color", Color) = (0.0, 0.0, 0.0, 1.0)

        _GlowColor("Glow Color", Color) = (0.65, 1.0, 0.85, 1)
        _GlowStrength("Glow Strength", Range(0,4)) = 1.0

        _JitterStrength("Jitter Strength", Range(0,0.5)) = 0.05
        _EdgeStrength("Edge Strength", Range(0,4)) = 1.2

        _ContourMaxSteps("Contour Max Steps", Float) = 6
        _ContourStepPixels("Contour Step Pixels", Float) = 1

        _RandomizeFill("Randomize Fill", Float) = 0
        _RandomFillStrength("Random Fill Strength", Range(0,1)) = 0.35

        _AlphaClip("Alpha Clip", Range(0,1)) = 0.35

        [Enum(Off,0,On,1)] _UseAsciiMask("Use Ascii Mask", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [Enum(Off,0,On,1)] _ZWrite("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
            "RenderType"="Opaque"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend Off

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

            TEXTURE2D(_AsciiMaskTex);
            SAMPLER(sampler_AsciiMaskTex);

            TEXTURE2D(_CharTex);
            SAMPLER(sampler_CharTex);

            CBUFFER_START(UnityPerMaterial)
                float _CharCount;
                float _FillCharCount;

                float _CellPixelSizeX;
                float _CellPixelSizeY;

                float4 _Tint;
                float4 _BackgroundColor;

                float4 _GlowColor;
                float _GlowStrength;

                float _JitterStrength;
                float _EdgeStrength;

                float _ContourMaxSteps;
                float _ContourStepPixels;

                float _RandomizeFill;
                float _RandomFillStrength;

                float _AlphaClip;
                float _UseAsciiMask;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = posInputs.positionCS;
                o.screenPos = ComputeScreenPos(posInputs.positionCS);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(_AsciiMaskTex, sampler_AsciiMaskTex, uv, 0).r;
            }

            float GetFillGlyphIndex(float density01, float fillCount)
            {
                fillCount = max(fillCount, 1.0);
                return round(saturate(density01) * (fillCount - 1.0));
            }

            float GetMaskEdge(float2 centerUV, float2 texel)
            {
                float m  = SampleMask(centerUV);
                float ml = SampleMask(centerUV + float2(-texel.x, 0));
                float mr = SampleMask(centerUV + float2( texel.x, 0));
                float mu = SampleMask(centerUV + float2(0,  texel.y));
                float md = SampleMask(centerUV + float2(0, -texel.y));
                return saturate(abs(m - ml) + abs(m - mr) + abs(m - mu) + abs(m - md));
            }

            float ApproxDistanceToContour(float2 centerUV, float2 texel, float maxSteps, float stepPixels)
            {
                float centerMask = SampleMask(centerUV);
                if (centerMask < 0.5)
                    return maxSteps;

                [loop]
                for (int s = 1; s <= 16; s++)
                {
                    if (s > (int)maxSteps)
                        break;

                    float stepMul = s * stepPixels;
                    float2 dx = float2(texel.x * stepMul, 0);
                    float2 dy = float2(0, texel.y * stepMul);

                    float mL = SampleMask(centerUV - dx);
                    float mR = SampleMask(centerUV + dx);
                    float mU = SampleMask(centerUV + dy);
                    float mD = SampleMask(centerUV - dy);

                    if (mL < 0.5 || mR < 0.5 || mU < 0.5 || mD < 0.5)
                        return s;
                }

                return maxSteps;
            }

            float GetContourEdgeGlyphIndex(float2 centerUV, float2 texel, float edgeStart, float charCount)
            {
                float edgeCount = max(charCount - edgeStart, 1.0);
                if (edgeCount <= 1.0)
                    return edgeStart;

                float ml = SampleMask(centerUV + float2(-texel.x, 0));
                float mr = SampleMask(centerUV + float2( texel.x, 0));
                float mu = SampleMask(centerUV + float2(0,  texel.y));
                float md = SampleMask(centerUV + float2(0, -texel.y));

                float gx = mr - ml;
                float gy = mu - md;

                float2 d = normalize(float2(gx, gy) + 1e-6);
                float ax = abs(d.x);
                float ay = abs(d.y);

                float dir01 = 0.0;
                if (ax > ay * 1.5) dir01 = 1.0 / 3.0;
                else if (ay > ax * 1.5) dir01 = 0.0;
                else dir01 = (d.x * d.y > 0.0) ? (2.0 / 3.0) : 1.0;

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

                float2 tileCoord = floor(screenPixel / cellSize);
                float2 localUV = frac(screenPixel / cellSize);

                float2 tileCenterPixel = tileCoord * cellSize + cellSize * 0.5;
                float2 tileCenterUV = tileCenterPixel / _ScaledScreenParams.xy;
                float2 texel = cellSize / _ScaledScreenParams.xy;

                float maskCenter = 1.0;
                if (_UseAsciiMask > 0.5)
                {
                    maskCenter = SampleMask(tileCenterUV);
                    if (maskCenter < 0.5)
                        discard;
                }

                float fillCount = clamp(_FillCharCount, 1.0, _CharCount - 1.0);
                float edgeStart = fillCount;

                float contourEdge = GetMaskEdge(tileCenterUV, texel);
                float useEdgeGlyph = step(0.1, contourEdge);

                float approxDist = ApproxDistanceToContour(
                    tileCenterUV,
                    texel,
                    max(_ContourMaxSteps, 1.0),
                    max(_ContourStepPixels, 1.0)
                );

                float t = saturate((approxDist - 1.0) / max(_ContourMaxSteps - 1.0, 1.0));
                float density01 = 1.0 - t;

                float n = Hash21(tileCoord);
                float jitter = (n - 0.5) * _JitterStrength;
                float jitteredDensity = saturate(density01 + jitter);

                float fillIndex = GetFillGlyphIndex(jitteredDensity, fillCount);

                if (_RandomizeFill > 0.5)
                {
                    float randomOffset01 = (n - 0.5) * 2.0;
                    float randomOffset = randomOffset01 * _RandomFillStrength * max(fillCount - 1.0, 1.0);
                    fillIndex = round(clamp(fillIndex + randomOffset, 0.0, fillCount - 1.0));
                }

                float edgeIndex = GetContourEdgeGlyphIndex(tileCenterUV, texel, edgeStart, _CharCount);
                float charIndex = lerp(fillIndex, edgeIndex, useEdgeGlyph);

                float2 charUV = float2(
                    (charIndex + localUV.x) / _CharCount,
                    1.0 - localUV.y
                );

                charUV.x = frac(charUV.x);
                charUV.y = frac(charUV.y);

                half charMask = SAMPLE_TEXTURE2D(_CharTex, sampler_CharTex, charUV).r;
                half glyphMask = step(_AlphaClip, charMask);

                half3 baseColor = lerp(_BackgroundColor.rgb, _Tint.rgb, glyphMask);

                float edgeGlow = useEdgeGlyph * _EdgeStrength;
                float innerGlow = density01 * 0.35;
                half3 glow = _GlowColor.rgb * _GlowStrength * (edgeGlow * 0.35 + innerGlow) * glyphMask;

                half3 finalRGB = baseColor + glow;
                return half4(finalRGB, 1.0);
            }
            ENDHLSL
        }
    }
}