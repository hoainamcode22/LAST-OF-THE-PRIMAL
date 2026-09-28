// PRIMAL FRONTIER - shared water functions for PF/Ocean, PF/Ocean Shore and PF/Water Flow.
// Include AFTER URP Core.hlsl + Lighting.hlsl. Per-material values stay in each shader's UnityPerMaterial CBUFFER;
// everything here takes them as arguments, so the SRP Batcher layout of each shader is untouched.
// Global values (all default to 0 when nothing sets them = the safe mobile path):
//   _PF_Wind (WeatherManager)          xy wind direction (world xz), z strength (calm 0.35 .. storm 1.4), w gust
//   _PF_Wetness (WeatherManager)       0..1 surfaces wet
//   _PF_Rain (WaterGlobals)            0..1 rain falling now (rain rings on the water)
//   _PF_WaterSceneDepth (WaterGlobals) 1 when the camera depth texture exists (soft edges against rocks, props, wreck)
//   _PF_WaterRefraction (WaterGlobals) 1 when the camera opaque texture exists (refraction of the bed)
//   _PF_WaterTime (PrimalWaterBuilder.Preview only) > 0 freezes the water clock for edit-mode captures
#ifndef PF_WATER_COMMON_INCLUDED
#define PF_WATER_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

#ifndef PF_WIND_INCLUDED
float4 _PF_Wind;
float _PF_Wetness;
#endif
float _PF_Rain;
float _PF_WaterSceneDepth;
float _PF_WaterRefraction;
float _PF_WaterTime;
float _PF_WaterDebug;
float _PF_NightFactor;     // TimeManager: 0 day .. 1 night (the baked sky reflection is a day sky: dim it at night)      // PrimalWaterBuilder.Preview "debug=N" only: 1 depth/scene/bank, 2 reflection, 3 alpha, 4 speed/foam/fresnel, 5 body

float PF_WaterNow() { return _PF_WaterTime > 0.0 ? _PF_WaterTime : _Time.y; }
float PF_WindStrength() { return _PF_Wind.z > 0.0 ? _PF_Wind.z : 0.35; }
float2 PF_WindDir() { return dot(_PF_Wind.xy, _PF_Wind.xy) > 0.01 ? normalize(_PF_Wind.xy) : float2(0.8, 0.6); }

// ------------------------------------------------------------------ noise (no sin hash: stable on mobile GPUs)
float PF_Hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float PF_Hash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float PF_Noise(float2 p)
{
    float2 c = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = PF_Hash21(c), b = PF_Hash21(c + float2(1, 0)), d = PF_Hash21(c + float2(0, 1)), e = PF_Hash21(c + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(d, e, u.x), u.y);
}

// ------------------------------------------------------------------ baked maps
// shore map (PrimalWaterBuilder): R height above sea, signed sqrt encoding over +-16 m; G smoothed distance to the
// shoreline, +-48 m (positive = offshore); BA unit direction towards the shore (xz). rect: xy = min xz, zw = 1 / size.
struct PFShore
{
    float height;     // m above sea level (negative = under water)
    float dist;       // m to the shoreline, positive offshore
    float2 toShore;   // unit xz direction the waves travel (towards the beach)
    float inside;     // 1 inside the baked area
};

float2 PF_MapUV(float2 xz, float4 rect) { return (xz - rect.xy) * rect.zw; }
float PF_MapInside(float2 uv, float4 rect) { return (rect.z > 0.0 && all(uv >= 0.0) && all(uv <= 1.0)) ? 1.0 : 0.0; }

PFShore PF_DecodeShore(float4 raw, float inside)
{
    PFShore s;
    float e = raw.r * 2.0 - 1.0;
    s.height = inside > 0.5 ? sign(e) * e * e * 16.0 : -16.0;
    s.dist = inside > 0.5 ? (raw.g - 0.5) * 96.0 : 48.0;
    float2 d = raw.ba * 2.0 - 1.0;
    s.toShore = inside > 0.5 && dot(d, d) > 0.01 ? normalize(d) : float2(0.0, 0.0);
    s.inside = inside;
    return s;
}

