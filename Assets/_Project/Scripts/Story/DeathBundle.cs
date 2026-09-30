using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.World;

namespace PrimalFrontier.Story
{
    /// <summary>the belongings left where the player died: one interactable pack, "Recover your belongings"</summary>
    public class DeathBundle : Interactable
    {
        public static readonly List<DeathBundle> All = new List<DeathBundle>();
        public readonly List<ItemStack> stacks = new List<ItemStack>();
        public double droppedAt;
        public override float Range => 2.2f;
        public override float Radius => 0.5f;
        public override int Priority => 3;
        string _prompt, _sub; int _cCount = -1;

        public static DeathBundle Spawn(Vector3 at, List<ItemStack> items, double when, bool settle = true)
        {
            var go = Model();
            go.name = "[Belongings]";
            if (settle && Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out var hit, 8f, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore)) at = hit.point;
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.02f, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            var b = go.AddComponent<DeathBundle>();
            b.stacks.AddRange(items); b.droppedAt = when; b.SaveId = "belongings_" + when.ToString("0");
            b.RefreshBounds();
            return b;
        }

        /// <summary>a rolled hide pack: the hide (else bedroll, else any item) world model, a little larger</summary>
        static GameObject Model()
        {
            var db = ItemDatabase.Instance;
            foreach (var id in new[] { "hide", "bedroll", "fiber" })
            {
                var it = db ? db.Item(id) : null;
                if (it && it.worldPrefab)
                {
                    var g = Instantiate(it.worldPrefab); g.transform.localScale *= id == "bedroll" ? 0.8f : 1.6f;
                    foreach (var p in g.GetComponentsInChildren<WorldPickup>()) Destroy(p);
                    var root = new GameObject("Bundle"); g.transform.SetParent(root.transform, false); return root;
                }
            }
            var box = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            box.transform.localScale = new Vector3(0.35f, 0.2f, 0.35f); box.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            var holder = new GameObject("Bundle"); box.transform.SetParent(holder.transform, false);
            var r = box.GetComponent<Renderer>(); if (r) r.material.color = new Color(0.42f, 0.3f, 0.2f);
            return holder;
        }

        protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); }
        protected override void OnDisable() { base.OnDisable(); All.Remove(this); }
        public static void ClearAll() { foreach (var b in All.ToArray()) if (b) Destroy(b.gameObject); All.Clear(); }

        int Count() { int n = 0; foreach (var s in stacks) if (!s.IsEmptyOrNull()) n += s.count; return n; }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            int n = Count();
            if (n != _cCount) { _cCount = n; _prompt = "Recover your belongings"; _sub = n == 1 ? "1 item" : n + " items"; }
            sub = _sub; return n > 0 ? _prompt : null;
        }

        public override void Interact(PlayerInteraction p) => p.DoOneShot(PlayerActions.Pickup, "OnPickup", () => Collect(p), FocusPoint, 0.8f, this);

        public void Collect(PlayerInteraction p)
        {
            if (this == null || p == null) return;
            var inv = p.Inventory; int got = 0;
            for (int i = stacks.Count - 1; i >= 0; i--)
            {
                var s = stacks[i]; if (s.IsEmptyOrNull()) { stacks.RemoveAt(i); continue; }
                if (inv.AddStack(s)) { stacks.RemoveAt(i); got++; }
            }
            if (got > 0) GameEvents.Raise(GameEventType.BelongingsRecovered, "belongings", got, transform.position);
            if (stacks.Count == 0) Destroy(gameObject);
            else PlayerInteraction.Notify(inv.IsOverweight ? "Too heavy to carry the rest." : "No room for the rest.");
        }
    }
}
