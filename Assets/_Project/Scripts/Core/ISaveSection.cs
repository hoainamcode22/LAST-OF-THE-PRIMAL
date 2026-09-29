namespace PrimalFrontier.Core
{
    /// <summary>
    /// A named JSON blob in the save file, owned by any system that keeps its own state (creatures, fruit clusters,
    /// status effects...), so it never has to edit SaveData. Register with <see cref="SaveSystem.RegisterSection"/>
    /// (e.g. in OnEnable) and unregister in OnDisable. Capture returns the section's JSON (null = nothing to save);
    /// Restore gets it back after the rest of the save was applied. A section missing from the file is not restored
    /// (the system keeps its reset state); a section that throws while capturing or restoring is skipped with a warning,
    /// never a crash. Keys must be unique and stable (they are stored in the file).
    /// </summary>
    public interface ISaveSection
    {
        string SectionKey { get; }
        string CaptureSection();
        void RestoreSection(string json);
    }
}
