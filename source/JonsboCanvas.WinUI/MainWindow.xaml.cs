using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace JonsboCanvas_WinUI;

// Pages that show live engine state refresh themselves once a second.
internal interface ILivePage
{
    void Refresh();
}

public sealed partial class MainWindow : Window
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["home"] = typeof(HomePage),
        ["content"] = typeof(ContentPage),
        ["wallpaper"] = typeof(WallpaperPage),
        ["lighting"] = typeof(LightingPage),
        ["devices"] = typeof(DevicesPage),
        ["settings"] = typeof(SettingsPage),
    };

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    // A page left behind keeps its images and visual tree until its managed
    // wrappers are collected, and the app allocates too little for that to
    // happen soon on its own.
    private readonly DispatcherTimer _collectTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private string _page = "";

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(AppAssetPaths.IconPath(AppContext.BaseDirectory));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        // AppWindow sizes are physical pixels; the layout is designed in effective pixels.
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        int width = Math.Min((int)(1240 * scale), area.WorkArea.Width - (int)(48 * scale));
        int height = Math.Min((int)(840 * scale), area.WorkArea.Height - (int)(48 * scale));
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            area.WorkArea.X + Math.Max(0, (area.WorkArea.Width - width) / 2),
            area.WorkArea.Y + Math.Max(0, (area.WorkArea.Height - height) / 2)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(900 * scale);
            presenter.PreferredMinimumHeight = (int)(600 * scale);
        }

        AppWindow.Changed += AppWindow_Changed;
        Closed += MainWindow_Closed;
        _refreshTimer.Tick += (_, _) => (ContentFrame.Content as ILivePage)?.Refresh();
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); NoticeBar.IsOpen = false; };
        _collectTimer.Tick += (_, _) =>
        {
            _collectTimer.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        };
        _refreshTimer.Start();

        AppAccent.Apply(App.Engine.AppAccent);
        ApplyLabels();
        string start = Environment.GetEnvironmentVariable("JONSBO_CANVAS_CAPTURE_PAGE") ?? "home";
        Select(Pages.ContainsKey(start) ? start : "home");
    }

    internal void Present()
    {
        // Quiet captures stay behind every other window and never take focus.
        if (Environment.GetEnvironmentVariable("JONSBO_CANVAS_CAPTURE_QUIET") == "1")
        {
            AppWindow.MoveInZOrderAtBottom();
            AppWindow.Show(false);
            return;
        }
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
    }

    internal void ShowNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        NoticeBar.Severity = severity;
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    internal void Select(string page)
    {
        NavigationViewItem item = page switch
        {
            "content" => ContentItem,
            "wallpaper" => WallpaperItem,
            "lighting" => LightingItem,
            "devices" => DevicesItem,
            "settings" => SettingsItem,
            _ => HomeItem,
        };
        Navigation.SelectedItem = item;
        Show(page);
    }

    // Rebuilds the labels and the visible page in the new language.
    internal void ApplyLanguage()
    {
        ApplyLabels();
        int cache = ContentFrame.CacheSize;
        ContentFrame.CacheSize = 0;
        ContentFrame.CacheSize = cache;
        string page = _page;
        _page = "";
        Show(page);
    }

    private void ApplyLabels()
    {
        AppTitleBar.Subtitle = Localization.Get("App.Subtitle");
        HomeItem.Content = Localization.Get("Nav.Home");
        ContentItem.Content = Localization.Get("Nav.Content");
        WallpaperItem.Content = Localization.Get("Nav.Wallpaper");
        LightingItem.Content = Localization.Get("Nav.Lighting");
        DevicesItem.Content = Localization.Get("Nav.Devices");
        SettingsItem.Content = Localization.Get("Nav.Settings");
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string page)
            Show(page);
    }

    private void Show(string page)
    {
        if (page == _page || !Pages.TryGetValue(page, out Type? type)) return;
        _page = page;
        ContentFrame.Navigate(type, null, new EntranceNavigationTransitionInfo());
        ContentFrame.BackStack.Clear();
        _collectTimer.Stop();
        _collectTimer.Start();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange)
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } &&
                App.Current.KeepsRunningWhenClosed)
                DispatcherQueue.TryEnqueue(Close);
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _refreshTimer.Stop();
        _noticeTimer.Stop();
        _collectTimer.Stop();
        AppWindow.Changed -= AppWindow_Changed;
        App.Engine.Preview = null;
        ContentFrame.Content = null;
    }
}
