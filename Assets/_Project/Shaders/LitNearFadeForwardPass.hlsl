// Forward pass of "TheArchOfTime/Lit Near Fade": URP's own Lit forward pass, plus two dithers:
//   - near-camera fade: geometry closer than _NearFadeStart dissolves in a 4x4 dither pattern and is
//     gone at _NearFadeEnd (metres); equal values switch it off;
//   - see-through: _ModelSeeThrough (0 = solid, 0.5 = half the pixels dropped) makes the whole model
//     look semi-transparent, e.g. while adjusting.
// The surface stays opaque (no sorting or transparency issues); on a phone screen the dither reads as
// transparency. NearFade.cs sets all three values (unset globals are 0 = no effect).
#ifndef THE_ARCH_OF_TIME_LIT_NEAR_FADE_FORWARD_PASS_INCLUDED
#define THE_ARCH_OF_TIME_LIT_NEAR_FADE_FORWARD_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

// Global (not per material), so they stay outside the material CBUFFER (SRP Batcher compatible).
float _NearFadeStart;
float _NearFadeEnd;
float _ModelSeeThrough;

void NearFadeClip(float3 positionWS, float2 pixel)
{
    float visible = 1.0 - saturate(_ModelSeeThrough);
    if (_NearFadeStart > _NearFadeEnd)
    {
        float distanceToCamera = distance(positionWS, GetCameraPositionWS());
        visible *= saturate((distanceToCamera - _NearFadeEnd) / (_NearFadeStart - _NearFadeEnd));
    }
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
    // positionWS is always present: URP's Shadows.hlsl defines REQUIRES_WORLD_SPACE_POS_INTERPOLATOR.
    NearFadeClip(input.positionWS, input.positionCS.xy);
    LitPassFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}

#endif
