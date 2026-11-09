using System.Windows;

namespace Vigia;

public partial class App : Application
{
    private Mutex? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\VigiaLocal.Desktop", out bool first);
        if (!first)
        {
            MessageBox.Show("O Vigia Local já está aberto. Procure o ícone perto do relógio.", "Vigia Local");
            Shutdown();
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
        if (e.Args.Contains("--tray")) MainWindow.Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
