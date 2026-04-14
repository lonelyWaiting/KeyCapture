using System.Windows;
using KeyCapture.Services;

namespace KeyCapture.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        SpecialKeysOnlyCheckBox.IsChecked = _settings.SpecialKeysOnly;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        _settings.SpecialKeysOnly = SpecialKeysOnlyCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
