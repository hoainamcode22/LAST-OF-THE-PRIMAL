using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.VFX
{
    /// <summary>kinds of zone atmosphere effect (serialised as ints: append only)</summary>
    public enum ZoneFxKind
    {
        GroundFog = 0, WaterMist = 1, Insects = 2, Dragonflies = 3, Fireflies = 4, DustMotes = 5, Haze = 6, Flies = 7,
        WindDust = 8, SmokeWisp = 9, AshFall = 10, HeatShimmer = 11, CaveDrips = 12, CaveMist = 13,
    }

    /// <summary>
    /// A place where a zone atmosphere effect lives (wetland fog, an insect swarm over the reeds, dust motes in a light shaft,
    /// flies over a carcass, smoke from a crack, ash, heat shimmer, cave drips). Data only: <see cref="ZoneFxManager"/> lends a
    /// pooled particle system to the nearest anchors around the camera and fades it in and out. Placed by
    /// PrimalAtmosphereBuilder.Zones2 (World/Environment/Weather/Zones2/&lt;Zone&gt;), from the zone agents' FX_ anchors.
    /// </summary>
    public class ZoneFxAnchor : MonoBehaviour
    {
        public ZoneFxKind kind;
        [Tooltip("half width of the emission volume (m)")] [Min(0.2f)] public float radius = 6f;
        [Tooltip("height of the emission volume (m), centred on this point")] [Min(0.1f)] public float height = 2f;
        [Tooltip("0..2 x the kind's normal density (the builder lowers it in a zone's blend band)")] [Range(0, 2)] public float strength = 1f;

        public static readonly List<ZoneFxAnchor> All = new List<ZoneFxAnchor>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { All.Clear(); }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.5f);
            Gizmos.DrawWireCube(transform.position, new Vector3(radius * 2f, height, radius * 2f));
        }
    }
}
