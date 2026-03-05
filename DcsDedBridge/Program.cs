using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DedSharp;
using DcsDedShared;

class Program
{
    static string Fit(string s, int width)
    {
        s ??= "";
        if (s.Length > width) return s[..width];
        return s.PadRight(width);
    }

    static void Main(string[] args)
    {
        const int port = 7778;
        const int width = LineRenderer.Width;

        bool testMode = args.Length > 0 &&
            (args[0].Equals("--test", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("-t", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine("Opening WinWing DED...");
        using var device = new DedDevice();
        var displayProvider = new BmsDedDisplayProvider();

        var invertedLines = Enumerable.Repeat(new string(' ', width), 5).ToArray();

        if (testMode)
        {
            RunTestMode(device, displayProvider, invertedLines, width);
            return;
        }

        var config = ConfigStore.Load();
        Console.WriteLine($"Config loaded from {ConfigStore.ConfigPath}");
        Console.WriteLine($"  {config.Planes.Count} aircraft profile(s) configured.");

        var effectiveFields = config.Fields.Count > 0
            ? config.Fields
            : LineRenderer.DefaultFields();

        var seenUnknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine("Listening for DCS UDP data on port {0}...", port);

        using var udp = new UdpClient(port);
        IPEndPoint remoteEP = new(IPAddress.Any, 0);

        while (true)
        {
            var result = udp.Receive(ref remoteEP);
            var json = Encoding.UTF8.GetString(result);

            // Parse all JSON properties into a flat string dict (keys case-insensitive)
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var rawParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in root.EnumerateObject())
            {
                rawParams[prop.Name] = prop.Value.ValueKind == JsonValueKind.Number
                    ? prop.Value.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : (prop.Value.GetString() ?? "");
            }

            var aircraftName = rawParams.TryGetValue("name", out var n) ? n : "";

            var profile = config.Planes.FirstOrDefault(
                p => string.Equals(p.Name, aircraftName, StringComparison.OrdinalIgnoreCase));

            var mode = profile?.Mode ?? config.DefaultMode;

            if (mode == PlaneMode.Skip)
                continue;

            if (!string.IsNullOrEmpty(aircraftName) &&
                profile == null && seenUnknown.Add(aircraftName))
            {
                Console.WriteLine($"[DcsDedBridge] Unconfigured aircraft: {aircraftName} — using {mode} mode");
            }

            string[] lines = (mode == PlaneMode.Custom && profile?.Lines is { } customLines)
                ? LineRenderer.RenderLines(customLines, rawParams, effectiveFields)
                : LineRenderer.RenderDefault(rawParams);

            displayProvider.UpdateDedLines(lines, invertedLines);
            device.UpdateDisplay(displayProvider);
        }
    }

    static void RunTestMode(DedDevice device, BmsDedDisplayProvider displayProvider,
        string[] invertedLines, int width)
    {
        Console.WriteLine("=== TEST MODE ===");
        Console.WriteLine("Sending test patterns to display. Press any key to advance, Q to quit.\n");

        var test1 = new[]
        {
            Fit("DCSDEDBRIDGE  TEST",  width),
            Fit("LINE 2   HELLO WORLD", width),
            Fit("LINE 3   ABCDEFGHIJKL", width),
            Fit("LINE 4   1234567890",  width),
            Fit("LINE 5   OK!",         width)
        };
        SendTestPattern(device, displayProvider, test1, invertedLines, "Pattern 1: Basic text");
        if (WaitForKey()) return;

        var test2 = new[]
        {
            Fit("F-16C HDG270 M0.85",  width),
            Fit("ALT 25000ft IAS 350kt", width),
            Fit("STPT  4  N38 58 24",  width),
            Fit("        W077 27 36",   width),
            Fit("CHAN 14   T 1500",     width)
        };
        SendTestPattern(device, displayProvider, test2, invertedLines, "Pattern 2: Simulated flight data");
        if (WaitForKey()) return;

        var defaultLines = LineRenderer.RenderDefault(LineRenderer.SampleParams);
        SendTestPattern(device, displayProvider, defaultLines, invertedLines, "Pattern 3: Default token layout with sample data");
        if (WaitForKey()) return;

        var blank = Enumerable.Repeat(Fit("", width), 5).ToArray();
        SendTestPattern(device, displayProvider, blank, invertedLines, "Pattern 4: Blank / clear");

        Console.WriteLine("\nTest complete. Press any key to exit.");
        Console.ReadKey(true);
        device.ClearDisplay();
    }

    static void SendTestPattern(DedDevice device, BmsDedDisplayProvider displayProvider,
        string[] lines, string[] invertedLines, string label)
    {
        Console.WriteLine(label);
        for (int i = 0; i < lines.Length; i++)
            Console.WriteLine("  [{0}]", lines[i]);
        displayProvider.UpdateDedLines(lines, invertedLines);
        device.UpdateDisplay(displayProvider);
    }

    static bool WaitForKey()
    {
        Console.WriteLine("\nPress any key for next pattern (Q to quit)...");
        var key = Console.ReadKey(true);
        Console.WriteLine();
        return key.Key == ConsoleKey.Q;
    }
}
