using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RadmirMonitor.Views;

public partial class PathPickerWindow : Window
{
    public enum PickerMode { File, Folder }

    private sealed class PickerItem
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public bool IsDirectory { get; init; }
        public string Icon => IsDirectory ? "▣" : "◆";
        public string KindText => IsDirectory ? "ПАПКА" : "ФАЙЛ";
    }

    private readonly PickerMode _mode;
    private readonly string? _fileNameFilter;
    private readonly ObservableCollection<PickerItem> _items = [];
    private readonly Stack<string> _history = new();
    private string _currentPath = "";

    public string SelectedPath { get; private set; } = "";

    private PathPickerWindow(PickerMode mode, string title, string subtitle, string initialPath, string? fileNameFilter)
    {
        InitializeComponent();
        _mode = mode;
        _fileNameFilter = fileNameFilter;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        SelectButton.Content = mode == PickerMode.Folder ? "Выбрать папку" : "Выбрать файл";
        ItemsList.ItemsSource = _items;

        var start = ResolveStartDirectory(initialPath);
        NavigateTo(start, remember: false);
    }

    public static string? PickFile(Window owner, string title, string subtitle, string initialPath = "", string? exactFileName = null)
    {
        var dialog = new PathPickerWindow(PickerMode.File, title, subtitle, initialPath, exactFileName) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    public static string? PickFolder(Window owner, string title, string subtitle, string initialPath = "")
    {
        var dialog = new PathPickerWindow(PickerMode.Folder, title, subtitle, initialPath, null) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    private static string ResolveStartDirectory(string initialPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                if (Directory.Exists(initialPath)) return Path.GetFullPath(initialPath);
                if (File.Exists(initialPath)) return Path.GetDirectoryName(Path.GetFullPath(initialPath)) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var parent = Path.GetDirectoryName(initialPath);
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) return Path.GetFullPath(parent);
            }
        }
        catch { }

        var preferred = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(preferred) ? preferred : Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
    }

    private void NavigateTo(string path, bool remember = true)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (!Directory.Exists(path)) return;
            if (remember && !string.IsNullOrWhiteSpace(_currentPath) && !string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
                _history.Push(_currentPath);

            _currentPath = path;
            PathBox.Text = path;
            SelectionText.Text = _mode == PickerMode.Folder ? path : "Выбери файл";
            _items.Clear();

            foreach (var dir in Directory.EnumerateDirectories(path).OrderBy(x => Path.GetFileName(x), StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    var attrs = File.GetAttributes(dir);
                    if ((attrs & FileAttributes.Hidden) != 0 || (attrs & FileAttributes.System) != 0) continue;
                    _items.Add(new PickerItem { Name = Path.GetFileName(dir), FullPath = dir, IsDirectory = true });
                }
                catch { }
            }

            if (_mode == PickerMode.File)
            {
                foreach (var file in Directory.EnumerateFiles(path).OrderBy(x => Path.GetFileName(x), StringComparer.CurrentCultureIgnoreCase))
                {
                    var name = Path.GetFileName(file);
                    if (!string.IsNullOrWhiteSpace(_fileNameFilter) && !name.Equals(_fileNameFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    _items.Add(new PickerItem { Name = name, FullPath = file, IsDirectory = false });
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            SelectionText.Text = "Нет доступа к этой папке";
        }
        catch (Exception ex)
        {
            SelectionText.Text = ex.Message;
        }
    }

    private void ItemsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is not PickerItem item) return;
        SelectionText.Text = item.FullPath;
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is not PickerItem item) return;
        if (item.IsDirectory) NavigateTo(item.FullPath);
        else if (_mode == PickerMode.File) Confirm(item.FullPath);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0) return;
        var target = _history.Pop();
        NavigateTo(target, remember: false);
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var parent = Directory.GetParent(_currentPath);
            if (parent is not null) NavigateTo(parent.FullName);
            else
            {
                var root = Path.GetPathRoot(_currentPath);
                if (!string.IsNullOrWhiteSpace(root) && !string.Equals(root, _currentPath, StringComparison.OrdinalIgnoreCase)) NavigateTo(root);
            }
        }
        catch { }
    }

    private void PathBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var typed = PathBox.Text.Trim().Trim('"');
        if (Directory.Exists(typed)) NavigateTo(typed);
        else if (_mode == PickerMode.File && File.Exists(typed)) Confirm(typed);
        e.Handled = true;
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == PickerMode.Folder)
        {
            if (ItemsList.SelectedItem is PickerItem selectedDir && selectedDir.IsDirectory) Confirm(selectedDir.FullPath);
            else Confirm(_currentPath);
            return;
        }

        if (ItemsList.SelectedItem is PickerItem item && !item.IsDirectory)
            Confirm(item.FullPath);
        else
            SelectionText.Text = "Сначала выбери файл";
    }

    private void Confirm(string path)
    {
        if (_mode == PickerMode.File && !File.Exists(path)) return;
        if (_mode == PickerMode.Folder && !Directory.Exists(path)) return;
        SelectedPath = path;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }
}
