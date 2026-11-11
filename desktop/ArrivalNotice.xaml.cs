using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Vigia;

public partial class ArrivalNotice : Window
{
    private readonly Action openMonitor;
    private readonly DispatcherTimer dismissTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    internal ArrivalNotice(string title, DateTimeOffset time, ImageSource? photo, Action openMonitor)
    {
        this.openMonitor = openMonitor;
        InitializeComponent();
        NoticeTitle.Text = title;
        NoticeTime.Text = time.ToString("HH:mm:ss");
        NoticePhoto.Source = photo;
        PhotoPanel.Visibility = photo is null ? Visibility.Collapsed : Visibility.Visible;
        dismissTimer.Tick += (_, _) => Close();
        SizeChanged += (_, _) => PlaceInCorner();
    }

    internal static BitmapImage? DecodePhoto(byte[]? jpeg)
    {
        if (jpeg is null) return null;
        using var stream = new MemoryStream(jpeg, writable: false);
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    internal void ShowNotice()
    {
        Measure(new System.Windows.Size(Width, double.PositiveInfinity));
        PlaceInCorner();
        Show();
        dismissTimer.Start();
    }

    private void PlaceInCorner()
    {
        var workArea = SystemParameters.WorkArea;
        double height = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
        Left = Math.Max(workArea.Left, workArea.Right - Width - 16);
        Top = Math.Max(workArea.Top, workArea.Bottom - height - 16);
    }

    protected override void OnClosed(EventArgs e)
    {
        dismissTimer.Stop();
        NoticePhoto.Source = null;
        base.OnClosed(e);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Close();
        openMonitor();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
}
