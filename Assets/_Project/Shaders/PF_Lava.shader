// PRIMAL FRONTIER - cheap flowing lava (ENV, PC phase): the lava channel and the glowing cracks of the volcanic ridge.
// Opaque. Crust plates (dark, rough, lit by the sun) float on a glowing melt that shows in the cracks between them; the
// pattern flows along uv.y (metres along the channel, from the builder) at _FlowSpeed and the glow breathes slowly.
// Emission is HDR so bloom picks it up at night; _FogResist keeps some glow through the fog. 2 texture samples.
Shader "PF/Lava"
{
    Properties
    {
        _LavaTex ("Lava (R crust plates, G cracks, B noise)", 2D) = "gray" {}
        _CrustColor ("Crust", Color) = (0.07, 0.06, 0.055, 1)
        _HotColor ("Melt (HDR)", Color) = (4.0, 1.25, 0.25, 1)
        _WarmColor ("Cooling melt (HDR)", Color) = (1.4, 0.28, 0.05, 1)
        _Crust ("Crust cover", Range(0,1)) = 0.55
        _FlowSpeed ("Flow (m/s)", Float) = 0.08
        _Tiling ("Tiling (m)", Float) = 3.0
        _Pulse ("Glow pulse", Range(0,1)) = 0.25
        _FogResist ("Glow through fog", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _LavaTex_ST; half4 _CrustColor, _HotColor, _WarmColor; half _Crust; float _FlowSpeed, _Tiling; half _Pulse, _FogResist;
            CBUFFER_END
            TEXTURE2D(_LavaTex); SAMPLER(sampler_LavaTex);
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half3 normalWS : TEXCOORD2; half fogFactor : TEXCOORD3; half heat : TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A i)
            {
                V o = (V)0; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv; o.heat = i.color.r; o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = float2(i.uv.x, i.uv.y - t * _FlowSpeed) / _Tiling;
                half4 a = SAMPLE_TEXTURE2D(_LavaTex, sampler_LavaTex, uv);
                half4 b = SAMPLE_TEXTURE2D(_LavaTex, sampler_LavaTex, uv * 0.43 + float2(0.37, -t * _FlowSpeed * 0.2));
                half cover = lerp(1.0h, _Crust, i.heat);                     // vertex R = heat: 1 at the vent, 0 at the cooled front
                half crust = saturate((a.r + (b.b - 0.5h) * 0.4h - (1.0h - cover)) * 4.0h);
                half crack = saturate(a.g * 1.4h + (1.0h - crust) * 0.8h);
                half pulse = 1.0h + _Pulse * sin(t * 0.8h + b.b * 6.2831h);
                half3 melt = lerp(_WarmColor.rgb, _HotColor.rgb, saturate(b.b * 1.2h + crack * 0.4h - 0.2h)) * pulse;
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 n = normalize(i.normalWS);
                half3 lit = _CrustColor.rgb * (1.0h - a.b * 0.3h) * (L.color * saturate(dot(n, L.direction)) * L.shadowAttenuation + SampleSH(n));
                half3 col = lerp(melt, lit, crust) + _WarmColor.rgb * crack * crust * 0.18h * pulse;
                half3 fogged = MixFog(col, i.fogFactor);
                col = lerp(fogged, col, _FogResist * (1.0h - crust));
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(A i) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(i); float3 ws = TransformObjectToWorld(i.positionOS.xyz); return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(ws, TransformObjectToWorldNormal(i.normalOS), _LightDirection))); }
            half4 frag() : SV_Target { return 0; }
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
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(A i) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(i); return TransformObjectToHClip(i.positionOS.xyz); }
            float frag(float4 p : SV_POSITION) : SV_Target { return p.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
