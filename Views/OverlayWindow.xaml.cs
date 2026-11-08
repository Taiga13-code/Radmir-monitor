using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace RadmirMonitor.Views;

public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private bool _clickThrough;

    public OverlayWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && !_clickThrough) DragMove(); };
    }

    public void Update(int reports, int onlineMinutes, int punishments)
    {
        ReportsText.Text = reports.ToString();
        OnlineText.Text = $"{onlineMinutes / 60}:{onlineMinutes % 60:00}";
        PunishmentsText.Text = punishments.ToString();
    }

    public void Apply(double opacity, double scale, bool clickThrough)
    {
        Opacity = Math.Clamp(opacity, .25, 1);
        LayoutTransform = new System.Windows.Media.ScaleTransform(Math.Clamp(scale, .7, 1.8), Math.Clamp(scale, .7, 1.8));
        _clickThrough = clickThrough;
        ApplyClickThrough();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThrough();
    }

    private void ApplyClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = GetWindowLong(hwnd, GwlExstyle);
        ex = _clickThrough ? ex | WsExTransparent | WsExLayered : ex & ~WsExTransparent;
        SetWindowLong(hwnd, GwlExstyle, ex);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
