// Cartoon shader for the Blender-made 3D models (built-in render pipeline, mobile friendly, shader model 3.0).
// Uses a fixed light direction so the look does not depend on scene lights.
//   - hard two-tone light ramp with a tinted shadow and a soft extra highlight band
//   - baked ambient occlusion from the vertex colors (Blender export writes it; meshes without colors read white)
//   - small toon specular blob and a lit-side rim
//   - subtle painted detail noise (_DetailTex, object-space planar UV0 baked by the Blender export)
// CPW/ToonOutline adds an inverted-hull outline pass on top of this pass.
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
        _Gloss ("Specular (rgb color, a strength)", Color) = (1,1,1,0.32)
        _Highlight ("Highlight Band", Range(0,0.3)) = 0.07
        _AO ("Baked AO Strength", Range(0,1)) = 1
        _DetailTex ("Detail Noise (gray = none)", 2D) = "gray" {}
        _Detail ("Detail Strength", Range(0,0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Pass
        {
            Name "FORWARD"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 v : TEXCOORD2; fixed4 color : COLOR; };
            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _DetailTex; float4 _DetailTex_ST;
            fixed4 _Color, _ShadowColor, _RimColor, _Gloss; float4 _LightDir; float _Flash, _Highlight, _AO, _Detail;
            v2f vert (appdata i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.uv.xy = TRANSFORM_TEX(i.uv, _MainTex);
                o.uv.zw = TRANSFORM_TEX(i.uv, _DetailTex);
                o.n = UnityObjectToWorldNormal(i.normal);
                // orthographic cameras: every pixel looks along the camera forward axis
                float3 persp = normalize(WorldSpaceViewDir(i.vertex));
                o.v = lerp(persp, UNITY_MATRIX_V[2].xyz, unity_OrthoParams.w);
                o.color = i.color;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 l = normalize(_LightDir.xyz);
                float3 v = normalize(i.v);
                float ndl = dot(n, l);
                float lit = smoothstep(-0.05, 0.12, ndl);
                fixed4 albedo = tex2D(_MainTex, i.uv.xy) * _Color;
                fixed detail = tex2D(_DetailTex, i.uv.zw).r;
                albedo.rgb *= 1.0 + (detail - 0.5) * 2.0 * _Detail;
                fixed3 vcol = lerp(fixed3(1,1,1), i.color.rgb, _AO);
                fixed ao = (vcol.r + vcol.g + vcol.b) * 0.3333;
                // AO also pushes toward the shadow tint so creases read as shade, not as dirt
                fixed3 shade = lerp(_ShadowColor.rgb, fixed3(1,1,1), lit * smoothstep(0.35, 0.85, ao));
                fixed3 c = albedo.rgb * vcol * shade;
                c *= 1.0 + _Highlight * smoothstep(0.55, 0.7, ndl);
                float ndv = saturate(dot(n, v));
                float rim = smoothstep(0.55, 0.78, 1.0 - ndv) * (0.35 + 0.65 * lit);
                c += _RimColor.rgb * rim * _RimColor.a;
                float3 h = normalize(l + v);
                float spec = smoothstep(0.93, 0.965, dot(n, h)) * lit;
                c += _Gloss.rgb * spec * _Gloss.a;
                c = lerp(c, fixed3(1,1,1), _Flash);
                return fixed4(c, albedo.a * i.color.a);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
