Shader "Hidden/Ympp/SlashEffectStudio/PixelArt"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _CanvasSize; // xy = width, height
            float _PixelSize;
            float _AlphaCutoff;

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float pSize = max(2.0, _PixelSize);
                float2 gridCount = max(float2(1.0, 1.0), floor(_CanvasSize.xy / pSize));
                float2 snappedUv = (floor(i.uv * gridCount) + 0.5) / gridCount;
                float4 c = tex2D(_MainTex, snappedUv);

                if (c.a < _AlphaCutoff)
                {
                    return float4(0, 0, 0, 0);
                }

                // Quantize alpha slightly for crisp retro look while preserving soft tails
                c.a = saturate(floor(c.a * 5.0 + 0.5) / 5.0);
                return c;
            }
            ENDCG
        }
    }
}
