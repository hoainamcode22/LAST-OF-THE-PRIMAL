// PRIMAL FRONTIER - night sky: procedural stars (hash of the view direction, two layers, slow twinkle) and a moon disc,
// drawn additively on a camera-centred dome (NightSky component). Faded by _PF_NightFactor (TimeManager: 0 by day),
// hidden by cloud cover (_PF_Overcast) and below the horizon. No fog, no lighting, no depth write.
// Draw order: queue Transparent-499 runs after the skybox (URP draws the skybox after opaques) and before every other
// transparent; the vertex shader puts the dome on the far plane, so ZTest LEqual only passes where nothing opaque was
// drawn (the sky). Water / particles drawn later blend over it normally. By day the component turns the renderer off.
Shader "PF/Night Sky"
{
    Properties
    {
        _StarDensity ("Star density", Range(0, 1)) = 0.35
        _StarBrightness ("Star brightness", Float) = 1.6
        _StarSize ("Star size (fraction of a cell)", Range(0.02, 0.2)) = 0.12
        _TwinkleSpeed ("Twinkle speed", Float) = 2.5
        _HorizonFade ("Horizon fade (sine of elevation)", Range(0.01, 0.6)) = 0.18
        _MoonColor ("Moon colour", Color) = (0.86, 0.9, 1, 1)
        _MoonSize ("Moon angular radius (rad)", Range(0, 0.1)) = 0.022
        _MoonGlow ("Moon glow", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-499" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Skybox" "DisableBatching"="True" }
        Pass
        {
            Name "NightSky"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _StarDensity;
                float _StarBrightness;
                float _StarSize;
                float _TwinkleSpeed;
                float _HorizonFade;
                float4 _MoonColor;
                float _MoonSize;
                float _MoonGlow;
            CBUFFER_END

            float _PF_NightFactor;     // TimeManager: 0 day .. 1 night
            float _PF_Overcast;        // WeatherManager: cloud cover 0..1
            float _PF_StarRotation;    // NightSky: radians, one turn per in-game day
            float4 _PF_MoonDir;        // NightSky: xyz direction to the moon, w 1 while the moon is up

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                float3 dir = normalize(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(_WorldSpaceCameraPos + dir * 100.0);
                // on the far plane (clip-space z = far): only the sky pixels pass the depth test
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1e-6;
                #else
                    o.positionCS.z = o.positionCS.w * (1.0 - 1e-6);
                #endif
                o.dir = dir;
                return o;
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float3 RotateAxis(float3 v, float3 k, float a)
            {
                float s, c;
                sincos(a, s, c);
                return v * c + cross(k, v) * s + k * dot(k, v) * (1.0 - c);
            }

            // one possible star per grid cell at a random point inside it; px = one screen pixel in cell units
            float StarLayer(float3 d, float scale, float density, float px, float t)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float3 h = Hash33(cell);
                float3 star = cell + 0.25 + 0.5 * Hash33(cell + 19.19);
                float dist = length(p - star);
                float r = _StarSize * (0.4 + h.y);
                float rr = clamp(max(r, px), 1e-4, 0.25);                   // at least a pixel wide: no sparkle
                float s = saturate(1.0 - dist / rr);
                s *= s * saturate(r / rr + 0.25);                           // sub-pixel stars get dimmer, not bigger
                float twinkle = 0.75 + 0.25 * sin(t * (0.6 + h.z) + h.y * 40.0);
                return step(h.x, density) * s * twinkle * (0.3 + 0.7 * h.z * h.z);
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float pxBase = max(length(fwidth(d)), 1e-5);                // derivatives before any branch
                float night = saturate(_PF_NightFactor) * (1.0 - saturate(_PF_Overcast) * 0.9);
                float fade = night * smoothstep(-0.02, _HorizonFade, d.y);
                if (fade <= 0.0005)
                    return half4(0, 0, 0, 0);

                // stars turn slowly around a pole low in the north
                float3 rd = RotateAxis(d, float3(0.0, 0.342, 0.940), _PF_StarRotation);
                float t = _Time.y * _TwinkleSpeed;
                float s = StarLayer(rd, 110.0, _StarDensity * 0.15, pxBase * 110.0, t)
                        + StarLayer(rd, 220.0, _StarDensity * 0.12, pxBase * 220.0, t * 1.3) * 0.45;
                float3 col = float3(0.85, 0.9, 1.0) * s * _StarBrightness;

                // moon: disc (hides the stars behind it) + faint glow
                float3 md = _PF_MoonDir.xyz;
                float up = _PF_MoonDir.w * step(0.001, dot(md, md)) * step(0.0001, _MoonSize);
                float ang = length(d - md);                                 // chord ~ angle for small angles
                float disc = (1.0 - smoothstep(_MoonSize - pxBase, _MoonSize + pxBase, ang)) * up;
                float g = saturate(1.0 - ang / max(_MoonSize * 10.0, 1e-4));
                float glow = g * g * _MoonGlow * up;
                col = col * (1.0 - disc) + _MoonColor.rgb * (disc * 1.3 + glow);

                return half4(col * fade, 0.0);
            }
            ENDHLSL
        }
    }
}
