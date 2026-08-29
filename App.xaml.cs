using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using KeyCapture.Data;
using KeyCapture.Interop;
using KeyCapture.Services;
using KeyCapture.Views;

namespace KeyCapture;

public partial class App : Application
{
    private Mutex? _mutex;
    private KeyboardHookManager? _hookManager;
    private MouseHookManager? _mouseHookManager;
    private ExplorerFolderUpService? _folderUpService;
    private TrayIcon.TrayIconManager? _trayManager;
    private OverlayWindow? _overlay;
    private ForegroundWindowService? _fgService;
    private KeyDisplayFormatter? _formatter;
    private AppSettings? _settings;
    private SettingsWindow? _settingsWindow;
    private WindowChangeTracker? _windowTracker;
    private AnalyticsCollector? _collector;
    private AnalyticsService? _analyticsService;
    private DataRetentionService? _dataRetention;
    private AnalyticsWindow? _analyticsWindow;
    private bool _cleanedUp;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Setup global exception handlers before anything else
        SetupExceptionHandlers();

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
        _settings = AppSettings.Load();
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

        // Double-click on empty space in an Explorer folder navigates to the parent folder
        _mouseHookManager = new MouseHookManager();
        _folderUpService = new ExplorerFolderUpService(_mouseHookManager);
        ApplyFolderUpSetting();

        // Setup tray icon
        _trayManager = new TrayIcon.TrayIconManager();
        _trayManager.ExitRequested += OnExitRequested;
        _trayManager.SettingsRequested += OnSettingsRequested;
        _trayManager.StatisticsRequested += OnStatisticsRequested;

        // Analytics setup
        using (var dbInit = new AnalyticsDbContext())
        {
            dbInit.Database.EnsureCreated();
            dbInit.EnableWriteAheadLogging();
        }
        _collector = new AnalyticsCollector();
        _analyticsService = new AnalyticsService();
        _dataRetention = new DataRetentionService();
        _dataRetention.Start();
    }

    private void ApplyFolderUpSetting()
    {
        if (_mouseHookManager is null)
            return;

        // The mouse hook sees every mouse message in the system, so it is only installed
        // while the feature is actually switched on.
        if (_settings!.FolderUpOnDoubleClick)
        {
            try
            {
                _mouseHookManager.Install();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to install mouse hook: {ex}");
            }
        }
        else
        {
            _mouseHookManager.Uninstall();
        }
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

        // Queued here, written to SQLite in batches by the collector's background writer
        _collector?.RecordKeyEvent(
            args.VirtualKeyCode,
            keyText,
            ModifierKeyTracker.Describe(args.Modifiers),
            appName,
            args.Modifiers != ActiveModifiers.None);

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

        var window = new SettingsWindow(_settings!);
        window.SettingsApplied += ApplyFolderUpSetting;
        // Drop the reference once the dialog is gone so the window can be collected.
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        window.ShowDialog();
    }

    private void OnStatisticsRequested()
    {
        if (_analyticsWindow is { IsLoaded: true })
        {
            _analyticsWindow.Activate();
            return;
        }

        var window = new AnalyticsWindow(_analyticsService!);
        // A closed dashboard keeps its loaded statistics alive as long as it is referenced.
        window.Closed += (_, _) => _analyticsWindow = null;
        _analyticsWindow = window;
        window.Show();
    }

    private void OnExitRequested()
    {
        Cleanup(releaseMutex: true);
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup(releaseMutex: false);
        base.OnExit(e);
    }

    private void Cleanup(bool releaseMutex)
    {
        if (_cleanedUp)
            return;
        _cleanedUp = true;

        _windowTracker?.Dispose();
        _hookManager?.Dispose();
        _folderUpService?.Dispose();
        _mouseHookManager?.Dispose();
        _trayManager?.Dispose();
        _analyticsWindow?.Close();
        _dataRetention?.Dispose();
        // Disposed last: it flushes whatever is still queued before the process exits.
        _collector?.Dispose();

        if (releaseMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }

    private void SetupExceptionHandlers()
    {
        // Catch exceptions from background threads
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Debug.WriteLine($"Unhandled AppDomain exception: {e.ExceptionObject}");
        };

        // Catch exceptions from dispatcher (UI thread)
        DispatcherUnhandledException += (s, e) =>
        {
            Debug.WriteLine($"Unhandled Dispatcher exception: {e.Exception}");
            e.Handled = true; // Prevent application crash
        };

        // Catch unobserved task exceptions
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Debug.WriteLine($"Unobserved Task exception: {e.Exception}");
            e.SetObserved(); // Prevent application crash
        };
    }
}
