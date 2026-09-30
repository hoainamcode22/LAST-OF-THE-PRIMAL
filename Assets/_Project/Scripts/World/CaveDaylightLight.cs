using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.World
{
    /// <summary>
    /// CAVE (Phase 2): a cave light that only exists by day (daylight bouncing in at a mouth, the shaft through the
    /// ceiling cleft over the underground pool). Follows TimeManager.Daylight01 twice a second; off at night.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class CaveDaylightLight : MonoBehaviour
    {
        public float dayIntensity = 1f;
        public float nightIntensity;
        Light _light;
        float _next;

        void Awake() { _light = GetComponent<Light>(); }

        void Update()
        {
            if (!_light || Time.time < _next) return;
            _next = Time.time + 0.5f;
            var tm = TimeManager.Instance;
            float d = tm ? tm.Daylight01 : 1f;
            _light.intensity = Mathf.Lerp(nightIntensity, dayIntensity, d);
            _light.enabled = _light.intensity > 0.01f;
        }
    }
}
