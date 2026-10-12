#ifndef ECHO_CUTAWAY_INCLUDED
#define ECHO_CUTAWAY_INCLUDED
// Main publishes positions in the same floating-origin space as rendered fragments.
float4 _EchoPlayer;
float4 _EchoCamera;
float _EchoCutawayRadius;
float _EchoDaylight;

void EchoCutaway(float3 worldPosition, float2 pixelPosition)
{
    // The supporting floor does not occlude the avatar. Keep it opaque.
    if (worldPosition.y <= _EchoPlayer.y - 0.8) return;
    float3 segment = _EchoPlayer.xyz - _EchoCamera.xyz;
    float segmentLength = length(segment);
    if (_EchoCutawayRadius <= 0 || segmentLength < 0.001) return;
    float3 direction = segment / segmentLength;
    float3 relative = worldPosition - _EchoCamera.xyz;
    float along = dot(relative, direction);
    // Bounded sight-line tube: never affect anything behind the player/camera.
    if (along <= 0.15 || along >= segmentLength - 0.25) return;
    float distanceToRay = length(relative - direction * along);
    if (distanceToRay >= _EchoCutawayRadius) return;
    float obstruction = 1 - smoothstep(_EchoCutawayRadius * 0.65, _EchoCutawayRadius, distanceToRay);
    float coverage = lerp(1.0, 0.08, obstruction);
    // Surviving fragments write depth; identical dither in base/add passes.
    float noise = frac(52.9829189 * frac(dot(floor(pixelPosition), float2(0.06711056, 0.00583715))));
    clip(coverage - noise);
}
#endif