// ------------------------------------------------------------------ waves that roll towards the beach
// along-shore phase offset (cycles): broad bends + a mid-scale wobble so crest lines are never ruler straight
float PF_ShoreNoise(float2 xz)
{
    return PF_Noise(xz * 0.012) * 1.7 + PF_Noise(xz * 0.037 + 5.0) * 0.45 + PF_Noise(xz * 0.09 + 11.0) * 0.22;
}

// Crest lines follow the smoothed shoreline distance, so they arrive parallel to any beach shape. phase = d / length
// + t / period + slow noise along the shore (crests are never perfectly straight). u = frac(phase): the crest is at
// the wrap, u small = just passed (offshore side), u near 1 = about to arrive.
float PF_ShorePhase(float dist, float2 xz, float t, float waveLength, float period)
{
    return dist / max(0.5, waveLength) + t / max(0.5, period) + PF_ShoreNoise(xz);
}

// normalised wave height (steep front, long back) and its derivative d height / d u
float PF_ShoreWaveHeight(float u) { return exp(-u * 4.0) + exp(-(1.0 - u) * 14.0); }
float PF_ShoreWaveSlope(float u) { return -4.0 * exp(-u * 4.0) + 14.0 * exp(-(1.0 - u) * 14.0); }
// softer version for the surface normal: no razor highlight line along the crest
float PF_ShoreWaveSlopeSoft(float u) { return -3.0 * exp(-u * 3.0) + 6.0 * exp(-(1.0 - u) * 6.0); }

// how strong the rolling waves are here: nothing far out (the distance field stops at 48 m), growing towards the
// breaking depth, dying in the last few centimetres
float PF_ShoreEnvelope(PFShore s, float depth, float reach)
{
    float far = saturate((reach - s.dist) / 15.0);
    float grow = lerp(0.35, 1.0, saturate(1.0 - depth / 8.0));
    return s.inside * far * grow * saturate(depth / 0.15);
}

// ------------------------------------------------------------------ gentle offshore swell (vertex + normal)
// three long waves around the wind direction; returns (height, d/dx, d/dz) per metre of amplitude
float3 PF_Swell(float2 xz, float t, float waveLength, float2 wind)
{
    float2 d0 = wind;
    float2 d1 = float2(wind.x * 0.906 - wind.y * 0.423, wind.x * 0.423 + wind.y * 0.906);    // +25 deg
    float2 d2 = float2(wind.x * 0.819 + wind.y * 0.574, -wind.x * 0.574 + wind.y * 0.819);   // -35 deg
    float3 lens = max(4.0, waveLength) * float3(1.0, 0.63, 0.41);
    float3 amps = float3(0.55, 0.28, 0.17);
    float3 k = 6.2831853 / lens;
    float3 w = sqrt(9.81 * k);                                                                 // deep water speed
    float3 ph = float3(dot(d0, xz), dot(d1, xz), dot(d2, xz)) * k - w * t + float3(0.0, 1.7, 4.1);
    float3 sn = sin(ph), cs = cos(ph);
    float h = dot(amps, sn);
    float3 ak = amps * k * cs;
    float dx = ak.x * d0.x + ak.y * d1.x + ak.z * d2.x;
    float dz = ak.x * d0.y + ak.y * d1.y + ak.z * d2.y;
    return float3(h, dx, dz);
}

// ------------------------------------------------------------------ normals
// tangent-space normal map sample -> xz slope (u = world x, v = world z)
float2 PF_Slope(half4 packedNormal, half scale)
{
    half3 n = UnpackNormalScale(packedNormal, scale);
    return float2(n.x, n.y);
}

float3 PF_NormalFromSlope(float2 s) { return normalize(float3(s.x, 1.0, s.y)); }

