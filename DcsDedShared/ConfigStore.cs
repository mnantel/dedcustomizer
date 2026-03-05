using System.Text.Json;
using System.Text.Json.Serialization;

namespace DcsDedShared;

public static class ConfigStore
{
    public static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DcsDedBridge");

    public static readonly string ConfigPath =
        Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    // Maps old DedTokenType enum name → new default alias
    private static readonly Dictionary<string, string> _tokenMigration = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HeadingDeg"] = "HDG",
        ["AltitudeFt"] = "ALT",
        ["IasKnots"]   = "IAS",
        ["UhfFreq"]    = "UHF",
        ["VhfAmFreq"]  = "AM",
        ["VhfFmFreq"]  = "FM",
        ["FuelLbs"]    = "FUEL",
        ["Steerpoint"] = "STPT",
    };

    public static DedConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return Seed(new DedConfig());

            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<DedConfig>(json, _opts) ?? new DedConfig();

            MigrateOldTokens(json, config);

            if (config.Fields.Count == 0)
                config.Fields = LineRenderer.DefaultFields();

            MigrateFieldFormats(config);
            MigrateSeparators(config);

            return config;
        }
        catch
        {
            return Seed(new DedConfig());
        }
    }

    public static void Save(DedConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, _opts);
        File.WriteAllText(ConfigPath, json);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts old-style "HDG XXX" / "XXX.XXX" format strings (no % delimiters)
    /// to the new "%placeholder%" style.  Safe to call multiple times — skips fields
    /// whose Format already contains a '%'.
    /// </summary>
    private static void MigrateFieldFormats(DedConfig config)
    {
        var oldXPat = new System.Text.RegularExpressions.Regex(
            @"(X+)(?:\.(X+))?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var fd in config.Fields)
        {
            if (fd.Format.Contains('%')) continue;  // already new-style

            var m = oldXPat.Match(fd.Format);
            if (!m.Success) continue;               // literal-only — leave as-is

            string prefix = fd.Format[..m.Index];
            string suffix = fd.Format[(m.Index + m.Length)..];
            string newPh;

            if (m.Groups[2].Success)
            {
                // Float: XXX.XXX → %XXX.XXX%
                newPh = $"%{m.Groups[1].Value}.{m.Groups[2].Value}%";
            }
            else if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(suffix))
            {
                // Entire format is only X's with no surrounding text → string field
                newPh = "%" + new string('S', m.Groups[1].Length) + "%";
            }
            else
            {
                // Integer with prefix / suffix: HDG XXX → HDG %XXX%
                newPh = $"%{m.Groups[1].Value}%";
            }

            fd.Format = prefix + newPh + suffix;
        }
    }

    /// <summary>
    /// Converts old-style separator tokens that stored their width as an int
    /// (SeparatorWidth) to the new SeparatorText string.  Safe to call multiple
    /// times — skips separators whose SeparatorText is already populated.
    /// </summary>
    private static void MigrateSeparators(DedConfig config)
    {
        foreach (var profile in config.Planes)
            foreach (var line in profile.Lines)
                foreach (var tok in line)
                    if (tok.IsSeparator && string.IsNullOrEmpty(tok.SeparatorText))
                        tok.SeparatorText = new string(' ', Math.Max(1, tok.SeparatorWidth));
    }

    private static DedConfig Seed(DedConfig config)
    {
        config.Fields = LineRenderer.DefaultFields();
        return config;
    }

    /// <summary>
    /// Detects and migrates old-style tokens that had a "Type" enum property
    /// (e.g. {"Type":"HeadingDeg","SeparatorWidth":2}) to the new alias-based model.
    /// </summary>
    private static void MigrateOldTokens(string originalJson, DedConfig config)
    {
        // Quick check: if the JSON doesn't contain "HeadingDeg" or other old type names, skip
        bool needsMigration = false;
        foreach (var key in _tokenMigration.Keys)
        {
            if (originalJson.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                needsMigration = true;
                break;
            }
        }
        if (!needsMigration) return;

        // Re-parse planes section to detect old token format
        using var doc = JsonDocument.Parse(originalJson);
        if (!doc.RootElement.TryGetProperty("Planes", out var planesEl)) return;
        if (planesEl.ValueKind != JsonValueKind.Array) return;

        int planeIdx = 0;
        foreach (var planeEl in planesEl.EnumerateArray())
        {
            if (planeIdx >= config.Planes.Count) break;
            var profile = config.Planes[planeIdx++];

            if (!planeEl.TryGetProperty("Lines", out var linesEl)) continue;
            if (linesEl.ValueKind != JsonValueKind.Array) continue;

            int lineIdx = 0;
            foreach (var lineEl in linesEl.EnumerateArray())
            {
                if (lineIdx >= profile.Lines.Length) break;

                var migratedTokens = new List<DedLineToken>();
                if (lineEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tokEl in lineEl.EnumerateArray())
                    {
                        if (tokEl.TryGetProperty("Type", out var typeEl))
                        {
                            string typeName = typeEl.GetString() ?? "";
                            int sepW = tokEl.TryGetProperty("SeparatorWidth", out var sw)
                                ? sw.GetInt32() : 2;

                            if (string.Equals(typeName, "Separator", StringComparison.OrdinalIgnoreCase))
                                migratedTokens.Add(DedLineToken.MakeSeparator(new string(' ', Math.Max(1, sepW))));
                            else if (_tokenMigration.TryGetValue(typeName, out var alias))
                                migratedTokens.Add(new DedLineToken(alias));
                        }
                        else
                        {
                            // Already new format — keep what was deserialized
                            migratedTokens.Add(profile.Lines[lineIdx].Count > migratedTokens.Count
                                ? profile.Lines[lineIdx][migratedTokens.Count]
                                : DedLineToken.MakeSeparator("  "));
                        }
                    }
                    profile.Lines[lineIdx] = migratedTokens;
                }
                lineIdx++;
            }
        }
    }
}
