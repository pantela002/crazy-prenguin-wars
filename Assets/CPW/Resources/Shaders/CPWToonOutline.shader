// CPW/Toon plus a thick cartoon outline (inverted hull, the Flash art's ink line).
// The hull is pushed along the smoothed vertex normal that the Blender export stores in UV1.xy / UV2.x
// (UV2.y = 1 marks it as present), so hard-edged meshes get an unbroken line; meshes without it use the normal.
// Width is a fraction of the screen height (constant line weight at any zoom, like the Flash vector art), capped at
// _OutlineWidth world units and thinned near the object pivot so tiny props (bullets, pellets) keep their shape.
Shader "CPW/ToonOutline"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _ShadowColor ("Shadow Tint", Color) = (0.62,0.66,0.8,1)
        _RimColor ("Rim", Color) = (1,1,1,0.35)
        _LightDir ("Light Dir", Vector) = (-0.4,0.8,-0.6,0)
        _Flash ("Hit Flash", Range(0,1)) = 0
        _Gloss ("Specular (rgb color, a strength)", Color) = (1,1,1,0.32)
        _Highlight ("Highlight Band", Range(0,0.3)) = 0.07
        _AO ("Baked AO Strength", Range(0,1)) = 1
        _DetailTex ("Detail Noise (gray = none)", 2D) = "gray" {}
        _Detail ("Detail Strength", Range(0,0.5)) = 0.12
        _OutlineColor ("Outline Color", Color) = (0.07,0.07,0.11,1)
        _OutlineScreen ("Outline Width (fraction of screen height)", Range(0,0.01)) = 0.0028
        _OutlineWidth ("Max Outline Width (world units)", Range(0,0.3)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        UsePass "CPW/Toon/FORWARD"
        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct v2f { float4 pos : SV_POSITION; };
            fixed4 _OutlineColor; float _OutlineWidth, _OutlineScreen;
            v2f vert (appdata i)
            {
                v2f o;
                float3 smoothN = float3(i.uv1.x, i.uv1.y, i.uv2.x);
                float3 dir = i.uv2.y > 0.5 ? smoothN : i.normal;
                float3 wn = normalize(UnityObjectToWorldNormal(dir));
                float3 wp = mul(unity_ObjectToWorld, i.vertex).xyz;
                float3 fromPivot = mul((float3x3)unity_ObjectToWorld, i.vertex.xyz);
                // world size of one screen height at this depth: 2 * w / P[1][1] (w = 1 for orthographic cameras)
                float clipW = mul(UNITY_MATRIX_VP, float4(wp, 1.0)).w;
                float screenH = 2.0 * clipW / max(abs(UNITY_MATRIX_P[1][1]), 1e-4);
                float w = min(min(_OutlineWidth, _OutlineScreen * screenH), length(fromPivot) * 0.3 + 0.004);
                wp += wn * w;
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { return _OutlineColor; }
            ENDCG
        }
    }
    Fallback "CPW/Toon"
}
