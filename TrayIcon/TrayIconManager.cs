using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace KeyCapture.TrayIcon;

internal sealed class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;

    public event Action? ExitRequested;
    public event Action? SettingsRequested;

    public TrayIconManager()
    {
        _taskbarIcon = new TaskbarIcon
        {
            Icon = LoadEmbeddedIcon(),
            ToolTipText = "KeyCapture - Running"
        };

        var menuItemStyle = (Style)Application.Current.FindResource("TrayMenuItem");
        var separatorStyle = (Style)Application.Current.FindResource("TraySeparator");

        var settingsItem = new MenuItem { Header = "Settings", Style = menuItemStyle };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var separator = new Separator { Style = separatorStyle };

        var exitItem = new MenuItem { Header = "Exit", Style = menuItemStyle };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _taskbarIcon.ContextMenu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("TrayContextMenu"),
            Items = { settingsItem, separator, exitItem }
        };
    }

    private static Icon LoadEmbeddedIcon()
    {
        var uri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
        var stream = Application.GetResourceStream(uri)?.Stream;
        if (stream != null)
            return new Icon(stream);

        // Fallback to app exe icon
        var processPath = Environment.ProcessPath;
        if (processPath != null)
        {
            var icon = Icon.ExtractAssociatedIcon(processPath);
            if (icon != null) return icon;
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _taskbarIcon.Dispose();
    }
}
