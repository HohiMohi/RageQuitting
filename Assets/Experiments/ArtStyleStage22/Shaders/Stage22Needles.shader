Shader "RageQuitting/Stage22Needles"
{
 Properties
 {
  [MainTexture]_BaseMap("Pigment Atlas",2D)="white"{}
  [MainColor]_BaseColor("Tint",Color)=(1,1,1,1)
  _StylizationStrength("Stylization Strength",Range(0,1))=1
  _ShadowFillStrength("Shadow Fill Strength",Range(0,1))=.25
  _ShadowColor("Shadow Color",Color)=(.46,.52,.60,1)
  _Softness("Light Softness",Range(0,1))=.5
  [Toggle]_AlphaClipEnabled("Alpha Clip Enabled",Float)=1
  _AlphaCutoff("Alpha Cutoff",Range(0,1))=.5
  _NormalUpBias("Foliage Normal Up Bias",Range(0,1))=.65
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
  Pass
  {
   Name "ForwardLit" Tags { "LightMode"="UniversalForward" } ZWrite On Cull Off
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma multi_compile_instancing
   #pragma instancing_options assumeuniformscaling
   #pragma fragment Frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Stage22NeedlesLighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   struct A { UNITY_VERTEX_INPUT_INSTANCE_ID float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
   struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; float4 shadowCoord:TEXCOORD2;float3 positionWS:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
   V Vert(A i){UNITY_SETUP_INSTANCE_ID(i);V o;VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);UNITY_TRANSFER_INSTANCE_ID(i,o);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);o.shadowCoord=GetShadowCoord(p);return o;}
   half4 Frag(V i, bool facing:SV_IsFrontFace):SV_Target
   {
    UNITY_SETUP_INSTANCE_ID(i);
    half4 tex=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
    if(_AlphaClipEnabled>.5h) clip(tex.a*_BaseColor.a-_AlphaCutoff);
    half3 n=normalize(i.normalWS); if(!facing) n=-n; n=normalize(lerp(n,half3(0,1,0),saturate(_NormalUpBias))); 
    float4 shadowCoord=i.shadowCoord;
    #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
     shadowCoord=TransformWorldToShadowCoord(i.positionWS);
    #endif
    return half4(Stage22NeedlesDiffuseLighting(tex.rgb*_BaseColor.rgb,n,shadowCoord),1);
   }
   ENDHLSL
  }
  Pass
  {
   Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" } ZWrite On ZTest LEqual ColorMask 0 Cull Off
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex ShadowVert
   #pragma multi_compile_instancing
   #pragma instancing_options assumeuniformscaling
   #pragma fragment ShadowFrag
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
   #include "Stage22NeedlesLighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   float3 _LightDirection; float3 _LightPosition;
   struct A { UNITY_VERTEX_INPUT_INSTANCE_ID float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0; };
   struct V { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
   V ShadowVert(A i){UNITY_SETUP_INSTANCE_ID(i);V o;float3 pos=TransformObjectToWorld(i.positionOS.xyz);float3 n=TransformObjectToWorldNormal(i.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
     float3 ld=normalize(_LightPosition-pos);
    #else
     float3 ld=_LightDirection;
    #endif
    o.positionCS=TransformWorldToHClip(ApplyShadowBias(pos,n,ld));o.positionCS=ApplyShadowClamping(o.positionCS);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;
   }
   half4 ShadowFrag(V i):SV_Target{half a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a*_BaseColor.a;if(_AlphaClipEnabled>.5h)clip(a-_AlphaCutoff);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ZWrite On ColorMask R Cull Off
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex DepthVert
   #pragma multi_compile_instancing
   #pragma instancing_options assumeuniformscaling
   #pragma fragment DepthFrag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Stage22NeedlesLighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   struct A { UNITY_VERTEX_INPUT_INSTANCE_ID float4 positionOS:POSITION;float2 uv:TEXCOORD0; };struct V { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
   V DepthVert(A i){UNITY_SETUP_INSTANCE_ID(i);V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;}
   half DepthFrag(V i):SV_Target{half a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a*_BaseColor.a;if(_AlphaClipEnabled>.5h)clip(a-_AlphaCutoff);return i.positionCS.z;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormals" Tags { "LightMode"="DepthNormals" } ZWrite On Cull Off
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex NormVert
   #pragma multi_compile_instancing
   #pragma instancing_options assumeuniformscaling
   #pragma fragment NormFrag
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Stage22NeedlesLighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   struct A { UNITY_VERTEX_INPUT_INSTANCE_ID float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0; };struct V { float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;float2 uv:TEXCOORD1; };
   V NormVert(A i){UNITY_SETUP_INSTANCE_ID(i);V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;}
   half4 NormFrag(V i, bool facing:SV_IsFrontFace):SV_Target{half a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a*_BaseColor.a;if(_AlphaClipEnabled>.5h)clip(a-_AlphaCutoff);float3 n=normalize(i.normalWS);if(!facing)n=-n;n=normalize(lerp(n,float3(0,1,0),saturate(_NormalUpBias))); 
   #if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormalWS=PackNormalOctQuadEncode(n);float2 remapped=saturate(octNormalWS*.5+.5);half3 packed=PackFloat2To888(remapped);return half4(packed,0);
   #else
    return half4(n,0);
   #endif
   }
   ENDHLSL
  }
 }
}

