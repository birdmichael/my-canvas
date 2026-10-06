using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using JonsboCanvas;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;

namespace JonsboCanvas_WinUI;

internal enum MusicLink { Off, Connecting, WaitingForTrack, Synced }

// Receives each rendered frame while a window shows the screens. Called on the
// render thread; the frames are only valid during the call.
internal interface IPreviewSink
{
    void Present(DrawingBitmap longFrame, DrawingBitmap squareFrame);
}

// Drives the two screens, the case lighting, the wallpaper and the accent
// colour. It runs for the whole process on its own thread, so the window can
// be closed (and its memory released) while everything keeps going. Windows
// read its state and hand it changes; anything that touches the renderer or
// the collectors runs on the render thread.
internal sealed class CanvasEngine : IDisposable
{
    private readonly string _configPath;
    private readonly object _saveSync = new();
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly DualLayoutRenderer _renderer;
    private readonly DualDisplayController _display;
    private readonly WeatherService _weather;
    private readonly WallpaperService _wallpaper;
    private MetricsCollector? _metricsCollector;
    private NeteaseMediaCollector? _media;
    private MetricsSnapshot? _metrics;
    private volatile MusicSnapshot? _music;
    private DateTime _metricsAt = DateTime.MinValue;
    private DateTime _musicAt = DateTime.MinValue;
    private DateTime _lastFrameAt = DateTime.MinValue;
    private int _forceFrame;
    private volatile bool _disposed;
    private volatile bool _connectionRequested;
    private volatile bool _connectBusy;
    private volatile bool _resumeBusy;
    private volatile bool _musicBusy;
    private volatile string _resolvedLong = "hardware";
    private volatile string _resolvedSquare = "clock";
    private WallpaperSnapshot? _releasedWallpaper;

    public AppConfig Config { get; }
    public bool CaptureMode { get; }
    public string? StartupError { get; }

    // Short status lines for the window, already localised.
    public event Action<string>? Notice;
    // The light colour the Windows accent now follows; Empty when the user's own accent is back.
    public event Action<DrawingColor>? AccentChanged;

    // Set while a window shows the live screens.
    public IPreviewSink? Preview { get; set; }

    public CanvasEngine(string? startupError)
    {
        StartupError = startupError;
        _configPath = EmbeddedRuntime.ConfigPath;
        Config = AppConfig.Load(_configPath);
        Localization.SetLanguage(Config.Language);
        _brightness = Config.LightingBrightness;
        CaptureMode = Environment.GetEnvironmentVariable("JONSBO_CANVAS_CAPTURE") == "1";
        if (CaptureMode)
        {
            string mode = Environment.GetEnvironmentVariable("JONSBO_CANVAS_CAPTURE_MODE") ?? "hardware";
            Config.DisplayMode = mode is "auto" or "music" ? mode : "hardware";
            Config.AutoConnect = false;
            if (Config.DisplayMode == "music") _music = CaptureMusic();
        }

        _renderer = new DualLayoutRenderer(Config);
        _weather = new WeatherService(Config.WeatherLocation);
        _wallpaper = new WallpaperService(EmbeddedRuntime.DataDirectory);
        _wallpaper.SetWallhavenQuery(Config.WallpaperQuery);
        _wallpaper.SetSource(Config.WallpaperSource);
        if (Config.WallpaperSource == "custom")
            _wallpaper.UseCustom(Config.WallpaperPath);
        if (!CaptureMode)
        {
            if (Config.DesktopWallpaperSync && string.IsNullOrEmpty(Config.DesktopWallpaperOriginal)) RememberDesktopWallpaper();
            if (Config.AccentColorSync && string.IsNullOrEmpty(Config.AccentColorOriginal)) RememberAccentColor();
        }
        _display = new DualDisplayController(Config);
        _thread = new Thread(Loop) { IsBackground = true, Name = "Render loop" };
    }

    public void Start()
    {
        _thread.Start();
        if (!CaptureMode && Config.AutoConnect && StartupError == null)
            _ = ConnectAsync(confirmTakeover: null);
    }

    public void SaveConfig()
    {
        lock (_saveSync)
        {
            try { AppConfig.Save(_configPath, Config); }
            catch (Exception exception) { Log.Write("Config save failed: " + exception.Message); }
        }
    }

    // Renders a new frame as soon as possible, e.g. after a setting changed.
    public void Invalidate()
    {
        Interlocked.Exchange(ref _forceFrame, 1);
        _wake.Set();
    }

