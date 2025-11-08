Shader "Polyart/Dreamscape/Foliage/Impostor"
{
    Properties
    {
        _MainTex("Albedo1", 2D) = "white" {}
        _NormalMap("Normals", 2D) = "bump" {}
        // Impostor Grid Params
        _GridSize("Grid Size", float) = 64
        _HorizontalSegments("Horizontal Segments", range(1, 64)) = 16
        _VerticalSegments("VerticalSegments", range(1, 15)) = 3
        _VerticalOffset("Vertical Offset", range(-15, 15)) = 0
        _VerticalStep("Horizontal Step", range(0.0, 90.0)) = 15.0
            // Color Customization
        _Hue("Hue Shift", Range(-1, 1)) = 0
        _Saturation("Saturation", Range(0, 2)) = 1
        _Value("Value (Brightness)", Range(0, 2)) = 1
        _Contrast("Contrast", Range(0, 2)) = 1
        _NormalStrength("Normal Strength", range(0, 10)) = 1.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.1
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [HideInInspector] [MainTexture] _BaseMap("Albedo", 2D) = "white" {}

        // Opacity
        _AlphaClipThresshold("Alpha Clip Thresshold", range(0, 1)) = 0.355

        // Blending state
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0



                
    }

        SubShader
    {
        // Universal Pipeline tag is required. If Universal render pipeline is not set in the graphics settings
        // this Subshader will fail. One can add a subshader below or fallback to Standard built-in to make this
        // material work with both Universal Render Pipeline and Builtin Unity Pipeline
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        // ------------------------------------------------------------------
        //  Forward pass. Shades all light in a single pass. GI + emission + Fog
        Pass
        {
            // Lightmode matches the ShaderPassName set in UniversalRenderPipeline.cs. SRPDefaultUnlit and passes with
            // no LightMode tag are also rendered by Universal Render Pipeline
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

        // -------------------------------------
        // Render State Commands
        Blend[_SrcBlend][_DstBlend],[_SrcBlendAlpha][_DstBlendAlpha]
        ZWrite[_ZWrite]
        Cull[_Cull]
        AlphaToMask[_AlphaToMask]

        HLSLPROGRAM
        #pragma target 2.0

        // -------------------------------------
        // Shader Stages
        #pragma vertex LitPassVertex
        #pragma fragment LitPassFragment

        // -------------------------------------
        // Material Keywords
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _PARALLAXMAP
        #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
        #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
        #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
        #pragma shader_feature_local_fragment _ALPHATEST_ON
        #pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON _ALPHAMODULATE_ON
        #pragma shader_feature_local_fragment _EMISSION
        #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
        #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        #pragma shader_feature_local_fragment _OCCLUSIONMAP
        #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
        #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
        #pragma shader_feature_local_fragment _SPECULAR_SETUP

        // -------------------------------------
        // Universal Pipeline keywords
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
        #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
        #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
        #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
        #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
        #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
        #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
        #pragma multi_compile_fragment _ _LIGHT_COOKIES
        #pragma multi_compile _ _LIGHT_LAYERS
        #pragma multi_compile _ _FORWARD_PLUS
        #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
        #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"


        // -------------------------------------
        // Unity defined keywords
        #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
        #pragma multi_compile _ SHADOWS_SHADOWMASK
        #pragma multi_compile _ DIRLIGHTMAP_COMBINED
        #pragma multi_compile _ LIGHTMAP_ON
        #pragma multi_compile _ DYNAMICLIGHTMAP_ON
        #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
        #pragma multi_compile_fog
        #pragma multi_compile_fragment _ DEBUG_DISPLAY

        //--------------------------------------
        // GPU Instancing
        #pragma multi_compile_instancing
        #pragma instancing_options renderinglayer
        #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

#ifndef UNIVERSAL_LIT_INPUT_INCLUDED
#define UNIVERSAL_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

#if defined(_DETAIL_MULX2) || defined(_DETAIL_SCALED)
#define _DETAIL
#endif

        // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
    float4 _DetailAlbedoMap_ST;
    half4 _BaseColor;
    half4 _SpecColor;
    half4 _EmissionColor;
    half _Cutoff;
    half _Smoothness;
    half _Metallic;
    half _BumpScale;
    half _Parallax;
    half _OcclusionStrength;
    half _ClearCoatMask;
    half _ClearCoatSmoothness;
    half _DetailAlbedoMapScale;
    half _DetailNormalMapScale;
    half _Surface;
    sampler2D _MainTex;
    sampler2D _NormalMap;
    float _GridSize;
    uint _HorizontalSegments;
    float _VerticalSegments;
    float _VerticalOffset;
    float _VerticalStep;
    float _NormalStrength;
    float _Hue;
    float _Saturation;
    float _Value;
    float _Contrast;
    float _AlphaClipThresshold;
    CBUFFER_END

        // NOTE: Do not ifdef the properties for dots instancing, but ifdef the actual usage.
        // Otherwise you might break CPU-side as property constant-buffer offsets change per variant.
        // NOTE: Dots instancing is orthogonal to the constant buffer above.
#ifdef UNITY_DOTS_INSTANCING_ENABLED

            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _SpecColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _EmissionColor)
            UNITY_DOTS_INSTANCED_PROP(float, _Cutoff)
            UNITY_DOTS_INSTANCED_PROP(float, _Smoothness)
            UNITY_DOTS_INSTANCED_PROP(float, _Metallic)
            UNITY_DOTS_INSTANCED_PROP(float, _BumpScale)
            UNITY_DOTS_INSTANCED_PROP(float, _Parallax)
            UNITY_DOTS_INSTANCED_PROP(float, _OcclusionStrength)
            UNITY_DOTS_INSTANCED_PROP(float, _ClearCoatMask)
            UNITY_DOTS_INSTANCED_PROP(float, _ClearCoatSmoothness)
            UNITY_DOTS_INSTANCED_PROP(float, _DetailAlbedoMapScale)
            UNITY_DOTS_INSTANCED_PROP(float, _DetailNormalMapScale)
            UNITY_DOTS_INSTANCED_PROP(float, _Surface)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

        // Here, we want to avoid overriding a property like e.g. _BaseColor with something like this:
        // #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor0)
        //
        // It would be simpler, but it can cause the compiler to regenerate the property loading code for each use of _BaseColor.
        //
        // To avoid this, the property loads are cached in some static values at the beginning of the shader.
        // The properties such as _BaseColor are then overridden so that it expand directly to the static value like this:
        // #define _BaseColor unity_DOTS_Sampled_BaseColor
        //
        // This simple fix happened to improve GPU performances by ~10% on Meta Quest 2 with URP on some scenes.
        static float4 unity_DOTS_Sampled_BaseColor;
    static float4 unity_DOTS_Sampled_SpecColor;
    static float4 unity_DOTS_Sampled_EmissionColor;
    static float  unity_DOTS_Sampled_Cutoff;
    static float  unity_DOTS_Sampled_Smoothness;
    static float  unity_DOTS_Sampled_Metallic;
    static float  unity_DOTS_Sampled_BumpScale;
    static float  unity_DOTS_Sampled_Parallax;
    static float  unity_DOTS_Sampled_OcclusionStrength;
    static float  unity_DOTS_Sampled_ClearCoatMask;
    static float  unity_DOTS_Sampled_ClearCoatSmoothness;
    static float  unity_DOTS_Sampled_DetailAlbedoMapScale;
    static float  unity_DOTS_Sampled_DetailNormalMapScale;
    static float  unity_DOTS_Sampled_Surface;

    void SetupDOTSLitMaterialPropertyCaches()
    {
        unity_DOTS_Sampled_BaseColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
        unity_DOTS_Sampled_SpecColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SpecColor);
        unity_DOTS_Sampled_EmissionColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EmissionColor);
        unity_DOTS_Sampled_Cutoff = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Cutoff);
        unity_DOTS_Sampled_Smoothness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Smoothness);
        unity_DOTS_Sampled_Metallic = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Metallic);
        unity_DOTS_Sampled_BumpScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _BumpScale);
        unity_DOTS_Sampled_Parallax = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Parallax);
        unity_DOTS_Sampled_OcclusionStrength = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _OcclusionStrength);
        unity_DOTS_Sampled_ClearCoatMask = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ClearCoatMask);
        unity_DOTS_Sampled_ClearCoatSmoothness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ClearCoatSmoothness);
        unity_DOTS_Sampled_DetailAlbedoMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _DetailAlbedoMapScale);
        unity_DOTS_Sampled_DetailNormalMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _DetailNormalMapScale);
        unity_DOTS_Sampled_Surface = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Surface);
    }

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSLitMaterialPropertyCaches()

