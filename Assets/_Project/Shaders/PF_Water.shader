// PRIMAL FRONTIER - water for the sea, the pond and the stream. No depth texture needed (mobile friendly):
// two scrolling layers of a tiling wave normal map in world space, sky reflection with fresnel, sun glint,
// deep / shallow colour, wind from the weather (_PF_Wind) speeds the waves up, rain (_PF_Wetness while it rains)
// adds small ripple rings. Transparent, fogged like the rest of the scene.
Shader "PF/Water"
{
    Properties
    {
        _ShallowColor ("Shallow (looking down)", Color) = (0.10, 0.32, 0.34, 0.55)
        _DeepColor ("Deep (grazing)", Color) = (0.03, 0.14, 0.18, 0.9)
        [Normal] _NormalMap ("Wave normals", 2D) = "bump" {}
        _NormalScale ("Wave strength", Range(0, 2)) = 0.6
        _Tiling ("Wave size (m)", Float) = 6
        _Speed ("Wave speed", Float) = 0.03
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92
        _FresnelPower ("Fresnel power", Range(0.5, 8)) = 4
        _ReflectionStrength ("Sky reflection", Range(0, 1.5)) = 0.85
        _SpecStrength ("Sun glint", Range(0, 4)) = 1.2
        _RainRipples ("Rain ripples", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardWater"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor;
                float4 _NormalMap_ST;
                half _NormalScale, _Smoothness, _FresnelPower, _ReflectionStrength, _SpecStrength, _RainRipples;
                float _Tiling, _Speed;
            CBUFFER_END
            float4 _PF_Wind;       // xy direction, z strength, w gust (WeatherManager)
            float _PF_Wetness;     // 0..1

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fogFactor : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // rain: expanding rings in a grid of random cells
            half2 Ripples(float2 p, float t)
            {
                float2 cell = floor(p); float2 f = frac(p) - 0.5;
                float h = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                float2 c = (float2(frac(h * 7.1), frac(h * 3.7)) - 0.5) * 0.5;
                float ph = frac(t * 0.9 + h);
                float2 d = f - c; float r = length(d);
                float ring = sin((r - ph * 0.45) * 60.0) * saturate(1.0 - ph) * saturate(1.0 - abs(r - ph * 0.45) * 12.0);
                return r > 1e-4 ? d / r * ring : float2(0, 0);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float wind = _PF_Wind.z > 0 ? _PF_Wind.z : 0.4;
                float2 dir = dot(_PF_Wind.xy, _PF_Wind.xy) > 0.01 ? normalize(_PF_Wind.xy) : float2(0.8, 0.6);
                float t = _Time.y * _Speed * (0.7 + wind * 0.6);
                float2 uv = i.positionWS.xz / max(0.5, _Tiling);
                half strength = _NormalScale * (0.7 + wind * 0.4 + _PF_Wind.w * 0.2);
                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv + dir * t), strength);
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 1.73 + float2(-dir.y, dir.x) * t * 1.3 + 0.37), strength * 0.7);
                half3 n3 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 0.19 + dir * t * 0.35 + 0.71), strength * 0.8);   // large swell, breaks tiling far away
                half2 nxy = n1.xy + n2.xy + n3.xy;
                float rain = _PF_Wetness * _RainRipples;
                if (rain > 0.01) nxy += Ripples(i.positionWS.xz * 1.6, _Time.y) * rain * 0.6 + Ripples(i.positionWS.xz * 1.6 + 0.5, _Time.y + 0.37) * rain * 0.6;
                float3 N = normalize(float3(nxy.x, 1.0, nxy.y));                // world: the water is flat (y up)
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half ndv = saturate(dot(N, V));
                half fres = 0.04 + 0.96 * pow(1.0 - ndv, _FresnelPower);
                Light sun = GetMainLight();
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, ndv) * (sun.color * saturate(sun.direction.y) * 0.55 + ambient);
                half perceptualRoughness = 1.0 - _Smoothness;
                half3 R = reflect(-V, N);
                R.y = max(R.y, 0.04); R = normalize(R);                          // wave backs reflect the sky, not the ground half of the probe
                half3 sky = GlossyEnvironmentReflection(R, perceptualRoughness, 1.0) * _ReflectionStrength;
                half3 H = normalize(sun.direction + V);
                half spec = pow(saturate(dot(N, H)), lerp(64.0, 900.0, _Smoothness)) * _SpecStrength * saturate(sun.direction.y * 4.0);
                half3 col = lerp(body, sky, fres) + sun.color * spec;
                half a = saturate(lerp(_ShallowColor.a, _DeepColor.a, 1.0 - ndv) + fres * 0.5 + spec);
                col = MixFog(col, i.fogFactor);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
