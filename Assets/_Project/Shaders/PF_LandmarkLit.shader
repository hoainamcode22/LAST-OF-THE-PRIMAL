// PRIMAL FRONTIER - lit shader for far scenery (the volcano across the sea). Simple lighting (sun + ambient probes),
// height / slope tint so the shape reads from far away, optional emission (lava), and fog scaled by _FogStrength so
// a landmark 500+ m away is hazy but still readable (URP Lit would fog it to a flat silhouette).
Shader "PF/Landmark Lit"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        _TopColor ("Upper slopes tint", Color) = (1,1,1,1)
        _TopHeight ("Upper slopes from (m above pivot)", Float) = 140
        _TopBlend ("Upper slopes blend (m)", Float) = 60
        _GullyDark ("Steep / gully darkening", Range(0,1)) = 0.35
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)
        _FogStrength ("Fog strength", Range(0,1)) = 0.5
        _Wrap ("Light wrap", Range(0,1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor, _TopColor, _EmissionColor;
                float _TopHeight, _TopBlend;
                half _GullyDark, _FogStrength, _Wrap;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float heightOS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.heightOS = i.positionOS.y;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                half top = saturate((i.heightOS - _TopHeight) / max(1.0, _TopBlend));
                albedo = lerp(albedo, albedo * _TopColor.rgb, top);
                half steep = 1.0 - saturate(n.y);
                albedo *= 1.0 - _GullyDark * steep * steep;
                Light sun = GetMainLight();
                half ndl = saturate((dot(n, sun.direction) + _Wrap) / (1.0 + _Wrap));
                half3 col = albedo * (sun.color * ndl + SampleSH(n)) + _EmissionColor.rgb;
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    half fi = ComputeFogIntensity(i.fogFactor);
                    fi = lerp(1.0h, fi, _FogStrength);
                    col = lerp(unity_FogColor.rgb, col, fi);
                #endif
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor, _TopColor, _EmissionColor;
                float _TopHeight, _TopBlend;
                half _GullyDark, _FogStrength, _Wrap;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