#define _BaseColor              unity_DOTS_Sampled_BaseColor
#define _SpecColor              unity_DOTS_Sampled_SpecColor
#define _EmissionColor          unity_DOTS_Sampled_EmissionColor
#define _Cutoff                 unity_DOTS_Sampled_Cutoff
#define _Smoothness             unity_DOTS_Sampled_Smoothness
#define _Metallic               unity_DOTS_Sampled_Metallic
#define _BumpScale              unity_DOTS_Sampled_BumpScale
#define _Parallax               unity_DOTS_Sampled_Parallax
#define _OcclusionStrength      unity_DOTS_Sampled_OcclusionStrength
#define _ClearCoatMask          unity_DOTS_Sampled_ClearCoatMask
#define _ClearCoatSmoothness    unity_DOTS_Sampled_ClearCoatSmoothness
#define _DetailAlbedoMapScale   unity_DOTS_Sampled_DetailAlbedoMapScale
#define _DetailNormalMapScale   unity_DOTS_Sampled_DetailNormalMapScale
#define _Surface                unity_DOTS_Sampled_Surface

#endif

        TEXTURE2D(_ParallaxMap);        SAMPLER(sampler_ParallaxMap);
        TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
        TEXTURE2D(_DetailMask);         SAMPLER(sampler_DetailMask);
        TEXTURE2D(_DetailAlbedoMap);    SAMPLER(sampler_DetailAlbedoMap);
        TEXTURE2D(_DetailNormalMap);    SAMPLER(sampler_DetailNormalMap);
        TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
        TEXTURE2D(_SpecGlossMap);       SAMPLER(sampler_SpecGlossMap);
        TEXTURE2D(_ClearCoatMap);       SAMPLER(sampler_ClearCoatMap);

