// PRIMAL FRONTIER - rain wetness for the URP terrain (ENV, PC phase). Included by PF/Terrain Lit Wet and its add pass right
// before URP's own TerrainLitPasses.hlsl: the terrain fragment's call to UniversalFragmentPBR is routed through
// PF_WetTerrainPBR, which darkens the (already normalised) layer mix and makes it smoother as the WeatherManager global
// _PF_Wetness (0..1, with drying lag) rises; ground facing up gets a little more gloss (water film). Everything else is URP's.
#ifndef PF_TERRAIN_WET_INCLUDED
#define PF_TERRAIN_WET_INCLUDED

float _PF_Wetness;

half4 PF_WetTerrainPBR(InputData inputData, half3 albedo, half metallic, half3 specular, half smoothness, half occlusion, half3 emission, half alpha)
{
    half wet = (half)saturate(_PF_Wetness);
    half flat = saturate(inputData.normalWS.y * 2.5h - 1.55h);
    half lum = dot(albedo, half3(0.3h, 0.59h, 0.11h));
    albedo *= lerp(1.0h, 0.66h - 0.14h * saturate(lum * 2.5h), wet);          // light soils and sand darken the most
    smoothness = lerp(smoothness, max(smoothness, 0.32h + 0.42h * flat), wet);
    return UniversalFragmentPBR(inputData, albedo, metallic, specular, smoothness, occlusion, emission, alpha);
}

#define UniversalFragmentPBR PF_WetTerrainPBR
#endif
