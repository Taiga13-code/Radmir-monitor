using System.Windows;

namespace RadmirMonitor.Views;

public partial class AppDialogWindow : Window
{
    private AppDialogWindow(string title, string message, bool confirm, string okText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
    }

    public static void ShowInfo(Window owner, string title, string message)
    {
        var dialog = new AppDialogWindow(title, message, false, "Понятно") { Owner = owner };
        _ = dialog.ShowDialog();
    }

    public static bool ShowConfirm(Window owner, string title, string message, string okText = "Подтвердить")
    {
        var dialog = new AppDialogWindow(title, message, true, okText) { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
