Shader "Hidden/Sizzle/SlashFx/GlowBlur"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "black" {}
        _GlowTex ("Glow Texture", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Separable 9-tap Gaussian Blur (Premultiplied for clean edge-free glow)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBlur
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float2 _BlurDir;
            float _BlurRadius;

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float4 SamplePremul(float2 uv)
            {
                // Base pass uses hardware Blend SrcAlpha OneMinusSrcAlpha (or SrcAlpha One) onto transparent black,
                // so _MainTex RGB is already premultiplied by alpha.
                return tex2D(_MainTex, saturate(uv));
            }

            float4 fragBlur(v2f i) : SV_Target
            {
                static const float weights[5] = { 0.22702703, 0.19459459, 0.12162162, 0.05405405, 0.01621622 };
                float2 stepUv = _BlurDir * _MainTex_TexelSize.xy * max(0.25, _BlurRadius * 0.28);

                float4 sum = SamplePremul(i.uv) * weights[0];
                for (int k = 1; k < 5; k++)
                {
                    float2 off = stepUv * k;
                    sum += SamplePremul(i.uv + off) * weights[k];
                    sum += SamplePremul(i.uv - off) * weights[k];
                }

                return float4(sum.rgb, saturate(sum.a));
            }
            ENDCG
        }

        // Pass 1: Composite Base Ribbon + Blurred Glow + Alpha Output Mode
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _GlowTex;
            float4 _GlowColor;
            float _GlowIntensity;
            float _OutputMode; // 0=StandardAlpha, 1=Premultiplied, 2=AdditiveBlackBg, 3=GrayscaleMask

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float4 fragComposite(v2f i) : SV_Target
            {
                // _MainTex RGB is already premultiplied from hardware blending in RibbonBake
                float4 baseCol = tex2D(_MainTex, i.uv);
                float4 glowSample = tex2D(_GlowTex, i.uv);

                float baseA = saturate(baseCol.a);
                float3 basePremul = baseCol.rgb;

                float glowA = saturate(glowSample.a * _GlowIntensity);
                float3 glowRgb = (_OutputMode > 2.5)
                    ? float3(1.0, 1.0, 1.0)
                    : _GlowColor.rgb;
                float3 glowPremul = glowRgb * glowA;

                // Porter-Duff "base over glow" in premultiplied space
                float3 outPremul = basePremul + glowPremul * (1.0 - baseA);
                float outAlpha = saturate(baseA + glowA * (1.0 - baseA));

                if (_OutputMode > 1.5 && _OutputMode < 2.5)
                {
                    // 2: Additive (Black Background, alpha = 1)
                    return float4(saturate(outPremul), 1.0);
                }
                else if (_OutputMode > 0.5 && _OutputMode < 1.5)
                {
                    // 1: Premultiplied Alpha
                    return float4(saturate(outPremul), outAlpha);
                }
                else
                {
                    // 0: Standard Alpha / 3: Grayscale Mask
                    float3 straightRgb = outAlpha > 1e-4 ? saturate(outPremul / outAlpha) : float3(0, 0, 0);
                    if (_OutputMode > 2.5)
                    {
                        float lum = dot(straightRgb, float3(0.299, 0.587, 0.114));
                        straightRgb = float3(lum, lum, lum);
                    }
                    return float4(straightRgb, outAlpha);
                }
            }
            ENDCG
        }
    }
}
