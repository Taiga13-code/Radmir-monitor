using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace RadmirMonitor.Views;

public partial class TrayMenuWindow : Window
{
    public event Action? OpenRequested;
    public event Action? ToggleMonitoringRequested;
    public event Action? ToggleOverlayRequested;
    public event Action? ExitRequested;

    public TrayMenuWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
    }

    public void UpdateState(bool monitoring, bool overlay)
    {
        MonitorButton.Content = monitoring ? "Остановить мониторинг" : "Запустить мониторинг";
        OverlayButton.Content = overlay ? "Оверлей: включён" : "Оверлей: выключен";
        StateDot.Fill = (System.Windows.Media.Brush)FindResource(monitoring ? "GreenBrush" : "RedBrush");
    }

    public void ShowNearCursor()
    {
        if (!IsVisible) Show();
        UpdateLayout();

        var cursor = Forms.Cursor.Position;
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = Forms.Screen.FromPoint(cursor);
        var work = screen.WorkingArea;

        var x = cursor.X / dpi.DpiScaleX - ActualWidth - 8;
        var y = cursor.Y / dpi.DpiScaleY - ActualHeight - 8;
        var left = work.Left / dpi.DpiScaleX;
        var top = work.Top / dpi.DpiScaleY;
        var right = work.Right / dpi.DpiScaleX;
        var bottom = work.Bottom / dpi.DpiScaleY;

        Left = Math.Clamp(x, left + 6, Math.Max(left + 6, right - ActualWidth - 6));
        Top = Math.Clamp(y, top + 6, Math.Max(top + 6, bottom - ActualHeight - 6));
        Activate();
    }

    private void Open_Click(object sender, RoutedEventArgs e) { Hide(); OpenRequested?.Invoke(); }
    private void Monitor_Click(object sender, RoutedEventArgs e) { Hide(); ToggleMonitoringRequested?.Invoke(); }
    private void Overlay_Click(object sender, RoutedEventArgs e) { Hide(); ToggleOverlayRequested?.Invoke(); }
    private void Exit_Click(object sender, RoutedEventArgs e) { Hide(); ExitRequested?.Invoke(); }
}
