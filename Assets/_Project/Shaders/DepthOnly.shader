// Depth only: writes depth, draws no colour, so the camera image stays visible and anything virtual behind it is
// hidden. Used for the real buildings' meshes from ARCore's Streetscape Geometry (StreetscapeOcclusion, In Iasi).
// Drawn just before the opaque models (Geometry-10).
Shader "TheArchOfTime/DepthOnly"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry-10"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            return output;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "Occluder"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        // Also in URP's depth prepass, when the renderer makes one.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
