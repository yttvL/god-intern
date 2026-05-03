Shader "Hidden/ASCIIURP"
{   
    Properties
    {
        _AsciiTex("ASCII Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags{ "RenderType"="Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #pragma target 4.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        #define PI 3.14159265358979323846

        TEXTURE2D_X(_LuminanceTex);
        // SAMPLER(sampler_PointClamp);
        // float4 _BlitTexture_TexelSize;

        float _Sigma;
        float _K;
        float _Tau;
        float _Threshold;

        int _GaussianKernelSize;
        int _Invert;

        float ASCIILuminance(float3 color)
        {
            return dot(color, float3(0.299, 0.587, 0.114));
        }

        float Gaussian(float sigma, float pos)
        {
            return (1.0 / sqrt(2.0 * PI * sigma * sigma))
                 * exp(-(pos * pos) / (2.0 * sigma * sigma));
        }

        float4 SampleBlitPoint(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
        }

        float4 SampleLuminancePoint(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_LuminanceTex, sampler_PointClamp, uv);
        }

        ENDHLSL

        // Pass 0: Copy / Point Sampler
        Pass
        {
            Name "Copy"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag (Varyings input) : SV_Target
            {
                return SampleBlitPoint(input.texcoord);
            }
            
            ENDHLSL
        }

        // Pass 1: Luminance
        Pass
        {
            Name "Luminance"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag (Varyings input) : SV_Target
            {
                float4 col = saturate(SampleBlitPoint(input.texcoord));
                return ASCIILuminance(col.rgb);
            }
            
            ENDHLSL
        }

        // Pass 2: Pack Luminance
        Pass
        {
            Name "Pack Luminance"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag (Varyings input) : SV_Target
            {
                float3 col = saturate(SampleBlitPoint(input.texcoord)).rgb;
                float lum = SampleLuminancePoint(input.texcoord).r;

                return float4(col, lum);
            }
            
            ENDHLSL
        }

        // Pass 3: Horizontal Gaussian Blur
        // input: Luminance
        // output: r = blur with sigma, g = blur with sigma * k
        Pass
        {
            Name "Horizontal Blur"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag (Varyings input) : SV_Target
            {
                float2 blur = 0.0;
                float2 kernelSum = 0.0;

                for (int x = -_GaussianKernelSize; x <= _GaussianKernelSize; x++)
                {
                    float2 offset = float2(x, 0) * _BlitTexture_TexelSize.xy;

                    float lum = SampleBlitPoint(input.texcoord + offset).r;
                    float2 gauss = float2(Gaussian(_Sigma, x), Gaussian(_Sigma * _K, x));
                    blur += lum * gauss;
                    kernelSum += gauss;
                }
                float2 result = blur / max(kernelSum, 1e-5);

                return float4(result, 0, 0);
            }
            
            ENDHLSL
        }

        // Pass 4: Vertical Gaussian Blur + DoG
        // input: r = horizontal blur with sigma, g = horizontal blur with sigma * k
        // output: binary DoG mask
        Pass
        {
            Name "Vertical Blur And Difference"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag (Varyings input) : SV_Target
            {
                float2 blur = 0.0;
                float2 kernelSum = 0.0;

                for (int y = -_GaussianKernelSize; y <= _GaussianKernelSize; y++)
                {
                    float2 offset = float2(0, y) * _BlitTexture_TexelSize.xy;
                    
                    float2 sampleValue = SampleBlitPoint(input.texcoord + offset).rg;
                    float2 gauss = float2(Gaussian(_Sigma, y), Gaussian(_Sigma * _K, y));

                    blur += sampleValue * gauss;
                    kernelSum += gauss;
                }

                blur = blur / max(kernelSum, 1e-5);

                float dog = blur.x - _Tau * blur.y;
                dog = dog >= _Threshold ? 1.0 : 0.0;

                if (_Invert != 0)
                    dog = 1.0 - dog;

                return dog;
            }
            
            ENDHLSL
        }

        // Pass 5: Sobel Horizontal
        Pass
        {
            Name "Sobel Horizontal"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag (Varyings input) : SV_Target
            {
                float2 texel = _BlitTexture_TexelSize.xy;

                float lum1 = SampleBlitPoint(input.texcoord - float2(1, 0) * texel).r;
                float lum2 = SampleBlitPoint(input.texcoord).r;
                float lum3 = SampleBlitPoint(input.texcoord + float2(1, 0) * texel).r;

                // horizontal difference [-3, 0, 3]
                float Gx = 3.0 * lum1 + 0.0 * lum2 - 3.0 * lum3;

                // vertical smoothing [3, 10, 3]
                float Gy = 3.0 * lum1 + 10.0 * lum2 + 3.0 * lum3; 

                return float4(Gx, Gy, 0, 0);
            }
            
            ENDHLSL
        }

        // Pass 6: Sobel Vertical
        // input: r = Gx difference, g = Gy smoothing
        // output: r = edge magnitude, g = edge angle theta, b = valid theta mask
        Pass
        {
            Name "Sobel Vertical"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag (Varyings input) : SV_Target
            {
                float2 texel = _BlitTexture_TexelSize.xy;

                float2 grad1 = SampleBlitPoint(input.texcoord - float2(0, 1) * texel).rg;
                float2 grad2 = SampleBlitPoint(input.texcoord).rg;
                float2 grad3 = SampleBlitPoint(input.texcoord + float2(0, 1) * texel).rg;

                // horizontal smoothing [3, 10, 3]
                float Gx = 3.0 * grad1.x + 10.0 * grad2.x + 3.0 * grad3.x;

                // vertical difference [-3, 0, 3]
                float Gy = 3.0 * grad1.y + 0.0 * grad2.y - 3.0 * grad3.y;

                float2 G = float2(Gx, Gy);
                float magnitude = length(G);

                G = magnitude > 1e-5 ? G / magnitude : float2(0, 0);

                float theta = atan2(G.y, G.x); // [-pi, pi])
                float validTheta = isnan(theta) ? 0.0 : 1.0;

                return float4(max(magnitude, 0), theta, validTheta, 0);
            }
            
            ENDHLSL
        }
    }

    FallBack Off
}
