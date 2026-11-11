using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Vigia;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon? trayIcon;
    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    private LocalSettings settings = LocalSettings.Load();
    private bool quitting;
    private bool cloudBusy;
    private bool updateBusy;
    private ArrivalNotice? arrivalNotice;
    private readonly ArrivalAudio arrivalAudio = new();
    private string? preparedUpdate;

    public MainWindow()
    {
        InitializeComponent();
        ShowPage(0);
        VersionLabel.Text = "Versão " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        if (Environment.ProcessPath is string path)
            trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir Vigia Local", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Testar notificação do Vigia", null, (_, _) => Dispatcher.Invoke(TestLocalNotice));
        menu.Items.Add("Encerrar", null, (_, _) => Dispatcher.Invoke(Quit));
        tray = new Forms.NotifyIcon { Icon = trayIcon ?? System.Drawing.SystemIcons.Information, Text = "Vigia Local", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        Camera.Restore(settings.Camera);
        Camera.IncludeThumbnail = settings.IncludeThumbnail;
        IncludeThumbnail.IsChecked = settings.IncludeThumbnail;
        Camera.SettingsChanged += value =>
        {
            settings = settings with { Camera = value };
            Save();
        };
        Camera.Arrival += async entry =>
        {
            string title = entry.People == 1 ? "Uma pessoa chegou" : $"{entry.People} pessoas chegaram";
            ShowArrivalNotice(title, entry.Time, ArrivalNotice.DecodePhoto(entry.Photo), entry.People);
            if (settings.Writer is null) return;
            try { await CloudRelay.Arrival(settings.Writer, entry); }
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

    private void TestLocalNotice_Click(object sender, RoutedEventArgs e) => TestLocalNotice();

    private void TestLocalNotice() =>
        ShowArrivalNotice("Aviso de teste", DateTimeOffset.Now, Camera.CurrentPreview, 2);

    private void ShowArrivalNotice(string title, DateTimeOffset time, ImageSource? photo, int people)
    {
        arrivalNotice?.Close();
        var notice = new ArrivalNotice(title, time, photo, () =>
        {
            ShowPage(0);
            ShowFromTray();
        });
        notice.Closed += (_, _) => arrivalNotice = null;
        arrivalNotice = notice;
        notice.ShowNotice();
        arrivalAudio.Play(people);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!quitting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    private void ShowPage(int page)
    {
        Camera.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        TeamPage.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        Button[] navigation = [MonitorNav, TeamNav, SettingsNav];
        for (int index = 0; index < navigation.Length; index++)
        {
            bool selected = index == page;
            navigation[index].Background = selected ? (Brush)FindResource("SurfaceRaised") : Brushes.Transparent;
            navigation[index].BorderBrush = selected ? (Brush)FindResource("Hairline") : Brushes.Transparent;
        }
    }

    private void Monitor_Click(object sender, RoutedEventArgs e) => ShowPage(0);
    private void Team_Click(object sender, RoutedEventArgs e) => ShowPage(1);
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowPage(2);

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Quit()
    {
        if (quitting) return;
        if (preparedUpdate is string installer)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(installer,
                    "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
                if (process is null) throw new InvalidOperationException("Instalador não iniciado.");
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                preparedUpdate = null;
                UpdateButton.Content = "Verificar atualizações";
                UpdateStatus.Text = "Não foi possível abrir o instalador. Tente verificar novamente.";
                ShowPage(2);
                ShowFromTray();
                return;
            }
        }
        quitting = true;
        arrivalNotice?.Close();
        arrivalAudio.Dispose();
        Camera.Stop();
        heartbeat.Stop();
        updateTimer.Stop();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        trayIcon?.Dispose();
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
        CloudStatus.Text = linked ? "Loja vinculada. Gere um código para convidar a equipe." : "Loja ainda não vinculada";
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (settings.Writer is null) return;
        CopyButton.IsEnabled = false;
        try
        {
            string invite = await CloudRelay.Invite(settings.Writer);
            InviteCodeLabel.Text = invite;
            InviteCodeLabel.Visibility = Visibility.Visible;
            Clipboard.SetText(invite);
            CloudStatus.Text = "Código copiado. Use no celular em até 7 dias.";
        }
        catch { CloudStatus.Text = "Não foi possível copiar o código. Tente novamente."; }
        finally { CopyButton.IsEnabled = true; }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (settings.Writer is null) return;
        TestButton.IsEnabled = false;
        try
        {
            var result = await CloudRelay.Test(settings.Writer);
            CloudStatus.Text = result switch
            {
                { Accepted: false } => "Aguarde alguns segundos antes de enviar outro teste.",
                { Push.Subscribed: 0 } => "Teste registrado. Abra o app Android com os avisos ativos e aguarde a próxima consulta.",
                { Push.Accepted: 0 } => "Teste registrado. O aviso pelo navegador falhou. Confira também no app Android.",
                { Push.Accepted: > 0 } => "O serviço aceitou o aviso. Confira se ele aparece e toca no celular.",
                _ => "Confira o painel e os avisos do celular."
            };
        }
        catch { CloudStatus.Text = "O teste não foi enviado. Confira a internet."; }
        finally { TestButton.IsEnabled = true; }
    }

    private void Thumbnail_Click(object sender, RoutedEventArgs e)
    {
        Camera.IncludeThumbnail = IncludeThumbnail.IsChecked == true;
        settings = settings with { IncludeThumbnail = Camera.IncludeThumbnail };
        Save();
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

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (preparedUpdate is not null)
        {
            Quit();
            return;
        }
        await CheckUpdate(true);
    }

    private async Task CheckUpdate(bool manual)
    {
        if (updateBusy || preparedUpdate is not null) return;
        updateBusy = true;
        UpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Verificando o GitHub. Se houver uma versão nova, o Vigia vai baixá-la.";
        try
        {
            preparedUpdate = await DesktopUpdate.Prepare();
            if (preparedUpdate is not null)
            {
                UpdateButton.Content = "Instalar atualização";
                UpdateStatus.Text = "Atualização pronta. O monitoramento pausa durante a instalação e o Vigia reabre em seguida.";
                if (!Camera.Connected) Quit();
            }
            else UpdateStatus.Text = manual ? "Você está na versão mais recente." : "";
        }
        catch { UpdateStatus.Text = "Não foi possível baixar ou verificar a atualização agora. Tente novamente."; }
        finally { updateBusy = false; UpdateButton.IsEnabled = true; }
    }

    private void Save()
    {
        try { settings.Save(); }
        catch { CloudStatus.Text = "Não foi possível salvar as configurações neste Windows."; }
    }
}
