#ifndef RQ_STAGE22_NEEDLES_LIGHTING_INCLUDED
#define RQ_STAGE22_NEEDLES_LIGHTING_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half4 _ShadowColor;
half _StylizationStrength;
half _Softness;
half _ShadowFillStrength;
half _AlphaClipEnabled;
half _AlphaCutoff;
half _NormalUpBias;
CBUFFER_END
half3 Stage22NeedlesDiffuseLighting(half3 albedo, half3 normalWS, float4 shadowCoord)
{
    Light mainLight = GetMainLight(shadowCoord);
    half ndotl = saturate(dot(normalWS, mainLight.direction));
    half softDiffuse = lerp(ndotl, smoothstep(0.0h, 1.0h, ndotl), saturate(_Softness));
    half attenuation = mainLight.distanceAttenuation * saturate(mainLight.shadowAttenuation);
    half3 ambient = SampleSHPixel(SampleSHVertex(normalWS), normalWS);
    half3 lambert = mainLight.color * (ndotl * attenuation) + ambient;
    half keyVisibility = softDiffuse * attenuation;
    half shadowFill = saturate(_ShadowFillStrength) * (1.0h - keyVisibility);
    half3 stylized = mainLight.color * keyVisibility + _ShadowColor.rgb * shadowFill + ambient;
    return albedo * lerp(lambert, stylized, saturate(_StylizationStrength));
}
#endif

