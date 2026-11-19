// Inverted-hull cartoon outline (V14). Width is in object space, scaled by distance so it stays readable far away.
Shader "NeonSiege/Outline"
{
    Properties { _Color ("Color", Color) = (0, 0, 0, 1) _Width ("Width", Float) = .012 }
    SubShader
    {
        Tags { "Queue"="Geometry+10" "RenderType"="Opaque" }
        Cull Front
        ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog   // V24：原来有 UNITY_APPLY_FOG 但没开 fog 变体 → 描边不吃雾，整片蒸汽里还能看到人的黑色轮廓
            #include "UnityCG.cginc"
            half4 _Color; float _Width;
            struct v2f { float4 pos : SV_POSITION; UNITY_FOG_COORDS(0) };
            v2f vert (appdata_base i)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, i.vertex).xyz;
                float3 wn = normalize(UnityObjectToWorldNormal(i.normal));
                float d = distance(_WorldSpaceCameraPos, wp);
                wp += wn * _Width * clamp(d * .12, 1, 4);
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            half4 frag (v2f i) : SV_Target { half4 c = _Color; UNITY_APPLY_FOG(i.fogCoord, c); return c; }
            ENDCG
        }
    }
}
