using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Something to look at closely: giant footprints, claw marks, an old camp, the captain's log. Examining it raises its
    /// event with <see cref="discoveryId"/> (the journal DISCOVERIES page and missions listen) and the survivor says a short
    /// thought as a subtitle. The thought and the name come from the story texts when the prop leaves them empty (ENV only
    /// sets the id; "claw_marks_2" reads as "claw_marks"). Examined props are saved by their SaveId.
    /// </summary>
    public class Examinable : Interactable
    {
        public string displayName = "Something";
        public string verb = "Examine";
        public string discoveryId;
        public GameEventType eventType = GameEventType.Discovery;
        [TextArea(2, 5)] [Tooltip("empty = the built-in thought for the discovery id")] public string thought;
        public bool once = true;
        public float range = 2.4f;
        public bool Examined { get; private set; }
        public override float Range => range;
        public override float Radius => 0.6f;
        public override int Priority => 2;

        string _prompt;
        string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(displayName) && displayName != "Something") return displayName;
                var t = Story.StoryTexts.Discovery(discoveryId);
                return t != null ? t.title.ToLowerInvariant() : "something strange";
            }
        }
        /// <summary>the survivor's thought (the prop's own text wins)</summary>
        public string Thought
        {
            get
            {
                if (!string.IsNullOrEmpty(thought)) return thought.Replace(" (Journal updated)", "");     // the HUD already notes the journal
                var t = Story.StoryTexts.Discovery(discoveryId);
                return t != null ? t.thought : null;
            }
        }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (Examined && once) return null;
            return _prompt ??= verb + " " + Name;
        }

        public override void Interact(PlayerInteraction p)
        {
            p.DoOneShot(PlayerActions.Interact, "OnInteract", () => Examine(), FocusPoint, 0.7f, this);
        }

        public void Examine()
        {
            if (Examined && once) return;
            Examined = true;
            var j = Story.JournalSystem.Instance; if (j && !string.IsNullOrEmpty(discoveryId)) j.EnsureDiscoveryPage(this);
            var line = Thought;
            if (!string.IsNullOrEmpty(line)) Story.ProtagonistVoice.SayText("examine:" + (discoveryId ?? name), line, true, true);
            GameEvents.Raise(eventType, discoveryId, 1, transform.position);
        }
        public void Restore(bool examined) => Examined = examined;
    }
}
