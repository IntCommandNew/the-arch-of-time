// Shadow catcher: invisible, except where the main light's shadow falls on it. Lies on the real
// ground under the model (GroundShadow), so the building casts a shadow onto the camera image.
// Needs "Transparent Receive Shadows" on in the URP renderer (Mobile_Renderer).
Shader "TheArchOfTime/ShadowCatcher"
{
    Properties
    {
        _ShadowColor ("Shadow colour", Color) = (0, 0, 0, 1)
        _ShadowStrength ("Shadow strength", Range(0, 1)) = 0.5
        _EdgeFade ("Edge fade (fraction of the size)", Range(0.01, 0.5)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShadowCatcher"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _ShadowStrength;
                half _EdgeFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                half lit = MainLightRealtimeShadow(shadowCoord); // 1 = lit, 0 = in shadow

                // Fade out towards the border of the quad, so it never shows an edge.
                half2 fromCentre = abs(input.uv - 0.5h) * 2.0h;
                half edge = saturate((1.0h - max(fromCentre.x, fromCentre.y)) / _EdgeFade);

                half alpha = (1.0h - lit) * _ShadowStrength * edge;
                return half4(_ShadowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
