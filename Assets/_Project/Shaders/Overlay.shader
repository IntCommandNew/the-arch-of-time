// Flat colour that also shows through other geometry, fainter: e.g. the footprint outline while
// adjusting. Where it is in view it is drawn in full; where something (the building) is in front of
// it, it is drawn at _HiddenAlpha x the colour's alpha, so the whole footprint stays readable without
// a full-strength line running across the walls.
Shader "TheArchOfTime/Overlay"
{
    Properties
    {
        _Color ("Colour", Color) = (0.85, 0.64, 0.25, 0.9)
        _HiddenAlpha ("Strength where hidden", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _HiddenAlpha;
        CBUFFER_END

        float4 Vert(float4 positionOS : POSITION) : SV_POSITION
        {
            return TransformObjectToHClip(positionOS.xyz);
        }
        ENDHLSL

        // Hidden parts (behind something): faint. URP also draws "SRPDefaultUnlit" passes.
        Pass
        {
            Name "Hidden"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Offset -2, -2 // lying on the ground: counts as in view, not hidden behind it
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag() : SV_Target { return half4(_Color.rgb, _Color.a * _HiddenAlpha); }
            ENDHLSL
        }

        // Parts in view: full colour.
        Pass
        {
            Name "Visible"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -2, -2
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag() : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