    public void Run(Action action)
    {
        _commands.Enqueue(action);
        _wake.Set();
    }

    public Task<T> RunAsync<T>(Func<T> function)
    {
        TaskCompletionSource<T> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Run(() =>
        {
            try { done.SetResult(function()); }
            catch (Exception exception) { done.SetException(exception); }
        });
        return done.Task;
    }

    // ---------- state for the window ----------

    public ScreenState LongState => CaptureMode ? ScreenState.Off : _display.LongState;
    public ScreenState SquareState => CaptureMode ? ScreenState.Off : _display.SquareState;
    public string LongPortName => _display.LongPortName;
    public bool ConnectionRequested => _connectionRequested;
    public bool ConnectBusy => _connectBusy || _resumeBusy;
    public string ShownLongMode => _resolvedLong;
    public string ShownSquareMode => _resolvedSquare;
    public MusicSnapshot? Music => _music;
    public WallpaperService Wallpapers => _wallpaper;
    public DrawingColor LightColor => DrawingColor.FromArgb(Volatile.Read(ref _lightArgb));

    public MusicLink MusicState =>
        CaptureMode || _music?.Realtime == true ? MusicLink.Synced
        : _musicBusy ? MusicLink.Connecting
        : _media?.RealtimeConnected == true ? MusicLink.WaitingForTrack
        : MusicLink.Off;

    // ---------- render loop ----------

    private void Loop()
    {
        DateTime lastLoop = DateTime.MinValue;
        DateTime lastError = DateTime.MinValue;
        while (!_disposed)
        {
            while (_commands.TryDequeue(out Action? command))
            {
                try { command(); }
                catch (Exception exception) { Log.Write("Engine command failed: " + exception); }
            }

            DateTime now = DateTime.UtcNow;
            if (DisplayResumePolicy.ShouldRestartDisplay(lastLoop, now, _connectionRequested))
                _ = RecoverAfterResumeAsync();
            lastLoop = now;

            int interval = FrameInterval();
            bool force = Interlocked.Exchange(ref _forceFrame, 0) == 1;
            if (force || (now - _lastFrameAt).TotalMilliseconds >= interval)
            {
                // Cadence is measured from frame start, so render time is not added on top.
                _lastFrameAt = now;
                try { Tick(); }
                catch (Exception exception)
                {
                    if ((now - lastError).TotalMinutes >= 1) Log.Write("Frame failed: " + exception);
                    lastError = now;
                }
            }
            int wait = interval - (int)(DateTime.UtcNow - _lastFrameAt).TotalMilliseconds;
            _wake.WaitOne(Math.Max(10, wait));
        }
    }

    private bool OutputActive => Preview != null || (!CaptureMode && _connectionRequested);

    private int FrameInterval()
    {
        int interval = Resolve(Config.DisplayMode) == "music" || Resolve(Config.SquareDisplayMode) == "music"
            ? _music?.Playing == false ? 200 : Math.Max(50, 1000 / Config.MusicFrameRate)
            : Math.Min(1000, Config.RefreshMilliseconds);
        // With nothing to show, only the lighting, wallpaper and accent need a beat.
        return OutputActive ? interval : Math.Max(1000, interval);
    }

    private string Resolve(string mode) => ScreenModePolicy.Resolve(mode, _music?.Available == true, _music?.Playing == true);

