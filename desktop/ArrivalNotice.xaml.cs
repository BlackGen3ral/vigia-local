using System.Windows;
using System.Windows.Threading;

namespace Vigia;

public partial class ArrivalNotice : Window
{
    private readonly Action openMonitor;
    private readonly DispatcherTimer dismissTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    internal ArrivalNotice(string title, string message, Action openMonitor)
    {
        this.openMonitor = openMonitor;
        InitializeComponent();
        NoticeTitle.Text = title;
        NoticeMessage.Text = message;
        dismissTimer.Tick += (_, _) => Close();
        SizeChanged += (_, _) => PlaceInCorner();
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
        base.OnClosed(e);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Close();
        openMonitor();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
}
