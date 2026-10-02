// Alpha blended unlit shader with vertex colors (water, smoke, UI-in-world, trajectory dots).
Shader "CPW/UnlitTransparent"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color;
            v2f vert (appdata i) { v2f o; o.pos = UnityObjectToClipPos(i.vertex); o.uv = TRANSFORM_TEX(i.uv, _MainTex); o.color = i.color; return o; }
            fixed4 frag (v2f i) : SV_Target { return tex2D(_MainTex, i.uv) * _Color * i.color; }
            ENDCG
        }
    }
}
