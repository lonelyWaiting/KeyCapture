using System.Windows;
using KeyCapture.Interop;
using KeyCapture.Services;
using KeyCapture.Views;

namespace KeyCapture;

public partial class App : Application
{
    private Mutex? _mutex;
    private KeyboardHookManager? _hookManager;
    private TrayIcon.TrayIconManager? _trayManager;
    private OverlayWindow? _overlay;
    private ForegroundWindowService? _fgService;
    private KeyDisplayFormatter? _formatter;
    private AppSettings? _settings;
    private SettingsWindow? _settingsWindow;
    private WindowChangeTracker? _windowTracker;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard
        _mutex = new Mutex(true, "Global\\KeyCaptureInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("KeyCapture is already running.", "KeyCapture",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Create services
        _settings = new AppSettings();
        var modifierTracker = new ModifierKeyTracker();
        _fgService = new ForegroundWindowService();
        _formatter = new KeyDisplayFormatter();

        // Create overlay window
        _overlay = new OverlayWindow();
        _overlay.Show();

        // Install keyboard hook
        _hookManager = new KeyboardHookManager(modifierTracker);
        _hookManager.KeyPressed += OnKeyPressed;
        _hookManager.Install();

        // Install foreground window change tracker
        _windowTracker = new WindowChangeTracker();
        _windowTracker.ForegroundSwitched += OnForegroundSwitched;
        _windowTracker.Install();

        // Setup tray icon
        _trayManager = new TrayIcon.TrayIconManager();
        _trayManager.ExitRequested += OnExitRequested;
        _trayManager.SettingsRequested += OnSettingsRequested;
    }

    private void OnKeyPressed(KeyPressedEventArgs args)
    {
        // If special-keys-only mode, skip regular typing keys (letters, digits,
        // symbols) unless they are pressed with a modifier (Ctrl / Alt / Win).
        if (_settings!.SpecialKeysOnly)
        {
            bool hasNonShiftModifier = (args.Modifiers & ~ActiveModifiers.Shift) != ActiveModifiers.None;
            bool isRegularTyping = ModifierKeyTracker.IsRegularTypingKey((uint)args.VirtualKeyCode);

            // Allow: special keys (F-keys, arrows, Enter...) regardless of modifiers
            // Allow: any key pressed with Ctrl / Alt / Win
            // Block: regular typing keys pressed alone or with only Shift
            if (isRegularTyping && !hasNonShiftModifier)
                return;
        }

        var keyText = _formatter!.Format(args);
        var appName = _fgService!.GetActiveApplicationName();
        _overlay!.ShowNotification($"{keyText} : {appName}");

        // Start tracking if a combo key was pressed (potential hotkey trigger)
        bool hasComboModifier = args.Modifiers != ActiveModifiers.None
            && !ModifierKeyTracker.IsModifierKey((uint)args.VirtualKeyCode);
        if (hasComboModifier)
        {
            _windowTracker!.BeginTracking(keyText, appName);
        }
    }

    private void OnForegroundSwitched(string chainText)
    {
        _overlay!.UpdateNotification(chainText);
    }

    private void OnSettingsRequested()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings!);
        _settingsWindow.ShowDialog();
    }

    private void OnExitRequested()
    {
        _windowTracker?.Dispose();
        _hookManager?.Dispose();
        _trayManager?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _windowTracker?.Dispose();
        _hookManager?.Dispose();
        _trayManager?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
