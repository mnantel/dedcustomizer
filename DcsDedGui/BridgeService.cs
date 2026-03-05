using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using DcsDedShared;
using Application = System.Windows.Application;
using DedSharp;

namespace DcsDedGui;

public enum ServiceStatus { Ok, Warning, Error }

public class BridgeService : INotifyPropertyChanged, IDisposable
{
    private DedDevice? _dedDevice;
    private UdpClient? _udp;
    private CancellationTokenSource _cts = new();
    private DedConfig _config;
    private readonly BmsDedDisplayProvider _displayProvider = new();
    private readonly string[] _invertedLines = Enumerable.Repeat(new string(' ', LineRenderer.Width), 5).ToArray();
    private readonly object _dedLock = new();
    private readonly HashSet<string> _seenUnknown = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastPacketTime = DateTime.MinValue;

    // ── Status properties ─────────────────────────────────────────────────────

    private string _dedStatusText = "Connecting…";
    public string DedStatusText { get => _dedStatusText; private set => Set(ref _dedStatusText, value); }

    private bool _dedConnected;
    public bool DedConnected { get => _dedConnected; private set => Set(ref _dedConnected, value); }

    private string _udpStatusText = "Starting…";
    public string UdpStatusText { get => _udpStatusText; private set => Set(ref _udpStatusText, value); }

    private bool _udpListening;
    public bool UdpListening { get => _udpListening; private set => Set(ref _udpListening, value); }

    private string _dcsStatusText = "Waiting for DCS…";
    public string DcsStatusText { get => _dcsStatusText; private set => Set(ref _dcsStatusText, value); }

    private string _currentAircraft = "";
    public string CurrentAircraft { get => _currentAircraft; private set => Set(ref _currentAircraft, value); }

    private ServiceStatus _overallStatus = ServiceStatus.Warning;
    public ServiceStatus OverallStatus { get => _overallStatus; private set => Set(ref _overallStatus, value); }

    /// <summary>
    /// All key-value pairs from the most recently received DCS packet.
    /// Null until the first packet arrives. Exposed for the Fields browser UI.
    /// </summary>
    private IReadOnlyDictionary<string, string>? _lastSnapshot;
    public IReadOnlyDictionary<string, string>? LastSnapshot
    {
        get => _lastSnapshot;
        private set => Set(ref _lastSnapshot, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BridgeService(DedConfig config) => _config = config;

    public void UpdateConfig(DedConfig config) => _config = config;

    // ── Start ─────────────────────────────────────────────────────────────────

    public void Start()
    {
        Task.Run(() => ConnectDedLoop(_cts.Token));
        Task.Run(() => RunUdpLoop(_cts.Token));
    }

    // ── DED hardware connection loop ──────────────────────────────────────────

    private async Task ConnectDedLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                DedDevice? old;
                lock (_dedLock) { old = _dedDevice; _dedDevice = null; }
                old?.Dispose();

                var dev = new DedDevice();
                lock (_dedLock) { _dedDevice = dev; }

                Dispatch(() =>
                {
                    DedConnected = true;
                    DedStatusText = "Connected (WinWing ICP)";
                    RefreshOverall();
                });
                return;
            }
            catch
            {
                Dispatch(() =>
                {
                    DedConnected = false;
                    DedStatusText = "Not found — retrying…";
                    RefreshOverall();
                });
                await Task.Delay(5_000, ct).ConfigureAwait(false);
            }
        }
    }

    // ── UDP receive loop ──────────────────────────────────────────────────────

    private async Task RunUdpLoop(CancellationToken ct)
    {
        try
        {
            _udp = new UdpClient(7778);
            Dispatch(() => { UdpListening = true; UdpStatusText = "Listening on port 7778"; RefreshOverall(); });
        }
        catch (Exception ex)
        {
            Dispatch(() => { UdpListening = false; UdpStatusText = $"Error: {ex.Message}"; RefreshOverall(); });
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(ct).ConfigureAwait(false);
                HandlePacket(result.Buffer);
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore transient UDP errors */ }
        }
    }

    private void HandlePacket(byte[] bytes)
    {
        _lastPacketTime = DateTime.UtcNow;

        string json = Encoding.UTF8.GetString(bytes);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Parse all JSON properties into a flat string dict (keys case-insensitive)
        var rawParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.EnumerateObject())
        {
            rawParams[prop.Name] = prop.Value.ValueKind == JsonValueKind.Number
                ? prop.Value.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture)
                : (prop.Value.GetString() ?? "");
        }

        string aircraftName = rawParams.TryGetValue("name", out var n) ? n : "";

        var profile = _config.Planes.FirstOrDefault(
            p => string.Equals(p.Name, aircraftName, StringComparison.OrdinalIgnoreCase));
        var mode = profile?.Mode ?? _config.DefaultMode;

        var snapshot = (IReadOnlyDictionary<string, string>)rawParams;
        Dispatch(() =>
        {
            CurrentAircraft = aircraftName;
            DcsStatusText   = $"Active — {aircraftName}";
            LastSnapshot    = snapshot;
            RefreshOverall();
        });

        if (mode == PlaneMode.Skip) return;

        if (!string.IsNullOrEmpty(aircraftName) && profile == null && _seenUnknown.Add(aircraftName))
            Console.WriteLine($"[DcsDedGui] Unconfigured aircraft: {aircraftName} — using {mode}");

        var effectiveFields = _config.Fields.Count > 0
            ? _config.Fields
            : LineRenderer.DefaultFields();

        string[] lines = (mode == PlaneMode.Custom && profile?.Lines is { } cl)
            ? LineRenderer.RenderLines(cl, rawParams, effectiveFields)
            : LineRenderer.RenderDefault(rawParams);

        _displayProvider.UpdateDedLines(lines, _invertedLines);

        lock (_dedLock)
        {
            if (_dedDevice != null)
                try { _dedDevice.UpdateDisplay(_displayProvider); } catch { }
        }
    }

    // ── Status tick ───────────────────────────────────────────────────────────

    public void Tick()
    {
        if (_lastPacketTime == DateTime.MinValue)
            DcsStatusText = "Waiting for DCS…";
        else if ((DateTime.UtcNow - _lastPacketTime).TotalSeconds > 10)
        {
            DcsStatusText   = "DCS not active";
            CurrentAircraft = "";
        }
        RefreshOverall();
    }

    private void RefreshOverall()
    {
        if (!DedConnected || !UdpListening)
            OverallStatus = ServiceStatus.Error;
        else if ((DateTime.UtcNow - _lastPacketTime).TotalSeconds > 10)
            OverallStatus = ServiceStatus.Warning;
        else
            OverallStatus = ServiceStatus.Ok;
    }

    private static void Dispatch(Action a)
    {
        if (Application.Current?.Dispatcher is { } d)
            d.BeginInvoke(a);
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp?.Dispose();
        lock (_dedLock) { _dedDevice?.Dispose(); }
    }
}
