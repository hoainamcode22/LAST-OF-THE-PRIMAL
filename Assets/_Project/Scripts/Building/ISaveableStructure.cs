namespace PrimalFrontier.Building
{
    /// <summary>
    /// A placed structure component with extra state for the save (campfire cooking slots, rain collector water...).
    /// SaveSystem stores the returned string in StructureData.state and hands it back after the structure is spawned
    /// on load. One per structure object; keep it small (JsonUtility of a tiny class is fine).
    /// </summary>
    public interface ISaveableStructure
    {
        string CaptureState();
        void RestoreState(string state);
    }
}
