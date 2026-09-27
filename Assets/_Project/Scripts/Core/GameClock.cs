namespace PrimalFrontier.Core
{
    /// <summary>
    /// Game time in seconds since the new game started (advanced by TimeManager, saved, jumps forward when sleeping).
    /// Respawn timers use this instead of Time.time so they survive save / load and sleeping.
    /// </summary>
    public static class GameClock
    {
        public static double Now;
        /// <summary>real seconds per in-game hour (TimeManager keeps it in sync)</summary>
        public static float SecondsPerHour = 90f;
        public static double Hours(float h) => h * SecondsPerHour;
    }
}
