// World-space TextMesh shader with a real depth test (the built-in font shader draws on top of everything).
Shader "NeonSiege/WorldText"
{
    Properties { _MainTex ("Font Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off ZTest LEqual Cull Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST;
            struct a2v { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert(a2v v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color; o.uv = TRANSFORM_TEX(v.uv, _MainTex); UNITY_TRANSFER_FOG(o, o.pos); return o; }
            fixed4 frag(v2f i) : SV_Target { fixed4 c = i.color; c.a *= tex2D(_MainTex, i.uv).a; UNITY_APPLY_FOG(i.fogCoord, c); return c; }
            ENDCG
        }
    }
}
