// PRIMAL FRONTIER - fresh water that moves: the stream flowing downhill and the calm pond (same shader, other numbers).
// Flow comes from a flow map PrimalWaterBuilder bakes per water body (_FlowMap / _FlowMapRect): RG flow direction x
// speed (downstream along the channel, faster where it is steep), B distance to the bank or a rock (0..3 m), A water
// depth (0..2 m, 0 where another water body takes over).
// Surface: a streak texture (_StreakTex, stretched along +x) is laid along the local flow (angle blended between the two
// nearest of 12 fixed directions, so the pattern is continuous) and scrolled downstream at the local speed with two
// cross-faded time phases (_FlowCycle), so streaks line up with the current and visibly move, longer where it runs fast.
// Fine wind ripples and broad slow waves on top (the pond is mostly these).
// Body: clear water; light through it is absorbed per channel (_AbsorbColor, red first) and scattered back
// (_ScatterColor), so the bed turns green-blue with depth. Foam only in thin bands: along banks and rocks, on steep
// rapids and very shallow running water. Sky / probe reflection with fresnel; on PC (WaterGlobals: depth + opaque
// textures) screen-space reflection of trees and banks, refraction of the bed, scene-depth soft edges.
// Rain rings (_PF_Rain). Premultiplied alpha, fogged. Debug views: _PF_WaterDebug (PrimalWaterBuilder.Preview).
Shader "PF/Water Flow"
{
    Properties
    {
        [Header(Baked flow map)]
        [NoScaleOffset] _FlowMap ("Flow map (PrimalWaterBuilder)", 2D) = "gray" {}
        _FlowMapRect ("Flow map rect (min x, min z, 1/size x, 1/size z)", Vector) = (0, 0, 0, 0)
        _BankRange ("Bank distance range baked (m)", Float) = 3
        _DepthRange ("Depth range baked (m)", Float) = 2

        [Header(Flow)]
        _FlowSpeed ("Flow speed at full (m/s)", Float) = 1.3
        _WindDrift ("Wind drift (m/s)", Float) = 0.05
        _FlowCycle ("Pattern cycle (s)", Range(0.5, 6)) = 2
        _Stretch ("Streak stretch when fast", Range(1, 6)) = 4
        [NoScaleOffset] _StreakTex ("Flow streaks (RG slope, B foam streaks, A bubbles)", 2D) = "gray" {}
        _StreakTiling ("Streak tile across (m)", Float) = 2.3
        _StreakStrength ("Streak strength", Range(0, 2)) = 0.32

        [Header(Surface detail)]
        [Normal][NoScaleOffset] _RippleNormal ("Ripple normals", 2D) = "bump" {}
        _RippleTiling ("Ripple tile (m)", Float) = 1.4
        _RippleStrength ("Ripple strength", Range(0, 2)) = 0.12
        [Normal][NoScaleOffset] _WaveNormal ("Wave normals", 2D) = "bump" {}
        _WaveTiling ("Broad wave tile (m)", Float) = 7
        _WaveStrength ("Broad wave strength", Range(0, 2)) = 0.1

        [Header(Water body)]
        _AbsorbColor ("Absorption per metre (rgb)", Vector) = (0.9, 0.34, 0.27, 0)
        _ScatterColor ("Scattered light colour", Color) = (0.06, 0.12, 0.11, 1)
        _Opacity ("Opacity without refraction (mobile)", Range(0, 2)) = 1.1
        _EdgeSoftness ("Soft edge (m of depth)", Range(0.01, 1)) = 0.08
        [NoScaleOffset] _FoamTex ("Foam (R lace, G specks, B macro, A caustics)", 2D) = "black" {}
        _CausticsStrength ("Caustics", Range(0, 2)) = 0.5
        _CausticsTiling ("Caustics size (m)", Float) = 1.8

        [Header(White water)]
        _FoamColor ("Foam colour", Color) = (0.93, 0.95, 0.94, 1)
        _BankFoam ("Foam along banks and rocks", Range(0, 2)) = 0.8
        _BankFoamWidth ("Bank foam band width (m)", Range(0.05, 1.5)) = 0.3
        _RapidsFoam ("Foam on steep rapids", Range(0, 2)) = 0.45
        _ShallowFoam ("Foam on very shallow running water", Range(0, 2)) = 0.35

        [Header(Lighting)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.94
        _FresnelPower ("Fresnel power", Range(1, 8)) = 5
        _ReflectionStrength ("Reflection", Range(0, 1.5)) = 0.9
        _SSRStrength ("Screen reflection (PC)", Range(0, 1)) = 1
        _SpecStrength ("Sun glint", Range(0, 4)) = 1.3
        _RefractionStrength ("Refraction (PC)", Range(0, 0.2)) = 0.035

        [Header(Rain)]
        [NoScaleOffset] _RainRippleTex ("Rain rings", 2D) = "black" {}
        _RainTiling ("Rain ring tile (m)", Float) = 1.6
        _RainStrength ("Rain rings", Range(0, 2)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardWaterFlow"
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

            TEXTURE2D(_FlowMap); SAMPLER(sampler_FlowMap);
            TEXTURE2D(_StreakTex); SAMPLER(sampler_StreakTex);
            TEXTURE2D(_RippleNormal); SAMPLER(sampler_RippleNormal);
            TEXTURE2D(_WaveNormal); SAMPLER(sampler_WaveNormal);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_RainRippleTex); SAMPLER(sampler_RainRippleTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FlowMapRect;
                float _BankRange, _DepthRange;
                float _FlowSpeed, _WindDrift, _FlowCycle, _Stretch;
                float _StreakTiling;
                half _StreakStrength;
                float _RippleTiling, _WaveTiling;
                half _RippleStrength, _WaveStrength;
                float4 _AbsorbColor;
                half4 _ScatterColor;
                half _Opacity, _EdgeSoftness, _CausticsStrength;
                float _CausticsTiling;
                half4 _FoamColor;
                half _BankFoam, _BankFoamWidth, _RapidsFoam, _ShallowFoam;
                half _Smoothness, _FresnelPower, _ReflectionStrength, _SSRStrength, _SpecStrength, _RefractionStrength;
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

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionWS = p.positionWS;
                o.positionCS = p.positionCS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // flow map at a world point (outside the baked area: still, deep, far from any bank)
            float4 FlowAt(float2 xz)
            {
                float2 uv = PF_MapUV(xz, _FlowMapRect);
                return PF_MapInside(uv, _FlowMapRect) > 0.5 ? SAMPLE_TEXTURE2D_LOD(_FlowMap, sampler_FlowMap, saturate(uv), 0) : float4(0.5, 0.5, 1.0, 1.0);
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
                float2 wind = PF_WindDir();
                float windK = PF_WindStrength();

                // ---- baked data here
                float4 m = FlowAt(xz);
                float2 flow = m.rg * 2.0 - 1.0;
                float speed01 = saturate(length(flow));
                float bank = m.b * _BankRange;
                float mapDepth = m.a * _DepthRange;
                float depth = min(mapDepth, PF_SceneWaterDepth(screenUV, viewDepth, V));

                // ---- directional flow: the streak pattern is laid out along the local flow. The flow angle is blended between
                // the two nearest of 12 fixed directions (patterns stay continuous, no cells, no seams) and each is scrolled
                // downstream with two time phases cross-faded every _FlowCycle seconds (no shearing where speed changes).
                float2 dirF = normalize(flow + wind * 0.03);                                    // continuous, wind when still
                float ang = atan2(dirF.y, dirF.x) / (PI / 6.0);
                float k0 = floor(ang);
                float fa = ang - k0;
                float vel = speed01 * _FlowSpeed + _WindDrift * (0.6 + 0.6 * windK);
                float stretch = lerp(1.0, _Stretch, saturate(speed01 * 1.6));
                float cyc = max(0.3, _FlowCycle);
                float ph0 = frac(t / cyc), ph1 = frac(t / cyc + 0.5);
                float wp1 = abs(1.0 - 2.0 * ph0);                                                 // phase 0 fades out as it resets
                float2 slope = float2(0.0, 0.0);
                half streakFoam = 0.0h, bubbles = 0.0h;
                float wsq = 0.0;
                [unroll]
                for (int c = 0; c < 4; c++)
                {
                    float kk = k0 + (float)(c & 1);
                    float ph = (c >> 1) == 0 ? ph0 : ph1;
                    float w = ((c & 1) == 0 ? 1.0 - fa : fa) * ((c >> 1) == 0 ? 1.0 - wp1 : wp1);
                    float2 dir = float2(cos(kk * (PI / 6.0)), sin(kk * (PI / 6.0)));
                    float2 perp = float2(-dir.y, dir.x);
                    float km = kk - 12.0 * floor(kk / 12.0);                                    // 0..11, same pattern at +-180 wrap
                    float2 jitter = float2(PF_Hash11(km * 1.7 + 0.3), PF_Hash11(km * 2.3 + 0.7)) + ((c >> 1) == 0 ? 0.0 : 0.5);
                    float2 p = float2(dot(xz, dir), dot(xz, perp));
                    float2 suv = float2((p.x - vel * ph * cyc) / (_StreakTiling * stretch), p.y / _StreakTiling) + jitter;
                    half4 st = SAMPLE_TEXTURE2D(_StreakTex, sampler_StreakTex, suv);
                    float2 n = (st.rg * 2.0 - 1.0) * (_StreakStrength * (0.45 + speed01));
                    slope += (dir * n.x + perp * n.y) * w;
                    streakFoam += (st.b - 0.234h) * (half)w;                                     // around the texture mean
                    bubbles += (st.a - 0.114h) * (half)w;
                    wsq += w * w;
                }
                // variance-preserving blend: mixing two patterns keeps the contrast of one (no washed-out zones)
                half contrast = (half)rsqrt(max(wsq, 0.25));
                slope *= contrast;
                streakFoam = saturate(0.234h + streakFoam * contrast);
                bubbles = saturate(0.114h + bubbles * contrast);
                // broad slow waves, then fine wind ripples and rain rings (detail only: they bend the reflection but do not
                // change how much the surface reflects, so a rippled or rained-on pond stays a bright mirror)
                float2 broad = PF_Slope(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, xz / max(0.5, _WaveTiling) + wind * (t * 0.012) + 0.3), _WaveStrength);
                float calmBank = lerp(0.45, 1.0, saturate(bank / 0.5));                           // stiller right at the bank
                float2 baseSlope = (broad + slope * 0.4) * calmBank;
                slope += broad;
                slope += PF_Slope(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, xz / max(0.2, _RippleTiling) + wind * (t * 0.03)), _RippleStrength * (0.6h + 0.5h * (half)windK));
                slope += PF_RainRipples(TEXTURE2D_ARGS(_RainRippleTex, sampler_RainRippleTex), xz, _RainTiling, _RainStrength, t);
                slope *= calmBank;
                float3 N = PF_NormalFromSlope(slope);
                float3 Nf = PF_NormalFromSlope(baseSlope);
                half ndv = (half)saturate(dot(Nf, V));

                // ---- white water: thin bands at banks / rocks, steep rapids, very shallow running water
                // a band that starts just inside the soft edge (so it is not faded away) and dies out 'width' further in
                half bankBand = (half)(smoothstep(0.04, 0.16, bank) * (1.0 - smoothstep(0.3, 0.3 + max(0.05, _BankFoamWidth), bank)));
                half rapids = (half)smoothstep(0.7, 1.0, speed01);
                half shallowRun = (1.0h - (half)smoothstep(0.02, 0.1, mapDepth)) * (half)saturate(speed01 * 1.5);
                half bankAmt = bankBand * _BankFoam * (0.5h + 0.5h * (half)speed01);
                half amount = saturate(bankAmt + rapids * _RapidsFoam + shallowRun * _ShallowFoam) * (half)saturate(mapDepth / 0.02);
                // streak lines on open water, bubbly lace along banks and rocks
                half foamTex = saturate(lerp(streakFoam * 0.8h + bubbles * 0.5h, max(streakFoam, bubbles * 1.8h), saturate(bankAmt)));
                half foam = smoothstep(1.0h - amount * 0.75h, 1.15h - amount * 0.75h, foamTex) * saturate(amount * 3.0h);

                // ---- light
                Light sun = PF_WaterMainLight(ws);
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half3 sunCol = sun.color * shadow;
                half sunUp = (half)saturate(sun.direction.y);
                half3 ambient = SampleSH(half3(0, 1, 0));

                // ---- water body: absorption along the view path through the water, light scattered back
                float path = depth / max(0.25, V.y);
                half3 T = PF_Transmittance((half3)_AbsorbColor.rgb, path);
                half Tavg = (T.r + T.g + T.b) * (1.0h / 3.0h);
                half3 scatter = _ScatterColor.rgb * (sunCol * (sunUp * 0.6h + 0.15h) + ambient);
                scatter += sunCol * (half)(speed01 * speed01) * 0.06h;                             // aerated, brighter rapids
                half caus = 0.0h;
                UNITY_BRANCH
                if (_CausticsStrength > 0.001 && depth < 2.0)
                {
                    float ct = max(0.3, _CausticsTiling);
                    float2 cuv = xz / ct + N.xz * 0.1 + flow * (t * 0.15);
                    half c1 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, cuv + float2(t * 0.03, t * 0.017)).a;
                    half c2 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, cuv * 1.37 + float2(-t * 0.021, t * 0.025) + 0.5).a;
                    caus = min(c1, c2) * 2.0h * _CausticsStrength * (half)(saturate(1.0 - depth / 2.0) * saturate(depth / 0.08)) * sunUp;
                }
                half edge = (half)(saturate(depth / max(0.01, _EdgeSoftness)) * saturate(bank / 0.25));
                // mobile: grey absorption only (alpha blend); PC: the refracted bed, tinted per channel
                half belowA = saturate((1.0h - Tavg) * _Opacity) * edge;
                half3 below = scatter * belowA + sunCol * caus * Tavg * edge;
                UNITY_BRANCH
                if (_PF_WaterRefraction > 0.5)
                {
                    half k = (half)saturate(depth / 0.25) * edge;
                    float2 off = N.xz * (_RefractionStrength * saturate(depth / 0.6)) / (1.0 + viewDepth * 0.05);
                    half3 bed = PF_Refracted(screenUV, off, viewDepth);
                    half3 refr = bed * T + scatter * (1.0h - T) + sunCol * caus * T;
                    below = lerp(below, refr, k);
                    belowA = lerp(belowA, 1.0h, k);
                }

                // ---- reflection: sky / probe, trees and banks from the screen on PC
                half3 sky = PF_SkyReflection(N, V, _Smoothness);
                half3 refl = sky;
                UNITY_BRANCH
                if (_SSRStrength > 0.001)
                {
                    float3 Nr = normalize(lerp(N, float3(0, 1, 0), 0.6));                        // steadier mirror image
                    half4 ssr = PF_ScreenReflection(ws + float3(0.0, 0.02, 0.0), reflect(-V, Nr), 40.0);
                    refl = lerp(sky, ssr.rgb, ssr.a * _SSRStrength);
                }
                refl *= _ReflectionStrength;
                half F = PF_Fresnel(ndv, 0.03h, _FresnelPower) * edge;
                half spec = PF_SunSpec(N, V, sun.direction, _Smoothness) * _SpecStrength * edge;
                half3 rgb = refl * F + sunCol * spec + below * (1.0h - F);
                half a = F + belowA * (1.0h - F);
                half fo = foam * (half)(saturate(depth / 0.03) * saturate(bank / 0.1));
                half3 foamRgb = _FoamColor.rgb * (sunCol * (half)saturate(sun.direction.y + 0.15) + ambient * 1.1h) * fo;
                PF_Over(rgb, a, foamRgb, fo);
                UNITY_BRANCH
                if (_PF_WaterDebug > 0.5)
                {
                    float dm = _PF_WaterDebug;
                    if (dm < 1.5) return half4(saturate(mapDepth / 2.0), saturate(PF_SceneWaterDepth(screenUV, viewDepth, V) / 2.0), saturate(bank / 3.0), 1);
                    if (dm < 2.5) return half4(refl, 1);
                    if (dm < 3.5) return half4(a, a, a, 1);
                    if (dm < 4.5) return half4(speed01, foam, F * 4.0, 1);
                    if (dm < 5.5) return half4(below / max(belowA, 0.01h), 1);
                    if (dm < 6.5) return half4(rgb, 1);
                    if (dm < 7.5) return half4(refl * F, 1);
                    return half4(F, ndv, edge, 1);
                }
                return PF_WaterOutput(rgb, a, i.fogFactor);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
