Shader "Hidden/Ympp/SlashEffectStudio/RibbonBake"
{
    Properties
    {
        _GradientTex ("Gradient LUT", 2D) = "white" {}
        _SrcBlend ("Src Blend", Float) = 5
        _DstBlend ("Dst Blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv0    : TEXCOORD0; // x = localU (0 Tail -> 1 Head), y = crossV (0 Left -> 1 Right)
                float2 uv1    : TEXCOORD1; // x = localPixelWidth, y = totalPixelLength
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            sampler2D _GradientTex;
            float _FrameAlpha;
            float _BaseAlpha;
            float _EnableTailFade;
            float _TailFadeRatio;
            float _EdgeSoftness;

            float _EnableCore;
            fixed4 _CoreColor;
            float _CoreWidthRatio;
            float _CoreAlpha;

            float _PatternType; // 0=None, 1=Wind, 2=Fire, 3=Lightning
            float _PatternIntensity;
            float _PatternScale;
            float _FrameSeed;

            float _EnableToonOutline;
            float _ToonOutlineWidth;
            fixed4 _ToonOutlineColor;

            float _IsPhantom;
            fixed4 _PhantomColor;
            float _GrayscaleMask;

            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash12(i);
                float b = hash12(i + float2(1.0, 0.0));
                float c = hash12(i + float2(0.0, 1.0));
                float d = hash12(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv0 = v.uv0;
                o.uv1 = v.uv1;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float u = saturate(i.uv0.x); // 0 = Tail, 1 = Head
                float v = saturate(i.uv0.y); // 0 = Left, 0.5 = Center, 1 = Right
                float distFromCenter = abs(v - 0.5) * 2.0; // 0 = Center, 1 = Edge
                float localPxWidth = max(1.0, i.uv1.x);

                // Base gradient color along curved ribbon UV.x
                fixed4 gradCol = tex2D(_GradientTex, float2(u, 0.5));
                fixed3 rgb = (_IsPhantom > 0.5) ? _PhantomColor.rgb : gradCol.rgb;
                float alpha = (_IsPhantom > 0.5) ? _PhantomColor.a : gradCol.a;

                // Spatial tail fade
                if (_EnableTailFade > 0.5)
                {
                    float fadeRatio = max(0.05, _TailFadeRatio);
                    float tailAlpha = saturate(u / fadeRatio);
                    alpha *= tailAlpha;
                }

                // Soft outer edge feathering (skip when toon outline is active so borders stay sharp)
                float edgeMask = 1.0;
                if (_EdgeSoftness > 0.001 && _EnableToonOutline < 0.5)
                {
                    edgeMask = smoothstep(1.0, max(0.0, 1.0 - _EdgeSoftness * 2.0), distFromCenter);
                }
                alpha *= edgeMask;

                // Procedural UV-space trail patterns (only on non-phantom main/sub ribbons)
                if (_IsPhantom < 0.5 && _PatternType > 0.5)
                {
                    float scale = max(0.25, _PatternScale);
                    if (_PatternType < 1.5)
                    {
                        // 1: Wind streaks flowing along UV.x
                        float lane = v * 14.0 * scale;
                        float laneId = floor(lane);
                        float laneFrac = abs(frac(lane) - 0.5) * 2.0;
                        float dash = step(0.35, frac(u * 4.0 * scale + hash12(float2(laneId, 1.7)) * 3.0 - _FrameSeed * 0.6));
                        float streak = (1.0 - smoothstep(0.15, 0.45, laneFrac)) * dash;
                        rgb = lerp(rgb, fixed3(1.0, 1.0, 1.0), streak * _PatternIntensity * 0.85);
                    }
                    else if (_PatternType < 2.5)
                    {
                        // 2: Fire plasma sinuous heat bands
                        float n1 = valueNoise(float2(u * 10.0 * scale - _FrameSeed * 2.5, v * 5.0 * scale));
                        float n2 = valueNoise(float2(u * 20.0 * scale - _FrameSeed * 4.0, v * 9.0 * scale + 3.1));
                        float flame = smoothstep(0.45, 0.82, n1 * 0.65 + n2 * 0.35);
                        fixed3 fireCol = fixed3(1.0, 0.94, 0.54);
                        rgb = lerp(rgb, fireCol, flame * _PatternIntensity);
                        // Carve subtle fire licking at tail edge
                        alpha *= lerp(1.0, smoothstep(0.2, 0.7, n1 + u * 0.55), _PatternIntensity * 0.45);
                    }
                    else
                    {
                        // 3: Lightning jagged high-voltage arcs
                        float segU = floor(u * 22.0 * scale);
                        float jitter1 = (hash12(float2(segU, floor(_FrameSeed * 17.0))) - 0.5) * 0.72;
                        float jitter2 = (hash12(float2(segU + 11.0, floor(_FrameSeed * 29.0) + 5.0)) - 0.5) * 0.82;
                        float d1 = abs((v - 0.5) - jitter1 * 0.45);
                        float d2 = abs((v - 0.5) - jitter2 * 0.45);
                        float bolt = max(1.0 - smoothstep(0.02, 0.09, d1), 0.75 * (1.0 - smoothstep(0.015, 0.075, d2)));
                        fixed3 boltCol = fixed3(0.88, 0.96, 1.0);
                        rgb = lerp(rgb, boltCol, saturate(bolt * _PatternIntensity * 1.25));
                    }
                }

                // Inner Core (hot sharp blade center)
                if (_IsPhantom < 0.5 && _EnableCore > 0.5 && _CoreWidthRatio > 0.01)
                {
                    float coreEdge = saturate(_CoreWidthRatio);
                    float coreSoft = max(0.03, coreEdge * 0.25);
                    float coreMask = 1.0 - smoothstep(max(0.0, coreEdge - coreSoft), coreEdge, distFromCenter);
                    float coreFade = (_EnableTailFade > 0.5) ? saturate(u / max(0.05, _TailFadeRatio)) : 1.0;
                    float coreWeight = coreMask * _CoreAlpha * coreFade;
                    rgb = lerp(rgb, _CoreColor.rgb, saturate(coreWeight));
                    alpha = max(alpha, coreWeight * _BaseAlpha);
                }

                // Toon Inking Dark Outline along pixel-consistent ribbon edge
                if (_IsPhantom < 0.5 && _EnableToonOutline > 0.5 && _ToonOutlineWidth > 0.1)
                {
                    float distFromEdgePx = (1.0 - distFromCenter) * (localPxWidth * 0.5);
                    float outlineMask = 1.0 - smoothstep(_ToonOutlineWidth - 0.75, _ToonOutlineWidth + 0.25, distFromEdgePx);
                    // Only draw outline where ribbon has meaningful alpha
                    float validAlpha = smoothstep(0.05, 0.25, alpha);
                    rgb = lerp(rgb, _ToonOutlineColor.rgb, outlineMask * validAlpha);
                    alpha = max(alpha, outlineMask * validAlpha);
                }

                // Combine global frame & base alpha
                alpha = saturate(alpha * _FrameAlpha * _BaseAlpha);

                // Optional Grayscale Mask mode (for HDR tinting inside Unity ParticleSystem)
                if (_GrayscaleMask > 0.5)
                {
                    float lum = dot(rgb, float3(0.299, 0.587, 0.114));
                    // Boost core contrast slightly while keeping full luminance range [0.35..1.0]
                    float maskVal = saturate(lerp(0.55, 1.0, lum));
                    rgb = fixed3(maskVal, maskVal, maskVal);
                }

                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
}
