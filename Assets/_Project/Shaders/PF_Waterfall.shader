// PRIMAL FRONTIER - falling water sheet (ENV, PC phase): the waterfall and the rivulets beside it.
// Mesh: PrimalEnvironmentBuilder.Water (u across 0..1, v = metres along the fall from the lip, vertex colour a = 0 at the lip,
// 1 at the pool). Two streak layers scroll down at different speeds (the water accelerates: speed grows with the fall), a noise
// layer bends them sideways, white water where the streaks bunch up and near the bottom, thin transparent edges, a darker
// clear core near the lip where the water is still smooth. Lit: main light (half-lambert, light through the thin sheet),
// SH ambient, a soft highlight, fog. Transparent, both sides, no depth write, no shadows. Cheap: 3 texture samples.
Shader "PF/Waterfall"
{
    Properties
    {
        _StreakTex ("Streaks (R streaks, G noise, B foam)", 2D) = "gray" {}
        _WaterColor ("Water colour", Color) = (0.55, 0.66, 0.66, 1)
        _FoamColor ("White water", Color) = (0.93, 0.96, 0.97, 1)
        _Opacity ("Opacity", Range(0,1)) = 0.85
        _Speed ("Fall speed (tiles per second at the lip)", Float) = 0.9
        _Accel ("Speed gain along the fall", Float) = 0.12
        _TilingU ("Streaks across", Float) = 2.2
        _TilingV ("Streak length (m per tile)", Float) = 3.0
        _Distort ("Sideways wobble", Range(0,0.3)) = 0.08
        _FoamAmount ("White water", Range(0,2)) = 1
        _BottomFoam ("Foam at the bottom", Range(0,2)) = 1
        _EdgeFade ("Edge fade", Range(0.01,0.5)) = 0.18
        _Translucency ("Light through the sheet", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _StreakTex_ST;
                half4 _WaterColor, _FoamColor;
                half _Opacity; float _Speed, _Accel, _TilingU, _TilingV; half _Distort, _FoamAmount, _BottomFoam, _EdgeFade, _Translucency;
            CBUFFER_END
            TEXTURE2D(_StreakTex); SAMPLER(sampler_StreakTex);
            float4 _PF_Wind;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half3 normalWS : TEXCOORD2;
                half fall : TEXCOORD3; half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float wind = _PF_Wind.z > 0 ? _PF_Wind.z : 0.35;
                float2 dir = dot(_PF_Wind.xy, _PF_Wind.xy) > 0.01 ? normalize(_PF_Wind.xy) : float2(0.8, 0.6);
                ws.xz += dir * (sin(_Time.y * 1.7 + ws.y * 0.6) * 0.05 * wind * i.color.a);          // the sheet sways a little
                o.positionWS = ws; o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv; o.fall = i.color.a;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float t = _Time.y;
                float along = i.uv.y / _TilingV;                        // tiles along the fall
                float speed = _Speed * (1.0 + _Accel * i.uv.y);
                half nz = SAMPLE_TEXTURE2D(_StreakTex, sampler_StreakTex, float2(i.uv.x * 0.7, along * 0.35 - t * speed * 0.35)).g;
                float u = i.uv.x * _TilingU + (nz - 0.5) * _Distort * 4.0;
                half s1 = SAMPLE_TEXTURE2D(_StreakTex, sampler_StreakTex, float2(u, along - t * speed)).r;
                half4 s2 = SAMPLE_TEXTURE2D(_StreakTex, sampler_StreakTex, float2(u * 1.37 + 0.31, along * 1.6 - t * speed * 1.35));
                half streak = s1 * 0.6h + s2.r * 0.4h;
                half fall = i.fall;
                half foam = saturate((streak - 0.52h) * 3.0h) * _FoamAmount * (0.35h + fall);
                foam = max(foam, saturate((fall - 0.72h) * 3.5h) * s2.b * _BottomFoam * 1.4h);
                half edge = smoothstep(0.0h, _EdgeFade, i.uv.x) * smoothstep(1.0h, 1.0h - _EdgeFade, i.uv.x);
                half top = smoothstep(0.0h, 0.05h, fall);
                half alpha = saturate(_Opacity * (0.28h + streak * 0.9h + foam) * edge * top);

                half3 n = normalize(i.normalWS); n = IS_FRONT_VFACE(face, n, -n);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, L.direction);
                half atten = lerp(1.0h, L.shadowAttenuation, 0.6h) * L.distanceAttenuation;
                half3 light = L.color * atten * (saturate(ndl * 0.5h + 0.5h) + saturate(-ndl) * _Translucency) + SampleSH(n);
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half spec = pow(saturate(dot(n, normalize(L.direction + V))), 48.0h) * 0.35h * (1.0h - foam);
                half3 col = lerp(_WaterColor.rgb * (0.55h + streak * 0.6h), _FoamColor.rgb, saturate(foam)) * light + spec * L.color * atten;
                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
