using System.Windows;
using KeyCapture.Services;

namespace KeyCapture.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    /// <summary>Raised after the settings were changed and saved.</summary>
    public event Action? SettingsApplied;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        SpecialKeysOnlyCheckBox.IsChecked = _settings.SpecialKeysOnly;
        FolderUpOnDoubleClickCheckBox.IsChecked = _settings.FolderUpOnDoubleClick;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        _settings.SpecialKeysOnly = SpecialKeysOnlyCheckBox.IsChecked == true;
        _settings.FolderUpOnDoubleClick = FolderUpOnDoubleClickCheckBox.IsChecked == true;
        _settings.Save();
        SettingsApplied?.Invoke();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
