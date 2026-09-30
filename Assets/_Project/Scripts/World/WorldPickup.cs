using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;

namespace PrimalFrontier.World
{
    /// <summary>
    /// An item lying in the world. Scene pickups are saved as "taken"; dropped ones are saved with their position.
    /// Food that spoils keeps its age on the ground: DropStack carries the stack (its madeAt) and Collect puts it back with
    /// the same age, so meat dropped today is not fresh again when it is picked up tomorrow.
    /// Dropped / thrown items are physical (Phase 1): a box collider around the model and a Rigidbody, on the Ignore Raycast
    /// layer (camera, grounding and aim rays pass through) and never colliding with the player. A drop next to the player
    /// leaves the hands in front of the chest and is tossed forward; it falls, tumbles, settles and then goes kinematic
    /// (no physics cost while it lies there). A caller that poses the pickup right after spawning it (an arrow stuck at its
    /// angle) keeps it pinned where it was put. Falling through the ground puts it back on top.
    /// </summary>
    public class WorldPickup : Interactable
    {
        public ItemDefinition item;
        [Min(1)] public int count = 1;
        [System.NonSerialized] public ItemStack uniqueStack;       // tools with durability / filled containers / food with an age
        public bool Dynamic { get; private set; }                   // spawned at runtime (dropped, spilled, thrown)
        public static readonly List<WorldPickup> Dropped = new List<WorldPickup>();
        public static System.Action<WorldPickup> Taken;             // scene pickups -> save system

        /// <summary>true while the dropped item still moves under physics (false once it has settled or was pinned)</summary>
        public bool Moving => _rb && !_rb.isKinematic;
        public Rigidbody Body => _rb;

        Rigidbody _rb; BoxCollider _box; Vector3 _spawnPos; Quaternion _spawnRot; Vector3 _launch; bool _pinned;
        static PhysicsMaterial _mat;

        public override float Range => 1.8f;
        public override int Priority => 1;
        public override Vector3 FocusPoint => _box ? _box.bounds.center : base.FocusPoint;

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (item == null) return null;
            int n = uniqueStack != null ? uniqueStack.count : count;
            if (p.Inventory.SpaceFor(item) <= 0) sub = p.Inventory.IsOverweight ? "Too heavy" : "Inventory full";
            string age = uniqueStack != null ? Spoilage.Label(Spoilage.Stage(uniqueStack)) : "";
            return "Pick up " + item.displayName + (n > 1 ? " x" + n : "") + (string.IsNullOrEmpty(age) ? "" : " (" + age + ")");
        }
        public override bool CanInteract(PlayerInteraction p) => item != null && p.Inventory.SpaceFor(item) > 0;

        public override void Interact(PlayerInteraction p)
        {
            p.DoOneShot(PlayerActions.Pickup, "OnPickup", () => Collect(p), FocusPoint, 0.8f, this);
        }

        public void Collect(PlayerInteraction p)
        {
            if (this == null || !gameObject.activeSelf || item == null) return;
            if (uniqueStack != null && !Stackable(uniqueStack))
            {
                if (!p.Inventory.AddStack(uniqueStack)) { PlayerInteraction.Notify("No room."); return; }
                count = 0;
            }
            else
            {
                // stackable (food with an age too): what fits goes in with its age, the rest stays on the ground
                int n = uniqueStack != null ? uniqueStack.count : count;
                double made = uniqueStack != null ? uniqueStack.madeAt : Core.GameClock.Now;
                int left = p.Inventory.Add(item, n, false, made);
                if (left == n) { PlayerInteraction.Notify(p.Inventory.IsOverweight ? "Too heavy." : "Inventory full."); return; }
                count = left; if (uniqueStack != null) uniqueStack.count = left;
            }
            if (count > 0) return;                                   // partially taken, the rest stays
            if (Dynamic) { Dropped.Remove(this); Destroy(gameObject); }
            else { Taken?.Invoke(this); gameObject.SetActive(false); }
        }

        // ------------------------------------------------------------------ spawning
        public static WorldPickup Drop(ItemDefinition item, int count, Vector3 pos)
        {
            if (item == null || count <= 0) return null;
            var pk = Spawn(item, pos, null); pk.count = count; return pk;
        }

        public static WorldPickup DropStack(ItemStack stack, Vector3 pos)
        {
            if (stack == null || stack.IsEmpty) return null;
            var pk = Spawn(stack.item, pos, null);
            pk.count = stack.count;
            if (!Stackable(stack) || Spoils(stack)) pk.uniqueStack = stack.Clone();   // keeps durability / water / the food's age (saved by SaveSystem)
            return pk;
        }

        /// <summary>a thrown stack: leaves pos with this velocity (m/s) and lands where physics puts it</summary>
        public static WorldPickup Throw(ItemStack stack, Vector3 pos, Vector3 velocity)
        {
            if (stack == null || stack.IsEmpty) return null;
            var pk = Spawn(stack.item, pos, velocity);
            pk.count = stack.count;
            if (!Stackable(stack) || Spoils(stack)) pk.uniqueStack = stack.Clone();
            return pk;
        }

        static bool Spoils(ItemStack s) => Spoilage.Spoils(s.item);

        /// <summary>a plain stack: no durability loss, no water; several can share a slot (food with an age is stackable too)</summary>
        static bool Stackable(ItemStack s) => s.item.maxStack > 1 && s.water == 0 && !(s.item.HasDurability && s.durability < s.item.maxDurability);

