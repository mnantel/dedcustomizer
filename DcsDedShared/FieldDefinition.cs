namespace DcsDedShared;

/// <summary>
/// A user-defined display field: binds a short alias to a DCS cockpit param key
/// and a format template that controls exactly how the value is rendered.
/// </summary>
public class FieldDefinition
{
    /// <summary>Short user-facing name used in line tokens, e.g. "HDG", "ALT", "UHF".</summary>
    public string Alias { get; set; } = "";

    /// <summary>
    /// The key in the flat JSON packet sent by ded_bridge.lua.
    /// E.g. "hdg_deg", "alt_ft", "UHF_FREQ", "BASE_SENSOR_MACH".
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Display format template using %placeholder% syntax:
    ///   %XXX%        integer, right-justify to 3 chars (truncate left on overflow)
    ///   %XXX.XXX%    float, 3 decimal places, right-justify integer part
    ///   %SSSSSSSSS%  string, left-justify to 9 chars, truncate right
    ///   %B%          boolean: 0/"false" → "OFF", non-zero/"true" → "ON " (always 3 chars)
    ///
    /// Literal text outside %...% is preserved verbatim.
    /// Output width is always ComputeFormatWidth(Format) — use the Width property.
    /// Example: "HDG %XXX%" → width 7, output "HDG 270"
    /// </summary>
    public string Format { get; set; } = "%X%";

    /// <summary>
    /// A representative value shown in the preview when DCS is not running.
    /// Populated automatically when a field is imported via "Browse DCS Fields…".
    /// </summary>
    public string Sample { get; set; } = "";

    /// <summary>Fixed output width in characters, computed from the format template.</summary>
    public int Width => LineRenderer.ComputeFormatWidth(Format);
}