#ifdef _SPECULAR_SETUP
#define SAMPLE_METALLICSPECULAR(uv) SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv)
#else
#define SAMPLE_METALLICSPECULAR(uv) SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv)
#endif

        half4 SampleMetallicSpecGloss(float2 uv, half albedoAlpha)
        {
            half4 specGloss;

#ifdef _METALLICSPECGLOSSMAP
            specGloss = half4(SAMPLE_METALLICSPECULAR(uv));
#ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            specGloss.a = albedoAlpha * _Smoothness;
#else
            specGloss.a *= _Smoothness;
#endif
#else // _METALLICSPECGLOSSMAP
#if _SPECULAR_SETUP
            specGloss.rgb = _SpecColor.rgb;
#else
            specGloss.rgb = _Metallic.rrr;
#endif

#ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            specGloss.a = albedoAlpha * _Smoothness;
#else
            specGloss.a = _Smoothness;
#endif
#endif

            return specGloss;
        }

        half SampleOcclusion(float2 uv)
        {
#ifdef _OCCLUSIONMAP
            half occ = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
            return LerpWhiteTo(occ, _OcclusionStrength);
#else
            return half(1.0);
#endif
        }


        // Returns clear coat parameters
        // .x/.r == mask
        // .y/.g == smoothness
        half2 SampleClearCoat(float2 uv)
        {
#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
            half2 clearCoatMaskSmoothness = half2(_ClearCoatMask, _ClearCoatSmoothness);

#if defined(_CLEARCOATMAP)
            clearCoatMaskSmoothness *= SAMPLE_TEXTURE2D(_ClearCoatMap, sampler_ClearCoatMap, uv).rg;
#endif

            return clearCoatMaskSmoothness;
#else
            return half2(0.0, 1.0);
#endif  // _CLEARCOAT
        }

        void ApplyPerPixelDisplacement(half3 viewDirTS, inout float2 uv)
        {
#if defined(_PARALLAXMAP)
            uv += ParallaxMapping(TEXTURE2D_ARGS(_ParallaxMap, sampler_ParallaxMap), viewDirTS, _Parallax, uv);
#endif
        }

        // Used for scaling detail albedo. Main features:
        // - Depending if detailAlbedo brightens or darkens, scale magnifies effect.
        // - No effect is applied if detailAlbedo is 0.5.
        half3 ScaleDetailAlbedo(half3 detailAlbedo, half scale)
        {
            // detailAlbedo = detailAlbedo * 2.0h - 1.0h;
            // detailAlbedo *= _DetailAlbedoMapScale;
            // detailAlbedo = detailAlbedo * 0.5h + 0.5h;
            // return detailAlbedo * 2.0f;

            // A bit more optimized
            return half(2.0) * detailAlbedo * scale - scale + half(1.0);
        }

        half3 ApplyDetailAlbedo(float2 detailUv, half3 albedo, half detailMask)
        {
#if defined(_DETAIL)
            half3 detailAlbedo = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, detailUv).rgb;

            // In order to have same performance as builtin, we do scaling only if scale is not 1.0 (Scaled version has 6 additional instructions)