    private void Tick()
    {
        DateTime now = DateTime.UtcNow;
        string mode = Config.DisplayMode, squareMode = Config.SquareDisplayMode;
        bool needsMusic = Config.LightingEnabled || mode is "music" or "auto" || squareMode is "music" or "auto";
        if (!CaptureMode && needsMusic &&
            (_music == null || (now - _musicAt).TotalMilliseconds >= (_music.Realtime ? 20 : 500)))
        {
            if (_media == null && (mode == "music" || squareMode == "music" || NeteaseLauncher.IsRunning()))
                _media = new NeteaseMediaCollector(Config.NeteaseDebugPort);
            _music = _media?.Collect() ?? new MusicSnapshot();
            _musicAt = now;
        }
        string longShown = Resolve(mode), squareShown = Resolve(squareMode);
        _resolvedLong = longShown;
        _resolvedSquare = squareShown;
        RefreshMusicLightingColor();
        UpdateBeatLoop(MusicPlaying);
        ApplyLighting(MusicPlaying);
        if ((longShown == "hardware" || squareShown == "hardware") &&
            (_metrics == null || (now - _metricsAt).TotalMilliseconds >= Config.RefreshMilliseconds))
        {
            _metricsCollector ??= new MetricsCollector(Config.EnableCpuidSensors && !CaptureMode);
            _metrics = _metricsCollector.Collect();
            _metricsAt = now;
        }
        if (longShown != "music" || squareShown != "music")
            _wallpaper.Refresh();
        SyncDesktopWallpaper();
        SyncAccentColor();

        if (!OutputActive) return;
        if (squareShown != "music" && !CaptureMode) _weather.Refresh();
        DrawingBitmap longFrame = _renderer.RenderLong(_metrics, _music, longShown, _wallpaper.Current);
        DrawingBitmap squareFrame;
        try { squareFrame = _renderer.RenderSquare(squareShown, _metrics, _music, _weather.Current, _wallpaper.Current); }
        catch { longFrame.Dispose(); throw; }
        ReleaseWallpaperPhotos();

        IPreviewSink? preview = Preview;
        if (preview != null)
        {
            try { preview.Present(longFrame, squareFrame); }
            catch (Exception exception) { Log.Write("Preview failed: " + exception.Message); }
        }
        if (!CaptureMode && _connectionRequested)
            _display.Post(longFrame, squareFrame, longShown, squareShown);
        else
        {
            longFrame.Dispose();
            squareFrame.Dispose();
        }
    }

    // The renderer keeps its own layers and the lighting its colour, so once both
    // have read a photo its full-size bitmaps can go.
    private void ReleaseWallpaperPhotos()
    {
        WallpaperSnapshot? current = _wallpaper.Current;
        if (current == null || ReferenceEquals(current, _releasedWallpaper)) return;
        if (_renderer.WallpaperId != current.Id || _wallpaperLightId != current.Id) return;
        current.DisposeImages();
        _releasedWallpaper = current;
    }

    // ---------- lighting ----------

    private static readonly DrawingColor DefaultHardwareLight = DrawingColor.FromArgb(0xFF, 0x6B, 0x35);
    private static readonly DrawingColor DefaultMusicLight = DrawingColor.FromArgb(0x8B, 0x5C, 0xF6);
    private int _brightness;
    private int _lightArgb;
    private string _wallpaperLightId = "";
    private (DrawingColor Color, double Level)? _wallpaperLight;
    private double _wallpaperLuminance;
    private DrawingColor _musicLightingColor = DefaultMusicLight;
    private DrawingColor _beatColor = DefaultMusicLight;
    private string _coverColorSongId = "";
    private DrawingColor _coverColor = DrawingColor.Empty;
    private DrawingColor[] _coverPalette = Array.Empty<DrawingColor>();
    private DrawingColor[] _coverColors = Array.Empty<DrawingColor>();
    private CancellationTokenSource? _beatCts;
    // Guards the hand-over between the beat loop and static lighting.
    private readonly object _beatSync = new();

    private bool MusicPlaying => _music?.Available == true && _music.Playing;

    private void ApplyLighting(bool musicActive)
    {
        var (color, level) = LightColorFor(musicActive);
        Volatile.Write(ref _lightArgb, color.ToArgb());
        if (!Config.LightingEnabled)
        {
            LightingController.SetColor(0, 0, 0, 0);
            return;
        }
        if (_beatCts != null) return;
        byte brightness = (byte)Math.Clamp(Math.Round(Volatile.Read(ref _brightness) * 255 / 100.0 * level), 0, 255);
        LightingController.SetColor(color.R, color.G, color.B, brightness);
    }

    // The case's base colour, before brightness and the beat effect.
    private (DrawingColor Color, double Level) LightColorFor(bool musicActive)
    {
        if (musicActive && _music?.Available == true && _music.Playing)
            return (_musicLightingColor, 1);
        if (Config.LightingWallpaperColor && WallpaperLight() is { Color.IsEmpty: false } scene)
            return scene;
        return (Config.GetColor(Config.LightingHardwareColor, DefaultHardwareLight), 1);
    }

