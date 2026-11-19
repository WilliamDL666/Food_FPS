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
            #pragma multi_compile_fog   // V24：叠加发光的轮廓也要被雾吃掉（雾里淡成黑 = 叠加 0）
            #include "UnityCG.cginc"
            half4 _Color;
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; UNITY_FOG_COORDS(2) };
            v2f vert (appdata_base i)
            {
                v2f o; o.pos = UnityObjectToClipPos(i.vertex);
                o.n = UnityObjectToWorldNormal(i.normal);
                o.v = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, i.vertex).xyz);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            half4 frag (v2f i) : SV_Target
            {
                float rim = 1 - saturate(dot(normalize(i.n), i.v));
                half4 c = half4(_Color.rgb * (0.16 + pow(rim, 2) * 1.7), 1);   // V13: brighter body + thicker edge
                #if defined(FOG_LINEAR)   // 只在整片蒸汽里（线性雾）生效；平时的远景指数雾不削弱敌人高亮
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0, 0, 0, 0));
                #endif
                return c;
            }
            ENDCG
        }
    }
}
