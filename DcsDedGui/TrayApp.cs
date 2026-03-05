using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using DcsDedShared;

namespace DcsDedGui;

public sealed class TrayApp : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly BridgeService _bridge;
    private ConfigWindow? _configWindow;
    private DedConfig _config;

    private readonly System.Drawing.Icon _iconGreen;
    private readonly System.Drawing.Icon _iconYellow;
    private readonly System.Drawing.Icon _iconRed;

    private readonly DispatcherTimer _statusTimer;

    public TrayApp()
    {
        _iconGreen  = MakeCircleIcon(System.Drawing.Color.LimeGreen);
        _iconYellow = MakeCircleIcon(System.Drawing.Color.Gold);
        _iconRed    = MakeCircleIcon(System.Drawing.Color.OrangeRed);

        _config = ConfigStore.Load();
        _bridge = new BridgeService(_config);
        _bridge.PropertyChanged += (_, _) => UpdateTrayIcon();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Configuration…", null, (_, _) => ShowConfigWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "DCS DED Bridge",
            Icon = _iconYellow,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.MouseDoubleClick += (_, _) => ShowConfigWindow();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => { _bridge.Tick(); UpdateTrayIcon(); };
        _statusTimer.Start();
    }

    public void Start() => _bridge.Start();

    private void ShowConfigWindow()
    {
        if (_configWindow == null)
        {
            _configWindow = new ConfigWindow(_bridge, _config);
            _configWindow.ConfigSaved += OnConfigSaved;
        }
        _configWindow.Show();
        _configWindow.Activate();
    }

    private void OnConfigSaved(DedConfig config)
    {
        _config = config;
        _bridge.UpdateConfig(config);
    }

    private void UpdateTrayIcon()
    {
        (_trayIcon.Icon, _trayIcon.Text) = _bridge.OverallStatus switch
        {
            ServiceStatus.Ok      => (_iconGreen,  $"DCS DED — Active ({_bridge.CurrentAircraft})"),
            ServiceStatus.Warning => (_iconYellow, "DCS DED — Waiting for DCS"),
            _                     => (_iconRed,    "DCS DED — Hardware/port error"),
        };
    }

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        _bridge.Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _statusTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _bridge.Dispose();
        _iconGreen.Dispose();
        _iconYellow.Dispose();
        _iconRed.Dispose();
    }

    // ── Icon helpers ──────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static System.Drawing.Icon MakeCircleIcon(System.Drawing.Color fill)
    {
        using var bmp = new System.Drawing.Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new System.Drawing.SolidBrush(fill);
            g.FillEllipse(brush, 2, 2, 12, 12);
        }
        IntPtr hIcon = bmp.GetHicon();
        var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }
}
