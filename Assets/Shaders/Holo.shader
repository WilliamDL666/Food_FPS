// Additive hologram for shields, spawn barriers, build previews and the noise dome.
Shader "NeonSiege/Holo"
{
    Properties { [HDR] _Color ("Color", Color) = (0.3, 0.9, 1, 1) _Lines ("Line density", Float) = 18 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            half4 _Color; half _Lines;
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 wn : TEXCOORD1; };
            v2f vert (appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            half4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 view = normalize(_WorldSpaceCameraPos - i.wpos);
                float fres = 1 - abs(dot(normalize(i.wn), view));
                float lines = 0.55 + 0.45 * step(0.5, frac(i.wpos.y * _Lines - _Time.y * 1.5));
                float a = (0.12 + pow(fres, 2.5) * 0.9) * lines;
                return half4(_Color.rgb * a, 1);
            }
            ENDCG
        }
    }
}
