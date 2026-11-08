// World-space triplanar PBR so procedurally sized boxes keep a consistent texel density.
Shader "NeonSiege/Triplanar"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        _Scale ("Metres per tile", Float) = 3
        _Glossiness ("Smoothness", Range(0,1)) = 0.3
        _Metallic ("Metallic", Range(0,1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.5
        sampler2D _MainTex, _BumpMap;
        half4 _Color, _EmissionColor;
        half _Glossiness, _Metallic, _Scale, _BumpScale;
        struct Input { float3 worldPos; float3 worldNormal; INTERNAL_DATA };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 wN = WorldNormalVector(IN, float3(0,0,1));
            float3 wT = WorldNormalVector(IN, float3(1,0,0));
            float3 wB = WorldNormalVector(IN, float3(0,1,0));
            float3 blend = pow(abs(wN), 4);
            blend /= max(dot(blend, 1), 1e-4);
            float2 uvX = IN.worldPos.zy / _Scale;
            float2 uvY = IN.worldPos.xz / _Scale;
            float2 uvZ = IN.worldPos.xy / _Scale;
            half4 c = tex2D(_MainTex, uvX) * blend.x + tex2D(_MainTex, uvY) * blend.y + tex2D(_MainTex, uvZ) * blend.z;
            half3 nX = UnpackScaleNormal(tex2D(_BumpMap, uvX), _BumpScale);
            half3 nY = UnpackScaleNormal(tex2D(_BumpMap, uvY), _BumpScale);
            half3 nZ = UnpackScaleNormal(tex2D(_BumpMap, uvZ), _BumpScale);
            nX = half3(nX.xy + wN.zy, abs(nX.z) * wN.x);
            nY = half3(nY.xy + wN.xz, abs(nY.z) * wN.y);
            nZ = half3(nZ.xy + wN.xy, abs(nZ.z) * wN.z);
            float3 worldNormal = normalize(nX.zyx * blend.x + nY.xzy * blend.y + nZ.xyz * blend.z);
            o.Normal = normalize(float3(dot(worldNormal, wT), dot(worldNormal, wB), dot(worldNormal, wN)));
            o.Albedo = c.rgb * _Color.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = _EmissionColor.rgb;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
