namespace PrimalFrontier.Core
{
    /// <summary>Volume groups (settings menu). Master goes through AudioListener.volume; the others scale sources.</summary>
    public static class AudioBus
    {
        public static float Sfx = 1f, Ambience = 1f, Music = 1f;
    }
}
