// PRIMAL FRONTIER - the beach line on top of PF/Ocean: breaking foam lines, the thin sheet of water that runs up the
// sand and slides back (swash), its bubbly leading edge, and the darker wet sand it leaves behind (dries in seconds).
// Drawn on a mesh PrimalWaterBuilder generates along the shoreline (terrain band from ~2.5 m under water to ~1.2 m
// above): vertices sit on the sand, or on the water surface where the sand is under water. Everything is computed
// from the same baked shore map and the same wave phase as PF/Ocean, so the foam arrives with the ocean's wave bands
// and every wave that reaches the beach becomes one run-up (each with its own reach). Pure overlay: transparent
// where nothing happens. The mesh is pulled towards the camera (_ViewBias) so it never flickers into the sand.
// Keep the wave numbers equal to the ocean material (PrimalWaterBuilder copies them).
Shader "PF/Ocean Shore"
{
    Properties
    {
        [Header(Baked shore map)]
        [NoScaleOffset] _ShoreMap ("Shore map (PrimalWaterBuilder)", 2D) = "gray" {}
        _ShoreMapRect ("Shore map rect (min x, min z, 1/size x, 1/size z)", Vector) = (0, 0, 0, 0)

        [Header(Waves rolling to the beach (same as the ocean))]
        _ShoreWaveLength ("Wave length (m)", Float) = 9
        _ShoreWavePeriod ("Wave period (s)", Float) = 6.5

        [Header(Breaking foam)]
        [NoScaleOffset] _FoamTex ("Foam (R lace, G specks, B macro, A caustics)", 2D) = "black" {}
        _FoamTiling ("Foam tile (m)", Float) = 5
        _FoamColor ("Foam colour", Color) = (0.95, 0.97, 0.97, 1)
        _BreakDepth ("Breaking depth (m)", Range(0.2, 3)) = 1.2
        _SurfFoam ("Breaking foam", Range(0, 2)) = 1
        _ShoreFoam ("Foam at the waterline", Range(0, 2)) = 0.6

        [Header(Swash)]
        _SwashHeight ("Run-up height (m, vertical)", Range(0.05, 1.5)) = 0.45
        _SwashUprush ("Uprush share of the cycle", Range(0.15, 0.6)) = 0.35
        _EdgeFoam ("Leading edge foam", Range(0, 2)) = 0.85
        _FilmColor ("Water sheet colour", Color) = (0.42, 0.66, 0.64, 1)
        _FilmAlpha ("Water sheet opacity", Range(0, 1)) = 0.32
        [Normal][NoScaleOffset] _RippleNormal ("Ripple normals", 2D) = "bump" {}
        _RippleTiling ("Ripple tile (m)", Float) = 2.4

        [Header(Wet sand)]
        _WetColor ("Wet sand tint", Color) = (0.20, 0.15, 0.10, 1)
        _WetDarkness ("Wet darkening", Range(0, 1)) = 0.42
        _DryTime ("Drying time (s)", Float) = 6

        [Header(Lighting)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.9
        _ReflectionStrength ("Sky reflection", Range(0, 1.5)) = 0.9
        _SpecStrength ("Sun glint", Range(0, 4)) = 1.2
        _ViewBias ("Pull towards the camera (m)", Range(0, 0.5)) = 0.06
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-9" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardShore"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            Offset -1, -1
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
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_RippleNormal); SAMPLER(sampler_RippleNormal);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShoreMapRect;
                float _ShoreWaveLength, _ShoreWavePeriod;
                float _FoamTiling;
                half4 _FoamColor;
                half _BreakDepth, _SurfFoam, _ShoreFoam;
                half _SwashHeight, _SwashUprush, _EdgeFoam;
                half4 _FilmColor;
                half _FilmAlpha;
                float _RippleTiling;
                half4 _WetColor;
                half _WetDarkness;
                float _DryTime;
                half _Smoothness, _ReflectionStrength, _SpecStrength;
                float _ViewBias;
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

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionWS = ws;                                         // shading position: on the sand
                float3 toCam = _WorldSpaceCameraPos - ws;
                float dist = length(toCam);
                // along the view ray: same pixel, just closer, so terrain LOD never covers the sheet
                float3 drawn = ws + toCam / max(dist, 1e-3) * min(_ViewBias + dist * 0.002, dist * 0.5);
                o.positionCS = TransformWorldToHClip(drawn);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // run-up height of this cycle's swash at phase p (0 = the wave reaches the waterline)
            float RunUp(float p, float reach, float uprush)
            {
                float up = 1.0 - (1.0 - p / uprush) * (1.0 - p / uprush);        // fast, slowing (ease out)
                float q = saturate((p - uprush) / (1.0 - uprush));
                float down = 1.0 - q * q;                                         // slow start, drains faster
                return reach * (p < uprush ? up : down);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 ws = i.positionWS;
                float2 xz = ws.xz;
                float t = PF_WaterNow();
                float3 V = GetWorldSpaceNormalizeViewDir(ws);

                float2 muv = PF_MapUV(xz, _ShoreMapRect);
                PFShore s = PF_DecodeShore(SAMPLE_TEXTURE2D_LOD(_ShoreMap, sampler_ShoreMap, saturate(muv), 0), PF_MapInside(muv, _ShoreMapRect));
                float h = s.height;
                float depth = max(0.0, -h);
                float period = max(0.5, _ShoreWavePeriod);

                // ---- breaking waves (over the water)
                float phase = PF_ShorePhase(s.dist, xz, t, _ShoreWaveLength, period);
                float u = frac(phase);
                float breakK = saturate((_BreakDepth + 0.5 - depth) / 0.5) * saturate(depth / 0.05) * s.inside;
                // each wave breaks in patches (tens of metres, different for every wave), never as one ruler line
                float segment = smoothstep(0.4, 0.75, PF_Noise(xz * 0.045 + floor(phase) * 3.1)) * lerp(0.5, 1.0, PF_Noise(xz * 0.13 - floor(phase) * 1.7));
                float crestFoam = exp(-u * 16.0) + exp(-(1.0 - u) * 40.0);
                float trail = exp(-u * 3.5) * 0.6;
                float amount = breakK * (crestFoam * 0.9 + trail) * segment * _SurfFoam;

                // ---- swash: every wave that reaches the waterline runs up the sand, each with its own reach
                float n = PF_ShoreNoise(xz);                                                    // same as PF_ShorePhase at dist 0
                float cyc = t / period + n;
                float p = frac(cyc);
                float ci = floor(cyc);
                float along = PF_Noise(xz * 0.03);
                float reach = _SwashHeight * (0.6 + 0.4 * lerp(PF_Hash11(ci * 1.37), PF_Hash11(ci * 1.37 + 0.5), along));
                float R = RunUp(p, reach, _SwashUprush);
                float hEff = h + (PF_Noise(xz * 0.45) - 0.5) * 0.12 * reach + (PF_Noise(xz * 1.3 + 7.0) - 0.5) * 0.03;   // lobed front
                float th = R - hEff;                                                            // water thickness
                float q = saturate((p - _SwashUprush) / (1.0 - _SwashUprush));                  // 0 uprush .. 1 drained
                float onSand = smoothstep(-0.08, 0.0, h);
                float film = smoothstep(0.0, 0.035, th) * onSand * (1.0 - 0.45 * q) * s.inside;
                // leading edge: a band ~10 cm of water deep, broken along the shore, strongest while the water runs up
                float edgeBreak = lerp(0.35, 1.0, smoothstep(0.25, 0.75, PF_Noise(xz * 0.8 + ci * 5.3)));
                float edgeFoam = (1.0 - smoothstep(0.0, 0.10, th)) * smoothstep(-0.01, 0.02, th) * (p < _SwashUprush ? 1.0 : 1.0 - q * 0.8) * saturate(R / 0.03) * edgeBreak;
                float waterHere = max(1.0 - smoothstep(-0.03, 0.0, h), saturate(film * 3.0));      // under the sea or under the sheet
                float innerSurf = saturate(1.0 - depth / 0.35) * waterHere * (1.0 - 0.6 * q) * 0.5;
                amount += (innerSurf * _ShoreFoam + edgeFoam * _EdgeFoam) * s.inside + film * 0.15 * (1.0 - q);

                // ---- wet sand: covered a moment ago, dries in _DryTime seconds; the band the waves reach stays damp
                float hr = saturate(hEff / max(reach, 1e-3));
                float pDown = _SwashUprush + (1.0 - _SwashUprush) * sqrt(saturate(1.0 - hr));
                float tau = p > pDown ? p - pDown : p + 1.0 - pDown;
                float wetNow = th > 0.0 ? 1.0 : hEff < reach ? exp(-tau * period / max(0.5, _DryTime)) : 0.0;   // under the sheet: soaked
                float damp = saturate(1.0 - hEff / (_SwashHeight * 1.2)) * 0.55;
                float wet = max(damp, wetNow) * smoothstep(-0.05, 0.02, h) * s.inside;

                // ---- surface normal of the sheet: ripples carried up and back with the run-up (continuous in time)
                float2 ruv = (xz - s.toShore * (R / max(reach, 1e-3)) * 1.5) / max(0.2, _RippleTiling) + PF_WindDir() * (t * 0.02);
                float2 slope = PF_Slope(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, ruv), 0.35h);
                float3 N = PF_NormalFromSlope(slope);
                half ndv = (half)saturate(dot(N, V));

                // ---- light
                Light sun = PF_WaterMainLight(ws);
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half3 sunCol = sun.color * shadow;
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 diffuse = sunCol * (half)saturate(sun.direction.y * 0.9 + 0.1) + ambient;
                half3 sky = PF_SkyReflection(N, V, _Smoothness) * _ReflectionStrength;
                half spec = PF_SunSpec(N, V, sun.direction, _Smoothness) * _SpecStrength;
                half F = PF_Fresnel(ndv, 0.02h, 5.0h);

                // ---- layers, bottom to top: wet sand, water sheet, foam (premultiplied)
                half3 rgb = half3(0, 0, 0); half a = 0.0h;
                half wa = (half)wet * _WetDarkness;
                half gloss = (half)(wetNow * smoothstep(-0.05, 0.02, h) * s.inside);                  // freshly wet sand shines, damp sand is matte
                half3 wetRgb = _WetColor.rgb * diffuse * wa + (sky * F * 0.5h + sunCol * spec * 0.35h) * gloss;
                PF_Over(rgb, a, wetRgb, wa);
                half fa = (half)film * _FilmAlpha;
                half filmCover = (half)film * F + fa * (1.0h - F);
                half3 filmRgb = _FilmColor.rgb * diffuse * fa * (1.0h - F) + (sky * F + sunCol * spec) * (half)film;
                PF_Over(rgb, a, filmRgb, filmCover);
                half ft = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, xz / max(0.3, _FoamTiling) + float2(-t * 0.012, t * 0.007)).r;
                half fs = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, xz / max(0.3, _FoamTiling * 0.37) + 0.41).g;
                half am = (half)saturate(amount);
                half foamTex = ft * 0.75h + fs * 0.25h;
                half foam = smoothstep(1.0h - am, 1.0h - am + 0.35h, foamTex) * saturate(am * 3.0h);
                half3 foamRgb = _FoamColor.rgb * (sunCol * (half)saturate(sun.direction.y + 0.15) + ambient * 1.1h) * foam;
                PF_Over(rgb, a, foamRgb, foam);
                return PF_WaterOutput(rgb, a, i.fogFactor);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
