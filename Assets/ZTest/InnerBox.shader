Shader "Custom/InnerBox"
{
    Properties
    {
        [Header(Stencil)]
        _StencilRef("Stencil Ref", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)]
        _StencilComp("Stencil Comp", Float) = 3

        [Header(Colors)]
        _BackgroundColor("Background Color", Color) = (0.01, 0.015, 0.03, 1)
        [HDR]_GridColor("Grid Color", Color) = (0.1, 0.8, 1.0, 1)
        [HDR]_MajorGridColor("Major Grid Color", Color) = (0.6, 1.0, 1.0, 1)

        [Header(Grid)]
        _GridScale("Grid Scale", Float) = 1.0
        _LineWidth("Line Width", Range(0.001, 0.2)) = 0.03
        _MajorGridEvery("Major Grid Every", Float) = 5.0
        _MajorLineWidth("Major Line Width", Range(0.001, 0.3)) = 0.06
        _GridHeight("Grid Plane Height", Float) = 0.0

        [Header(Fade)]
        _DistanceFade("Distance Fade", Float) = 0.03
        _HorizonFade("Horizon Fade", Range(0.001, 1.0)) = 0.08

        [Header(Animation)]
        _ScrollSpeedX("Scroll Speed X", Float) = 0.0
        _ScrollSpeedZ("Scroll Speed Z", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry+10"
            "RenderType"="Opaque"
        }

        Pass
        {
            Name "InnerFloorGrid"

            Cull Front
            ZWrite On
            ZTest Always

            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _StencilRef;
                float _StencilComp;

                float4 _BackgroundColor;
                float4 _GridColor;
                float4 _MajorGridColor;

                float _GridScale;
                float _LineWidth;
                float _MajorGridEvery;
                float _MajorLineWidth;
                float _GridHeight;

                float _DistanceFade;
                float _HorizonFade;

                float _ScrollSpeedX;
                float _ScrollSpeedZ;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = posInputs.positionCS;
                output.positionOS = input.positionOS.xyz;

                return output;
            }

            float GridLine(float2 uv, float lWidth)
            {
                float2 grid = abs(frac(uv - 0.5) - 0.5);
                float2 aa = fwidth(uv);

                float2 l = 1.0 - smoothstep(
                    lWidth,
                    lWidth + aa,
                    grid
                );

                return saturate(max(l.x, l.y));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 cameraOS = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 pixelOS = input.positionOS;

                float3 rayDirOS = normalize(pixelOS - cameraOS);

                // XZ / ZX floor plane:
                // local y = _GridHeight
                float denom = rayDirOS.y;

                // If the ray is almost parallel to the floor, fade it out.
                float horizonMask = smoothstep(
                    0.0,
                    _HorizonFade,
                    abs(denom)
                );

                if (abs(denom) < 0.0001)
                {
                    return half4(_BackgroundColor.rgb, 1);
                }

                float t = (_GridHeight - cameraOS.y) / denom;

                // If the plane is behind the camera ray, show background only.
                if (t <= 0.0)
                {
                    return half4(_BackgroundColor.rgb, 1);
                }

                float3 hitOS = cameraOS + rayDirOS * t;

                float2 scroll = float2(_ScrollSpeedX, _ScrollSpeedZ) * _Time.y;

                // Grid lies on local XZ plane.
                float2 gridUV = hitOS.xz * _GridScale + scroll;

                float minorLine = GridLine(gridUV, _LineWidth);

                float majorEvery = max(_MajorGridEvery, 1.0);
                float majorLine = GridLine(gridUV / majorEvery, _MajorLineWidth / majorEvery);

                float distFade = exp(-t * _DistanceFade);

                float minorMask = minorLine * distFade * horizonMask;
                float majorMask = majorLine * distFade * horizonMask;

                float3 color = _BackgroundColor.rgb;

                color += _GridColor.rgb * minorMask;
                color += _MajorGridColor.rgb * majorMask;
                return half4(color, 1);
            }

            ENDHLSL
        }
    }
}