using System.Windows;
using WENDY.Managers;
using WENDY.Modules;
using WENDY.Wendy;

namespace WENDY;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Composition root: the application assembles the core and its components.
        var core = new WendyCore();
        core.RegisterManager(new NetworkManager());
        core.RegisterManager(new NetworkEnvironmentManager());
        core.RegisterModule(new NetworkModule());
        core.RegisterModule(new NetworkRecoveryModule());

        var mainWindow = new MainWindow(core);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
