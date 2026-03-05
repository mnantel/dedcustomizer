namespace DcsDedShared;

public enum PlaneMode
{
    Default,
    Skip,
    Custom,
}

public class PlaneProfile
{
    public string Name { get; set; } = "";
    public PlaneMode Mode { get; set; } = PlaneMode.Default;

    /// <summary>
    /// 5-element array of token lists — one list per DED line.
    /// Only used when Mode == Custom.
    /// </summary>
    public List<DedLineToken>[] Lines { get; set; } =
        Enumerable.Range(0, 5).Select(_ => new List<DedLineToken>()).ToArray();
}
