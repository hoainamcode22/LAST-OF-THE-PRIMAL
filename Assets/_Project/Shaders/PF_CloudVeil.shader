// PRIMAL FRONTIER - weather clouds (WORLD): a procedural cloud layer drawn over the sky on a camera-centred dome
// (VFX/CloudVeil). Cover follows the weather (_PF_Overcast 0 clear .. 0.5 broken clouds .. 1 a closed grey deck), the
// clouds drift with the weather wind (_PF_Wind), take the air colour of the hour (unity_FogColor: golden at sunset, blue
// at night, rain grey), are lit on the sun side, go dark in storms (_PF_StormK) and flash with lightning (_PF_Flash).
// Draw order: queue Transparent-498, after the skybox and the night sky (-499), so clouds hide the stars; the vertex shader
// puts the dome on the far plane, so only sky pixels pass ZTest. No lighting of the scene, no depth write, no fog.
Shader "PF/Cloud Veil"
{
    Properties
    {
        _Scale ("Cloud scale", Float) = 0.9
        _Speed ("Drift speed", Float) = 0.004
        _Opacity ("Opacity at full cover", Range(0, 1)) = 0.96
        _Softness ("Edge softness", Range(0.02, 0.6)) = 0.28
        _SunTint ("Sun light on clouds", Range(0, 1)) = 0.3
        _Darkness ("Storm darkness", Range(0, 1)) = 0.62
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-498" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Skybox" "DisableBatching"="True" }
        Pass
        {
            Name "CloudVeil"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Scale;
                float _Speed;
                float _Opacity;
                float _Softness;
                float _SunTint;
                float _Darkness;
            CBUFFER_END

            float _PF_Overcast;        // WeatherManager: cloud cover 0..1
            float4 _PF_Wind;           // xy direction, z strength, w gust
            float _PF_StormK;          // WeatherManager: 0..1 storm
            float _PF_Flash;           // WeatherManager: lightning 0..1
            float _PF_NightFactor;     // TimeManager: 0 day .. 1 night

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                float3 dir = normalize(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(_WorldSpaceCameraPos + dir * 100.0);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1e-6;
                #else
                    o.positionCS.z = o.positionCS.w * (1.0 - 1e-6);
                #endif
                o.dir = dir;
                return o;
            }

            float PFC_Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float PFC_Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(PFC_Hash(i), PFC_Hash(i + float2(1, 0)), u.x), lerp(PFC_Hash(i + float2(0, 1)), PFC_Hash(i + float2(1, 1)), u.x), u.y);
            }

            float PFC_Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++) { v += a * PFC_Noise(p); p = p * 2.03 + float2(17.1, 9.2); a *= 0.5; }
                return v;
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float cover = saturate(_PF_Overcast);
                if (cover <= 0.01 || d.y < -0.05)
                    return half4(0, 0, 0, 0);
                // a flat cloud deck seen from below: far clouds bunch up towards the horizon
                float2 wind = _PF_Wind.xy * (0.5 + _PF_Wind.z);
                float2 uv = d.xz / (max(d.y, 0.0) + 0.15) * _Scale + wind * (_Time.y * _Speed * 10.0);
                float n = PFC_Fbm(uv);
                float n2 = PFC_Fbm(uv * 2.7 + 5.3);
                float thr = 1.0 - cover * 1.15;                       // 0 -> none, 0.5 -> broken, 1 -> closed deck
                float dens = smoothstep(thr - _Softness * 0.5, thr + _Softness, n);
                float alpha = dens * _Opacity * smoothstep(-0.04, 0.12, d.y);

                Light l = GetMainLight();
                float day = 1.0 - saturate(_PF_NightFactor);
                float sunUp = saturate(l.direction.y * 4.0 + 0.2) * day;
                float facing = pow(saturate(dot(d, l.direction) * 0.5 + 0.5), 3.0);
                float3 baseC = unity_FogColor.rgb;
                float thick = saturate((n - thr) * 1.6) * 0.5 + n2 * 0.25;
                float3 col = baseC * (1.05 - thick * 0.45) + l.color * (_SunTint * sunUp * (0.25 + facing * 0.6) * (1.0 - thick));
                col *= 1.0 - _Darkness * saturate(_PF_StormK) * (0.6 + 0.4 * thick);
                col += float3(0.75, 0.8, 1.0) * saturate(_PF_Flash) * (0.6 + n2);
                col = lerp(baseC, col, smoothstep(0.0, 0.25, d.y));     // far clouds melt into the haze
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
