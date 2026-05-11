Shader "Custom/FakeSun_HDR"
{
    Properties
    {
        [Header(Stencil)]
        [Enum(UnityEngine.Rendering.CompareFunction)] 
        _StencilComp("Stencil Comp", Float) = 3

        [Header(HDR Colors)]
        [HDR]_CoreColor("Core / Dark Center Color", Color) = (0, 0, 0, 1)
        [HDR]_BandColor("Dissolve Band Color", Color) = (1, 0.12, 0.02, 1)
        [HDR]_BodyColor("Body Color", Color) = (1, 0.45, 0.05, 1)

        [Header(Dissolve)]
        _Threshold("Noise Threshold", Range(0, 1)) = 0.45
        _BandWidth("Band Width", Range(0.001, 0.5)) = 0.08
        _CoreWidth("Core Width", Range(0.001, 0.5)) = 0.12
        _NoiseScale("Noise Scale", Float) = 8.0
        _NoiseSpeed("Noise Speed", Float) = 0.5

        [Header(Emission)]
        _EmissionIntensity("Emission Intensity", Float) = 2.5
        _Alpha("Alpha", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Stencil
        {
            Ref 1
            Comp [_StencilComp]
        }

        Pass
        {
            Name "FakeSunHDR"

            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _BandColor;
                float4 _BodyColor;

                float _Threshold;
                float _BandWidth;
                float _CoreWidth;
                float _NoiseScale;
                float _NoiseSpeed;

                float _EmissionIntensity;
                float _Alpha;
            CBUFFER_END

            // Simple hash / value noise
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(
                    lerp(a, b, u.x),
                    lerp(c, d, u.x),
                    u.y
                );
            }

            float FBM(float2 uv)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;

                for (int i = 0; i < 4; i++)
                {
                    value += ValueNoise(uv * frequency) * amplitude;
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }

                return value;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = posInputs.positionCS;

                output.uv = input.uv;
                output.positionOS = input.positionOS.xyz;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float time = _Time.y * _NoiseSpeed;

                // Animated noise UV.
                float2 noiseUV_A = input.uv * _NoiseScale + float2(time, time * 0.37);
                float2 noiseUV_B = input.uv * (_NoiseScale * 1.7) + float2(-time * 0.43, time * 0.21);

                float noiseA = FBM(noiseUV_A);
                float noiseB = FBM(noiseUV_B);

                float noise = saturate(noiseA * 0.7 + noiseB * 0.3);

                // Main dissolve band around threshold.
                float bandMask =
                    smoothstep(_Threshold - _BandWidth, _Threshold, noise) *
                    (1.0 - smoothstep(_Threshold, _Threshold + _BandWidth, noise));

                // Core/dark center region under threshold.
                float coreMask = 1.0 - smoothstep(
                    _Threshold - _CoreWidth,
                    _Threshold,
                    noise
                );

                // Body region above threshold.
                float bodyMask = smoothstep(
                    _Threshold,
                    _Threshold + _BandWidth,
                    noise
                );

                float3 color =
                    _BodyColor.rgb * bodyMask +
                    _BandColor.rgb * bandMask +
                    _CoreColor.rgb * coreMask;

                color *= _EmissionIntensity;

                float alpha = saturate(_Alpha * (bodyMask + bandMask + coreMask));

                return half4(color, alpha);
            }

            ENDHLSL
        }
    }
}