    // The wallpaper's tone, and a brightness level following how light the photo
    // is: a night scene runs the case at about half of the brightness setting.
    private (DrawingColor Color, double Level)? WallpaperLight()
    {
        WallpaperSnapshot? wallpaper = _wallpaper.Current;
        if (wallpaper == null) return null;
        if (wallpaper.Id != _wallpaperLightId)
        {
            var (color, luminance) = DominantColor.Ambient(wallpaper.Image);
            _wallpaperLightId = wallpaper.Id;
            _wallpaperLuminance = luminance;
            _wallpaperLight = color.IsEmpty ? null : (color, Math.Clamp(0.3 + 1.2 * luminance, 0.3, 1.0));
            LightingController.LogInfo($"wallpaper {wallpaper.Id} -> color=#{color.R:X2}{color.G:X2}{color.B:X2} luminance={luminance:0.00}");
        }
        return _wallpaperLight;
    }

    // Album art's vibrant colour when enabled, otherwise the manual colour.
    private void RefreshMusicLightingColor()
    {
        DrawingColor manual = Config.GetColor(Config.LightingMusicColor, DefaultMusicLight);
        if (Config.LightingCoverColor && _music?.Cover != null)
        {
            string id = _music.SongId ?? "";
            if (id != _coverColorSongId || _coverColor.IsEmpty)
            {
                _coverColorSongId = id;
                _coverPalette = DominantColor.FromBitmapMulti(_music.Cover, 3);
                _coverColor = _coverPalette.Length > 0 ? _coverPalette[0] : DominantColor.FromBitmap(_music.Cover);
            }
            _coverColors = _coverPalette;
            _musicLightingColor = _coverColor.IsEmpty ? manual : _coverColor;
        }
        else
        {
            _coverColors = Array.Empty<DrawingColor>();
            _musicLightingColor = manual;
        }
    }

    private void UpdateBeatLoop(bool musicVisible)
    {
        bool active = Config.LightingEnabled && Config.LightingBeatSync && musicVisible
            && _music?.Available == true && _music.Playing;
        if (active && _beatCts == null)
        {
            CancellationTokenSource cts = new();
            _beatCts = cts;
            _beatColor = _musicLightingColor;
            LightingController.LogInfo("mode=music beat started");
            Task.Run(() => BeatLoop(cts.Token));
        }
        else if (!active && _beatCts != null)
        {
            lock (_beatSync) _beatCts.Cancel();
            _beatCts.Dispose();
            _beatCts = null;
            LightingController.Reset();
            LightingController.LogInfo("beat stopped; restoring static theme");
            Invalidate();
        }
    }

    private async Task BeatLoop(CancellationToken token)
    {
        MusicLightEnvelope envelope = new();
        Stopwatch clock = Stopwatch.StartNew();
        double previous = 0;
        int colorIndex = 0;
        double lastColorSwitch = 0;
        double lastAudioLog = 0;
        // Palette colours change on a beat at most this often, and drift on
        // by themselves after the longer interval; each change crossfades.
        const double minColorSeconds = 1.4;
        const double maxColorSeconds = 5.0;
        const double crossfadeSeconds = 0.35;
        DrawingColor start = _beatColor;
        double r = start.R, g = start.G, b = start.B;
        while (!token.IsCancellationRequested)
        {
            if (!SystemAudioAnalyzer.TryRead(out float bass, out float full))
                bass = full = SystemAudioLevel.GetPeak();
            double now = clock.Elapsed.TotalSeconds;
            double dt = now - previous;
            var frame = envelope.Sample(bass, full, Volatile.Read(ref _brightness), dt);
            if (now - lastAudioLog >= 3)
            {
                lastAudioLog = now;
                LightingController.LogInfo($"audio bass={bass:0.0000} full={full:0.0000} out={frame.Brightness} beat={(frame.Beat ? 1 : 0)}");
            }
            DrawingColor[] palette = _coverColors;
            DrawingColor target = _musicLightingColor;
            if (palette.Length > 1)
            {
                double sinceSwitch = now - lastColorSwitch;
                if ((frame.Beat && sinceSwitch >= minColorSeconds) || sinceSwitch >= maxColorSeconds)
                {
                    colorIndex = (colorIndex + 1) % palette.Length;
                    lastColorSwitch = now;
                }
                target = palette[colorIndex % palette.Length];
            }
            double k = 1 - Math.Exp(-Math.Clamp(dt, 0.001, 0.25) / crossfadeSeconds);
            r += (target.R - r) * k; g += (target.G - g) * k; b += (target.B - b) * k;
            // A flash also lifts the colour slightly towards white.
            double lift = frame.Flash * 0.18;
            byte Lift(double v) => (byte)Math.Clamp(Math.Round(v + (255 - v) * lift), 0, 255);
            lock (_beatSync)
            {
                // Mode switches cancel under this lock before restoring static lighting.
                if (token.IsCancellationRequested || _disposed) break;
                LightingController.Pulse(Lift(r), Lift(g), Lift(b), frame.Brightness);
            }
            previous = now;
            try { await Task.Delay(15, token); } catch (OperationCanceledException) { break; }
        }
        SystemAudioAnalyzer.Stop();
    }

