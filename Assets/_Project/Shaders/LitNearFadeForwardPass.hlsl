// Forward pass of "TheArchOfTime/Lit Near Fade": URP's own Lit forward pass, plus a see-through dither:
// _ModelSeeThrough (0 = solid, 0.5 = half the pixels dropped, in a 4x4 pattern) makes the whole model look
// semi-transparent while adjusting. The surface stays opaque (no sorting or transparency issues); on a
// phone screen the dither reads as transparency. NearFade.cs sets it (an unset global is 0 = solid).
// The near-camera fade that gave the shader its name was removed on 8 Nov 2026 (the user wants to inspect
// the model up close); the name stays because the academy's materials use it.
#ifndef THE_ARCH_OF_TIME_LIT_NEAR_FADE_FORWARD_PASS_INCLUDED
#define THE_ARCH_OF_TIME_LIT_NEAR_FADE_FORWARD_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

// Global (not per material), so they stay outside the material CBUFFER (SRP Batcher compatible).
float _ModelSeeThrough;

void SeeThroughClip(float2 pixel)
{
    float visible = 1.0 - saturate(_ModelSeeThrough);
    if (visible >= 1.0) return;

    // 4x4 Bayer matrix: each pixel of a 4x4 block disappears at a different level.
    const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    uint2 p = uint2(pixel) % 4;
    float threshold = (bayer[p.y * 4 + p.x] + 0.5) / 16.0;
    clip(visible - threshold);
}

void NearFadeLitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    SeeThroughClip(input.positionCS.xy);
    LitPassFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}

#endif