        static WorldPickup Spawn(ItemDefinition item, Vector3 pos, Vector3? velocity)
        {
            GameObject go;
            if (item.worldPrefab) go = Instantiate(item.worldPrefab);
            else { go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.localScale = Vector3.one * 0.2f; }
            go.name = "Pickup_" + item.id;
            // a drop next to the player (inventory drop, spilled gather, crafting leftovers) leaves the hands in front of the chest
            var launch = velocity ?? Vector3.zero;
            var pl = PlayerLocator.Player;
            if (velocity == null && pl)
            {
                var d = pos - pl.position; float up = d.y; d.y = 0f;
                if (d.magnitude < 1.3f && up > 0.1f && up < 1.0f && (d.sqrMagnitude < 0.01f || Vector3.Dot(d.normalized, pl.forward) > 0.6f))
                {
                    var fwd = d.sqrMagnitude > 0.01f ? d.normalized : pl.forward;
                    var chest = pl.position + Vector3.up * 1.15f;
                    var want = chest + fwd * 0.55f;
                    if (Physics.Raycast(chest, fwd, out var wall, 0.75f, ~LayerMask.GetMask("Player", "Ignore Raycast"), QueryTriggerInteraction.Ignore))
                        want = wall.point - fwd * 0.2f;
                    pos = want; launch = fwd * 1.6f + Vector3.up * 1.2f;
                }
            }
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Quaternion.Euler(item.worldEuler));
            // one box around the model: model colliders go (mesh colliders cannot be dynamic), the box keeps it out of the ground
            foreach (var c in go.GetComponentsInChildren<Collider>()) { c.enabled = false; Destroy(c); }
            var rs = go.GetComponentsInChildren<Renderer>();
            Bounds lb = new Bounds(Vector3.zero, Vector3.one * 0.15f); bool any = false;
            var inv = go.transform.worldToLocalMatrix;
            foreach (var r in rs)
            {
                if (r is ParticleSystemRenderer) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = inv.MultiplyPoint3x4(c);
                    if (!any) { lb = new Bounds(lp, Vector3.zero); any = true; } else lb.Encapsulate(lp);
                }
            }
            var box = go.AddComponent<BoxCollider>();
            var s = go.transform.lossyScale; float k = Mathf.Max(0.001f, Mathf.Min(Mathf.Abs(s.x), Mathf.Min(Mathf.Abs(s.y), Mathf.Abs(s.z))));
            box.center = lb.center; box.size = Vector3.Max(lb.size, Vector3.one * (0.05f / k));
            if (!_mat) _mat = new PhysicsMaterial("PickupPhysics") { dynamicFriction = 0.7f, staticFriction = 0.8f, bounciness = 0.08f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
            box.sharedMaterial = _mat;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;     // Ignore Raycast: camera / grounding / aim rays pass through
            // never below the ground: the box's lowest point rests at least on the surface under it
            if (Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out var hit, 8f, ~LayerMask.GetMask("Player", "Ignore Raycast"), QueryTriggerInteraction.Ignore))
            {
                Physics.SyncTransforms();
                float bottom = box.bounds.min.y;
                if (bottom < hit.point.y + 0.01f) go.transform.position += Vector3.up * (hit.point.y + 0.01f - bottom);
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(item.weight, 0.1f, 25f);
            rb.linearDamping = 0.15f; rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;                                  // one frame: a caller may still pose it (stuck arrow)
            if (pl) foreach (var pc in pl.GetComponentsInChildren<Collider>(true)) if (pc) Physics.IgnoreCollision(box, pc, true);
            var pk = go.GetOrAdd<WorldPickup>();
            pk.item = item; pk.Dynamic = true; pk._rb = rb; pk._box = box; pk._launch = launch;
            pk._spawnPos = go.transform.position; pk._spawnRot = go.transform.rotation;
            pk.RefreshBounds();
            Dropped.Add(pk);
            pk.StartCoroutine(pk.Settle());
            return pk;
        }

        /// <summary>keeps it where it is (no physics): stuck arrows, items placed by a system</summary>
        public void Pin()
        {
            _pinned = true;
            if (_rb) { if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; } _rb.isKinematic = true; }
            RefreshBounds();
        }

        IEnumerator Settle()
        {
            yield return new WaitForFixedUpdate();
            if (_pinned || !_rb) yield break;
            // posed by the caller right after the spawn (Projectile: stuck at its angle): it stays there
            if (Quaternion.Angle(transform.rotation, _spawnRot) > 0.5f || (transform.position - _spawnPos).sqrMagnitude > 1e-4f) { Pin(); yield break; }
            _rb.isKinematic = false;
            _rb.linearVelocity = _launch;
            if (_launch.sqrMagnitude > 0.01f) _rb.angularVelocity = Random.insideUnitSphere * 4f;
            float t = 0f, still = 0f;
            var wait = new WaitForSeconds(0.2f);
            while (_rb && !_rb.isKinematic && !_pinned)
            {
                yield return wait; t += 0.2f;
                var terrain = Terrain.activeTerrain;
                if (terrain)
                {
                    float g = terrain.SampleHeight(transform.position) + terrain.transform.position.y;
                    if (transform.position.y < g - 1.0f)                  // fell through: back on top, at rest
                    {
                        _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
                        transform.position = new Vector3(transform.position.x, g + 0.3f, transform.position.z);
                    }
                }
                bool slow = _rb.IsSleeping() || (_rb.linearVelocity.sqrMagnitude < 0.0025f && _rb.angularVelocity.sqrMagnitude < 0.01f);
                still = slow ? still + 0.2f : 0f;
                if ((still >= 0.4f && t >= 0.6f) || t >= 12f)
                {
                    _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
                    _rb.isKinematic = true;                              // at rest: no physics cost, still collides with what falls on it
                }
            }
            RefreshBounds();
        }

        public static void ClearDropped()
        {
            foreach (var d in Dropped) if (d) Destroy(d.gameObject);
            Dropped.Clear();
        }
    }
}
