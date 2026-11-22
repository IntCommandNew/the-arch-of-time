// Contact shadow: a soft dark band on the ground around where a model stands on it (ContactShadow.cs), so the
// academy and the arch sit on the real ground instead of floating over it. Independent of the light. The shape
// comes from a baked distance texture (ContactShadowShape): darkest on the outline, gone at the width from it.
// Strength and width are globals set by SceneLighting (LightingTuning, tunable on the phone's Light panel).
Shader "TheArchOfTime/ContactShadow"
{
    Properties
    {
        _ShadowColor ("Shadow colour", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-11"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ContactShadow"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
            CBUFFER_END

            // Globals (SceneLighting): darkness on the outline, and reach in model units at scale 1.
            half _ContactShadowStrength;
            float _ContactShadowWidth;

            // Per renderer (MaterialPropertyBlock, always set by ContactShadow), so outside the CBUFFER.
            TEXTURE2D(_ShapeTex);
            SAMPLER(sampler_ShapeTex);
            float _ShapeMaxDistance; // what the texture's 1 stands for
            float _ShadowOpacity;    // 0 = none (a ghost piece, the hidden base) .. 1

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float distance = SAMPLE_TEXTURE2D(_ShapeTex, sampler_ShapeTex, input.uv).r * _ShapeMaxDistance;
                half s = saturate(1.0 - distance / max(_ContactShadowWidth, 0.002));
                s *= s; // soft towards its outer edge
                return half4(_ShadowColor.rgb, saturate(s * _ContactShadowStrength) * saturate(_ShadowOpacity));
            }
            ENDHLSL
        }
    }
}
