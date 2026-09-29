using System.IO;
using UnityEditor;
using UnityEngine;
using PrimalFrontier.Combat.Weapons;

namespace PrimalFrontier.EditorTools
{
    public static partial class PrimalCharacterBuilder
    {
        const string BareHandsAsset = "Assets/_Project/Resources/Combat/WPN_bare_hands.asset";

        /// <summary>
        /// Resources/Combat/WPN_bare_hands: the WeaponData the empty hands fight with (WeaponController.BareHandData). A new
        /// asset gets WeaponData.ApplyBareHandDefaults; an existing one keeps its hand-tuned numbers unless arg = "reset".
        /// Bridge: PrimalCharacterBuilder.BuildBareHands "" | "reset"
        /// </summary>
        [PrimalBridgeCommand]
        public static string BuildBareHands(string arg)
        {
            Directory.CreateDirectory("Assets/_Project/Resources/Combat");
            var d = AssetDatabase.LoadAssetAtPath<WeaponData>(BareHandsAsset);
            string what;
            if (d == null)
            {
                d = ScriptableObject.CreateInstance<WeaponData>();
                WeaponData.ApplyBareHandDefaults(d);
                AssetDatabase.CreateAsset(d, BareHandsAsset); what = "created";
            }
            else if (arg == "reset") { WeaponData.ApplyBareHandDefaults(d); EditorUtility.SetDirty(d); what = "reset"; }
            else what = "kept";
            AssetDatabase.SaveAssets();
            return $"{what} {BareHandsAsset}: damage {d.damage} / heavy {d.heavyDamage}, stamina {d.staminaCost} (heavy x{d.heavyStaminaMultiplier}), " +
                   $"speed x{d.attackSpeed}, reach {d.reach} / {d.HeavyReach} down to {d.reachDown} m, knockback {d.knockback} / {d.heavyKnockback} m/s, " +
                   $"chain {d.attacks.Length} + heavy {(d.HasHeavy ? d.heavyAttack.action : 0)}, combo window {d.comboWindow} s";
        }
    }
}
