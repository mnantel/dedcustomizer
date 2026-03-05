using System.Text;
using System.Text.RegularExpressions;

namespace DcsDedShared;

public static class LineRenderer
{
    public const int Width = 24;

    // ── Sample data for UI preview ────────────────────────────────────────────

    /// <summary>
    /// Representative raw packet values used by the config UI preview.
    /// Keys match what ded_bridge.lua sends in the JSON payload.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SampleParams =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hdg_deg"]   = "270",
            ["alt_ft"]    = "25000",
            ["ias_kt"]    = "350",
            ["uhf_mhz"]   = "251.000",
            ["vhfam_mhz"] = "130.000",
            ["vhffm_mhz"] = "046.600",
            ["fuel_lbs"]  = "7865",
            ["stpt_name"] = "INIT POSIT",
            ["stpt_num"]  = "4",
            // representative cockpit params
            ["BASE_SENSOR_BAROALT"]              = "7620.00",
            ["BASE_SENSOR_IAS"]                  = "180.04",
            ["BASE_SENSOR_MACH"]                 = "0.430",
            ["BASE_SENSOR_MAG_HEADING"]          = "4.712",
            ["BASE_SENSOR_FUEL_TOTAL"]           = "3571.00",
            ["BASE_SENSOR_LEFT_ENGINE_RPM"]      = "0.747",
            ["BASE_SENSOR_RIGHT_ENGINE_RPM"]     = "0.747",
            ["UHF_FREQ"]                         = "251.000",
            ["VHF_AM_FREQ"]                      = "130.000",
            ["VHF_FREQ"]                         = "046.600",
            ["STEERPOINT"]                       = "INIT POSIT",
            ["HUD_MODE"]                         = "NAV",
        };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Renders 5 lines using a custom token layout and raw packet values.</summary>
    public static string[] RenderLines(
        List<DedLineToken>[] lines,
        IReadOnlyDictionary<string, string> rawParams,
        IEnumerable<FieldDefinition> fields)
    {
        var fieldMap = fields.ToDictionary(f => f.Alias, f => f, StringComparer.OrdinalIgnoreCase);
        var result = new string[5];
        for (int i = 0; i < 5; i++)
        {
            var tokens = i < lines.Length ? lines[i] : new List<DedLineToken>();
            result[i] = RenderLine(tokens, rawParams, fieldMap);
        }
        return result;
    }

    /// <summary>Renders the default 5-line layout.</summary>
    public static string[] RenderDefault(IReadOnlyDictionary<string, string> rawParams)
        => RenderLines(DefaultTokenLines(), rawParams, DefaultFields());

    /// <summary>Default token layout (alias-based).</summary>
    public static List<DedLineToken>[] DefaultTokenLines() =>
    [
        [new("HDG"), DedLineToken.MakeSeparator("  "), new("ALT")],
        [new("UHF"), DedLineToken.MakeSeparator("  "), new("IAS")],
        [new("STPT")],
        [new("AM"),  DedLineToken.MakeSeparator("  "), new("FM")],
        [new("FUEL")],
    ];

    /// <summary>Pre-built field catalog matching the default layout.</summary>
    public static List<FieldDefinition> DefaultFields() =>
    [
        new() { Alias = "HDG",  Key = "hdg_deg",   Format = "HDG %XXX%"                       },
        new() { Alias = "ALT",  Key = "alt_ft",    Format = "ALT %XXXXX% FT"                  },
        new() { Alias = "IAS",  Key = "ias_kt",    Format = "%XXX%KT"                         },
        new() { Alias = "UHF",  Key = "uhf_mhz",   Format = "UHF %XXX.XXX%"                  },
        new() { Alias = "AM",   Key = "vhfam_mhz", Format = "AM %XXX.XXX%"                   },
        new() { Alias = "FM",   Key = "vhffm_mhz", Format = "FM %XXX.XXX%"                   },
        new() { Alias = "FUEL", Key = "fuel_lbs",  Format = "FUEL %XXXXX% LBS"               },
        new() { Alias = "STPT", Key = "stpt_name", Format = "%SSSSSSSSSSSSSSSSSSSSSSSS%"      },
    ];

    // ── Format template ───────────────────────────────────────────────────────

    // %SSSS%       — string, left-justify to S-count chars, truncate right
    // %B%          — boolean: 0/"false" → "OFF", else → "ON " (always 3 chars)
    // %XXX%        — integer, right-justify to X-count chars, truncate left on overflow
    // %XXX.XXX%    — float, round to decimal X-count, right-justify integer part
    // Literal text outside %...% is preserved verbatim.
    private static readonly Regex _fmtPattern =
        new Regex(@"%(S+|B|X+(?:\.X+)?)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Computes the fixed output width of a format template string.
    /// Literal chars contribute 1 each; %S...S% → S-count; %B% → 3; %X...X% / %X.X% → X-count (dot included).
    /// </summary>
    public static int ComputeFormatWidth(string format)
    {
        if (string.IsNullOrEmpty(format)) return 0;

        int total = 0;
        int lastEnd = 0;
        foreach (Match m in _fmtPattern.Matches(format))
        {
            total += m.Index - lastEnd;                            // literal chars before placeholder
            string ph = m.Groups[1].Value.ToUpperInvariant();
            if (ph[0] == 'B')      total += 3;                    // "ON " or "OFF"
            else                   total += ph.Length;            // S-count or X-count (dot counts)
            lastEnd = m.Index + m.Length;
        }
        total += format.Length - lastEnd;                         // trailing literal chars
        return total;
    }

    /// <summary>
    /// Applies a %placeholder%-style format template to a raw string value.
    /// Output length always equals ComputeFormatWidth(formatTemplate).
    /// </summary>
    public static string RenderValue(string rawValue, string formatTemplate)
    {
        if (string.IsNullOrEmpty(formatTemplate)) return rawValue;

        var m = _fmtPattern.Match(formatTemplate);
        if (!m.Success)
        {
            // Literal-only format — return it as-is (fixed-width label, no substitution)
            return formatTemplate;
        }

        string ph     = m.Groups[1].Value.ToUpperInvariant();
        string prefix = formatTemplate[..m.Index];
        string suffix = formatTemplate[(m.Index + m.Length)..];
        string rendered;

        if (ph[0] == 'S')
        {
            // String: left-align, truncate right to S-count
            int w = ph.Length;
            rendered = rawValue.Length > w ? rawValue[..w] : rawValue.PadRight(w);
        }
        else if (ph[0] == 'B')
        {
            // Boolean: non-zero / "true" → "ON " (3 chars), else → "OFF"
            bool isTrue = rawValue.Equals("true", StringComparison.OrdinalIgnoreCase)
                || (double.TryParse(rawValue,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double bv) && bv != 0.0);
            rendered = isTrue ? "ON " : "OFF";
        }
        else
        {
            // Numeric: %XXX% (integer) or %XXX.XXX% (float)
            int dotIdx = ph.IndexOf('.');
            int intW   = dotIdx >= 0 ? dotIdx : ph.Length;
            int decW   = dotIdx >= 0 ? ph.Length - dotIdx - 1 : 0;

            if (double.TryParse(rawValue,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double num))
            {
                if (decW > 0)
                {
                    string formatted = num.ToString("F" + decW,
                        System.Globalization.CultureInfo.InvariantCulture);
                    int di       = formatted.IndexOf('.');
                    string intPt = di >= 0 ? formatted[..di] : formatted;
                    string decPt = di >= 0 ? formatted[(di + 1)..] : new string('0', decW);
                    intPt = intPt.Length > intW ? intPt[^intW..] : intPt.PadLeft(intW);
                    rendered = intPt + "." + decPt;
                }
                else
                {
                    string s = ((long)Math.Round(num)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    rendered = s.Length > intW ? s[^intW..] : s.PadLeft(intW);
                }
            }
            else
            {
                // Non-numeric in a numeric field: left-align, truncate to intW
                rendered = rawValue.Length > intW ? rawValue[..intW] : rawValue.PadRight(intW);
            }
        }

        return prefix + rendered + suffix;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string RenderLine(
        List<DedLineToken> tokens,
        IReadOnlyDictionary<string, string> rawParams,
        Dictionary<string, FieldDefinition> fieldMap)
    {
        var sb = new StringBuilder();
        foreach (var tok in tokens)
        {
            if (tok.IsSeparator)
            {
                sb.Append(tok.SeparatorText);
            }
            else if (tok.Alias != null && fieldMap.TryGetValue(tok.Alias, out var fd))
            {
                string raw = rawParams.TryGetValue(fd.Key, out var v) ? v : fd.Sample;
                sb.Append(RenderValue(raw, fd.Format));
            }
        }
        var s = sb.ToString();
        return s.Length > Width ? s[..Width] : s.PadRight(Width);
    }
}