    public void SetBrightness(int value)
    {
        Config.LightingBrightness = value;
        Volatile.Write(ref _brightness, value);
        Run(() => ApplyLighting(MusicPlaying));
    }

    // Applies lighting settings already written to Config.
    public void LightingChanged(bool coverChanged = false, bool beatChanged = false)
    {
        SaveConfig();
        Run(() =>
        {
            if (coverChanged) _coverColorSongId = "";
            if (beatChanged && !Config.LightingBeatSync) LightingController.Reset();
            ApplyLighting(MusicPlaying);
        });
        Invalidate();
    }

    // ---------- desktop wallpaper and accent ----------

    private string _desktopWallpaperPath = "";
    private DrawingColor _accentApplied = DrawingColor.Empty;
    private DateTime _accentNextTry;
    private int _accentBusy;

    // Keeps the user's own desktop picture so turning sync off can put it back.
    private void RememberDesktopWallpaper()
    {
        string current = DesktopWallpaper.Current();
        if (string.IsNullOrEmpty(current) || current.StartsWith(EmbeddedRuntime.DataDirectory, StringComparison.OrdinalIgnoreCase)) return;
        Config.DesktopWallpaperOriginal = current;
        SaveConfig();
    }

    private void RememberAccentColor()
    {
        string snapshot = SystemAccent.Snapshot();
        if (string.IsNullOrEmpty(snapshot)) return;
        Config.AccentColorOriginal = snapshot;
        SaveConfig();
    }

    // Called with every frame; only acts when the photo changed.
    private void SyncDesktopWallpaper()
    {
        if (!Config.DesktopWallpaperSync || CaptureMode || _wallpaper.Current is not { } current) return;
        string path = WallpaperService.DesktopPath(current);
        if (string.IsNullOrEmpty(path) || path == _desktopWallpaperPath) return;
        _desktopWallpaperPath = path;
        if (path == current.SourcePath) _wallpaper.FetchDesktopCopy(current);
        _ = Task.Run(() =>
        {
            bool done = DesktopWallpaper.Set(path);
            Log.Write((done ? "Desktop wallpaper set: " : "Desktop wallpaper refused: ") + path);
        });
    }

    // Called with every frame; the photo's main colour (the hardware-mode light)
    // becomes the Windows accent. Music never changes the photo, so it leaves
    // the accent alone. Waits while a game or video is full screen.
    private void SyncAccentColor()
    {
        if (!Config.AccentColorSync || CaptureMode || WallpaperLight() is not { Color.IsEmpty: false } scene) return;
        DrawingColor color = scene.Color;
        double photoLuminance = _wallpaperLuminance;
        DateTime now = DateTime.UtcNow;
        if (color.ToArgb() == _accentApplied.ToArgb() || now < _accentNextTry) return;
        _accentNextTry = now.AddSeconds(3);
        if (SystemAccent.UserBusy() || Interlocked.Exchange(ref _accentBusy, 1) == 1) return;
        _accentApplied = color;
        _ = Task.Run(() =>
        {
            try
            {
                DrawingColor accent = SystemAccent.ForUi(color, SystemAccent.SystemToneFor(photoLuminance));
                bool done = SystemAccent.Set(accent);
                Log.Write($"Accent colour {(done ? "set" : "refused")}: #{accent.R:X2}{accent.G:X2}{accent.B:X2} (light #{color.R:X2}{color.G:X2}{color.B:X2})");
                if (done) AccentChanged?.Invoke(color);
            }
            finally { Volatile.Write(ref _accentBusy, 0); }
        });
    }

    // The colour the app's own highlight should follow right now.
    public DrawingColor AppAccent => Config.AccentColorSync ? _accentApplied : DrawingColor.Empty;

