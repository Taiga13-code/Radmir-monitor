using QRCoder;
using RadmirMonitor.Models;
using RadmirMonitor.Services;
using RadmirMonitor.Views;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace RadmirMonitor;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly LocalStore _store = new();
    private readonly ChatLogMonitor _monitor = new();
    private readonly MobileServer _mobile;
    private MonitorSettings _settings;
    private OverlayWindow? _overlay;
    private Forms.NotifyIcon? _tray;
    private TrayMenuWindow? _trayMenu;
    private bool _allowClose;
    private bool _shuttingDown;
    private List<DailyStat> _mergedDaily = [];
    private readonly System.Windows.Threading.DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public ObservableCollection<DailyStat> DailyRows { get; } = [];
    public ObservableCollection<HistoryRow> HistoryRows { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(_settings.ChatLogPath)) _settings.ChatLogPath = PathDiscovery.FindChatLog();
        _mobile = new MobileServer(
            () => _store.History(_settings.Nick, 31),
            () => _settings.Nick,
            () => _settings.MobileToken);

        DailyGrid.ItemsSource = DailyRows;
        HistoryGrid.ItemsSource = HistoryRows;
        DashboardHistoryGrid.ItemsSource = HistoryRows;

        LoadSettingsToUi();
        WireMonitor();
        CreateTray();
        ApplyOverlay();
        ApplyMobileServer();

        _uiTimer.Tick += (_, _) => RefreshAll();
        _uiTimer.Start();

        Loaded += (_, _) =>
        {
            Log("Radmir Monitor Offline запущен");
            RefreshAll();
            if (_settings.StartMonitoringWithApp && !string.IsNullOrWhiteSpace(_settings.Nick) && File.Exists(_settings.ChatLogPath)) StartMonitoring();
        };
        Closing += MainWindow_Closing;
    }

    private void WireMonitor()
    {
        _monitor.ActionParsed += action => Dispatcher.Invoke(() =>
        {
            var record = _store.AddEvent(_settings.Nick, action);
            Log($"{ActionLabel(record.Action)}: {record.Target ?? "—"}{(record.TargetId is not null ? $" [{record.TargetId}]" : "")}{(!string.IsNullOrWhiteSpace(record.Reason) ? $" · {record.Reason}" : "")}");
            RefreshAll();
        });
        _monitor.OnlineMinute += () => Dispatcher.Invoke(() => _store.AddOnlineMinute(_settings.Nick));
        _monitor.Log += text => Dispatcher.Invoke(() => Log(text));
    }

    private void LoadSettingsToUi()
    {
        NickBox.Text = _settings.Nick;
        ChatLogBox.Text = _settings.ChatLogPath;
        AutostartCheck.IsChecked = _settings.Autostart || AutostartService.IsEnabled();
        TrayCheck.IsChecked = _settings.MinimizeToTray;
        AutoMonitorCheck.IsChecked = _settings.StartMonitoringWithApp;
        OverlayEnabledCheck.IsChecked = _settings.OverlayEnabled;
        ClickThroughCheck.IsChecked = _settings.OverlayClickThrough;
        OverlayOpacitySlider.Value = _settings.OverlayOpacity;
        OverlayScaleSlider.Value = _settings.OverlayScale;
        ReportGoalBox.Text = _settings.ReportGoal.ToString();
        OnlineGoalBox.Text = _settings.OnlineGoalMinutes.ToString();
        MobileEnabledCheck.IsChecked = _settings.MobileEnabled;
        LocalDataPathText.Text = AppPaths.Root;
        MonitorPathText.Text = string.IsNullOrWhiteSpace(_settings.ChatLogPath) ? "chatlog.txt не выбран" : _settings.ChatLogPath;
        DashboardNickText.Text = string.IsNullOrWhiteSpace(_settings.Nick) ? "Профиль не настроен" : _settings.Nick;
        UpdateOverlaySliderLabels();
    }

    private void ReadSettingsFromUi()
    {
        _settings.Nick = NickBox.Text.Trim();
        _settings.ChatLogPath = ChatLogBox.Text.Trim().Trim('"');
        _settings.Autostart = AutostartCheck.IsChecked == true;
        _settings.MinimizeToTray = TrayCheck.IsChecked == true;
        _settings.StartMonitoringWithApp = AutoMonitorCheck.IsChecked == true;
        _settings.OverlayEnabled = OverlayEnabledCheck.IsChecked == true;
        _settings.OverlayClickThrough = ClickThroughCheck.IsChecked == true;
        _settings.OverlayOpacity = OverlayOpacitySlider.Value;
        _settings.OverlayScale = OverlayScaleSlider.Value;
        _settings.MobileEnabled = MobileEnabledCheck.IsChecked == true;
        _settings.ReportGoal = int.TryParse(ReportGoalBox.Text, out var rg) ? Math.Max(0, rg) : 0;
        _settings.OnlineGoalMinutes = int.TryParse(OnlineGoalBox.Text, out var og) ? Math.Clamp(og, 0, 1440) : 0;
    }

    private void SaveSettings(bool showMessage = true)
    {
        ReadSettingsFromUi();
        _settingsService.Save(_settings);
        try { AutostartService.Set(_settings.Autostart); }
        catch (Exception ex) { Log("Автозапуск: " + ex.Message); }
        MonitorPathText.Text = string.IsNullOrWhiteSpace(_settings.ChatLogPath) ? "chatlog.txt не выбран" : _settings.ChatLogPath;
        DashboardNickText.Text = string.IsNullOrWhiteSpace(_settings.Nick) ? "Профиль не настроен" : _settings.Nick;
        ApplyOverlay();
        ApplyMobileServer();
        if (showMessage) FooterText.Text = "Настройки сохранены";
    }

    private void RefreshAll()
    {
        RefreshHistory();
        RefreshDashboard();
        RefreshOverlay();
        FooterText.Text = "Офлайн · история и настройки хранятся на этом компьютере";
    }

    private void RefreshDashboard()
    {
        if (string.IsNullOrWhiteSpace(_settings.Nick)) return;

        var todayKey = MoscowClock.Today.ToString("yyyy-MM-dd");
        var mergedToday = _mergedDaily.FirstOrDefault(x => string.Equals(x.Day, todayKey, StringComparison.OrdinalIgnoreCase));

        int reports;
        int punishments;
        int online;
        if (mergedToday is not null)
        {
            reports = mergedToday.Report;
            punishments = mergedToday.Ban + mergedToday.Mute + mergedToday.Kick + mergedToday.Warn + mergedToday.Jail;
            online = mergedToday.OnlineMinutes;
        }
        else
        {
            (reports, punishments, online) = _store.Today(_settings.Nick);
        }

        ReportsTodayText.Text = reports.ToString();
        PunishmentsTodayText.Text = punishments.ToString();
        OnlineTodayText.Text = FormatOnline(online);
        ReportsPerHourText.Text = online > 0 ? (reports / (online / 60d)).ToString("0.0") : "0.0";
        UpdateGoalProgress(reports, _settings.ReportGoal, ReportGoalProgressBar, ReportGoalProgressText, x => x.ToString());
        UpdateGoalProgress(online, _settings.OnlineGoalMinutes, OnlineGoalProgressBar, OnlineGoalProgressText, FormatOnline);
    }

    private static void UpdateGoalProgress(int current, int goal, System.Windows.Controls.ProgressBar progress, TextBlock label, Func<int, string> format)
    {
        if (goal <= 0)
        {
            progress.Value = 0;
            label.Text = "ЦЕЛЬ НЕ ЗАДАНА";
            return;
        }

        var percentage = Math.Clamp(current * 100d / goal, 0, 100);
        progress.Value = percentage;
        label.Text = current >= goal
            ? $"ЦЕЛЬ ВЫПОЛНЕНА · {percentage:0}%"
            : $"{format(current)} / {format(goal)}";
    }

    private void RefreshHistory()
    {
        var nick = _settings.Nick;
        _mergedDaily = string.IsNullOrWhiteSpace(nick) ? [] : _store.History(nick, 31);

        Replace(DailyRows, _mergedDaily);
        HistoryReportsText.Text = _mergedDaily.Sum(x => x.Report).ToString();
        HistoryPunishmentsText.Text = _mergedDaily.Sum(x => x.Ban + x.Mute + x.Kick + x.Warn + x.Jail).ToString();
        HistoryOnlineText.Text = FormatOnline(_mergedDaily.Sum(x => x.OnlineMinutes));
        var best = _mergedDaily.OrderByDescending(Score).FirstOrDefault();
        BestDayText.Text = best is null || Score(best) == 0 ? "—" : $"{best.Day[5..]} · {best.Report}";
        RefreshHistorySearch();
    }

    private void RefreshHistorySearch()
    {
        var nick = _settings.Nick;
        var q = HistorySearchBox.Text.Trim();
        var candidates = _store.SearchPunishments(nick, q)
            .Where(e => !string.Equals(e.Action, "report", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.CreatedAt)
            .ToList();

        var all = candidates.Take(300)
            .Select(e => new HistoryRow
            {
                Time = e.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm"),
                Action = ActionLabel(e.Action),
                Player = e.Target ?? "—",
                Id = e.TargetId?.ToString() ?? "—",
                Reason = e.Reason ?? "—",
                Source = "Локально"
            }).ToList();
        Replace(HistoryRows, all);
    }

    private void StartMonitoring()
    {
        SaveSettings(showMessage: false);
        if (string.IsNullOrWhiteSpace(_settings.Nick))
        {
            AppDialogWindow.ShowInfo(this, "Профиль не настроен", "Сначала укажи ник администратора в настройках.");
            return;
        }
        try
        {
            _monitor.Start(_settings.ChatLogPath, _settings.Nick);
            StatusText.Text = "Работает";
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource("GreenBrush");
            HeroStatusText.Text = "Мониторинг работает";
            HeroStatusDot.Fill = (System.Windows.Media.Brush)FindResource("GreenBrush");
            StartStopButton.Content = "ОСТАНОВИТЬ";
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            AppDialogWindow.ShowInfo(this, "Не удалось запустить мониторинг", ex.Message);
        }
    }

    private void StopMonitoring()
    {
        _monitor.Stop();
        StatusText.Text = "Остановлен";
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource("RedBrush");
        HeroStatusText.Text = "Мониторинг остановлен";
        HeroStatusDot.Fill = (System.Windows.Media.Brush)FindResource("RedBrush");
        StartStopButton.Content = "ЗАПУСТИТЬ";
    }

    private void ApplyOverlay()
    {
        if (OverlayEnabledCheck.IsChecked != true)
        {
            _overlay?.Hide();
            return;
        }
        _overlay ??= new OverlayWindow();
        if (!_overlay.IsVisible)
        {
            _overlay.Left = _settings.OverlayLeft;
            _overlay.Top = _settings.OverlayTop;
            _overlay.Show();
        }
        _overlay.Apply(OverlayOpacitySlider.Value, OverlayScaleSlider.Value, ClickThroughCheck.IsChecked == true);
        RefreshOverlay();
    }

    private void RefreshOverlay()
    {
        if (_overlay is null) return;
        var todayKey = MoscowClock.Today.ToString("yyyy-MM-dd");
        var row = _mergedDaily.FirstOrDefault(x => string.Equals(x.Day, todayKey, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            var punishments = row.Ban + row.Mute + row.Kick + row.Warn + row.Jail;
            _overlay.Update(row.Report, row.OnlineMinutes, punishments);
            return;
        }
        var t = _store.Today(_settings.Nick);
        _overlay.Update(t.Reports, t.Online, t.Punishments);
    }

    private void ApplyMobileServer()
    {
        try
        {
            if (MobileEnabledCheck.IsChecked == true)
            {
                _mobile.Start();
                MobileUrlBox.Text = _mobile.Url();
                MobileQrImage.Source = CreateQr(MobileUrlBox.Text);
            }
            else
            {
                _mobile.Stop(); MobileUrlBox.Text = "Выключено"; MobileQrImage.Source = null;
            }
        }
        catch (Exception ex)
        {
            MobileUrlBox.Text = "Ошибка: " + ex.Message;
            MobileQrImage.Source = null;
        }
    }

    private static BitmapImage CreateQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var image = new BitmapImage();
        using var ms = new MemoryStream(png);
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = ms; image.EndInit(); image.Freeze();
        return image;
    }

    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = "Radmir Monitor Offline",
            Visible = true,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "RadmirMonitor.exe"))
                   ?? System.Drawing.SystemIcons.Application
        };

        // The tray icon stays native, but the menu itself is our WPF dashboard-style window.
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right) Dispatcher.Invoke(ShowTrayMenu);
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void ShowTrayMenu()
    {
        _trayMenu ??= CreateTrayMenuWindow();
        _trayMenu.UpdateState(_monitor.IsRunning, OverlayEnabledCheck.IsChecked == true);
        _trayMenu.ShowNearCursor();
    }

    private TrayMenuWindow CreateTrayMenuWindow()
    {
        var menu = new TrayMenuWindow();
        menu.OpenRequested += ShowFromTray;
        menu.ToggleMonitoringRequested += () =>
        {
            if (_monitor.IsRunning) StopMonitoring(); else StartMonitoring();
        };
        menu.ToggleOverlayRequested += () =>
        {
            OverlayEnabledCheck.IsChecked = !(OverlayEnabledCheck.IsChecked == true);
            ReadSettingsFromUi();
            _settingsService.Save(_settings);
            ApplyOverlay();
        };
        menu.ExitRequested += ExitApp;
        return menu;
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shuttingDown) return;

        if (TrayCheck.IsChecked == true && !_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // A normal close without tray is a full application exit.
        // Cancel this close request and run the same deterministic cleanup path as tray -> Exit.
        e.Cancel = true;
        ShutdownApplication();
    }

    private void ExitApp() => ShutdownApplication();

    private void ShutdownApplication()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _allowClose = true;
        CleanupForExit();

        try
        {
            System.Windows.Application.Current.Shutdown();
        }
        finally
        {
            // WPF, NotifyIcon and background tasks have all been cleaned above.
            // This final fallback guarantees that an orphaned window/message loop cannot keep
            // RadmirMonitor.exe visible in Task Manager after the user explicitly chooses Exit.
            Environment.Exit(0);
        }
    }

    private void CleanupForExit()
    {
        try
        {
            if (_overlay is not null)
            {
                _settings.OverlayLeft = _overlay.Left;
                _settings.OverlayTop = _overlay.Top;
            }
            ReadSettingsFromUi();
            _settingsService.Save(_settings);
            try { AutostartService.Set(_settings.Autostart); } catch { }
        }
        catch { }

        try { _uiTimer.Stop(); } catch { }
        try { _monitor.Dispose(); } catch { }
        try { _mobile.Dispose(); } catch { }

        try
        {
            if (_overlay is not null)
            {
                _overlay.Close();
                _overlay = null;
            }
        }
        catch { }

        try
        {
            if (_trayMenu is not null)
            {
                _trayMenu.Close();
                _trayMenu = null;
            }
        }
        catch { }

        try
        {
            if (_tray is not null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }
        catch { }
    }

    private void Log(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {text}";
        LogList.Items.Insert(0, line);
        while (LogList.Items.Count > 150) LogList.Items.RemoveAt(LogList.Items.Count - 1);
        try { File.AppendAllText(AppPaths.Log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}"); } catch { }
    }

    private static string FormatOnline(int minutes) => $"{Math.Max(0, minutes) / 60}ч {Math.Max(0, minutes) % 60:00}м";
    private static int Score(DailyStat x) => x.Report + 5 * (x.Ban + x.Mute + x.Kick + x.Warn + x.Jail) + x.OnlineMinutes / 15;
    private static string ActionLabel(string action) => action switch { "ban" => "Бан", "mute" => "Мут", "kick" => "Кик", "warn" => "Варн", "jail" => "Джайл", "report" => "Репорт", _ => action };
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source) { target.Clear(); foreach (var item in source) target.Add(item); }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (DashboardPage is null || HistoryPage is null || NormPage is null || SettingsPage is null || PageTitleText is null) return;
        var tag = (sender as System.Windows.Controls.RadioButton)?.Tag?.ToString() ?? "Dashboard";
        DashboardPage.Visibility = tag == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
        NormPage.Visibility = tag == "Norm" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        PageTitleText.Text = tag switch
        {
            "History" => "История",
            "Norm" => "Норма",
            "Settings" => "Настройки",
            _ => "Обзор"
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void StartStop_Click(object sender, RoutedEventArgs e) { if (_monitor.IsRunning) StopMonitoring(); else StartMonitoring(); }
    private void SearchHistory_Click(object sender, RoutedEventArgs e) => RefreshHistorySearch();
    private void ResetHistory_Click(object sender, RoutedEventArgs e) { HistorySearchBox.Clear(); RefreshHistorySearch(); }
    private void HistorySearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) RefreshHistorySearch(); }
    private void SaveSettings_Click(object sender, RoutedEventArgs e) => SaveSettings();

    private void BrowseChatLog_Click(object sender, RoutedEventArgs e)
    {
        var selected = PathPickerWindow.PickFile(
            this,
            "Выбрать chatlog.txt",
            "Открой папку RADMIR и выбери chatlog.txt",
            ChatLogBox.Text,
            "chatlog.txt");
        if (!string.IsNullOrWhiteSpace(selected)) ChatLogBox.Text = selected;
    }

    private void AutoChatLog_Click(object sender, RoutedEventArgs e)
    {
        var path = PathDiscovery.FindChatLog();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            ChatLogBox.Text = path;
            MonitorPathText.Text = path;
            FooterText.Text = "chatlog.txt найден автоматически";
        }
        else FooterText.Text = "chatlog.txt автоматически не найден";
    }

    private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Nested tables/lists scroll themselves while they have room. At their edge (or when
        // they have no scrollbar at all) the same wheel gesture continues scrolling the page.
        var source = e.OriginalSource as DependencyObject;
        var grid = FindVisualParent<DataGrid>(source);
        if (grid is not null && CanNestedScroll(grid, e.Delta)) return;

        var list = FindVisualParent<System.Windows.Controls.ListBox>(source);
        if (list is not null && CanNestedScroll(list, e.Delta)) return;

        var combo = FindVisualParent<System.Windows.Controls.ComboBox>(source);
        if (combo?.IsDropDownOpen == true) return;

        if (sender is not ScrollViewer viewer) return;
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta * 0.55);
        e.Handled = true;
    }

    private static bool CanNestedScroll(DependencyObject control, int delta)
    {
        var scroll = FindVisualChild<ScrollViewer>(control);
        if (scroll is null || scroll.ScrollableHeight <= 0) return false;
        return delta < 0 ? scroll.VerticalOffset < scroll.ScrollableHeight : scroll.VerticalOffset > 0;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T typed) return typed;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void OverlayChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ReadSettingsFromUi();
        _settingsService.Save(_settings);
        ApplyOverlay();
    }
    private void OverlaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { UpdateOverlaySliderLabels(); if (IsLoaded) ApplyOverlay(); }
    private void UpdateOverlaySliderLabels()
    {
        if (OverlayScaleText is null || OverlayOpacityText is null) return;
        OverlayScaleText.Text = $"{OverlayScaleSlider.Value * 100:0}%";
        OverlayOpacityText.Text = $"{OverlayOpacitySlider.Value * 100:0}%";
    }
    private void MobileChanged(object sender, RoutedEventArgs e) { if (IsLoaded) ApplyMobileServer(); }
    private void CopyMobileUrl_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(MobileUrlBox.Text)) System.Windows.Clipboard.SetText(MobileUrlBox.Text); FooterText.Text = "Ссылка скопирована"; }
}
