// PRIMAL FRONTIER - lean wind shader for trees and bushes (successor of the experimental PF/Foliage).
// Vertex-only sway: the plant bends along the weather wind (_PF_Wind, set every frame by WeatherManager: strength rises
// with rain / storms), weighted by (height above the object pivot / _WindHeight)^2, phase from the object's world
// position so neighbours never move in sync. Leaf materials add a small flutter (_Flutter > 0, bark keeps 0 so trunk
// and crown bend together). Optional mask: vertex colour R (_VertexMask = 1) for meshes that paint one; default off.
// Lighting is SimpleLit-style: main light with shadows + SH ambient + fog, a little light through the leaves from
// behind, no specular, no additional lights. Keywords: URP main light / soft shadows, fog, instancing, alpha clip only.
// ShadowCaster, DepthOnly and DepthNormals call the same PF_WindPositionWS, so shadows and depth follow the sway.
// Same property names as URP Lit (_BaseMap, _BaseColor, _Cutoff, _AlphaClip, _Cull): switching keeps textures / colours.
// Rollback: bridge PrimalShaderBuilder.WindRevert (restores the shader recorded by WindApply).
Shader "PF/Foliage Wind"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Translucency ("Light through leaves", Range(0,1)) = 0.35
        _WetDarken ("Wet darkening", Range(0,1)) = 0.25
        _WindSway ("Sway at the top (m at wind 1)", Float) = 0.25
        _WindHeight ("Plant height (m)", Float) = 9
        _Flutter ("Leaf flutter (m)", Float) = 0
        _VertexMask ("Vertex colours mask the wind (R sway, G flutter)", Range(0,1)) = 0
        _ColorVariation ("Per-plant colour variation", Range(0,0.5)) = 0
        _VariationTint ("Variation toward", Color) = (1.08, 0.97, 0.72, 1)
    }
    SubShader
    {
        // DisableBatching: batching bakes meshes into world space and would lose the pivot the sway is measured from
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "DisableBatching"="True" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _Translucency;
            half _WetDarken;
            float _WindSway;
            float _WindHeight;
            float _Flutter;
            float _VertexMask;
            half _ColorVariation;
            half4 _VariationTint;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        float4 _PF_Wind;      // WeatherManager: xy direction (world xz), z strength (calm 0.35 .. storm 1.4), w gust 0..1
        float _PF_Wetness;    // WeatherManager: 0..1

        // world position after the sway (every pass calls this, so shadows and depth match the lit surface)
        float3 PF_WindPositionWS(float3 positionOS, float mask, float flutterMask)
        {
            float3 ws = TransformObjectToWorld(positionOS);
            float3 origin = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            float wind = _PF_Wind.z > 0.0 ? _PF_Wind.z : 0.35;                                  // edit mode / no WeatherManager: calm
            float2 dir = dot(_PF_Wind.xy, _PF_Wind.xy) > 0.01 ? normalize(_PF_Wind.xy) : float2(0.8, 0.6);
            float h = saturate((ws.y - origin.y) / max(0.5, _WindHeight));
            float bend = h * h * mask;
            float phase = dot(origin.xz, float2(0.13, 0.17));
            float t = _Time.y;
            float gust = 0.6 + _PF_Wind.w * 0.8;
            float sway = (sin(t * 1.3 + phase) * 0.6 + sin(t * 2.3 + phase * 1.7) * 0.25 + 0.35) * gust;
            float3 offset = float3(dir.x, 0.0, dir.y) * (sway * _WindSway * wind * bend);
            float f = sin(t * 7.0 + dot(ws, float3(1.7, 2.3, 1.1))) * _Flutter * wind * h * mask * flutterMask;
            offset += float3(f * 0.6, f * 0.3, f * 0.6);
            offset.y -= length(offset.xz) * 0.25 * bend;                                      // bend, not shear
            return ws + offset;
        }

        float PF_WindMask(half4 vertexColor) { return lerp(1.0, (float)vertexColor.r, _VertexMask); }
        // G = leaf flutter weight (ENV prehistoric plants: leaf tips 1, fronds' base and trunks 0); models without colours keep 1
        float PF_FlutterMask(half4 vertexColor) { return lerp(1.0, (float)vertexColor.g, _VertexMask); }
        // subtle colour variation per plant (hash of the object's position: instancing and terrain trees keep it)
        half3 PF_PlantTint()
        {
            float3 o = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            float h = frac(sin(dot(o.xz, float2(12.9898, 78.233))) * 43758.5453);
            float h2 = frac(h * 5.31 + 0.19);
            half3 tint = lerp(half3(1, 1, 1), _VariationTint.rgb, (half)(h * _ColorVariation * 2.0));
            return tint * (half)(1.0 + (h2 - 0.5) * _ColorVariation);
        }
        half PF_Alpha(float2 uv) { return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a; }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = PF_WindPositionWS(i.positionOS.xyz, PF_WindMask(i.color), PF_FlutterMask(i.color));
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                #if defined(_ALPHATEST_ON)
                    clip(tex.a - _Cutoff);
                #endif
                float3 n = normalize(i.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);                                               // two-sided leaves
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS), i.positionWS, half4(1, 1, 1, 1));
                half ndl = (half)dot(n, light.direction);
                half atten = light.distanceAttenuation * light.shadowAttenuation;
                half3 direct = light.color * atten * (saturate(ndl) + saturate(-ndl) * _Translucency);
                half3 albedo = tex.rgb * PF_PlantTint() * (1.0h - _WetDarken * (half)saturate(_PF_Wetness));
                half3 c = albedo * (direct + SampleSH(n));
                c = MixFog(c, i.fogFactor);
                return half4(c, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;     // set by URP for the shadow caster pass
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 ws = PF_WindPositionWS(i.positionOS.xyz, PF_WindMask(i.color), PF_FlutterMask(i.color));
                float3 nws = TransformObjectToWorldNormal(i.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 L = normalize(_LightPosition - ws);
                #else
                    float3 L = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(ws, nws, L)));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(PF_Alpha(i.uv) - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformWorldToHClip(PF_WindPositionWS(i.positionOS.xyz, PF_WindMask(i.color), PF_FlutterMask(i.color)));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            float frag(V i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(PF_Alpha(i.uv) - _Cutoff);
                #endif
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformWorldToHClip(PF_WindPositionWS(i.positionOS.xyz, PF_WindMask(i.color), PF_FlutterMask(i.color)));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(V i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(PF_Alpha(i.uv) - _Cutoff);
                #endif
                float3 n = normalize(i.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);
                return half4(n, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
