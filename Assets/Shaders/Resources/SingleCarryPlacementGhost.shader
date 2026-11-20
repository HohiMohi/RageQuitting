Shader "Hidden/SingleCarryPlacementGhost"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (0.72, 0.74, 0.76, 1)
        _SourceAlpha("Source Alpha", Float) = 1
        _PreviewOpacity("Preview Opacity", Range(0, 1)) = 0.52
        _ValidationColor("Validation Color", Color) = (1, 1, 1, 1)
        _ValidationStrength("Validation Strength", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [Toggle] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }
        Pass
        {
            Name "PlacementPreview"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _SourceAlpha;
                half _PreviewOpacity;
                half4 _ValidationColor;
                half _ValidationStrength;
                half _AlphaClip;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half sourceAlpha = texel.a * _SourceAlpha;
                if (_AlphaClip > 0.5h) clip(sourceAlpha - _Cutoff);
                half4 color = texel * _BaseColor;
                color.rgb = lerp(color.rgb, _ValidationColor.rgb, _ValidationStrength);
                color.a = sourceAlpha * _PreviewOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
