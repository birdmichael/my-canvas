using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static JonsboCanvas_WinUI.UiText;
using DrawingBitmap = System.Drawing.Bitmap;

namespace JonsboCanvas_WinUI;

public sealed partial class HomePage : Page, ILivePage, IPreviewSink
{
    private readonly CanvasEngine _engine = App.Engine;
    private readonly byte[] _longPixels = new byte[1920 * 462 * 4];
    private readonly byte[] _squarePixels = new byte[480 * 480 * 4];
    private DispatcherQueue? _dispatcher;
    private int _uploadPending;
    private string _wallpaperShown = "";
    private bool _syncing;

    public HomePage()
    {
        InitializeComponent();
        LongMode.SetOptions(("auto", L("Mode.Auto")), ("hardware", L("Mode.Hardware")), ("music", L("Mode.Music")));
        SquareMode.SetOptions(("clock", L("Mode.Clock")), ("auto", L("Mode.Auto")), ("music", L("Mode.Music")));
        LongMode.SelectionChanged += mode => _engine.SetLongMode(mode);
        SquareMode.SelectionChanged += mode => _engine.SetSquareMode(mode);
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    private void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        _dispatcher = DispatcherQueue;
        _syncing = true;
        LongMode.Selected = _engine.Config.DisplayMode;
        SquareMode.Selected = _engine.Config.SquareDisplayMode == "hardware" ? "clock" : _engine.Config.SquareDisplayMode;
        LightToggle.IsOn = _engine.Config.LightingEnabled;
        BrightnessSlider.Value = _engine.Config.LightingBrightness;
        BrightnessText.Text = _engine.Config.LightingBrightness + "%";
        _syncing = false;
        Refresh();
        _engine.Preview = this;
        _engine.Invalidate();
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_engine.Preview, this)) _engine.Preview = null;
    }

    // Render thread: copies the frames and hands them to the UI thread, skipping
    // frames while the previous upload is still queued.
    public void Present(DrawingBitmap longFrame, DrawingBitmap squareFrame)
    {
        if (_dispatcher == null || Interlocked.CompareExchange(ref _uploadPending, 1, 0) != 0) return;
        CopyPixels(longFrame, _longPixels);
        CopyPixels(squareFrame, _squarePixels);
        if (!_dispatcher.TryEnqueue(Upload)) Volatile.Write(ref _uploadPending, 0);
    }

    private void Upload()
    {
        LongPreview.Show(_longPixels, 1920, 462);
        SquarePreview.Show(_squarePixels, 480, 480);
        Volatile.Write(ref _uploadPending, 0);
    }

    private static void CopyPixels(DrawingBitmap frame, byte[] target)
    {
        BitmapData data = frame.LockBits(new System.Drawing.Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = frame.Width * 4;
            for (int row = 0; row < frame.Height; row++)
                Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), target, row * rowBytes, rowBytes);
        }
        finally { frame.UnlockBits(data); }
    }

    public void Refresh()
    {
        ScreenState longState = _engine.LongState, squareState = _engine.SquareState;
        int online = (longState == ScreenState.Connected ? 1 : 0) + (squareState == ScreenState.Connected ? 1 : 0);
        ConnectionText.Text = _engine.CaptureMode ? L("State.Demo") : LF("Home.Online", online);
        LongStateText.Text = ScreenState(longState, _engine.LongPortName) + " · " + LF("Home.Showing", ModeName(_engine.ShownLongMode));
        LongDot.Fill = StateBrush(longState);
        SquareStateText.Text = ScreenState(squareState) + " · " + LF("Home.Showing", ModeName(_engine.ShownSquareMode == "music" ? "music" : "clock"));
        SquareDot.Fill = StateBrush(squareState);

        bool requested = _engine.ConnectionRequested;
        bool busy = _engine.ConnectBusy;
        ConnectButton.IsEnabled = !busy && !_engine.CaptureMode && _engine.StartupError == null;
        ConnectButton.Style = (Style)Application.Current.Resources[requested ? "DefaultButtonStyle" : "AccentButtonStyle"];
        ConnectLabel.Text = L(requested ? "Connection.Disconnect" : "Connection.Connect");
        ConnectIcon.Glyph = requested ? "\uE71A" : "\uE768";
        ConnectIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        ConnectProgress.IsActive = busy;
        ConnectProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (_engine.StartupError != null)
        {
            ProblemBar.Severity = InfoBarSeverity.Error;
            ProblemBar.Message = LF("Status.ResourceFailed", _engine.StartupError);
            ProblemBar.IsOpen = true;
        }
        else if (longState == ScreenState.NotResponding)
        {
            ProblemBar.Severity = InfoBarSeverity.Warning;
            ProblemBar.Message = L("State.NotRespondingHint");
            ProblemBar.IsOpen = true;
        }
        else ProblemBar.IsOpen = false;

        RefreshMusic();
        RefreshLighting();
        RefreshWallpaper();
    }

    private void RefreshMusic()
    {
        MusicLink state = _engine.MusicState;
        var music = _engine.Music;
        if (state == MusicLink.Synced && music?.Available == true)
        {
            SongText.Text = music.Title;
            ArtistText.Text = music.Artist + " · " + L(music.Playing ? "Music.Playing" : "Music.Paused");
        }
        else
        {
            SongText.Text = MusicState(state);
            ArtistText.Text = L("Music.Hint");
        }
        MusicButton.Visibility = state is MusicLink.Off or MusicLink.WaitingForTrack && !_engine.CaptureMode
            ? Visibility.Visible : Visibility.Collapsed;
        MusicButton.Content = MusicAction(state);
    }

    private void RefreshLighting()
    {
        var config = _engine.Config;
        System.Drawing.Color light = _engine.LightColor;
        LightSwatch.Background = new SolidColorBrush(config.LightingEnabled && !light.IsEmpty
            ? Windows.UI.Color.FromArgb(255, light.R, light.G, light.B)
            : Windows.UI.Color.FromArgb(255, 0x2B, 0x2B, 0x2B));
        LightSwatch.Opacity = config.LightingEnabled ? 1 : 0.5;
        LightTitle.Text = config.LightingEnabled ? $"#{light.R:X2}{light.G:X2}{light.B:X2}" : L("Common.Off");
        bool music = _engine.ShownLongMode == "music" || _engine.ShownSquareMode == "music";
        string source = music
            ? L(config.LightingCoverColor ? "Lighting.FromCover" : "Lighting.Fixed")
            : L(config.LightingWallpaperColor ? "Lighting.FromWallpaper" : "Lighting.Fixed");
        if (music && config.LightingBeatSync) source += " · " + L("Lighting.Beat");
        LightSource.Text = source;
        BrightnessSlider.IsEnabled = config.LightingEnabled;
    }

    private void RefreshWallpaper()
    {
        WallpaperSnapshot? current = _engine.Wallpapers.Current;
        string path = current?.SourcePath ?? "";
        NextWallpaperButton.IsEnabled = !_engine.Wallpapers.Busy && _engine.Config.WallpaperSource != "custom";
        if (path == _wallpaperShown) return;
        _wallpaperShown = path;
        WallpaperThumb.Source = File.Exists(path) ? new BitmapImage(new Uri(path)) { DecodePixelWidth = 224 } : null;
        WallpaperTitle.Text = current == null ? L("Wallpaper.Loading")
            : !string.IsNullOrWhiteSpace(current.Title) ? current.Title
            : Path.GetFileNameWithoutExtension(path);
        WallpaperSource.Text = L("Source." + _engine.Config.WallpaperSource);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        Task work = _engine.ConnectionRequested ? _engine.DisconnectAsync() : UiText.ConnectAsync(XamlRoot);
        Refresh();
        await work;
        Refresh();
    }

    private async void MusicButton_Click(object sender, RoutedEventArgs e)
    {
        Task work = EnsureMusicAsync(XamlRoot);
        Refresh();
        await work;
        Refresh();
    }

    private void LightToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _engine.Config.LightingEnabled = LightToggle.IsOn;
        _engine.LightingChanged();
        RefreshLighting();
    }

    private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        int value = (int)Math.Round(e.NewValue);
        BrightnessText.Text = value + "%";
        _engine.SetBrightness(value);
        BrightnessSave.Schedule(_engine);
    }

    private void NextWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.NextWallpaper())
        {
            NextWallpaperButton.IsEnabled = false;
            App.Current.ShowNotice(L("Wallpaper.Fetching"));
        }
    }
}

// Dragging the brightness slider applies at once; the config file is written once it settles.
internal static class BrightnessSave
{
    private static DispatcherQueueTimer? _timer;

    public static void Schedule(CanvasEngine engine)
    {
        if (_timer == null)
        {
            _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) => engine.SaveConfig();
        }
        _timer.Stop();
        _timer.Start();
    }
}
