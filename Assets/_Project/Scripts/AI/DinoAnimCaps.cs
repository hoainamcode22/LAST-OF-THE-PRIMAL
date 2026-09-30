using UnityEngine;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// What a creature's Animator can do, found at runtime so every species works with old (phase 2) and new (PC phase,
    /// DINO) controllers without warnings: the lying rest set (Rest_Down / Rest_Loop / Rest_Up, optional Rest_Shift), Breathe,
    /// Bite, the flyer's Land / Takeoff (from the controller's clips: the builder makes a state only when its clip exists),
    /// and the optional Intensity parameter (read once the Animator is initialised). Without the rest set, ActionType Rest
    /// keeps the phase 2 fallback (Idle_Variation one-shot) and nothing waits for a get-up clip. Allocates once per creature.
    /// </summary>
    public sealed class DinoAnimCaps
    {
        public bool RestSet { get; private set; }
        public bool RestShift { get; private set; }
        public bool Breathe { get; private set; }
        public bool Bite { get; private set; }
        public bool Land { get; private set; }
        public bool Takeoff { get; private set; }
        /// <summary>the controller has the float parameter Intensity (run -> Chase / Flee body)</summary>
        public bool Intensity { get; private set; }
        public bool Resolved => _clips && _params;

        public static readonly int IntensityHash = Animator.StringToHash("Intensity");
        /// <summary>state tag of the four rest states</summary>
        public static readonly int RestTag = Animator.StringToHash("Rest");

        bool _clips, _params; RuntimeAnimatorController _for;

        /// <summary>look at the Animator (cheap once resolved; call before using a capability)</summary>
        public void Resolve(Animator a)
        {
            if (!a) return;
            var rc = a.runtimeAnimatorController;
            if (!rc) return;
            if (rc != _for) { _for = rc; _clips = _params = false; RestSet = RestShift = Breathe = Bite = Land = Takeoff = Intensity = false; }
            if (!_clips)
            {
                _clips = true;
                bool down = false, loop = false, up = false;
                var clips = rc.animationClips;
                if (clips != null)
                    for (int i = 0; i < clips.Length; i++)
                    {
                        var c = clips[i]; if (!c) continue;
                        switch (c.name)
                        {
                            case "Rest_Down": down = true; break;
                            case "Rest_Loop": loop = true; break;
                            case "Rest_Up": up = true; break;
                            case "Rest_Shift": RestShift = true; break;
                            case "Breathe": Breathe = true; break;
                            case "Bite": Bite = true; break;
                            case "Land": Land = true; break;
                            case "Takeoff": Takeoff = true; break;
                        }
                    }
                RestSet = down && loop && up;
            }
            if (!_params && a.isActiveAndEnabled && a.isInitialized)
            {
                _params = true;
                var ps = a.parameters;
                for (int i = 0; i < ps.Length; i++) if (ps[i].nameHash == IntensityHash && ps[i].type == AnimatorControllerParameterType.Float) Intensity = true;
            }
        }

        /// <summary>the Animator is in (or blending into) one of the lying rest states</summary>
        public static bool InRestPose(Animator a)
        {
            if (!a || !a.isActiveAndEnabled || !a.isInitialized) return false;
            if (a.GetCurrentAnimatorStateInfo(0).tagHash == RestTag) return true;
            return a.IsInTransition(0) && a.GetNextAnimatorStateInfo(0).tagHash == RestTag;
        }
    }
}
