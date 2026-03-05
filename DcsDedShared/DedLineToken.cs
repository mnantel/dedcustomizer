namespace DcsDedShared;

/// <summary>
/// A single token on a DED display line.
/// If Alias is set the token renders a user-defined field from the Fields catalog.
/// If Alias is null the token renders the literal SeparatorText string.
/// </summary>
public class DedLineToken
{
    /// <summary>
    /// Alias of the FieldDefinition to render.
    /// Null means this is a separator (renders SeparatorText verbatim).
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>The literal text to output when this is a separator token.</summary>
    public string SeparatorText { get; set; } = "";

    /// <summary>Kept for migration of old config files that stored width as an int.</summary>
    public int SeparatorWidth { get; set; } = 0;

    public bool IsSeparator => Alias == null;

    public DedLineToken() { }

    public DedLineToken(string alias) => Alias = alias;

    public static DedLineToken MakeSeparator(string text = "  ") =>
        new DedLineToken { Alias = null, SeparatorText = text };
}
