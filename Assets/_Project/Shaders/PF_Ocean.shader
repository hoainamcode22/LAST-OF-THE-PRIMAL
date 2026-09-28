// PRIMAL FRONTIER - the sea around the island (successor of PF/Water on the ocean; PF/Water stays for old materials).
// Works without the depth texture (mobile): water depth, distance to the beach and the direction the waves travel come
// from a shore map baked from the terrain by PrimalWaterBuilder.Build (_ShoreMap / _ShoreMapRect).
// Look: turquoise shallows to deep blue by depth, soft transparent edge where the water meets the sand (no hard line),
// wave bands that roll in parallel to the beach (analytic normals from the shoreline distance), two scrolling chop
// layers + fine ripples (world space, rotated, macro-modulated so tiling does not show), gentle swell on the vertices
// offshore (none in the shallows so the shore sheet stays glued), caustics on the sandy bottom, light through the
// crests, fresnel sky reflection, sun glint, main light shadows, rain rings (_PF_Rain), storm whitecaps (_PF_Wind).
// PC extras, switched on at run time by WaterGlobals when the camera has them: scene depth (soft edge against rocks,
// the wreck, the player's legs) and refraction of the bed (opaque texture). Premultiplied alpha, fogged.
// Foam lines, the swash running up the sand and the wet sand band are drawn by PF/Ocean Shore on top.
Shader "PF/Ocean"
{
    Properties
    {
        [Header(Baked shore map)]
        [NoScaleOffset] _ShoreMap ("Shore map (PrimalWaterBuilder)", 2D) = "gray" {}
        _ShoreMapRect ("Shore map rect (min x, min z, 1/size x, 1/size z)", Vector) = (0, 0, 0, 0)
        _SeaLevel ("Sea level (m)", Float) = 0

        [Header(Colour)]
        _ShallowColor ("Shallow colour", Color) = (0.10, 0.60, 0.56, 1)
        _DeepColor ("Deep colour", Color) = (0.015, 0.13, 0.27, 1)
        _ScatterColor ("Light through the crests", Color) = (0.12, 0.62, 0.50, 1)
        _DepthScale ("Colour depth (m)", Range(0.5, 12)) = 2.8
        _ShallowAlpha ("Shallow opacity", Range(0, 1)) = 0.3
        _EdgeSoftness ("Soft edge (m of depth)", Range(0.02, 1.5)) = 0.35
        _CausticsStrength ("Caustics", Range(0, 2)) = 0.7
        _CausticsTiling ("Caustics size (m)", Float) = 3.5

        [Header(Surface)]
        [Normal][NoScaleOffset] _WaveNormal ("Wave normals", 2D) = "bump" {}
        _WaveTiling ("Wave tile (m)", Float) = 12
        _WaveStrength ("Wave strength", Range(0, 2)) = 0.55
        _WaveSpeed ("Wave drift (m/s)", Float) = 0.45
        [Normal][NoScaleOffset] _RippleNormal ("Ripple normals", 2D) = "bump" {}
        _RippleTiling ("Ripple tile (m)", Float) = 3.2
        _RippleStrength ("Ripple strength", Range(0, 2)) = 0.35
        _RippleSpeed ("Ripple drift (m/s)", Float) = 0.22
        [NoScaleOffset] _FoamTex ("Foam (R lace, G specks, B macro, A caustics)", 2D) = "black" {}
        _Whitecaps ("Storm whitecaps", Range(0, 1)) = 0.6

        [Header(Waves rolling to the beach)]
        _ShoreWaveLength ("Wave length (m)", Float) = 9
        _ShoreWavePeriod ("Wave period (s)", Float) = 6.5
        _ShoreWaveHeight ("Wave slope strength", Range(0, 2)) = 0.8
        _ShoreWaveReach ("Reach offshore (m, max 45)", Range(5, 45)) = 42

        [Header(Swell)]
        _SwellHeight ("Swell height (m)", Range(0, 1)) = 0.16
        _SwellLength ("Swell length (m)", Float) = 34
        _SwellFade ("Swell fades out at (m from camera)", Float) = 600

        [Header(Lighting)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.93
        _FresnelPower ("Fresnel power", Range(1, 8)) = 5
        _ReflectionStrength ("Sky reflection", Range(0, 1.5)) = 1
        _SpecStrength ("Sun glint", Range(0, 4)) = 1.5
        _RefractionStrength ("Refraction (PC)", Range(0, 0.2)) = 0.05

        [Header(Rain)]
        [NoScaleOffset] _RainRippleTex ("Rain rings", 2D) = "black" {}
        _RainTiling ("Rain ring tile (m)", Float) = 1.3
        _RainStrength ("Rain rings", Range(0, 2)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardOcean"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PF_WaterCommon.hlsl"

            TEXTURE2D(_ShoreMap); SAMPLER(sampler_ShoreMap);
            TEXTURE2D(_WaveNormal); SAMPLER(sampler_WaveNormal);
            TEXTURE2D(_RippleNormal); SAMPLER(sampler_RippleNormal);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_RainRippleTex); SAMPLER(sampler_RainRippleTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShoreMapRect;
                float _SeaLevel;
                half4 _ShallowColor, _DeepColor, _ScatterColor;
                half _DepthScale, _ShallowAlpha, _EdgeSoftness, _CausticsStrength;
                float _CausticsTiling;
                float _WaveTiling, _WaveSpeed, _RippleTiling, _RippleSpeed;
                half _WaveStrength, _RippleStrength, _Whitecaps;
                float _ShoreWaveLength, _ShoreWavePeriod, _ShoreWaveReach;
                half _ShoreWaveHeight;
                float _SwellHeight, _SwellLength, _SwellFade;
                half _Smoothness, _FresnelPower, _ReflectionStrength, _SpecStrength, _RefractionStrength;
                float _RainTiling;
                half _RainStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            PFShore Shore(float2 xz)
            {
                float2 uv = PF_MapUV(xz, _ShoreMapRect);
                float inside = PF_MapInside(uv, _ShoreMapRect);
                float4 raw = SAMPLE_TEXTURE2D_LOD(_ShoreMap, sampler_ShoreMap, saturate(uv), 0);
                return PF_DecodeShore(raw, inside);
            }

            // swell amplitude: nothing in the shallows (the shore sheet and the soft edge stay put), nothing far away
            float SwellAmp(float depth, float camDist)
            {
                return _SwellHeight * saturate((depth - 2.0) / 8.0) * saturate(1.0 - camDist / max(1.0, _SwellFade)) * (0.7 + 0.4 * PF_WindStrength());
            }

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                PFShore s = Shore(ws.xz);
                float depth = max(0.0, -s.height);
                float amp = SwellAmp(depth, distance(ws.xz, _WorldSpaceCameraPos.xz));
                UNITY_BRANCH
                if (amp > 0.001) ws.y += PF_Swell(ws.xz, PF_WaterNow(), _SwellLength, PF_WindDir()).x * amp;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 ws = i.positionWS;
                float2 xz = ws.xz;
                float t = PF_WaterNow();
                float3 V = GetWorldSpaceNormalizeViewDir(ws);
                float viewDepth = PF_ViewDepth(ws);
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float camDist = distance(ws, _WorldSpaceCameraPos);

                // ---- depth: baked terrain depth, sharpened by the camera depth texture when there is one
                PFShore s = Shore(xz);
                float depth = max(0.0, -s.height);                          // the map stores height above _SeaLevel
                depth = min(depth, PF_SceneWaterDepth(screenUV, viewDepth, V));

                // ---- normals (xz slopes, summed)
                float2 wind = PF_WindDir();
                float2 perp = float2(-wind.y, wind.x);
                float windK = PF_WindStrength();
                float2 xzr = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8);
                float waveT = max(1.0, _WaveTiling);
                float2 uvA = xz / waveT + wind * (t * _WaveSpeed / waveT);
                float2 uvB = xzr / (waveT * 1.73) + (wind * 0.6 + perp * 0.8) * (t * _WaveSpeed * 0.7 / (waveT * 1.73)) + 0.37;
                float2 uvC = xz / max(0.2, _RippleTiling) + (wind * 0.7 + perp * 0.3) * (t * _RippleSpeed / max(0.2, _RippleTiling));
                half4 macroTex = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, xz / 230.0 + 0.13);
                half macro = lerp(0.6h, 1.35h, macroTex.b);
                half distFade = lerp(0.35h, 1.0h, (half)saturate(1.0 - camDist / 320.0));
                half calm = lerp(0.3h, 1.0h, (half)saturate(depth / 1.6));
                half waveK = _WaveStrength * (0.55h + 0.5h * (half)windK) * macro * distFade * calm;
                float2 slope = PF_Slope(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, uvA), waveK)
                             + PF_Slope(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, uvB), waveK * 0.8h);
                slope += PF_Slope(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, uvC), _RippleStrength * lerp(0.5h, 1.0h, distFade));

                // waves rolling to the beach (height gradient along the distance field -> slope)
                float env = PF_ShoreEnvelope(s, depth, _ShoreWaveReach);
                float phase = PF_ShorePhase(s.dist, xz, t, _ShoreWaveLength, _ShoreWavePeriod);
                float u = frac(phase);
                float crest = 0.0;
                UNITY_BRANCH
                if (env > 0.001)
                {
                    float segment = lerp(0.3, 1.0, smoothstep(0.2, 0.8, PF_Noise(xz * 0.06 + floor(phase) * 3.1)));
                    float dHdd = PF_ShoreWaveSlopeSoft(u) / max(0.5, _ShoreWaveLength);
                    // grad(dist) = -toShore; the surface tilts against its height gradient
                    slope += s.toShore * (dHdd * _ShoreWaveHeight * env * segment * 0.35);
                    crest = saturate(PF_ShoreWaveHeight(u) - 0.35) * env * segment;
                }
                // swell
                float amp = SwellAmp(depth, length(xz - _WorldSpaceCameraPos.xz));
                UNITY_BRANCH
                if (amp > 0.001) { float3 sw = PF_Swell(xz, t, _SwellLength, wind); slope -= sw.yz * amp; }
                slope += PF_RainRipples(TEXTURE2D_ARGS(_RainRippleTex, sampler_RainRippleTex), xz, _RainTiling, _RainStrength, t);
                float3 N = PF_NormalFromSlope(slope);
                half ndv = (half)saturate(dot(N, V));

                // ---- light
                Light sun = PF_WaterMainLight(ws);
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half3 sunCol = sun.color * shadow;
                half sunUp = (half)saturate(sun.direction.y);
                half3 ambient = SampleSH(half3(0, 1, 0));

                // ---- water body: absorption colour by depth, caustics on the shallow bed, light through the crests
                half f = (half)(1.0 - exp(-depth / max(0.2, _DepthScale)));
                half3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, f);
                half3 body = waterCol * (sunCol * (sunUp * 0.55h + 0.1h) + ambient);
                UNITY_BRANCH
                if (_CausticsStrength > 0.001 && depth < 6.0)
                {
                    float ct = max(0.3, _CausticsTiling);
                    float2 cuv = xz / ct + N.xz * 0.15;
                    half c1 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, cuv + float2(t * 0.021, t * 0.013)).a;
                    half c2 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, cuv * 1.31 + float2(-t * 0.017, t * 0.019) + 0.5).a;
                    half caus = min(c1, c2) * 2.2h;
                    half shallowK = (half)(saturate(1.0 - depth / 6.0) * saturate(depth / 0.25));
                    body += sunCol * sunUp * caus * _CausticsStrength * shallowK * (1.0h - f * 0.6h);
                }
                half back = pow((half)saturate(dot(-V, sun.direction) * 0.5 + 0.5), 4.0h);
                body += _ScatterColor.rgb * sunCol * (back * (0.15h + (half)crest * 0.6h) + (half)crest * 0.04h) * (0.4h + 0.6h * f);
                // storm whitecaps
                half wc = (half)saturate((windK - 0.75) * 1.6) * _Whitecaps * (half)saturate(depth / 3.0);
                half whitecap = 0.0h;
                UNITY_BRANCH
                if (wc > 0.01h)
                {
                    half ft = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, uvB * 1.9 + macroTex.gr * 0.3).r;
                    whitecap = smoothstep(1.0h - wc * 0.45h, 1.05h - wc * 0.45h, ft * (0.6h + 0.6h * macroTex.b));
                }

                // ---- opacity: see the sand in the shallows, fully opaque out at sea, zero at the waterline
                half edge = (half)saturate(depth / max(0.02, _EdgeSoftness));
                half bodyA = lerp(_ShallowAlpha, 1.0h, f) * edge;
                half3 below = body * bodyA;                                  // premultiplied transmitted part
                half belowA = bodyA;
                UNITY_BRANCH
                if (_PF_WaterRefraction > 0.5)
                {
                    // PC: refract the bed and tint it by the water; the waterline itself stays alpha blended (no blur band)
                    half k = (half)saturate(depth / 0.6);
                    float2 off = N.xz * (_RefractionStrength * saturate(depth / 1.5)) / (1.0 + viewDepth * 0.04);
                    half3 bed = PF_Refracted(screenUV, off, viewDepth);
                    half3 tint = lerp(half3(1, 1, 1), saturate(waterCol * 2.2h + 0.25h), (half)saturate(depth / 0.8));
                    half3 refr = lerp(bed * tint, body, lerp(_ShallowAlpha * 0.6h, 1.0h, f));
                    below = lerp(below, refr, k);
                    belowA = lerp(belowA, 1.0h, k);
                }

                // ---- reflection + glint on top
                half F = PF_Fresnel(ndv, 0.02h, _FresnelPower) * edge;
                half3 sky = PF_SkyReflection(N, V, _Smoothness) * _ReflectionStrength;
                half spec = PF_SunSpec(N, V, sun.direction, _Smoothness) * _SpecStrength * edge;
                half3 rgb = sky * F + sunCol * spec + below * (1.0h - F);
                half a = F + belowA * (1.0h - F);
                half3 capCol = (sunCol * saturate(sun.direction.y + 0.2h) + ambient) * 0.9h;
                PF_Over(rgb, a, capCol * whitecap, whitecap);
                UNITY_BRANCH
                if (_PF_WaterDebug > 0.5)
                {
                    float dm = _PF_WaterDebug;
                    if (dm < 1.5) return half4(saturate(-s.height / 4.0), saturate(PF_SceneWaterDepth(screenUV, viewDepth, V) / 4.0), saturate(s.dist / 48.0), 1);
                    if (dm < 2.5) return half4(sky, 1);
                    if (dm < 3.5) return half4(a, a, a, 1);
                    if (dm < 4.5) return half4(crest, whitecap, F, 1);
                    return half4(below / max(belowA, 0.01h), 1);
                }
                return PF_WaterOutput(rgb, a, i.fogFactor);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