    public void SetDesktopWallpaperSync(bool enabled)
    {
        Config.DesktopWallpaperSync = enabled;
        if (enabled) RememberDesktopWallpaper();
        else
        {
            string original = Config.DesktopWallpaperOriginal;
            if (File.Exists(original)) _ = Task.Run(() => DesktopWallpaper.Set(original));
        }
        Run(() => _desktopWallpaperPath = "");
        SaveConfig();
        Invalidate();
    }

    public void SetAccentSync(bool enabled)
    {
        Config.AccentColorSync = enabled;
        Run(() =>
        {
            _accentApplied = DrawingColor.Empty;
            _accentNextTry = DateTime.MinValue;
        });
        if (enabled) RememberAccentColor();
        else
        {
            AccentChanged?.Invoke(DrawingColor.Empty);
            string original = Config.AccentColorOriginal;
            if (!string.IsNullOrEmpty(original))
                _ = Task.Run(() => Log.Write("Accent colour restored: " + SystemAccent.Restore(original)));
        }
        SaveConfig();
        Invalidate();
    }

    // ---------- wallpaper choices ----------

    public void SelectWallpaper(string path)
    {
        Run(() => _wallpaper.Select(path));
        Invalidate();
    }

    public bool NextWallpaper() => _wallpaper.Next();

    public void SetWallpaperQuery(string query)
    {
        Config.WallpaperQuery = string.IsNullOrWhiteSpace(query) ? "landscape,mountains,space,forest,lake,night sky" : query.Trim();
        _wallpaper.SetWallhavenQuery(Config.WallpaperQuery);
        SaveConfig();
        if (Config.WallpaperSource == "wallhaven") _wallpaper.Next();
        Invalidate();
    }

    // "bing", "wallhaven" or "custom" with a picture; false when the picture could not be read.
    public async Task<bool> SetWallpaperSourceAsync(string source, string path)
    {
        bool loaded = await RunAsync(() =>
        {
            bool ok = _wallpaper.UseCustom(source == "custom" ? path : null);
            if (ok && source != "custom") _wallpaper.SetSource(source);
            return ok;
        });
        if (!loaded) return false;
        Config.WallpaperSource = source;
        Config.WallpaperPath = path;
        SaveConfig();
        Invalidate();
        return true;
    }

    // ---------- screen content ----------

    public void SetLongMode(string mode)
    {
        Config.DisplayMode = mode is "hardware" or "music" ? mode : "auto";
        SaveConfig();
        Invalidate();
    }

    public void SetSquareMode(string mode)
    {
        Config.SquareDisplayMode = mode is "auto" or "hardware" or "music" ? mode : "clock";
        SaveConfig();
        Invalidate();
    }

    // Applies content settings already written to Config.
    public void ContentChanged()
    {
        Run(() => _metricsAt = DateTime.MinValue);
        SaveConfig();
        Invalidate();
    }

    public void SetLanguage(string language)
    {
        Config.Language = AppConfig.NormalizeLanguage(language);
        Localization.SetLanguage(Config.Language);
        SaveConfig();
        Invalidate();
    }

    // Saves a freshly rendered long-screen frame; returns the file.
    public Task<string> SaveFrameAsync() => RunAsync(() =>
    {
        using DrawingBitmap frame = _renderer.RenderLong(_metrics, _music, _resolvedLong, _wallpaper.Current);
        string path = EmbeddedRuntime.PreviewPath;
        frame.Save(path, ImageFormat.Png);
        return path;
    });

    // ---------- screen connection ----------

    // confirmTakeover asks the user whether to close the vendor app; null means
    // only the automatic takeover setting decides.
    public async Task ConnectAsync(Func<Task<bool>>? confirmTakeover)
    {
        if (CaptureMode || StartupError != null || _connectBusy) return;
        _connectBusy = true;
        try
        {
            if (OriginalAppRunning())
            {
                bool allowed = Config.AutoTakeOverOriginalApp || (confirmTakeover != null && await confirmTakeover());
                if (!allowed) { Notice?.Invoke(Localization.Get("Connection.InUse")); return; }
                if (!await Task.Run(TryStopOriginalApp)) { Notice?.Invoke(Localization.Get("Connection.TakeoverFailed")); return; }
            }
            int result = await Task.Run(_display.Start);
            if (result == 0)
            {
                _connectionRequested = true;
                Invalidate();
            }
            else Notice?.Invoke(Localization.Format("Connection.FailedCode", result));
        }
        catch (Exception exception)
        {
            Log.Write("Connect failed: " + exception);
            Notice?.Invoke(Localization.Format("Connection.Failed", exception.Message));
        }
        finally { _connectBusy = false; }
    }

