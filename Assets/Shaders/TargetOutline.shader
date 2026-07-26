Shader "RageQuitting/TargetOutline"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (1, 0.58, 0.08, 1)
        _OutlineWidth("Outline Width (Pixels)", Range(0, 8)) = 3
        _OutlineIntensity("Outline Intensity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+50"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "TargetOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
                float _OutlineIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 normalVS = TransformWorldToViewDir(normalWS, true);
                float2 direction = normalVS.xy;
                float directionLength = length(direction);
                direction = directionLength > 0.0001 ? direction / directionLength : float2(0, 1);

                output.positionCS = positionInputs.positionCS;
                float2 pixelOffset = direction * (_OutlineWidth * 2.0 / _ScreenParams.xy);
                output.positionCS.xy += pixelOffset * output.positionCS.w;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = _OutlineColor;
                color.a *= saturate(_OutlineIntensity);
                return color;
            }
            ENDHLSL
        }
    }
}
