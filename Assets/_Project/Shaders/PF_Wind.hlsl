// PRIMAL FRONTIER - shared wind bend for foliage / bark (global values set by WeatherManager every frame)
#ifndef PF_WIND_INCLUDED
#define PF_WIND_INCLUDED

float4 _PF_Wind;      // xy direction, z strength, w gust
float _PF_Wetness;    // 0..1

// sway: metres the top of a plant of height 'height' (object space, m) moves at wind 1; flutter: small fast leaf motion
float3 PF_ApplyWind(float3 positionOS, float3 positionWS, float sway, float height, float flutter)
{
    float wind = _PF_Wind.z > 0.0 ? _PF_Wind.z : 0.35;
    float2 dir = dot(_PF_Wind.xy, _PF_Wind.xy) > 0.01 ? normalize(_PF_Wind.xy) : float2(0.8, 0.6);
    float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float phase = dot(origin.xz, float2(0.13, 0.17));
    float t = _Time.y;
    float h = saturate(positionOS.y / max(0.5, height));
    float bend = h * h;
    float gust = 0.6 + _PF_Wind.w * 0.8;
    float mainSway = (sin(t * 1.3 + phase) * 0.6 + sin(t * 2.3 + phase * 1.7) * 0.25 + 0.35) * gust;
    float3 offset = float3(dir.x, 0.0, dir.y) * (mainSway * sway * wind * bend);
    float f = sin(t * 7.0 + dot(positionWS, float3(1.7, 2.3, 1.1))) * flutter * wind * h;
    offset += float3(f * 0.6, f * 0.3, f * 0.6);
    offset.y -= length(offset.xz) * 0.25 * bend;
    return positionWS + offset;
}
#endif
