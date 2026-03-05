using System.Windows;
using Application = System.Windows.Application;

namespace DcsDedGui;

public partial class App : Application
{
    private TrayApp? _trayApp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _trayApp = new TrayApp();
        _trayApp.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayApp?.Dispose();
        base.OnExit(e);
    }
}