#if defined(_DETAIL_SCALED)
            detailAlbedo = ScaleDetailAlbedo(detailAlbedo, _DetailAlbedoMapScale);
#else
            detailAlbedo = half(2.0) * detailAlbedo;
#endif

            return albedo * LerpWhiteTo(detailAlbedo, detailMask);
#else
            return albedo;
#endif
        }

        half3 ApplyDetailNormal(float2 detailUv, half3 normalTS, half detailMask)
        {
#if defined(_DETAIL)
#if BUMP_SCALE_NOT_SUPPORTED
            half3 detailNormalTS = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUv));
#else
            half3 detailNormalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUv), _DetailNormalMapScale);
#endif

            // With UNITY_NO_DXT5nm unpacked vector is not normalized for BlendNormalRNM
            // For visual consistancy we going to do in all cases
            detailNormalTS = normalize(detailNormalTS);

            return lerp(normalTS, BlendNormalRNM(normalTS, detailNormalTS), detailMask); // todo: detailMask should lerp the angle of the quaternion rotation, not the normals
#else
            return normalTS;
#endif
        }

        inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
        {
            half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
            outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

            half4 specGloss = SampleMetallicSpecGloss(uv, albedoAlpha.a);
            outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;
            outSurfaceData.albedo = AlphaModulate(outSurfaceData.albedo, outSurfaceData.alpha);

#if _SPECULAR_SETUP
            outSurfaceData.metallic = half(1.0);
            outSurfaceData.specular = specGloss.rgb;
#else
            outSurfaceData.metallic = specGloss.r;
            outSurfaceData.specular = half3(0.0, 0.0, 0.0);
#endif

            outSurfaceData.smoothness = specGloss.a;
            outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
            outSurfaceData.occlusion = SampleOcclusion(uv);
            outSurfaceData.emission = SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
            half2 clearCoat = SampleClearCoat(uv);
            outSurfaceData.clearCoatMask = clearCoat.r;
            outSurfaceData.clearCoatSmoothness = clearCoat.g;
#else
            outSurfaceData.clearCoatMask = half(0.0);
            outSurfaceData.clearCoatSmoothness = half(0.0);
#endif

#if defined(_DETAIL)
            half detailMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv).a;
            float2 detailUv = uv * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw;
            outSurfaceData.albedo = ApplyDetailAlbedo(detailUv, outSurfaceData.albedo, detailMask);
            outSurfaceData.normalTS = ApplyDetailNormal(detailUv, outSurfaceData.normalTS, detailMask);
#endif
        }

#endif // UNIVERSAL_INPUT_SURFACE_PBR_INCLUDED


#ifndef UNIVERSAL_FORWARD_LIT_PASS_INCLUDED
#define UNIVERSAL_FORWARD_LIT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

        // GLES2 has limited amount of interpolators
#if defined(_PARALLAXMAP) && !defined(SHADER_API_GLES)
#define REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
#endif

#if (defined(_NORMALMAP) || (defined(_PARALLAXMAP) && !defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR))) || defined(_DETAIL)
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR
#endif

