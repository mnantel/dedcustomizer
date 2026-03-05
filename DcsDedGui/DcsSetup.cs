using System.IO;

namespace DcsDedGui;

/// <summary>
/// Helpers for detecting the DCS Saved Games folder and deploying / checking
/// the ded_bridge Lua script and Export.lua hook.
/// </summary>
public static class DcsSetup
{
    // DCS variants to probe, in preference order
    private static readonly string[] _variants = ["DCS.openbeta", "DCS"];

    // The line we write into Export.lua
    private const string ExportHook =
        "\n-- DCS DED Bridge\npcall(dofile, lfs.writedir()..\"Scripts/ded_bridge/ded_bridge.lua\")\n";

    // ── Folder detection ──────────────────────────────────────────────────────

    /// <summary>Returns all DCS Saved Games folders that actually exist on disk.</summary>
    public static IReadOnlyList<string> FindAllSavedGamesDirs()
    {
        var savedGames = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        return _variants
            .Select(v => Path.Combine(savedGames, v))
            .Where(Directory.Exists)
            .ToList();
    }

    /// <summary>Auto-detects the first DCS Saved Games folder that exists.</summary>
    public static string? AutoDetect() => FindAllSavedGamesDirs().FirstOrDefault();

    /// <summary>Returns the configured override if set; otherwise auto-detects.</summary>
    public static string? ResolveDir(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? AutoDetect() : configured;

    // ── Path helpers ──────────────────────────────────────────────────────────

    public static string ScriptPath(string dcsDir) =>
        Path.Combine(dcsDir, "Scripts", "ded_bridge", "ded_bridge.lua");

    public static string ExportLuaPath(string dcsDir) =>
        Path.Combine(dcsDir, "Scripts", "Export.lua");

    // ── Health checks ─────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the deployed ded_bridge.lua exists and matches the
    /// current embedded version.  Returns Missing / OutOfDate / Current.
    /// </summary>
    public static ScriptStatus CheckScript(string dcsDir)
    {
        var path = ScriptPath(dcsDir);
        if (!File.Exists(path)) return ScriptStatus.Missing;

        // Normalise line endings before comparing so CRLF/LF differences don't matter
        var deployed = File.ReadAllText(path).ReplaceLineEndings("\n").Trim();
        var embedded = DedBridgeLua.Content.ReplaceLineEndings("\n").Trim();
        return deployed == embedded ? ScriptStatus.Current : ScriptStatus.OutOfDate;
    }

    /// <summary>
    /// Returns true if Export.lua exists and already contains a reference to ded_bridge.
    /// Returns false if the file is missing or doesn't load the script.
    /// </summary>
    public static ExportLuaStatus CheckExport(string dcsDir)
    {
        var path = ExportLuaPath(dcsDir);
        if (!File.Exists(path)) return ExportLuaStatus.Missing;
        var text = File.ReadAllText(path);
        return text.Contains("ded_bridge", StringComparison.OrdinalIgnoreCase)
            ? ExportLuaStatus.Configured
            : ExportLuaStatus.NotConfigured;
    }

    // ── Deployment actions ────────────────────────────────────────────────────

    /// <summary>
    /// Writes the embedded ded_bridge.lua to {dcsDir}/Scripts/ded_bridge/ded_bridge.lua.
    /// Creates the directory if needed. Safe to call even when already deployed (redeploy).
    /// </summary>
    public static void DeployScript(string dcsDir)
    {
        var dest = ScriptPath(dcsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllText(dest, DedBridgeLua.Content);
    }

    /// <summary>
    /// Appends the ded_bridge hook to Export.lua.
    /// Creates Export.lua (with just the hook) if it doesn't exist.
    /// </summary>
    public static void ConfigureExport(string dcsDir)
    {
        var path = ExportLuaPath(dcsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
            File.WriteAllText(path, ExportHook.TrimStart());
        else
            File.AppendAllText(path, ExportHook);
    }
}

public enum ExportLuaStatus
{
    /// <summary>Export.lua doesn't exist yet.</summary>
    Missing,
    /// <summary>Export.lua exists but doesn't reference ded_bridge.</summary>
    NotConfigured,
    /// <summary>Export.lua already contains a ded_bridge hook.</summary>
    Configured,
}

public enum ScriptStatus
{
    /// <summary>ded_bridge.lua has not been deployed.</summary>
    Missing,
    /// <summary>ded_bridge.lua exists but does not match the current embedded version.</summary>
    OutOfDate,
    /// <summary>ded_bridge.lua is deployed and up to date.</summary>
    Current,
}
