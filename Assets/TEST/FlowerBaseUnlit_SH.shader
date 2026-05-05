Shader "Custom/FlowerBaseUnlit"
{
    Properties
    {
        _BaseMap ("Diffuse / Base Map", 2D) = "white" {}
        _AlphaMap ("Binary Alpha Mask", 2D) = "white" {}
        _BaseColor ("Base Color Tint", Color) = (1,1,1,1)
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="TransparentCutout"
            "Queue"="AlphaTest"
        }

        Pass
        {
            Name "UnlitAlphaClip"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_AlphaMap);
            SAMPLER(sampler_AlphaMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _AlphaCutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);

                half alphaMask = SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, IN.uv).r;

                clip(alphaMask - _AlphaCutoff);

                return half4(baseSample.rgb * _BaseColor.rgb, 1);
            }

            ENDHLSL
        }
    }
}