// rain drops: expanding rings from a baked drop texture (RG ring direction, B drop time, A ring mask)
float2 PF_RainRingLayer(float4 r, float t, float weight)
{
    float2 dir = r.rg * 2.0 - 1.0;
    float dropFrac = frac(r.b + t);
    float timeFrac = dropFrac - 1.0 + r.a;
    float dropFactor = saturate(0.2 + weight * 0.8 - dropFrac);
    float f = dropFactor * r.a * sin(clamp(timeFrac * 9.0, 0.0, 3.0) * PI);
    return dir * f;
}

float2 PF_RainRipples(TEXTURE2D_PARAM(rainTex, rainSampler), float2 xz, float tiling, float strength, float t)
{
    float rain = saturate(_PF_Rain);
    float2 s = float2(0.0, 0.0);
    UNITY_BRANCH
    if (rain * strength > 0.01)
    {
        float2 uv = xz / max(0.05, tiling);
        float w1 = saturate(rain * 1.6);
        float w2 = saturate(rain * 2.0 - 0.8);
        // the ring texture has no mips: explicit LOD 0 is exact and needs no derivatives inside the branch
        s += PF_RainRingLayer(SAMPLE_TEXTURE2D_LOD(rainTex, rainSampler, uv, 0), t * 1.1, w1);
        s += PF_RainRingLayer(SAMPLE_TEXTURE2D_LOD(rainTex, rainSampler, uv * 0.77 + float2(0.31, 0.57), 0), t * 0.93 + 0.5, w2);
        s *= strength * 1.4;
    }
    return s;
}

// ------------------------------------------------------------------ lighting
Light PF_WaterMainLight(float3 positionWS)
{
    return GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, half4(1, 1, 1, 1));
}

half PF_Fresnel(half ndv, half f0, half power) { return f0 + (1.0h - f0) * pow(1.0h - ndv, power); }

// sky / reflection probe; wave backs reflect the sky, never the ground half of the probe. If the probe comes back far
// darker than the ambient light (not bound yet, e.g. the first edit-mode renders after a script reload), the ambient
// light in the reflected direction stands in, so the water never turns black.
half3 PF_SkyReflection(float3 N, float3 V, half smoothness)
{
    half3 R = reflect(-V, N);
    R.y = max(R.y, 0.03h);
    R = normalize(R);
    half3 probe = GlossyEnvironmentReflection(R, 1.0h - smoothness, 1.0h);
    half3 sh = max(SampleSH(R), half3(0, 0, 0));
    half w = saturate(Luminance(probe) / max(Luminance(sh) * 0.35h, 1e-3h));
    return lerp(sh * 1.15h, probe * lerp(1.0h, 0.1h, (half)saturate(_PF_NightFactor)), w);
}

// sun glint: a tight highlight plus a broader sheen
half PF_SunSpec(float3 N, float3 V, float3 L, half smoothness)
{
    float3 H = normalize(L + V);
    float nh = saturate(dot(N, H));
    half tight = pow(nh, lerp(80.0, 1400.0, smoothness)) * 4.0;
    half broad = pow(nh, lerp(16.0, 120.0, smoothness)) * 0.12;
    return (tight + broad) * saturate(L.y * 5.0);
}

// ------------------------------------------------------------------ optional screen textures (PC)
float PF_ViewDepth(float3 positionWS) { return -TransformWorldToView(positionWS).z; }

// vertical water depth from the camera depth texture (large when unavailable, so min() keeps the baked depth)
float PF_SceneWaterDepth(float2 screenUV, float viewDepth, float3 V)
{
    float d = 1e4;
    UNITY_BRANCH
    if (_PF_WaterSceneDepth > 0.5 && unity_OrthoParams.w < 0.5)
    {
        float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
        d = max(0.0, sceneEye - viewDepth) * max(0.08, abs(V.y));
    }
    return d;
}

