// Hexagonal energy field for shields / holo builds. Additive, fresnel edge, scrolling scanline, hit ripple.
Shader "NeonSiege/Hex"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 0.9, 1, 1)
        _Scale ("Hex scale", Float) = 4
        _HitPos ("Hit position (world)", Vector) = (0,0,0,0)
        _HitTime ("Hit time", Float) = -10
    }
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
            half4 _Color; float _Scale; float4 _HitPos; float _HitTime;
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 wn : TEXCOORD1; float2 uv : TEXCOORD2; };
            v2f vert (appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.uv = o.wpos.xz + o.wpos.yy;
                return o;
            }
            float hexEdge(float2 p)
            {
                p *= _Scale;
                const float2 s = float2(1, 1.7320508);
                float4 hC = floor(float4(p, p - float2(.5, 1)) / s.xyxy) + .5;
                float4 h = float4(p - hC.xy * s, p - (hC.zw + .5) * s);
                float2 q = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
                q = abs(q);
                float d = max(dot(q, s * .5), q.x);
                return smoothstep(.42, .5, d);
            }
            half4 frag (v2f i) : SV_Target
            {
                float3 view = normalize(_WorldSpaceCameraPos - i.wpos);
                float fres = 1 - abs(dot(normalize(i.wn), view));
                float2 p = float2(i.wpos.x + i.wpos.z, i.wpos.y);
                float hex = hexEdge(p);
                float scan = 0.5 + 0.5 * sin(i.wpos.y * 6 - _Time.y * 4);
                float age = _Time.y - _HitTime;
                float dist = distance(i.wpos, _HitPos.xyz);
                float ripple = saturate(1 - abs(dist - age * 6) * 2) * saturate(1 - age * 1.5);
                float a = 0.06 + hex * (0.35 + scan * 0.2) + pow(fres, 2) * 0.6 + ripple * 1.5;
                return half4(_Color.rgb * a, 1);
            }
            ENDCG
        }
    }
}
