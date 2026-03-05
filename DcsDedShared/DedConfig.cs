namespace DcsDedShared;

public class DedConfig
{
    /// <summary>Mode applied to aircraft that have no explicit profile.</summary>
    public PlaneMode DefaultMode { get; set; } = PlaneMode.Default;

    public List<PlaneProfile> Planes { get; set; } = new();

    /// <summary>
    /// Override for the DCS Saved Games directory (e.g. "C:\Users\...\Saved Games\DCS.openbeta").
    /// Leave null to let the app auto-detect it.
    /// </summary>
    public string? DcsSavedGamesDir { get; set; }

    /// <summary>User-defined field catalog. Each entry maps a short alias to a DCS param key + format.</summary>
    public List<FieldDefinition> Fields { get; set; } = new();
}