// keep this file in sync with LitGBufferPass.hlsl

        struct Attributes
        {
            float4 positionOS   : POSITION;
            float3 normalOS     : NORMAL;
            float4 tangentOS    : TANGENT;
            float2 texcoord     : TEXCOORD0;
            float2 staticLightmapUV   : TEXCOORD1;
            float2 dynamicLightmapUV  : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float2 uv                       : TEXCOORD0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
            float3 positionWS               : TEXCOORD1;
#endif

            float3 normalWS                 : TEXCOORD2;
            //#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
                        half4 tangentWS                : TEXCOORD3;    // xyz: tangent, w: sign
            //#endif

            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                        half4 fogFactorAndVertexLight   : TEXCOORD5; // x: fogFactor, yzw: vertex light
            #else
                        half  fogFactor                 : TEXCOORD5;
            #endif

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                        float4 shadowCoord              : TEXCOORD6;
            #endif

            #if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
                        half3 viewDirTS                : TEXCOORD7;
            #endif

                        DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
            #ifdef DYNAMICLIGHTMAP_ON
                        float2  dynamicLightmapUV : TEXCOORD9; // Dynamic lightmap UVs
            #endif
                        float3 originViewDir : TEXCOORD10;

                        float4 positionCS               : SV_POSITION;
                        UNITY_VERTEX_INPUT_INSTANCE_ID
                            UNITY_VERTEX_OUTPUT_STEREO
                    };

                    void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
                    {
                        inputData = (InputData)0;

            #if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
                        inputData.positionWS = input.positionWS;
            #endif

                        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(_NORMALMAP) || defined(_DETAIL)
                        float sgn = input.tangentWS.w;      // should be either +1 or -1
                        float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                        half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);

            #if defined(_NORMALMAP)
                        inputData.tangentToWorld = tangentToWorld;
            #endif
                        inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
            #else
                        inputData.normalWS = input.normalWS;
            #endif

                        inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
                        inputData.viewDirectionWS = viewDirWS;

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                        inputData.shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                        inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
            #else
                        inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                        inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
                        inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
            #else
                        inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
            #endif

            #if defined(DYNAMICLIGHTMAP_ON)
                        inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
            #else
                        inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
            #endif

                        inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                        inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

            #if defined(DEBUG_DISPLAY)
            #if defined(DYNAMICLIGHTMAP_ON)
                        inputData.dynamicLightmapUV = input.dynamicLightmapUV;
            #endif
            #if defined(LIGHTMAP_ON)
                        inputData.staticLightmapUV = input.staticLightmapUV;
            #else
                        inputData.vertexSH = input.vertexSH;
            #endif
            #endif
                    }

            #define DEG2RAD 0.01745328

                    ///////////////////////////////////////////////////////////////////////////////
                    //                  Vertex and Fragment functions                            //
                    ///////////////////////////////////////////////////////////////////////////////

                    // Used in Standard (Physically Based) shader
                    Varyings LitPassVertex(Attributes input)
                    {
                        Varyings output = (Varyings)0;

                        UNITY_SETUP_INSTANCE_ID(input);
                        UNITY_TRANSFER_INSTANCE_ID(input, output);
                        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                        // Stelios Start -----------------------------------------!!!!!!!!!!!!!!!!!!!!!!!!!!!!_--------------------------/-/--/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-

                        // Compute the view direction
                        float3 viewVec = -_WorldSpaceCameraPos + mul(UNITY_MATRIX_M, float4(0, 0, 0, 1)).xyz;
                        float3 viewDir = normalize(viewVec);

                        // Rotation matrix to align the object correctly in world space
                        float3 M2 = mul((float3x3)unity_WorldToObject, viewDir);
                        float3 M0 = cross(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)), M2);
                        float3 M1 = cross(M2, M0);
                        float3x3 mat = float3x3(normalize(M0), normalize(M1), M2);

                        // Apply rotation and tangent transforms
                        input.positionOS.xyz = mul(input.positionOS.xyz, mat);
                        input.normalOS.xyz = mul(input.normalOS.xyz, mat);
                        input.tangentOS.xyz = mul(input.tangentOS.xyz, mat);

                        //output.uv_MainTex = v.uv;
                        //output.originViewDir = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz);
                        //output.position = mul(UNITY_MATRIX_VP, float4(v.position.xyz, 1.0));
                        //output.worldPos = mul(unity_ObjectToWorld, float4(v.position.xyz, 1.0)).xyz;
                        //output.worldNormal = normalize(mul(unity_ObjectToWorld, v.normal.xyz));
                        //output.worldTangent = normalize(mul(unity_ObjectToWorld, v.tangent.xyz));

                        //o.position = TransformObjectToHClip(v.position.xyz); // Transform to clip space


                        VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

                        // normalWS and tangentWS already normalize.
                        // this is required to avoid skewing the direction during interpolation
                        // also required for per-vertex lighting and SH evaluation
                        VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                        half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);

                        half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                        fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif

                        output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

                        // already normalized from normal transform to WS.
                        output.normalWS = normalInput.normalWS;
            #if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR) || defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
                        real sign = input.tangentOS.w * GetOddNegativeScale();
                        half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
            #endif
            #if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
                        output.tangentWS = tangentWS;
            #endif

            #if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
                        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
                        half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
                        output.viewDirTS = viewDirTS;
            #endif

                        OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
            #ifdef DYNAMICLIGHTMAP_ON
                        output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
            #endif
                        OUTPUT_SH(output.normalWS.xyz, output.vertexSH);
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                        output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
            #else
                        output.fogFactor = fogFactor;
            #endif

            #if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
                        output.positionWS = vertexInput.positionWS;
            #endif

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                        output.shadowCoord = GetShadowCoord(vertexInput);
            #endif

                        output.positionCS = vertexInput.positionCS;

                        output.originViewDir = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz);

                        return output;
                    }

                    void CalculateUVs(float2 uv_MainTex, float3 viewDir, out float2 UvLB, out float2 UvRB, out float2 UvLT, out float2 UvRT, out float alpha, out float beta)
                    {
                        float gridSide = round(sqrt(_GridSize));
                        float gridStep = 1.0 / gridSide;

                        float3x3 rotationScaleMatrix = float3x3(UNITY_MATRIX_M[0].xyz, UNITY_MATRIX_M[1].xyz, UNITY_MATRIX_M[2].xyz);
                        float3x3 rotationOnlyMatrix;
                        rotationOnlyMatrix[0] = normalize(rotationScaleMatrix[0]);
                        rotationOnlyMatrix[1] = normalize(rotationScaleMatrix[1]);
                        rotationOnlyMatrix[2] = normalize(rotationScaleMatrix[2]);
                        float trYawOffset = -atan2(rotationOnlyMatrix._31, rotationOnlyMatrix._11);

                        float yaw = atan2(viewDir.z, viewDir.x) + TWO_PI + trYawOffset;
                        float yawId = (round((yaw / TWO_PI) * _HorizontalSegments) % _HorizontalSegments);
                        float yawFrac = frac((yaw / TWO_PI) * _HorizontalSegments);

                        float elevation = asin(viewDir.y);
                        float elevationId = max(min(round(elevation / (_VerticalStep * DEG2RAD)) - _VerticalOffset, _VerticalSegments), -_VerticalSegments);
                        float elevationFrac = frac(elevation / (_VerticalStep * DEG2RAD));

                        alpha = 1.0 - yawFrac;
                        beta = 1.0 - elevationFrac;

                        float subdLB;
                        float subdRB;
                        float subdLT;
                        float subdRT;

                        if (elevationFrac < 0.5) {
                            if (yawFrac < 0.5) {
                                subdLB = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                                subdRB = (yawId + 1) % _HorizontalSegments + (elevationId + _VerticalSegments) * _HorizontalSegments;
                                subdLT = yawId + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments; /// 
                                subdRT = ((yawId + 1) % _HorizontalSegments + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments);
                            }
                            else {
                                subdLB = (yawId - 1) % _HorizontalSegments + (elevationId + _VerticalSegments) * _HorizontalSegments;
                                subdRB = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                                subdLT = ((yawId - 1) % _HorizontalSegments + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments);
                                subdRT = (yawId + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments); /// 
                            }
                        }
                        else {
                            if (yawFrac < 0.5) {
                                subdLB = yawId + (elevationId - (elevationId == -_VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments; /// 
                                subdRB = (yawId + 1) % _HorizontalSegments + (elevationId + _VerticalSegments - (elevationId == -_VerticalSegments ? 0 : 1)) * _HorizontalSegments;
                                subdLT = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                                subdRT = ((yawId + 1) % _HorizontalSegments + (elevationId + _VerticalSegments) * _HorizontalSegments);
                            }
                            else {
                                subdLB = (yawId - 1) % _HorizontalSegments + (elevationId + _VerticalSegments - (elevationId == -_VerticalSegments ? 0 : 1)) * _HorizontalSegments;
                                subdRB = yawId + (elevationId + _VerticalSegments - (elevationId == -_VerticalSegments ? 0 : 1)) * _HorizontalSegments; /// 
                                subdLT = ((yawId - 1) % _HorizontalSegments) + (elevationId + _VerticalSegments) * _HorizontalSegments;
                                subdRT = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                            }
                        }

                        UvLB = uv_MainTex / gridSide + float2((subdLB % gridSide), floor(subdLB / gridSide)) * gridStep;
                        UvRB = uv_MainTex / gridSide + float2((subdRB % gridSide), floor(subdRB / gridSide)) * gridStep;
                        UvRT = uv_MainTex / gridSide + float2((subdRT % gridSide), floor(subdRT / gridSide)) * gridStep;
                        UvLT = uv_MainTex / gridSide + float2((subdLT % gridSide), floor(subdLT / gridSide)) * gridStep;
                    }

                    // Convert RGB to HSV
                    float3 RGBtoHSV(float3 color)
                    {
                        float maxVal = max(color.r, max(color.g, color.b));
                        float minVal = min(color.r, min(color.g, color.b));
                        float delta = maxVal - minVal;

                        // Hue
                        float hue = 0.0;
                        if (delta > 0.0)
                        {
                            if (maxVal == color.r)
                                hue = (color.g - color.b) / delta + (color.g < color.b ? 6.0 : 0.0);
                            else if (maxVal == color.g)
                                hue = (color.b - color.r) / delta + 2.0;
                            else
                                hue = (color.r - color.g) / delta + 4.0;

                            hue /= 6.0;
                        }

                        // Saturation
                        float saturation = (maxVal == 0.0) ? 0.0 : (delta / maxVal);

                        // Value
                        float value = maxVal;

                        return float3(hue, saturation, value);
                    }

                    // Convert HSV to RGB
                    float3 HSVtoRGB(float3 hsv)
                    {
                        float h = hsv.x * 6.0;
                        float s = hsv.y;
                        float v = hsv.z;

                        float c = v * s;
                        float x = c * (1.0 - abs(fmod(h, 2.0) - 1.0));
                        float m = v - c;

                        float3 rgb;
                        if (h < 1.0)
                            rgb = float3(c, x, 0.0);
                        else if (h < 2.0)
                            rgb = float3(x, c, 0.0);
                        else if (h < 3.0)
                            rgb = float3(0.0, c, x);
                        else if (h < 4.0)
                            rgb = float3(0.0, x, c);
                        else if (h < 5.0)
                            rgb = float3(x, 0.0, c);
                        else
                            rgb = float3(c, 0.0, x);

                        return rgb + m;
                    }

                    float3 TransformTangentToWorld(float3 tangentNormal, float3 worldNormal, float3 worldTangent, float3 worldBinormal)
                    {
                        // Build the tangent-to-world matrix
                        float3x3 tangentToWorld = float3x3(worldTangent, worldBinormal, worldNormal);
                        // Transform the tangent-space normal to world space
                        return normalize(mul(tangentNormal, tangentToWorld));
                    }


                    // Used in Standard (Physically Based) shader
                    void LitPassFragment(Varyings input , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                        , out float4 outRenderingLayers : SV_Target1
            #endif
                                                                                          )
                    {
                        UNITY_SETUP_INSTANCE_ID(input);
                        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            #if defined(_PARALLAXMAP)
            #if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
                        half3 viewDirTS = input.viewDirTS;
            #else
                        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                        half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
            #endif
                        ApplyPerPixelDisplacement(viewDirTS, input.uv);
            #endif

                        SurfaceData surfaceData;
                        InitializeStandardLitSurfaceData(input.uv, surfaceData);

            #ifdef LOD_FADE_CROSSFADE
                        LODFadeCrossFade(input.positionCS);
            #endif

                        InputData inputData;
                        InitializeInputData(input, surfaceData.normalTS, inputData);
            #if UNITY_VERSION >= 60000000
                        SETUP_DEBUG_TEXTURE_DATA(inputData, input.uv);
#else
                        SETUP_DEBUG_TEXTURE_DATA(inputData, input.uv, _BaseMap);
#endif

            #ifdef _DBUFFER
                        ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
            #endif
                        // Stelios Start --------------------------------------------!!!!!!!!!!!!!!!-----------/-/--/-/-/--/-/-/-/--/-/-/-/-/-/-/--/-//--/--/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-

                        float4 color, normal;
                        float metallic;
                        float alpha, beta;
                        float2 UvLB, UvLT, UvRB, UvRT;

                        float2 uv_MainTex = input.uv;
                        float3 viewDir = input.originViewDir;

                        CalculateUVs(uv_MainTex, viewDir, UvLB, UvLT, UvRB, UvRT, alpha, beta);

                        float4 cLB = tex2D(_MainTex, UvLB);
                        float4 cRB = tex2D(_MainTex, UvRB);
                        float4 cLT = tex2D(_MainTex, UvLT);
                        float4 cRT = tex2D(_MainTex, UvRT);
                        float4 cB = lerp(cRB, cLB, beta);
                        float4 cT = lerp(cRT, cLT, beta);
                        color = lerp(cT, cB, alpha);

                        cLB = tex2D(_NormalMap, UvLB);
                        cRB = tex2D(_NormalMap, UvRB);
                        cLT = tex2D(_NormalMap, UvLT);
                        cRT = tex2D(_NormalMap, UvRT);
                        cB = lerp(cRB, cLB, beta);
                        cT = lerp(cRT, cLT, beta);
                        normal = lerp(cT, cB, alpha);

                        // Convert RGB to HSV
                        float3 hsv = RGBtoHSV(color.rgb);

                        // Apply Hue adjustment (wrapping around)
                        hsv.x = frac(hsv.x + _Hue);
                        if (hsv.x < 0.0) hsv.x += 1.0;

                        // Apply Saturation and Value adjustments
                        hsv.y = saturate(hsv.y * _Saturation);
                        hsv.z = saturate(hsv.z * _Value);

                        // Convert back to RGB
                        float3 rgb = HSVtoRGB(hsv);

                        // Apply Contrast adjustment
                        rgb = saturate((rgb - 0.5) * _Contrast + 0.5);

                        // Compute tangent-to-world transformation
                        float3 worldNormal = normalize(input.normalWS); // Replace if incorrect
                        float3 worldTangent = normalize(input.tangentWS.xyz); // Replace with your tangent logic
                        float3 worldBinormal = cross(worldNormal, worldTangent);

                        normal = (normal * 2.0 - 1.0);
                        normal.y = -normal.y;
                        normal.xy *= _NormalStrength;
                        normal = normalize(normal);
                        float3 normalWS = TransformTangentToWorld(normal, worldNormal, worldTangent, worldBinormal);
                        
                        float sgn = input.tangentWS.w;      // should be either +1 or -1
                        float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                        half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);


                        inputData.tangentToWorld = tangentToWorld;

                        inputData.normalWS = TransformTangentToWorld(normal, tangentToWorld);

                        normalWS = inputData.normalWS;


                        //o.normalWS = normalWS;
                        //o.Normal = normal;

                        //o.Smoothness = _Smoothness;
                        //o.Metallic = _Metallic;
                        //o.Alpha = color.a;
                        if (color.a < _AlphaClipThresshold)
                            discard;

                        // Populate InputData using standard struct
                        //inputData = (InputData)0;
                        inputData.positionWS = input.positionWS;
                        inputData.normalWS = normalWS;
                        inputData.viewDirectionWS = normalize(_WorldSpaceCameraPos - input.positionWS);

                        // Populate SurfaceData using standard struct
                        //ZERO_INITIALIZE(SurfaceData, surfaceData);
                        surfaceData.albedo = rgb;
                        surfaceData.metallic = _Metallic;
                        surfaceData.smoothness = _Smoothness;
                        surfaceData.alpha = color.a;
                        surfaceData.normalTS = normal;
                        surfaceData.specular = 0.5;


                        color = UniversalFragmentPBR(inputData, surfaceData);
                        color.rgb = MixFog(color.rgb, inputData.fogCoord);
                        //color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

                        outColor = float4(color.rgb,1);

            #ifdef _WRITE_RENDERING_LAYERS
                        uint renderingLayers = GetMeshRenderingLayer();
                        outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
            #endif
                    }

            #endif


                    ENDHLSL
                }

    }

        FallBack "Hidden/Universal Render Pipeline/FallbackError"
                        //CustomEditor "UnityEditor.Rendering.Universal.ShaderGUI.LitShader"
}
