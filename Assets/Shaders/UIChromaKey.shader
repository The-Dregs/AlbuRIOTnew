Shader "UI/ChromaKey Unlit"
{
    Properties
    {
        [PerRendererData]_MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _KeyColor ("Key Color", Color) = (0,1,0,1)
        _Threshold ("Threshold", Range(0,1)) = 0.35
        _Smoothing ("Smoothing", Range(0,1)) = 0.1
    }
    SubShader
    {
        Tags{ "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _KeyColor;
            float _Threshold;
            float _Smoothing;

            v2f vert (appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, IN.uv) * IN.color;

                // Compute chroma key alpha based on distance in RGB space
                float3 diff = col.rgb - _KeyColor.rgb;
                float dist = length(diff);
                float alpha = smoothstep(_Threshold, _Threshold + _Smoothing, dist);

                col.a *= alpha;
                return col;
            }
            ENDCG
        }
    }
}
