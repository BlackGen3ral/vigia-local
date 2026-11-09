using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Vigia;

public partial class CameraView : UserControl
{
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(130) };
    private CancellationTokenSource? session;
    private DoorZone? zone;
    private Point? dragStart;
    private CameraFrame? latest;
    private bool hasVideo;
    private int displayedEvents;

    private CameraSettings? activeSettings;
    public bool Connected => session is not null;
    public bool Monitoring => hasVideo && zone is not null && session?.IsCancellationRequested == false;
    internal event Action<CameraSettings>? SettingsChanged;
    internal event Action? Arrival;

    public CameraView()
    {
        InitializeComponent();
        Host.Text = Environment.GetEnvironmentVariable("VIGIA_CAMERA_IP") ?? "";
        refresh.Tick += (_, _) => ShowLatest();
    }

    internal void Restore(CameraSettings? settings)
    {
        if (settings is null) return;
        activeSettings = settings;
        Host.Text = settings.Host;
        Port.Text = settings.Port;
        Channel.Text = settings.Channel;
        Login.Text = settings.Login;
        Secret.Password = settings.Password;
        zone = settings.Zone;
        ShowSelectedZone();
    }

    internal void Resume() => Connect_Click(this, new RoutedEventArgs());

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (session is not null) return;
        bool retry = activeSettings is not null && Host.Text.Trim() == activeSettings.Host &&
            Port.Text.Trim() == activeSettings.Port && Channel.Text.Trim() == activeSettings.Channel &&
            Login.Text.Trim() == activeSettings.Login && Secret.Password == activeSettings.Password;

        var endpoint = CameraFeed.LocalEndpoint(Host.Text, Port.Text, Channel.Text, Login.Text, Secret.Password);
        if (endpoint is null)
        {
            CameraStatus.Text = "Confira o IP local, a porta, o canal, o usuário e a senha do DVR.";
            return;
        }

        if (activeSettings is not null && (Host.Text.Trim() != activeSettings.Host ||
            Port.Text.Trim() != activeSettings.Port || Channel.Text.Trim() != activeSettings.Channel))
        {
            zone = null;
            ShowSelectedZone();
        }
        activeSettings = new CameraSettings(Host.Text.Trim(), Port.Text.Trim(), Channel.Text.Trim(),
            Login.Text.Trim(), Secret.Password, zone);
        Secret.Clear();
        using var current = new CancellationTokenSource();
        session = current;
        SetConnectionControls(true);
        CameraStatus.Text = "Abrindo o vídeo na rede local…";
        EventStatus.Text = "Nenhuma presença detectada nesta sessão.";
        displayedEvents = 0;
        refresh.Start();
        try
        {
            int offset = 0;
            while (!current.IsCancellationRequested)
            {
                int frames = 0;
                int count = 0;
                await Task.Run(() => CameraFeed.Run(endpoint, () => Volatile.Read(ref zone), frame =>
                {
                    frames++;
                    count = frame.EventCount;
                    Interlocked.Exchange(ref latest, frame with { EventCount = frame.EventCount + offset });
                }, current.Token));
                ShowLatest(updateConnection: false);
                offset += count;
                retry |= frames > 0;
                if (!retry || current.IsCancellationRequested) break;
                hasVideo = false;
                Preview.Source = null;
                CameraStatus.Text = "Vídeo interrompido. Tentando reconectar…";
                await Task.Delay(TimeSpan.FromSeconds(8), current.Token);
            }
            CameraStatus.Text = current.IsCancellationRequested ? "Câmera desconectada."
                : "Vídeo indisponível. Confira a rede, o canal e as credenciais do DVR.";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            CameraStatus.Text = "Câmera desconectada.";
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            CameraStatus.Text = "O componente de vídeo não pôde iniciar neste Windows.";
        }
        catch (Exception)
        {
            CameraStatus.Text = "Não foi possível iniciar o vídeo ou a detecção. Tente conectar novamente.";
        }
        finally
        {
            ShowLatest(updateConnection: false);
            refresh.Stop();
            Interlocked.Exchange(ref latest, null);
            Preview.Source = null;
            hasVideo = false;
            dragStart = null;
            if (PreviewSurface.IsMouseCaptured) PreviewSurface.ReleaseMouseCapture();
            session = null;
            SetConnectionControls(false);
        }
    }

    public void Stop() => session?.Cancel();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        CameraStatus.Text = "Desconectando…";
        Stop();
    }

    private void SetConnectionControls(bool active)
    {
        ConnectionFields.IsEnabled = !active;
        ConnectButton.IsEnabled = !active;
        StopButton.IsEnabled = active;
    }

    private void ShowLatest(bool updateConnection = true)
    {
        var frame = Interlocked.Exchange(ref latest, null);
        if (frame is null) return;

        if (updateConnection)
        {
            using var buffer = new MemoryStream(frame.Jpeg, writable: false);
            var image = BitmapFrame.Create(buffer, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (PreviewSurface.Width != image.PixelWidth || PreviewSurface.Height != image.PixelHeight)
            {
                PreviewSurface.Width = image.PixelWidth;
                PreviewSurface.Height = image.PixelHeight;
                if (dragStart is null) ShowSelectedZone();
            }
            Preview.Source = image;
            if (!hasVideo && activeSettings is not null) SettingsChanged?.Invoke(activeSettings);
            hasVideo = true;
            if (session?.IsCancellationRequested == false)
            {
                string people = frame.People == 1 ? "1 pessoa na imagem" : $"{frame.People} pessoas na imagem";
                string area = zone is null ? "Marque a região da porta" : frame.InDoor ? "Área ocupada" : "Área livre";
                CameraStatus.Text = frame.Calibrating ? "Ao vivo · Calibrando a imagem. Aguarde alguns segundos."
                    : $"Ao vivo · {people} · {area}";
            }
        }
        if (frame.EventCount > displayedEvents)
        {
            displayedEvents = frame.EventCount;
            EventStatus.Text = $"Pessoa na área da entrada às {frame.LastEvent:HH:mm:ss} · {displayedEvents} nesta sessão";
            System.Media.SystemSounds.Asterisk.Play();
            Arrival?.Invoke();
        }
    }

    private void Zone_Down(object sender, MouseButtonEventArgs e)
    {
        if (!hasVideo || !PreviewSurface.CaptureMouse()) return;
        dragStart = e.GetPosition(PreviewSurface);
        RenderZone(new Rect(dragStart.Value, dragStart.Value));
    }

    private void Zone_Move(object sender, MouseEventArgs e)
    {
        if (dragStart is Point start)
            RenderZone(new Rect(start, ClampToPreview(e.GetPosition(PreviewSurface))));
    }

    private void Zone_Up(object sender, MouseButtonEventArgs e)
    {
        if (dragStart is not Point start) return;
        var bounds = new Rect(start, ClampToPreview(e.GetPosition(PreviewSurface)));
        if (bounds.Width >= 16 && bounds.Height >= 16)
        {
            Volatile.Write(ref zone, new DoorZone(bounds.Left / PreviewSurface.Width, bounds.Top / PreviewSurface.Height,
                bounds.Right / PreviewSurface.Width, bounds.Bottom / PreviewSurface.Height));
            if (activeSettings is not null)
            {
                activeSettings = activeSettings with { Zone = zone };
                SettingsChanged?.Invoke(activeSettings);
            }
        }
        dragStart = null;
        PreviewSurface.ReleaseMouseCapture();
        ShowSelectedZone();
    }

    private void Zone_LostCapture(object sender, MouseEventArgs e)
    {
        dragStart = null;
        ShowSelectedZone();
    }

    private void ShowSelectedZone()
    {
        if (zone is DoorZone selected)
            RenderZone(new Rect(new Point(selected.Left * PreviewSurface.Width, selected.Top * PreviewSurface.Height),
                new Point(selected.Right * PreviewSurface.Width, selected.Bottom * PreviewSurface.Height)));
        else
            ZoneOutline.Visibility = Visibility.Collapsed;
    }

    private Point ClampToPreview(Point point) => new(Math.Clamp(point.X, 0, PreviewSurface.Width),
        Math.Clamp(point.Y, 0, PreviewSurface.Height));

    private void RenderZone(Rect bounds)
    {
        Canvas.SetLeft(ZoneOutline, bounds.Left);
        Canvas.SetTop(ZoneOutline, bounds.Top);
        ZoneOutline.Width = bounds.Width;
        ZoneOutline.Height = bounds.Height;
        ZoneOutline.Visibility = Visibility.Visible;
    }
}