    public async Task DisconnectAsync()
    {
        if (CaptureMode) return;
        _connectionRequested = false;
        _connectBusy = true;
        try { await Task.Run(_display.Stop); }
        catch (Exception exception) { Log.Write("Disconnect failed: " + exception); }
        finally { _connectBusy = false; }
    }

    private async Task RecoverAfterResumeAsync()
    {
        if (CaptureMode || _resumeBusy || !_connectionRequested || _disposed) return;
        _resumeBusy = true;
        Log.Write("System resume gap detected; restarting the screens.");
        try
        {
            await Task.Run(_display.Stop);
            await Task.Delay(1200);
            if (_disposed || !_connectionRequested) return;
            int result = await Task.Run(_display.Start);
            if (result != 0) Notice?.Invoke(Localization.Format("Connection.ResumeFailedCode", result));
            Invalidate();
        }
        catch (Exception exception)
        {
            Log.Write("Resume recovery failed: " + exception);
        }
        finally { _resumeBusy = false; }
    }

    private static bool OriginalAppRunning()
    {
        try { return Process.GetProcessesByName("JONSBO-AIO").Length > 0; }
        catch { return false; }
    }

    private static bool TryStopOriginalApp()
    {
        try
        {
            foreach (Process process in Process.GetProcessesByName("JONSBO-AIO"))
            {
                using (process)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            Thread.Sleep(350);
            return !OriginalAppRunning();
        }
        catch (Exception exception)
        {
            Log.Write("Unable to stop original app: " + exception);
            return false;
        }
    }

    // ---------- NetEase realtime sync ----------

    // confirmRestart asks whether NetEase may be restarted with the sync port.
    public async Task EnsureMusicRealtimeAsync(Func<Task<bool>> confirmRestart)
    {
        if (CaptureMode || _musicBusy || _music?.Realtime == true) return;
        NeteaseMediaCollector media = await RunAsync(() => _media ??= new NeteaseMediaCollector(Config.NeteaseDebugPort));
        _musicBusy = true;
        try
        {
            if (media.RealtimeConnected)
            {
                media.ReconnectRealtime();
                for (int attempt = 0; attempt < 15 && !media.RealtimeConnected; attempt++)
                    await Task.Delay(300);
                Notice?.Invoke(Localization.Get(media.RealtimeConnected ? "Music.Reconnected" : "Music.RecheckFailed"));
                return;
            }

            bool running = NeteaseLauncher.IsRunning();
            if (running && !await confirmRestart()) return;
            string result = await Task.Run(() => running
                ? NeteaseLauncher.RestartRealtime(Config.NeteaseDebugPort)
                : NeteaseLauncher.StartRealtime(Config.NeteaseDebugPort));
            Log.Write("NetEase realtime: " + result);
            for (int attempt = 0; attempt < 30 && !media.RealtimeConnected; attempt++)
                await Task.Delay(400);
            Notice?.Invoke(Localization.Get(media.RealtimeConnected ? "Music.ConnectedStatus" : "Music.StillDisconnected"));
        }
        catch (Exception exception)
        {
            Log.Write("Unable to start NetEase realtime sync: " + exception);
            Notice?.Invoke(Localization.Format("Music.SyncFailed", exception.Message));
        }
        finally
        {
            _musicBusy = false;
            Run(() => _musicAt = DateTime.MinValue);
            Invalidate();
        }
    }

    private static MusicSnapshot CaptureMusic() => new()
    {
        Available = true,
        SongId = "capture-song",
        Title = "晚风经过海岸",
        Artist = "网易云音乐 · 演示曲目",
        PreviousLyric = "把白昼慢慢收进口袋",
        CurrentLyric = "晚风经过海岸，也经过你",
        NextLyric = "灯火在远处一盏一盏亮起",
        Progress = 42,
        ElapsedSeconds = 96,
        DurationSeconds = 228,
        Realtime = true,
        Playing = true
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Set();
        if (_thread.IsAlive) _thread.Join(3000);
        lock (_beatSync) { _beatCts?.Cancel(); _beatCts = null; }
        _display.Dispose();
        _media?.Dispose();
        _metricsCollector?.Dispose();
        _renderer.Dispose();
        _weather.Dispose();
        _wallpaper.Dispose();
    }
}
