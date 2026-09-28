// PRIMAL FRONTIER - heat shimmer over the volcano crater and lava (VolcanoLandmark particles, vertical billboards).
// Safe option, no distortion: a real refraction needs URP's Opaque Texture, which this project does not guarantee.
// Faint alpha-blended columns of rising two-octave value noise with soft edges; the upper part of each quad wobbles
// sideways. Pale by day, warm by night (_PF_NightFactor from TimeManager). Unlit, fog applied, no depth write.
// Per-particle variation from the StableRandom.x vertex stream (TEXCOORD0.z, set by VolcanoLandmark); 0 without it.
Shader "PF/Heat Shimmer"
{
    Properties
    {
        _DayColor ("Day colour", Color) = (0.86, 0.82, 0.76, 1)
        _NightColor ("Night colour", Color) = (1, 0.5, 0.2, 1)
        _Alpha ("Strength", Range(0, 1)) = 0.14
        _NoiseScale ("Noise scale (x across, y up)", Vector) = (3, 5, 0, 0)
        _RiseSpeed ("Rise speed", Float) = 0.3
        _Wobble ("Wobble at the top (m)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DayColor;
                half4 _NightColor;
                half _Alpha;
                float4 _NoiseScale;
                float _RiseSpeed;
                float _Wobble;
            CBUFFER_END
            float _PF_NightFactor;     // TimeManager: 0 day .. 1 night

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float3 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float3 uv : TEXCOORD0; half fog : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float3 right = UNITY_MATRIX_V[0].xyz;                       // camera right in world space
                float w = sin(_Time.y * 1.3 + i.uv.y * 4.0 + i.uv.z * 6.2832) * _Wobble * i.uv.y;
                ws += right * w;
                o.positionCS = TransformWorldToHClip(ws);
                o.color = i.color;
                o.uv = i.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise(float2 p)
            {
                float2 c = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash12(c), b = Hash12(c + float2(1, 0)), d = Hash12(c + float2(0, 1)), e = Hash12(c + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(d, e, u.x), u.y);
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.uv.xy;
                float seed = i.uv.z * 17.0;
                float t = _Time.y * _RiseSpeed;
                float2 q = float2(uv.x * _NoiseScale.x + seed, (uv.y - t) * _NoiseScale.y);
                float n = Noise(q) * 0.65 + Noise(q * 2.3 + float2(5.2, -t * _NoiseScale.y)) * 0.35;
                float side = saturate(1.0 - abs(uv.x * 2.0 - 1.0));
                float vert = smoothstep(0.0, 0.2, uv.y) * (1.0 - smoothstep(0.55, 1.0, uv.y));
                half a = (half)(side * side * vert * smoothstep(0.35, 0.85, n)) * _Alpha * i.color.a;
                half3 col = lerp(_DayColor.rgb, _NightColor.rgb, (half)saturate(_PF_NightFactor)) * i.color.rgb;
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
