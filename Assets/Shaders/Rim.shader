// Fresnel rim on characters that are directly visible (enemy highlight, V4).
Shader "NeonSiege/Rim"
{
    Properties { [HDR] _Color ("Color", Color) = (1, .2, .2, 1) }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Offset -1, -1
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            half4 _Color;
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };
            v2f vert (appdata_base i)
            {
                v2f o; o.pos = UnityObjectToClipPos(i.vertex);
                o.n = UnityObjectToWorldNormal(i.normal);
                o.v = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, i.vertex).xyz);
                return o;
            }
            half4 frag (v2f i) : SV_Target
            {
                float rim = 1 - saturate(dot(normalize(i.n), i.v));
                return half4(_Color.rgb * (0.16 + pow(rim, 2) * 1.7), 1);   // V13: brighter body + thicker edge
            }
            ENDCG
        }
    }
}
