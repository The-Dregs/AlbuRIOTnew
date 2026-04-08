Shader "Custom/SkyboxBlendPanoramic"
{
    Properties
    {
        _TexA ("Day Panorama (HDR)", 2D) = "grey" {}
        _TexB ("Night Panorama (HDR)", 2D) = "grey" {}
        _TintA ("Day Tint", Color) = (1,1,1,1)
        _TintB ("Night Tint", Color) = (1,1,1,1)
        _ExposureA ("Day Exposure", Range(0, 8)) = 1
        _ExposureB ("Night Exposure", Range(0, 8)) = 1
        _RotationA ("Day Rotation", Range(0, 360)) = 0
        _RotationB ("Night Rotation", Range(0, 360)) = 0
        _Blend ("Night Blend", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TexA;
            sampler2D _TexB;
            float4 _TexA_ST;
            float4 _TexB_ST;
            fixed4 _TintA;
            fixed4 _TintB;
            float _ExposureA;
            float _ExposureB;
            float _RotationA;
            float _RotationB;
            float _Blend;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            float3 RotateY(float3 dir, float degrees)
            {
                float r = radians(degrees);
                float s = sin(r);
                float c = cos(r);
                float3 outDir;
                outDir.x = dir.x * c - dir.z * s;
                outDir.y = dir.y;
                outDir.z = dir.x * s + dir.z * c;
                return outDir;
            }

            float2 DirToLatLongUV(float3 dir)
            {
                dir = normalize(dir);
                float2 uv;
                uv.x = atan2(dir.x, dir.z) / (2.0 * UNITY_PI) + 0.5;
                uv.y = asin(saturate(dir.y) * 2.0 - 1.0) / UNITY_PI + 0.5;
                // corrected y to avoid distortion from saturate trick
                uv.y = asin(clamp(dir.y, -1.0, 1.0)) / UNITY_PI + 0.5;
                return uv;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dayDir = RotateY(i.dir, _RotationA);
                float3 nightDir = RotateY(i.dir, _RotationB);

                float2 uvA = DirToLatLongUV(dayDir);
                float2 uvB = DirToLatLongUV(nightDir);

                fixed3 colA = tex2D(_TexA, uvA).rgb * _TintA.rgb * _ExposureA;
                fixed3 colB = tex2D(_TexB, uvB).rgb * _TintB.rgb * _ExposureB;
                fixed3 col = lerp(colA, colB, saturate(_Blend));
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}

