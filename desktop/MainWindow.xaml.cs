using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Vigia;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    private LocalSettings settings = LocalSettings.Load();
    private bool quitting;
    private bool cloudBusy;
    private bool updateBusy;
    private string? preparedUpdate;

    public MainWindow()
    {
        InitializeComponent();
        var icon = System.Drawing.SystemIcons.Information;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir Vigia Local", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Encerrar", null, (_, _) => Dispatcher.Invoke(Quit));
        tray = new Forms.NotifyIcon { Icon = icon, Text = "Vigia Local", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        Camera.Restore(settings.Camera);
        Camera.SettingsChanged += value =>
        {
            settings = settings with { Camera = value };
            Save();
        };
        Camera.Arrival += async () =>
        {
            if (settings.Writer is null) return;
            try { await CloudRelay.Arrival(settings.Writer, "arrival"); }
            catch { CloudStatus.Text = "Aviso local. Não foi possível encaminhar ao celular."; }
        };
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            StartWithWindows.IsChecked = run?.GetValue("VigiaLocal") is string;
        UpdateLinkedState();
        heartbeat.Tick += async (_, _) => await Ping();
        updateTimer.Tick += async (_, _) => await CheckUpdate(false);
        heartbeat.Start();
        updateTimer.Start();
        Loaded += async (_, _) =>
        {
            if (settings.Camera is not null) Camera.Resume();
            var enroll = Environment.GetEnvironmentVariable("VIGIA_ENROLL_ONCE");
            Environment.SetEnvironmentVariable("VIGIA_ENROLL_ONCE", null);
            if (settings.Writer is null && !string.IsNullOrEmpty(enroll)) await Activate(enroll);
            await CheckUpdate(false);
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!quitting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Quit()
    {
        quitting = true;
        Camera.Stop();
        heartbeat.Stop();
        updateTimer.Stop();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        if (preparedUpdate is string installer)
        {
            try { Process.Start(new ProcessStartInfo(installer, "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true }); }
            catch (Win32Exception) { }
        }
        Application.Current.Shutdown();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Quit();

    private async Task Activate(string enrollment)
    {
        ActivateButton.IsEnabled = false;
        var writer = CloudRelay.NewWriter();
        try
        {
            await CloudRelay.Activate(writer, enrollment);
            settings = settings with { Writer = writer };
            Save();
            UpdateLinkedState();
            await Ping();
        }
        catch { CloudStatus.Text = "Não foi possível vincular. Confira a chave de instalação e a internet."; }
        finally { ActivateButton.IsEnabled = true; }
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        string key = Enrollment.Password.Trim();
        Enrollment.Clear();
        if (key.Length == 64) await Activate(key);
        else CloudStatus.Text = "Digite a chave de instalação fornecida pelo administrador do Vigia.";
    }

    private void UpdateLinkedState()
    {
        bool linked = settings.Writer is not null;
        EnrollmentPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.Visibility = TestButton.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
        CloudStatus.Text = linked ? "Loja vinculada. Copie o convite para ativar os avisos no celular." : "Loja ainda não vinculada";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (settings.Writer is null) return;
        try { Clipboard.SetText(CloudRelay.Invite(settings.Writer)); CloudStatus.Text = "Convite copiado. Compartilhe apenas com a equipe."; }
        catch { CloudStatus.Text = "Não foi possível copiar. Tente novamente."; }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (settings.Writer is null) return;
        TestButton.IsEnabled = false;
        try { await CloudRelay.Arrival(settings.Writer, "test"); CloudStatus.Text = "Teste enviado. Confira o painel e os avisos do celular."; }
        catch { CloudStatus.Text = "O teste não foi enviado. Confira a internet."; }
        finally { TestButton.IsEnabled = true; }
    }

    private async Task Ping()
    {
        if (settings.Writer is null || !Camera.Monitoring || cloudBusy) return;
        cloudBusy = true;
        try { await CloudRelay.Heartbeat(settings.Writer); }
        catch { CloudStatus.Text = "Sem conexão com os avisos remotos. O vídeo continua local."; }
        finally { cloudBusy = false; }
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (StartWithWindows.IsChecked == true) run.SetValue("VigiaLocal", $"\"{Environment.ProcessPath}\" --tray");
            else run.DeleteValue("VigiaLocal", false);
        }
        catch { CloudStatus.Text = "Não foi possível alterar a inicialização automática."; }
    }

    private async void Update_Click(object sender, RoutedEventArgs e) => await CheckUpdate(true);

    private async Task CheckUpdate(bool manual)
    {
        if (updateBusy || preparedUpdate is not null) return;
        updateBusy = true;
        try
        {
            preparedUpdate = await DesktopUpdate.Prepare();
            if (preparedUpdate is not null)
            {
                UpdateStatus.Text = "Atualização pronta. Será instalada ao usar Encerrar Vigia Local.";
                if (!Camera.Connected) Quit();
            }
            else if (manual) UpdateStatus.Text = "Você está na versão mais recente.";
        }
        catch { if (manual) UpdateStatus.Text = "Não foi possível verificar a atualização agora."; }
        finally { updateBusy = false; }
    }

    private void Save()
    {
        try { settings.Save(); }
        catch { CloudStatus.Text = "Não foi possível salvar as configurações neste Windows."; }
    }
}