// the scene behind the water, bent by the surface normal; offsets that would pick up something in front of the
// water (the player's legs, a rock above the surface) fall back to the straight-through sample
half3 PF_Refracted(float2 screenUV, float2 offset, float viewDepth)
{
    float2 uv = screenUV + offset;
    UNITY_BRANCH
    if (_PF_WaterSceneDepth > 0.5)
    {
        float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
        if (sceneEye < viewDepth) uv = screenUV;
    }
    return SampleSceneColor(uv);
}

// ------------------------------------------------------------------ screen-space reflection (PC: needs depth + opaque textures)
// Marches the reflected ray in world space against the camera depth texture (explicit LOD samples only, no
// derivatives in the loop). Returns the reflected scene colour in rgb and the hit confidence in a (0 = use the sky).
float2 PF_ClipToUV(float4 clip)
{
    // same mapping as ComputeScreenPos (handles the render-texture flip through _ProjectionParams.x)
    float2 o = clip.xy * 0.5;
    o = float2(o.x, o.y * _ProjectionParams.x) + clip.w * 0.5;
    return o / clip.w;
}

half4 PF_ScreenReflection(float3 ws, float3 R, float maxDist)
{
    half4 res = half4(0, 0, 0, 0);
    UNITY_BRANCH
    if (_PF_WaterSceneDepth > 0.5 && _PF_WaterRefraction > 0.5 && unity_OrthoParams.w < 0.5 && R.y > 0.0)
    {
        float t = 0.3, prevT = 0.0;
        [loop]
        for (int i = 0; i < 18; i++)
        {
            float3 q = ws + R * t;
            float4 clip = TransformWorldToHClip(q);
            if (clip.w <= 0.01) break;
            float2 uv = PF_ClipToUV(clip);
            if (any(uv < 0.0) || any(uv > 1.0)) break;
            float scene = LinearEyeDepth(SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, UnityStereoTransformScreenSpaceTex(uv), 0).r, _ZBufferParams);
            float thick = 0.35 + t * 0.1;
            if (clip.w > scene + 0.02 && clip.w < scene + thick)
            {
                // refine between the last miss and this hit
                float lo = prevT, hi = t;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float mid = (lo + hi) * 0.5;
                    float4 c2 = TransformWorldToHClip(ws + R * mid);
                    float2 uv2 = PF_ClipToUV(c2);
                    float s2 = LinearEyeDepth(SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, UnityStereoTransformScreenSpaceTex(uv2), 0).r, _ZBufferParams);
                    if (c2.w > s2 + 0.02) hi = mid; else lo = mid;
                }
                float4 ch = TransformWorldToHClip(ws + R * hi);
                float2 huv = PF_ClipToUV(ch);
                float2 edge = saturate(min(huv, 1.0 - huv) * 12.0);                        // fade at the screen border
                res.rgb = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_LinearClamp, UnityStereoTransformScreenSpaceTex(huv), 0).rgb;
                res.a = edge.x * edge.y * saturate(1.0 - hi / maxDist);
                break;
            }
            prevT = t;
            t = t * 1.32 + 0.15;
            if (t > maxDist) break;
        }
    }
    return res;
}

// light passing through 'path' metres of water: per-channel absorption (red goes first)
half3 PF_Transmittance(half3 absorb, float path) { return (half3)exp(-(float3)absorb * path); }

// ------------------------------------------------------------------ output
// premultiplied colour (Blend One OneMinusSrcAlpha) with fog: the fog colour is scaled by coverage
half4 PF_WaterOutput(half3 premultiplied, half alpha, half fogFactor)
{
    alpha = saturate(alpha);
    half3 c = MixFogColor(premultiplied, unity_FogColor.rgb * alpha, fogFactor);
    return half4(c, alpha);
}

// "a over b" for premultiplied layers
void PF_Over(inout half3 rgb, inout half a, half3 topRgb, half topA)
{
    rgb = topRgb + rgb * (1.0h - topA);
    a = topA + a * (1.0h - topA);
}

#endif
