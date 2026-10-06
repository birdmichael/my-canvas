using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using static JonsboCanvas_WinUI.UiText;

namespace JonsboCanvas_WinUI;

public sealed class WallpaperThumb
{
    public required string Path { get; init; }
    public required BitmapImage Image { get; init; }
}

public sealed partial class WallpaperPage : Page, ILivePage
{
    private readonly CanvasEngine _engine = App.Engine;
    private string? _shown;
    private bool _syncing = true;

    public WallpaperPage()
    {
        InitializeComponent();
        SourceChoice.SetOptions(("bing", L("Source.bing")), ("wallhaven", L("Source.wallhaven")), ("custom", L("Source.custom")));
        SourceChoice.Selected = _engine.Config.WallpaperSource;
        SourceChoice.SelectionChanged += Source_Changed;
        DesktopToggle.IsOn = _engine.Config.DesktopWallpaperSync;
        AccentToggle.IsOn = _engine.Config.AccentColorSync;
        _syncing = false;
        UpdateSourceParts();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        bool busy = _engine.Wallpapers.Busy;
        bool custom = _engine.Config.WallpaperSource == "custom";
        HeroProgress.IsActive = busy;
        NextButton.IsEnabled = !busy && !custom;
        WallpaperSnapshot? current = _engine.Wallpapers.Current;
        string path = current?.SourcePath ?? "";
        if (path == _shown) return;
        _shown = path;
        OpenButton.IsEnabled = File.Exists(path);
        if (current == null || !File.Exists(path))
        {
            HeroImage.Source = null;
            HeroTitle.Text = L("Wallpaper.Loading");
            HeroInfo.Text = "";
        }
        else
        {
            HeroImage.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 1280 };
            HeroTitle.Text = !string.IsNullOrWhiteSpace(current.Title) ? current.Title : Path.GetFileNameWithoutExtension(path);
            HeroInfo.Text = L("Source." + _engine.Config.WallpaperSource) + " · " + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
        }
        BuildHistory(path);
    }

    private void BuildHistory(string current)
    {
        bool custom = _engine.Config.WallpaperSource == "custom";
        HistorySection.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
        if (custom) return;
        List<WallpaperThumb> thumbs = _engine.Wallpapers.History(18)
            .Select(path => new WallpaperThumb { Path = path, Image = new BitmapImage(new Uri(path)) { DecodePixelWidth = 304 } })
            .ToList();
        HistoryGrid.ItemsSource = thumbs;
        HistoryGrid.SelectedItem = thumbs.FirstOrDefault(t => string.Equals(t.Path, current, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateSourceParts()
    {
        bool custom = _engine.Config.WallpaperSource == "custom";
        FileCard.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        FileCard.Description = string.IsNullOrEmpty(_engine.Config.WallpaperPath)
            ? L("Wallpaper.None")
            : _engine.Config.WallpaperPath;
    }

    private void HistoryGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WallpaperThumb thumb) _engine.SelectWallpaper(thumb.Path);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_engine.NextWallpaper()) return;
        NextButton.IsEnabled = false;
        HeroProgress.IsActive = true;
        App.Current.ShowNotice(L("Wallpaper.Fetching"));
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        string? path = _engine.Wallpapers.Current?.SourcePath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
    }

    private async void Source_Changed(string source)
    {
        if (source == "custom" && !File.Exists(_engine.Config.WallpaperPath))
        {
            if (!await ChooseAsync()) SourceChoice.Selected = _engine.Config.WallpaperSource;
            return;
        }
        await ApplyAsync(source, _engine.Config.WallpaperPath);
    }

    private async void ChooseButton_Click(object sender, RoutedEventArgs e) => await ChooseAsync();

    private async Task<bool> ChooseAsync()
    {
        string? initial = File.Exists(_engine.Config.WallpaperPath)
            ? Path.GetDirectoryName(_engine.Config.WallpaperPath)
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string? path = NativeFileDialog.PickImage(WindowHandle(), L("Wallpaper.DialogTitle"), initial);
        return path != null && await ApplyAsync("custom", path);
    }

    private async Task<bool> ApplyAsync(string source, string path)
    {
        bool loaded = await _engine.SetWallpaperSourceAsync(source, path);
        if (!loaded) App.Current.ShowNotice(L("Wallpaper.Failed"));
        SourceChoice.Selected = _engine.Config.WallpaperSource;
        UpdateSourceParts();
        _shown = null;
        Refresh();
        return loaded;
    }

    private IntPtr WindowHandle() =>
        XamlRoot?.ContentIslandEnvironment?.AppWindowId is { } id ? Microsoft.UI.Win32Interop.GetWindowFromWindowId(id) : IntPtr.Zero;

    private void DesktopToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _engine.SetDesktopWallpaperSync(DesktopToggle.IsOn);
    }

    private void AccentToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _engine.SetAccentSync(AccentToggle.IsOn);
    }
}
