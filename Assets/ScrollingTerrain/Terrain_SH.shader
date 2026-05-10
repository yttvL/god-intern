Shader "Custom/Terrain"
{
    Properties
    {
        // stencil mode
        [Header(Stencil)]
        [Enum(UnityEngine.Rendering.CompareFunction)] 
        _StencilComp("Stencil Comp", Float) = 3

        // displacement parameters
        [Header(Displacement)]
        _Height ("Displacement Height", Float) = 0.08   //final displacement = noise * _Height
        _NoiseScale ("Noise Scale", Float) = 1.5
        _ScrollSpeed ("Scroll Speed", Float) = 0.15
        _ScrollDirection ("Scroll Direction", Vector) = (0, 1, 0, 0)    // use only .xy

        // FBM / Perlin Parameters
        // 每增加一层，频率乘以 lacunarity，振幅乘以 persistence
        [Header(FBM Parameters)]
        _Octaves ("Octaves", Float) = 4.0
        _Lacunarity ("Lacunarity", Float) = 2.0
        _Persistence ("Persistence", Float) = 0.5
        _SeedOffset ("Seed Offset", Vector) = (13.1, 27.7, 0, 0)

        // displacement mask
        [Header(Top Displacement Mask)]
        _HeightThreshold ("Height Threshold", Float) = 0.3  // object_space y超过这个值才开始位移
        _BlendRange ("Blend Range", Float) = 0.15
        _ObjectTopY ("Object Top Y", Float) = 1.0

        // top color
        [Header(Top Base Color)]
        _TopLowColor ("Top Noise Low Color", Color) = (0.25, 0.38, 0.42, 1)
        _TopHighColor ("Top Noise High Color", Color) = (0.75, 0.90, 0.92, 1)

        // 用同一张perlin noise的值来控制颜色插值
        // >1 = low color 占比更大
        // <1 = high color 占比更大
        _TopNoisePower ("Top Noise Power", Float) = 1.6

        // Top Toon Lighting
        [Header(Top Toon Lighting)]
        _TopLightColor ("Top Light Color", Color) = (1.0, 0.9, 0.65, 1)
        _TopShadowColor ("Top Shadow Color", Color) = (0.55, 0.65, 0.75, 1)

        _TopLightThreshold ("Top Light Threshold", Range(0,1)) = 0.55
        _TopLightSoftness ("Top Light Softness", Range(0.001,1)) = 0.12

        _NormalSampleOffset ("Normal Sample Offset", Float) = 0.01
        _TopLightStrength ("Top Light Strength", Range(0,2)) = 0.7

        // side color
        [Header(Side Base Color)]
        _SideBaseColor ("Side Base Color", Color) = (0.72, 0.82, 0.80, 1)
        _SideBottomColor ("Side Bottom Color", Color) = (0.52, 0.62, 0.62, 1)
        _SideGradientTopY ("Side Gradient Top Y", Float) = 1.0
        
        // crust color
        [Header(Crust Color)]
        _CrustColor ("Crust Color", Color) = (0.22, 0.38, 0.42, 1)
        _CrustThickness ("Crust Thickness", Float) = 0.08
        _CrustSoftness ("Crust Softness", Float) = 0.03

        // top / side mask
        // 用 normalWS.y 判断一个 fragment 属于 top face 还是 side face
        // 0 = pure side, 1 = pure top
        // Threshold 越大，越严格要求法线朝上才能算 top
        [Header(TopSide Mask)]
        _TopNormalThreshold ("Top Normal Threshold", Range(0,1)) = 0.45
        _TopNormalBlend ("Top Normal Blend", Range(0.001,1)) = 0.15

        // Fake Lighting
        [Header(Fake Lighting)]
        _LightDirection ("Fake Light Direction", Vector) = (0.4, 0.8, 0.3, 0)
        _LightStrength ("Light Strength", Range(0,1)) = 0.35
        _Ambient ("Ambient", Range(0,1)) = 0.75
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry+15"
        }

        Stencil
        {
            Ref 1
            Comp [_StencilComp]
        }

        Pass
        {
            Name "Forward"

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 terrainNormalWS : TEXCOORD5;

                // 位移后的 object-space position
                float3 positionOS : TEXCOORD2;

                // 0~1 noise，白=高，黑=低
                float noise01 : TEXCOORD3;

                // 当前 xz 位置对应的动态顶部高度
                float dynamicTopY : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)

            float _Height;

            float _NoiseScale;
            float _ScrollSpeed;
            float4 _ScrollDirection;

            float _Octaves;
            float _Lacunarity;
            float _Persistence;
            float4 _SeedOffset;

            float _HeightThreshold;
            float _BlendRange;

            float _ObjectTopY;

            float4 _TopLowColor;
            float4 _TopHighColor;
            float _TopNoisePower;

            float4 _TopLightColor;
            float4 _TopShadowColor;

            float _TopLightThreshold;
            float _TopLightSoftness;

            float _NormalSampleOffset;
            float _TopLightStrength;

            float4 _SideBaseColor;
            float4 _SideBottomColor;
            float _SideGradientTopY;

            float4 _CrustColor;
            float _CrustThickness;
            float _CrustSoftness;

            float _TopNormalThreshold;
            float _TopNormalBlend;

            float4 _LightDirection;
            float _LightStrength;
            float _Ambient;

            CBUFFER_END

            // Perlin / FBM

            // 2D Hash function that returns a pseudo-random gradient vector based on the input position
            // Used as gradient in Perlin noise
            float2 Hash22(float2 p)
            {
                p = float2(
                    dot(p, float2(127.1, 311.7)),
                    dot(p, float2(269.5, 183.3))
                );

                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float PerlinNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

                // gradient vector on four corners
                float2 g00 = normalize(Hash22(i + float2(0.0, 0.0)));
                float2 g10 = normalize(Hash22(i + float2(1.0, 0.0)));
                float2 g01 = normalize(Hash22(i + float2(0.0, 1.0)));
                float2 g11 = normalize(Hash22(i + float2(1.0, 1.0)));

                // contribution from each corner
                float n00 = dot(g00, f - float2(0.0, 0.0));
                float n10 = dot(g10, f - float2(1.0, 0.0));
                float n01 = dot(g01, f - float2(0.0, 1.0));
                float n11 = dot(g11, f - float2(1.0, 1.0));

                // interpolation
                float nx0 = lerp(n00, n10, u.x);
                float nx1 = lerp(n01, n11, u.x);

                return lerp(nx0, nx1, u.y);
            }

            float FBM(float2 p)
            {
                float value = 0.0;  // final noise value
                float amplitude = 1.0;  // current amplitude of the noise
                float frequency = 1.0;  // current frequency of the noise
                float maxValue = 0.0;  // maximum possible value for normalization

                int octaveCount = clamp((int)round(_Octaves), 1, 8);

                for (int o = 0; o < 8; o++)
                {
                    if (o >= octaveCount)
                        break;

                    value += PerlinNoise(p * frequency) * amplitude;
                    maxValue += amplitude;

                    frequency *= _Lacunarity;
                    amplitude *= _Persistence;
                }

                return value / maxValue;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionOS = input.positionOS.xyz;

                float2 scrollDir = normalize(_ScrollDirection.xy + 1e-5);

                float2 samplePos =
                    positionOS.xz * _NoiseScale
                    + scrollDir * (_Time.y * _ScrollSpeed)
                    + _SeedOffset.xy;

                // approx terrain normal from FBM height
                float eps = max(_NormalSampleOffset, 0.0001);

                float hL = FBM(samplePos + float2(-eps, 0.0)) * _Height;
                float hR = FBM(samplePos + float2( eps, 0.0)) * _Height;
                float hD = FBM(samplePos + float2(0.0, -eps)) * _Height;
                float hU = FBM(samplePos + float2(0.0,  eps)) * _Height;

                // object-space normal from height difference
                float3 terrainNormalOS =
                    normalize(
                        float3(
                            hL - hR,
                            eps * 2.0,
                            hD - hU
                        )
                    );

                output.terrainNormalWS =
                    TransformObjectToWorldNormal(terrainNormalOS);

                float noise = FBM(samplePos);
                float noise01 = saturate(noise * 0.5 + 0.5);

                float influence =
                    smoothstep(
                        _HeightThreshold,
                        _HeightThreshold + _BlendRange,
                        positionOS.y
                    );

                float displacement = noise * _Height;

                positionOS.y += displacement * influence;

                output.positionOS = positionOS;

                output.positionWS =
                    TransformObjectToWorld(positionOS);

                output.positionHCS =
                    TransformWorldToHClip(output.positionWS);

                output.normalWS =
                    TransformObjectToWorldNormal(input.normalOS);

                output.noise01 = noise01;

                // 原始顶部高度 + 这条 xz 对应的 noise 位移高度
                // for side crust effect
                output.dynamicTopY =
                    _ObjectTopY + displacement;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);

                // Top / Side mask
                float topMask =
                    smoothstep(
                        _TopNormalThreshold,
                        _TopNormalThreshold + _TopNormalBlend,
                        normalWS.y
                    );

                // Top color: weighted noise color
                float topT =
                    pow(
                        saturate(input.noise01),
                        max(_TopNoisePower, 0.0001)
                    );
                
                // top base color from noise
                float3 topBaseColor =
                    lerp(
                        _TopLowColor.rgb,
                        _TopHighColor.rgb,
                        topT
                    );

                // top toon from fake lighting
                float3 lightDir = normalize(_LightDirection.xyz);
                float3 terrainNormalWS = normalize(input.terrainNormalWS);

                float topNdotL = saturate(dot(terrainNormalWS, lightDir));

                float topLightBand =
                smoothstep(
                    _TopLightThreshold,
                    _TopLightThreshold + _TopLightSoftness,
                    topNdotL
                );

                float3 topToonLight =
                    lerp(
                        _TopShadowColor.rgb,
                        _TopLightColor.rgb,
                        topLightBand
                    );

                float3 topColor =
                    lerp(
                        topBaseColor,
                        topBaseColor * topToonLight,
                        _TopLightStrength
                    );



                // Side base vertical color

                // 原点在底边，所以 object-space y = 0 附近是底部
                // 用 _SideGradientTopY 控制从底部到顶部渐变的范围。
                float sideY01 =
                    saturate(input.positionOS.y / max(_SideGradientTopY, 0.0001));

                float3 sideBase =
                lerp(
                    _SideBottomColor.rgb,
                    _SideBaseColor.rgb,
                    sideY01
                );

                // Curved crust band

                // 当前 fragment 距离“动态顶部曲线”的距离。
                // 越接近 0，越靠近上边缘。
                float distanceBelowTop =
                    input.dynamicTopY - input.positionOS.y;

                // 只在 side 顶部形成一条窄带。
                // 0 到 thickness 之间是 crust 区域。
                float crustMask =
                    1.0 - smoothstep(
                        _CrustThickness,
                        _CrustThickness + _CrustSoftness,
                        distanceBelowTop
                    );

                crustMask = saturate(crustMask);

                float3 sideColor =
                    lerp(
                        sideBase,
                        _CrustColor.rgb,
                        crustMask
                    );

                // Fake lighting
                float ndotl = saturate(dot(normalWS, lightDir));
                float lighting = _Ambient + ndotl * _LightStrength;

                // Final
                float3 litSideColor = sideColor * lighting;
                float3 color =
                    lerp(
                        litSideColor,
                        topColor,
                        topMask
                    );

                return float4(color, 1);
            }

            ENDHLSL
        }
    }
}