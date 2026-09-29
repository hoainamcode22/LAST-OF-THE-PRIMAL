using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.Items;

namespace PrimalFrontier.World
{
    /// <summary>
    /// The sea at the player's feet: detected from the terrain height around the player (below sea level = ocean).
    /// With a water container in the active slot, Interact fills it with SaltWater (boil it at a campfire to make it
    /// drinkable). Empty-handed, "Drink sea water" makes thirst worse, so the player learns to boil it or look inland.
    /// Rules and numbers: WaterRules / SurvivalConfig. Prompts are cached (no per-frame strings).
    /// </summary>
    public class OceanShore : Interactable
    {
        public float seaLevel = 0f;
        public Terrain terrain;
        Vector3 _focus; bool _near;
        public override float Range => 2.2f;
        public override float Radius => 0f;
        public override Vector3 FocusPoint => _focus;
        public override bool LargeArea => true;
        public override int Priority => -2;
        public bool PlayerAtShore => _near;
        public const string SaltMessage = "Salt water. It burns your throat and makes the thirst worse.";
        const string DrinkPrompt = "Drink sea water", FillPrompt = "Fill with salt water", DrinkSub = "Salt water. It will make you thirstier.";
        readonly WaterPromptCache _fill = new WaterPromptCache();

        void Update()
        {
            _near = false;
            var pp = PlayerLocator.Position; if (!pp.HasValue) return;
            if (!terrain) terrain = Terrain.activeTerrain; if (!terrain) return;
            var p = pp.Value;
            if (p.y > seaLevel + 1.6f) return;
            float ty = terrain.transform.position.y;
            if (terrain.SampleHeight(p) + ty < seaLevel - 0.02f)                  // wading: the sea is right here
            {
                var fwd = PlayerLocator.Player ? PlayerLocator.Player.forward : Vector3.forward; fwd.y = 0;
                var q = p + fwd.normalized * 0.7f; _focus = new Vector3(q.x, seaLevel, q.z); _near = true; return;
            }
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                for (float r = 0.8f; r <= 2.6f; r += 0.6f)
                {
                    var q = p + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r;
                    if (terrain.SampleHeight(q) + ty < seaLevel - 0.08f)
                    {
                        _focus = new Vector3(q.x, seaLevel, q.z); _near = true; return;
                    }
                }
            }
        }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (!_near || WaterSource.IsNearFresh(p.transform.position, 3f)) return null;
            var st = p.ActiveStack;
            if (WaterRules.IsContainer(st) && !WaterRules.IsFull(st))
            {
                if (WaterRules.CanFill(st, WaterType.SaltWater)) { _fill.Fill(st, WaterType.SaltWater, out sub); return FillPrompt; }
                sub = _fill.Mixed(st, WaterType.SaltWater);
                return DrinkPrompt;
            }
            sub = DrinkSub;
            return DrinkPrompt;
        }

        public override void Interact(PlayerInteraction p)
        {
            if (!_near) return;
            var st = p.ActiveStack; int slot = p.Inventory ? p.Inventory.ActiveSlot : 0;
            if (WaterRules.CanFill(st, WaterType.SaltWater))
            {
                p.DoOneShot(PlayerActions.Drink, "OnDrink", () => WaterSource.FillSlot(p, slot, WaterType.SaltWater), _focus, 2.0f, this);
                return;
            }
            p.DoOneShot(PlayerActions.Drink, "OnDrink", () =>
            {
                WaterRules.ApplyDrink(p.Survival, WaterType.SaltWater);        // thirst worse + TriedSaltWater
                PlayerInteraction.Notify(SaltMessage);
            }, _focus, 2.0f, this);
        }
    }
}
