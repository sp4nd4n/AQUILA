using System.Windows;

namespace HackerSimulator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!VmContext.Init(e.Args, out string error))
        {
            MessageBox.Show(error, "AQUILA", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }
}