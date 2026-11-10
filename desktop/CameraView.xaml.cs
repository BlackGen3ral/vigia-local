using System.IO;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Vigia;

public partial class CameraView : UserControl
{
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(130) };
    private CancellationTokenSource? session;
    private DoorZone? zone; // Preserve the previous area in saved settings.
    private EntryLine? line;
    private readonly ConcurrentQueue<CameraArrival> arrivals = new();
    private Point? dragStart;
    private CameraFrame? latest;
    private bool hasVideo;
    private int displayedEvents;
    private static readonly Brush LiveSignal = FrozenBrush(39, 74, 60);
    private static readonly Brush OfflineSignal = FrozenBrush(45, 52, 66);

    private CameraSettings? activeSettings;
    public bool Connected => session is not null;
    public bool Monitoring => hasVideo && line?.Valid == true && session?.IsCancellationRequested == false;
    internal event Action<CameraSettings>? SettingsChanged;
    internal event Action<CameraArrival>? Arrival;
    internal volatile bool IncludeThumbnail;

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
        line = settings.Line is { Valid: true } ? settings.Line : null;
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
            ConnectionPanel.Visibility = Visibility.Visible;
            return;
        }

        if (activeSettings is not null && (Host.Text.Trim() != activeSettings.Host ||
            Port.Text.Trim() != activeSettings.Port || Channel.Text.Trim() != activeSettings.Channel))
        {
            zone = null;
            line = null;
            ShowSelectedZone();
        }
        activeSettings = new CameraSettings(Host.Text.Trim(), Port.Text.Trim(), Channel.Text.Trim(),
            Login.Text.Trim(), Secret.Password, zone, line);
        Secret.Clear();
        using var current = new CancellationTokenSource();
        session = current;
        SetConnectionControls(true);
        ConnectionPanel.Visibility = Visibility.Collapsed;
        RenderSignal("CONECTANDO", "Conectando à câmera", "Aguardando resposta do gravador.", live: false);
        CameraStatus.Text = "Abrindo o vídeo na rede local…";
        EventCount.Text = "0";
        EventStatus.Text = "Nenhuma presença detectada.";
        displayedEvents = 0;
        refresh.Start();
        try
        {
            while (!current.IsCancellationRequested)
            {
                int frames = 0;
                await Task.Run(() => CameraFeed.Run(endpoint, () => Volatile.Read(ref line), () => IncludeThumbnail, frame =>
                {
                    frames++;
                    Interlocked.Exchange(ref latest, frame);
                }, arrivals.Enqueue, current.Token));
                ShowLatest(updateConnection: false);
                retry |= frames > 0;
                if (!retry || current.IsCancellationRequested) break;
                hasVideo = false;
                Preview.Source = null;
                RenderSignal("RECONECTANDO", "Tentando reconectar", "O vídeo voltará quando o gravador responder.", live: false);
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
            RenderSignal("SEM SINAL", "Câmera desconectada", "Conecte o gravador para ver a entrada da loja.", live: false);
            if (!current.IsCancellationRequested) ConnectionPanel.Visibility = Visibility.Visible;
        }
    }

    public void Stop() => session?.Cancel();

    private void Configure_Click(object sender, RoutedEventArgs e)
    {
        bool opening = ConnectionPanel.Visibility != Visibility.Visible;
        ConnectionPanel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        if (opening && !Connected) Host.Focus();
    }

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
        StopButton.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        EmptyConnect.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void RenderSignal(string signal, string title, string description, bool live)
    {
        SignalLabel.Text = signal;
        SignalPill.Background = live ? LiveSignal : OfflineSignal;
        PreviewEmpty.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        ZoneLayer.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        if (!live)
        {
            EmptyTitle.Text = title;
            EmptyDescription.Text = description;
        }
    }

    private void ShowLatest(bool updateConnection = true)
    {
        while (arrivals.TryDequeue(out var entry))
        {
            displayedEvents++;
            EventCount.Text = displayedEvents.ToString();
            EventStatus.Text = $"Última entrada às {entry.Time:HH:mm:ss}.";
            Arrival?.Invoke(entry);
        }
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
            if (!hasVideo)
            {
                RenderSignal("AO VIVO", "", "", live: true);
                if (activeSettings is not null) SettingsChanged?.Invoke(activeSettings);
            }
            hasVideo = true;
            if (session?.IsCancellationRequested == false)
            {
                string people = frame.People == 1 ? "1 pessoa na imagem" : $"{frame.People} pessoas na imagem";
                ZoneStatus.Text = line is null ? "Linha a definir" : "Entrada no sentido da seta";
                CameraStatus.Text = frame.Calibrating ? "Ao vivo · Calibrando a imagem. Aguarde alguns segundos."
                    : $"Ao vivo · {people}";
            }
        }
    }

    private void Zone_Down(object sender, MouseButtonEventArgs e)
    {
        if (!hasVideo || !PreviewSurface.CaptureMouse()) return;
        dragStart = e.GetPosition(PreviewSurface);
        RenderLine(dragStart.Value, dragStart.Value, false);
    }

    private void Zone_Move(object sender, MouseEventArgs e)
    {
        if (dragStart is Point start)
            RenderLine(start, ClampToPreview(e.GetPosition(PreviewSurface)), false);
    }

    private void Zone_Up(object sender, MouseButtonEventArgs e)
    {
        if (dragStart is not Point start) return;
        var end = ClampToPreview(e.GetPosition(PreviewSurface));
        var selected = new EntryLine(start.X / PreviewSurface.Width, start.Y / PreviewSurface.Height,
            end.X / PreviewSurface.Width, end.Y / PreviewSurface.Height);
        if (selected.Valid)
        {
            Volatile.Write(ref line, selected);
            SaveLine();
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

    private void SaveLine()
    {
        if (activeSettings is null) return;
        activeSettings = activeSettings with { Line = line };
        SettingsChanged?.Invoke(activeSettings);
    }

    private void Reverse_Click(object sender, RoutedEventArgs e)
    {
        if (line is null) return;
        Volatile.Write(ref line, line with { Reverse = !line.Reverse });
        SaveLine(); ShowSelectedZone();
    }

    private void ShowSelectedZone()
    {
        ZoneStatus.Text = line is null ? "Linha a definir" : "Entrada no sentido da seta";
        if (line is EntryLine selected)
            RenderLine(new Point(selected.X1*PreviewSurface.Width, selected.Y1*PreviewSurface.Height),
                new Point(selected.X2*PreviewSurface.Width, selected.Y2*PreviewSurface.Height), selected.Reverse);
        else { ZoneOutline.Visibility = DirectionArrow.Visibility = Visibility.Collapsed; }
    }

    private Point ClampToPreview(Point point) => new(Math.Clamp(point.X, 0, PreviewSurface.Width),
        Math.Clamp(point.Y, 0, PreviewSurface.Height));

    private void RenderLine(Point start, Point end, bool reverse)
    {
        ZoneOutline.X1 = start.X; ZoneOutline.Y1 = start.Y;
        ZoneOutline.X2 = end.X; ZoneOutline.Y2 = end.Y;
        ZoneOutline.Visibility = Visibility.Visible;
        // Normal in normalized coordinates, transformed back to the preview.
        double dx = (end.X-start.X)/PreviewSurface.Width, dy = (end.Y-start.Y)/PreviewSurface.Height;
        double length = double.Hypot(dx, dy);
        if (length < .001) { DirectionArrow.Visibility = Visibility.Collapsed; return; }
        double sign = reverse ? -1 : 1;
        var center = new Point((start.X+end.X)/2, (start.Y+end.Y)/2);
        var vector = new Vector(-dy/length*sign*PreviewSurface.Width*.08, dx/length*sign*PreviewSurface.Height*.08);
        var tip = center + vector;
        var wing = new Vector(-vector.Y, vector.X)*.25;
        DirectionArrow.Points = new PointCollection { center, tip, tip-vector*.3+wing, tip, tip-vector*.3-wing };
        DirectionArrow.Visibility = Visibility.Visible;
    }
}
