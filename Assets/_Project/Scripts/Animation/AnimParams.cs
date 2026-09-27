using UnityEngine;

namespace PrimalFrontier.Animation
{
    /// <summary>Animator parameter names/hashes shared by the generated controllers (PrimalCharacterBuilder).</summary>
    public static class AnimParams
    {
        // shared
        public static readonly int Speed = Animator.StringToHash("Speed");                 // m/s (horizontal)
        public static readonly int Action = Animator.StringToHash("Action");               // player: int (PlayerActions); dinos: trigger
        public static readonly int ActionType = Animator.StringToHash("ActionType");       // int, see ActionTypes
        public static readonly int Attack = Animator.StringToHash("Attack");               // trigger
        public static readonly int AttackType = Animator.StringToHash("AttackType");       // int
        public static readonly int Hurt = Animator.StringToHash("Hurt");                   // trigger
        public static readonly int HurtType = Animator.StringToHash("HurtType");           // int 0 light, 1 heavy/stagger
        public static readonly int Dead = Animator.StringToHash("Dead");                   // bool
        // player
        public static readonly int VelX = Animator.StringToHash("VelX");                   // m/s, local right
        public static readonly int VelZ = Animator.StringToHash("VelZ");                   // m/s, local forward
        public static readonly int Turn = Animator.StringToHash("Turn");                   // -1 right .. 1 left (in place)
        public static readonly int IsGrounded = Animator.StringToHash("IsGrounded");
        public static readonly int VerticalVelocity = Animator.StringToHash("VerticalVelocity");
        public static readonly int IsCrouching = Animator.StringToHash("IsCrouching");
        public static readonly int IsAttacking = Animator.StringToHash("IsAttacking");     // bool, set by gameplay while an attack runs
        public static readonly int Jump = Animator.StringToHash("Jump");                   // trigger
        public static readonly int Revive = Animator.StringToHash("Revive");               // trigger
        public static readonly int UpperBody = Animator.StringToHash("UpperBody");         // int 0 none, 1 carry, 2 bow idle, 3 bow draw, 4 bow release
        public static readonly int HealthState = Animator.StringToHash("HealthState");     // int (PlayerHealthStates)
        public static readonly int TurnSpeed = Animator.StringToHash("TurnSpeed");         // deg/s, + = left (turn in place)
        // dinosaur
        public static readonly int Alert = Animator.StringToHash("Alert");                 // bool
    }

    /// <summary>Values of the player's "Action" int parameter (PlayerAnimator.controller).</summary>
    public static class PlayerActions
    {
        public const int None = 0, Pickup = 1, GatherWood = 2, GatherStone = 3, GatherPlant = 4, Interact = 5, Craft = 6, Eat = 7, Drink = 8,
            Build = 9, UseItem = 10, Sleep = 11, WakeUp = 12, GetUp = 13,
            AttackSpear = 20, AttackSpearHeavy = 21, ThrowSpear = 22,
            BowAim = 30, BowDraw = 31, BowRelease = 32, CarryItem = 40;
        public static bool IsLooping(int a) => a == GatherWood || a == GatherStone || a == GatherPlant || a == Craft || a == Build || a == Sleep;
        public static bool IsUpperBody(int a) => a >= BowAim && a <= CarryItem;
    }

    /// <summary>Values of the player's "HealthState" int parameter.</summary>
    public static class PlayerHealthStates
    {
        public const int Normal = 0, HurtLight = 1, HurtHeavy = 2, Dead = 3;
    }

    /// <summary>ActionType values for dinosaurs (mapped per species by the override controller).</summary>
    public static class DinoActions
    {
        public const int None = 0, Eat = 1, Drink = 2, Rest = 3, LookAround = 4, Roar = 5, Call = 6, Threaten = 7, Defend = 8, Investigate = 9;
    }
}
