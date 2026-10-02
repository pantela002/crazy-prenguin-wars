// Simple toon shader for the Blender-made 3D models (built-in render pipeline, mobile friendly).
// Uses a fixed light direction so the look does not depend on scene lights.
Shader "CPW/Toon"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _ShadowColor ("Shadow Tint", Color) = (0.62,0.66,0.8,1)
        _RimColor ("Rim", Color) = (1,1,1,0.35)
        _LightDir ("Light Dir", Vector) = (-0.4,0.8,-0.6,0)
        _Flash ("Hit Flash", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 v : TEXCOORD2; float4 color : COLOR; };
            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color, _ShadowColor, _RimColor; float4 _LightDir; float _Flash;
            v2f vert (appdata i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.n = UnityObjectToWorldNormal(i.normal);
                o.v = normalize(WorldSpaceViewDir(i.vertex));
                o.color = i.color;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float ndl = dot(n, normalize(_LightDir.xyz));
                float lit = smoothstep(-0.05, 0.1, ndl);
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color * i.color;
                fixed3 c = albedo.rgb * lerp(_ShadowColor.rgb, fixed3(1,1,1), lit);
                float rim = pow(1 - saturate(dot(n, normalize(i.v))), 3);
                c += _RimColor.rgb * rim * _RimColor.a;
                c = lerp(c, fixed3(1,1,1), _Flash);
                return fixed4(c, albedo.a);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
