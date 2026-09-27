using System;
using System.Collections;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;
using PrimalFrontier.Player;
using PrimalFrontier.UI;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// Opening: black screen, storm, the ship breaking, silence, fade in on the beach, the survivor wakes up
    /// ("Where am I?"), chapter card DAY ZERO, objective SURVIVE UNTIL NIGHT, "Stay alive." Skippable (Space / Esc).
    /// </summary>
    public class IntroSequence : MonoBehaviour
    {
        public bool Playing { get; private set; }
        public event Action Finished;
        public float wakeClipSeconds = 6f;
        GameObject _player; bool _skip;

        public void Play(GameObject player)
        {
            _player = player; _skip = false;
            StopAllCoroutines(); StartCoroutine(Run());
        }

        void Update()
        {
            if (!Playing) return;
            var i = PlayerInputReader.Instance;
            if (i != null && (i.PausePressed || i.JumpPressed || (UnityEngine.InputSystem.Keyboard.current?.spaceKey.wasPressedThisFrame ?? false))) _skip = true;
        }

        IEnumerator Wait(float s) { float t = 0; while (t < s && !_skip) { t += Time.deltaTime; yield return null; } }

        IEnumerator Run()
        {
            Playing = true;
            var hud = HUDManager.Instance;
            var drv = _player.GetComponent<PlayerAnimationDriver>();
            var input = PlayerInputReader.Instance;
            var cam = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            var amb = FindFirstObjectByType<AmbienceManager>();
            var weather = WeatherManager.Instance;
            var sv = _player.GetComponent<Survival.PlayerSurvival>();
            if (input) input.GameplayBlocked = true;
            if (sv) sv.Paused = true;
            if (hud) { hud.SetHudVisible(false); hud.Fade(1f, 0f); }
            if (drv) drv.SetUnconscious();
            if (cam) { cam.InputEnabled = false; cam.Yaw = _player.transform.eulerAngles.y + 150f; cam.Pitch = 22f; cam.SnapBehindTarget(); }
            if (weather) weather.SetWeather(WeatherState.Storm, -1f, true);
            if (amb) amb.SnapStorm(true);

            yield return Wait(1.2f);
            SfxPlayer.Instance.Play2D(SfxId.ShipCreak, 0.9f);
            if (hud) hud.ShowSubtitle("The storm came out of nowhere.", 3f, true);
            yield return Wait(3.2f);
            SfxPlayer.Instance.Play2D(SfxId.Thunder, 1f); if (hud) hud.Flash(0.35f);
            if (hud) hud.ShowSubtitle("The ship broke apart on the reef...", 3f, true);
            yield return Wait(2.6f);
            SfxPlayer.Instance.Play2D(SfxId.WoodBreak, 1f);
            yield return Wait(1.6f);
            if (amb) amb.stormOn = false;
            if (weather) weather.SetWeather(WeatherState.Clear, -1f, false);
            yield return Wait(2.2f);

            // wake up on the beach
            if (hud) hud.Fade(0f, 3.5f);
            yield return Wait(2.5f);
            if (drv && !_skip) drv.PlayAction(PlayerActions.WakeUp);
            yield return Wait(wakeClipSeconds);
            SfxPlayer.Instance.Play2D(SfxId.BreathIn, 0.6f);
            if (hud) hud.ShowSubtitle("Where am I?", 2.8f, true);
            yield return Wait(3.0f);
            if (hud) hud.ShowBanner("DAY ZERO", "Survive until night", 4f);
            yield return Wait(2.2f);
            if (hud) hud.ShowSubtitle("Stay alive.", 2.5f, true);
            yield return Wait(1.0f);
            End();
        }

        void End()
        {
            var hud = HUDManager.Instance;
            var drv = _player.GetComponent<PlayerAnimationDriver>();
            if (_skip)
            {
                if (drv) { drv.StopAction(); drv.animator.Play("Locomotion", 0, 0f); }
                var amb = FindFirstObjectByType<AmbienceManager>(); if (amb) amb.SnapStorm(false);
                if (WeatherManager.Instance) WeatherManager.Instance.SetWeather(WeatherState.Clear, -1f, true);
                if (hud) { hud.Fade(0f, 0.6f); hud.ShowBanner("DAY ZERO", "Survive until night", 3f); }
            }
            if (hud) hud.SetHudVisible(true);
            var input = PlayerInputReader.Instance; if (input) input.GameplayBlocked = false;
            var cam = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null; if (cam) cam.InputEnabled = true;
            var sv = _player.GetComponent<Survival.PlayerSurvival>(); if (sv) sv.Paused = false;
            Playing = false;
            Finished?.Invoke();
        }
    }
}
