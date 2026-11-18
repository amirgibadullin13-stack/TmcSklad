using System.Windows;
namespace Tmc;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Store.Load();
        if (new LoginWindow().ShowDialog() != true) { Shutdown(); return; }
        var m = new MainWindow();
        MainWindow = m;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        m.Show();
    }
}
