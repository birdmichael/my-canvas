using JonsboCanvas;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace JonsboCanvas_WinUI;

// The engine and the tray icon live for the whole process. The window is
// created when it is opened and destroyed when it is closed to the tray, so
// its visual tree and graphics memory are only held while it is on screen.
public partial class App : Application
{
    private SingleInstanceCoordinator? _singleInstance;
    private TrayIconController? _tray;
    private MainWindow? _window;
    private DispatcherQueue? _dispatcher;
    private bool _exiting;
    private bool _trayNoticeShown;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();

    internal static string? StartupError { get; private set; }
    internal static CanvasEngine Engine { get; private set; } = null!;
    internal static new App Current => (App)Application.Current;

    public App()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        bool watchdog = LaunchPolicy.IsWatchdog(arguments);
        // The watchdog task fires every few minutes; it only matters after a crash.
        if (watchdog && SingleInstanceCoordinator.IsRunning())
            Environment.Exit(0);
        try
        {
            EmbeddedRuntime.Initialize();
        }
        catch (Exception exception)
        {
            StartupError = exception.Message;
        }
        if (watchdog && UserExit.Marked)
            Environment.Exit(0);
        if (watchdog)
            Log.Write("Watchdog restarted My Canvas after it stopped without being closed.");
        else
            UserExit.Clear();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write("Fatal unhandled error (terminating=" + args.IsTerminating + "): " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write("Unobserved task error: " + args.Exception);
            args.SetObserved();
        };

        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            Log.Write("WinUI unhandled error: " + args.Exception);
            args.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool captureMode = Environment.GetEnvironmentVariable("JONSBO_CANVAS_CAPTURE") == "1";
        bool autoStarted = LaunchPolicy.IsAutoStarted(Environment.GetCommandLineArgs());

        // Visual captures run beside the live process: no single-instance lock,
        // no tray icon, no hardware.
        if (!captureMode)
        {
            _singleInstance = SingleInstanceCoordinator.TryAcquire();
            if (_singleInstance == null)
            {
                if (!autoStarted)
                    SingleInstanceCoordinator.RequestRestore();
                Exit();
                return;
            }
        }

        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Engine = new CanvasEngine(StartupError);
        Engine.AccentChanged += color => _dispatcher.TryEnqueue(() => AppAccent.Apply(color));
        _uiSettings.ColorValuesChanged += (_, _) => _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, AppAccent.Reapply);
        Engine.Notice += message => _dispatcher.TryEnqueue(() => _window?.ShowNotice(message));

        AppConfig config = Engine.Config;
        if (!captureMode && Environment.ProcessPath is string executablePath)
        {
            if (config.StartWithWindows)
                _ = Task.Run(() => StartupTaskManager.EnsureRegistered(executablePath, allowElevationPrompt: false));
            else
                _ = Task.Run(() => StartupTaskManager.RemoveRegistered());
        }

        if (!captureMode)
        {
            try
            {
                _tray = new TrayIconController(AppAssetPaths.IconPath(AppContext.BaseDirectory), Localization.Get("Tray.Tooltip"));
                _tray.OpenRequested += ShowWindow;
                _tray.ExitRequested += ExitApp;
            }
            catch (Exception exception)
            {
                Log.Write("Unable to initialize tray icon: " + exception);
            }
        }

        Engine.Start();
        if (!(autoStarted && config.StartHiddenOnAutoStart && _tray != null))
            ShowWindow();
    }

    internal void ShowWindow()
    {
        if (_exiting) return;
        if (_window == null)
        {
            _window = new MainWindow();
            _window.Closed += Window_Closed;
        }
        _window.Present();
    }

    // Closing or minimising the window keeps the app in the tray when that is
    // enabled and the tray icon exists; otherwise it quits.
    internal bool KeepsRunningWhenClosed => !_exiting && _tray != null && Engine.Config.MinimizeToTray;

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        _window = null;
        if (!KeepsRunningWhenClosed)
        {
            ExitApp();
            return;
        }
        if (!_trayNoticeShown)
        {
            _trayNoticeShown = true;
            _tray?.ShowInfo(Localization.Get("Tray.Running.Title"), Localization.Get("Tray.Running.Message"));
        }
        // The window's managed wrappers hold its native visual tree until collected.
        _dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        });
    }

    internal void ShowNotice(string message) => _window?.ShowNotice(message);

    internal void ApplyLanguage()
    {
        _tray?.UpdateTooltip(Localization.Get("Tray.Tooltip"));
        _window?.ApplyLanguage();
    }

    internal void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        if (Environment.GetEnvironmentVariable("JONSBO_CANVAS_CAPTURE") != "1")
            UserExit.Mark();
        _window?.Close();
        _tray?.Dispose();
        _tray = null;
        Engine.Dispose();
        _singleInstance?.Dispose();
        Exit();
    }
}

// The app's own highlight colour, following the wallpaper while accent sync is on.
internal static class AppAccent
{
    private static readonly Windows.UI.Color Default = Windows.UI.Color.FromArgb(0xFF, 0x7F, 0xD3, 0xE6);

    // The standard controls' accent brushes. Their templates resolve them inside
    // XamlControlsResources, so app-level overrides are ignored; the shared
    // instances are recoloured instead.
    private static readonly string[] ControlBrushes =
    {
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
        "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
    };

    private static System.Drawing.Color _light = System.Drawing.Color.Empty;

    public static void Apply(System.Drawing.Color light)
    {
        _light = light;
        var resources = Application.Current.Resources;
        if (resources["CanvasAccentBrush"] is not SolidColorBrush accent ||
            resources["CanvasAccentSubtleBrush"] is not SolidColorBrush subtle)
            return;
        Windows.UI.Color color = Default;
        if (!light.IsEmpty)
        {
            System.Drawing.Color tone = SystemAccent.ForUi(light, SystemAccent.AppTone);
            color = Windows.UI.Color.FromArgb(0xFF, tone.R, tone.G, tone.B);
        }
        accent.Color = color;
        subtle.Color = Windows.UI.Color.FromArgb(0x33, color.R, color.G, color.B);
        foreach (string key in ControlBrushes)
        {
            if (resources.TryGetValue(key, out object? value) && value is SolidColorBrush brush)
                brush.Color = Windows.UI.Color.FromArgb(brush.Color.A, color.R, color.G, color.B);
        }
    }

    // Windows refreshes the control brushes when its own accent changes.
    public static void Reapply() => Apply(_light);
}
