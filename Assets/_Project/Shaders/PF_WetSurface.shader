// PRIMAL FRONTIER - shared wet surface for rocks, wood, bark and wreck planks (ENV, PC phase).
// URP PBR (UniversalFragmentPBR: main light + shadows, Forward+ additional lights, SH ambient, reflection probe, fog, instancing).
// Same property names as URP Lit (_BaseMap, _BaseColor, _BumpMap, _BumpScale, _MetallicGlossMap, _Smoothness, _OcclusionMap,
// _OcclusionStrength, _Cutoff, _Cull): PrimalEnvironmentBuilder.Wet switches existing materials in place (textures kept, backup
// recorded) instead of duplicating them.
// Wetness: w = saturate(_PF_Wetness * _WetResponse + _BaseWetness) (global from WeatherManager, per material a permanent part
// for spray zones). Wet = darker albedo (porous surfaces more), smoother, flatter normal, a slight puddle gloss on faces that look
// up. Optional moss on up-facing sides (_MossAmount > 0, world-projected T_Moss). Subtle per-object tint from the object's
// position (_ColorVariation) so instanced rocks / logs are not identical.
Shader "PF/Wet Surface"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 1
        _MetallicGlossMap ("Mask (R metal, G AO, A smooth)", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0,1)) = 0.25
        _Metallic ("Metallic", Range(0,1)) = 0
        _OcclusionMap ("Occlusion (G)", 2D) = "white" {}
        _OcclusionStrength ("Occlusion strength", Range(0,1)) = 0.8
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Header(Wetness)]
        _WetResponse ("Reacts to rain (x _PF_Wetness)", Range(0,2)) = 1
        _BaseWetness ("Always wet (spray, stream beds)", Range(0,1)) = 0
        _Porosity ("Porosity (how much wet darkens)", Range(0,1)) = 0.6
        _WetSmoothness ("Smoothness when soaked", Range(0,1)) = 0.82
        [Header(Moss on top faces)]
        _MossMap ("Moss albedo", 2D) = "white" {}
        _MossAmount ("Moss amount", Range(0,1)) = 0
        _MossTiling ("Moss tiling (m)", Float) = 2.5
        _MossColor ("Moss tint", Color) = (1,1,1,1)
        [Header(Variation)]
        _ColorVariation ("Per-object tint variation", Range(0,0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale, _Smoothness, _Metallic, _OcclusionStrength, _Cutoff;
            half _WetResponse, _BaseWetness, _Porosity, _WetSmoothness;
            half _MossAmount; float _MossTiling; half4 _MossColor;
            half _ColorVariation;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        float _PF_Wetness;                  // WeatherManager global, 0..1

        half PF_Alpha(float2 uv) { return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a; }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);
            TEXTURE2D(_OcclusionMap);
            TEXTURE2D(_MossMap); SAMPLER(sampler_MossMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half3 tint : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = n.normalWS; o.tangentWS = half4(n.tangentWS, i.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float h = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453);
                float h2 = frac(h * 7.13 + 0.37);
                o.tint = (half3)(1.0 + (float3(h, h * 0.8 + h2 * 0.2, h2) - 0.5) * 2.0 * _ColorVariation);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                #if defined(_ALPHATEST_ON)
                    clip(alb.a - _Cutoff);
                #endif
                half4 mask = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, i.uv);
                half occ = lerp(1.0h, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, i.uv).g, _OcclusionStrength);
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float sgn = i.tangentWS.w; float3 bit = sgn * cross(i.normalWS, i.tangentWS.xyz);
                half3 geomN = normalize(i.normalWS); geomN = IS_FRONT_VFACE(face, geomN, -geomN);
                half3x3 tbn = half3x3(i.tangentWS.xyz, bit, i.normalWS);
                half wet = saturate(_PF_Wetness * _WetResponse + _BaseWetness);
                nTS = normalize(lerp(nTS, half3(0, 0, 1), wet * 0.55h));
                half3 nWS = normalize(TransformTangentToWorld(nTS, tbn));
                nWS = IS_FRONT_VFACE(face, nWS, -nWS);

                half3 albedo = alb.rgb * i.tint;
                half smooth = mask.a * _Smoothness;
                // moss on the up-facing sides (world-projected, no UV seams)
                if (_MossAmount > 0.001h)
                {
                    half3 moss = SAMPLE_TEXTURE2D(_MossMap, sampler_MossMap, i.positionWS.xz / max(0.1, _MossTiling)).rgb * _MossColor.rgb;
                    half up = saturate((geomN.y - (1.0h - _MossAmount * 1.4h)) * 3.0h + (moss.g - 0.25h) * 1.5h);
                    albedo = lerp(albedo, moss, up);
                    smooth = lerp(smooth, 0.08h, up);
                    occ = lerp(occ, 1.0h, up * 0.5h);
                }
                // wetness: darker (porous more), smoother, a puddle-like gloss on faces that look up
                half up2 = saturate(geomN.y * 1.4h - 0.3h);
                albedo *= lerp(1.0h, 1.0h - 0.45h * _Porosity, wet);
                smooth = lerp(smooth, max(smooth, _WetSmoothness * (0.75h + 0.25h * up2)), wet);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS; d.normalWS = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.bakedGI = SampleSH(nWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = mask.r * _Metallic; s.specular = half3(0, 0, 0);
                s.smoothness = smooth; s.normalTS = nTS; s.occlusion = occ; s.emission = half3(0, 0, 0); s.alpha = 1.0h;

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return half4(c.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i)
            {
                V o = (V)0; UNITY_SETUP_INSTANCE_ID(i);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz); float3 nws = TransformObjectToWorldNormal(i.normalOS);
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
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i) { V o = (V)0; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = TRANSFORM_TEX(i.uv, _BaseMap); return o; }
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
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i) { V o = (V)0; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(V i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(PF_Alpha(i.uv) - _Cutoff);
                #endif
                float3 n = normalize(i.normalWS); n = IS_FRONT_VFACE(face, n, -n);
                return half4(n, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
