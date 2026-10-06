using System.Drawing;
using System.Drawing.Imaging;
using JonsboCanvas;
using JonsboCanvas_WinUI;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

string scratch = Path.Combine(Path.GetTempPath(), "MyCanvasTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
foreach (string property in new[] { "DataDirectory", "RuntimeDirectory" })
    typeof(EmbeddedRuntime).GetProperty(property).SetValue(null, scratch);

AppConfig config = new();
Check(config.LongRotationDegrees == 270 && config.SquareRotationDegrees == 90, "verified physical orientations");
Check(!config.StartWithWindows, "first launch does not register startup");
string configPath = Path.Combine(scratch, "config.json");
File.WriteAllText(configPath, "{\"DisplayMode\":\"hardware\",\"SquareDisplayMode\":\"invalid\",\"LongRotationDegrees\":180,\"SquareRotationDegrees\":7}");
AppConfig migrated = AppConfig.Load(configPath);
Check(migrated.SquareDisplayMode == "clock" && migrated.LongRotationDegrees == 270 && migrated.SquareRotationDegrees == 90, "invalid and missing per-screen settings recover safely");
config.SquareDisplayMode = "music";
config.PreviewDisplay = "square";
AppConfig.Save(configPath, config);
AppConfig reloaded = AppConfig.Load(configPath);
Check(reloaded.SquareDisplayMode == "music" && reloaded.PreviewDisplay == "square", "independent screen settings survive restart");
File.WriteAllText(configPath, "{\"DisplayMode\":\"codex\",\"SquareDisplayMode\":\"claude\"}");
AppConfig retired = AppConfig.Load(configPath);
Check(retired.DisplayMode == "hardware" && retired.SquareDisplayMode == "hardware", "retired AI modes migrate both screens to hardware");
Check(ScreenModePolicy.Resolve("auto", true, true) == "music", "auto shows music while playing");
Check(ScreenModePolicy.Resolve("auto", true, false) == "hardware", "auto returns to hardware on pause");
Check(ScreenModePolicy.Resolve("auto", false, true) == "hardware", "auto uses hardware when playback is unavailable");
Check(ScreenModePolicy.Resolve("music", true, false) == "music", "manual music mode remains visible on pause");
using (Bitmap unchanged = new(480, 480, PixelFormat.Format32bppArgb))
{
    ulong signature = DualDisplayController.Signature(unchanged);
    using Bitmap identical = new(unchanged);
    Check(signature == DualDisplayController.Signature(identical), "identical frames can be suppressed independently");
    unchanged.SetPixel(479, 479, Color.White);
    Check(signature != DualDisplayController.Signature(unchanged), "changes at the screen edge are not missed by suppression");
}

using Bitmap marker = new(1920, 462, PixelFormat.Format32bppArgb);
using (Graphics graphics = Graphics.FromImage(marker))
{
    graphics.Clear(Color.Black);
    graphics.FillRectangle(Brushes.Red, 0, 0, 120, 120);
    graphics.FillRectangle(Brushes.Lime, 1800, 0, 120, 120);
}
using MemoryStream jpeg = new(SerialJpegDisplay.Encode(marker, 270));
using Bitmap physical = new(jpeg);
Check(physical.Width == 462 && physical.Height == 1920, "serial JPEG has the physical portrait dimensions");
Color bottomLeft = physical.GetPixel(30, 1880);
Color topLeft = physical.GetPixel(30, 40);
Check(bottomLeft.R > 180 && bottomLeft.G < 70 && topLeft.G > 180, "serial rotation preserves the expected corner mapping");
bool rejected = false;
try { using Bitmap wrong = new(480, 480); SerialJpegDisplay.Encode(wrong, 270); }
catch (ArgumentException) { rejected = true; }
Check(rejected, "wrong screen dimensions are rejected before transmission");
Check(SerialJpegDisplay.RetryDelay(1).TotalSeconds == 5 && SerialJpegDisplay.RetryDelay(3).TotalSeconds == 20 &&
      SerialJpegDisplay.RetryDelay(50).TotalSeconds == 60, "long-screen reconnects back off from 5 s to once a minute");
Check(SerialJpegDisplay.DeviceHung(new IOException("semaphore timeout", unchecked((int)0x80070079))) &&
      !SerialJpegDisplay.DeviceHung(new IOException("not found")), "a semaphore timeout is recognised as a hung device");
{
    var absent = new SerialJpegDisplay();
    var clock = System.Diagnostics.Stopwatch.StartNew();
    absent.Start("COM250");
    bool connected = absent.WaitConnected(TimeSpan.FromMilliseconds(300));
    bool posted = absent.Post(marker, 270);
    absent.Stop();
    Check(!connected && !posted && clock.ElapsedMilliseconds < 1500, "a missing long screen never blocks the caller");
}
{
    using var screens = new DualDisplayController(new AppConfig());
    using Bitmap square = new(480, 480);
    var clock = System.Diagnostics.Stopwatch.StartNew();
    for (int i = 0; i < 50; i++) screens.Post(new Bitmap(marker), new Bitmap(square), "hardware", "clock");
    Check(clock.ElapsedMilliseconds < 2000, "handing frames to the screens returns at once and keeps only the newest");
}
{
    var logon = System.Xml.Linq.XDocument.Parse(StartupTaskManager.TaskXml(@"C:\Code\MyCanvas\app\MyCanvas.exe", @"C:\Code\MyCanvas\app\data\", watchdog: false));
    var watch = System.Xml.Linq.XDocument.Parse(StartupTaskManager.TaskXml(@"C:\Code\MyCanvas\app\MyCanvas.exe", @"C:\Code\MyCanvas\app\data\", watchdog: true));
    string Value(System.Xml.Linq.XDocument d, string name) => d.Descendants().First(e => e.Name.LocalName == name).Value;
    Check(Value(logon, "ExecutionTimeLimit") == "PT0S" && Value(logon, "StopIfGoingOnBatteries") == "false" &&
          logon.Descendants().Any(e => e.Name.LocalName == "LogonTrigger") && !Value(logon, "Arguments").Contains("--watchdog") &&
          Value(logon, "Arguments") == "--autostart --data-dir=\"C:\\Code\\MyCanvas\\app\\data\"",
        "the logon task never times out and starts the app with its data folder");
    Check(Value(watch, "Interval") == "PT5M" && Value(watch, "Arguments").Contains("--watchdog") &&
          Value(watch, "MultipleInstancesPolicy") == "IgnoreNew" && Value(watch, "ExecutionTimeLimit") == "PT0S",
        "the watchdog task checks every five minutes");
}

DiskSnapshot[] disks =
{
    new() { Name = "C:", UsedGb = 342, TotalGb = 930 },
    new() { Name = "D:", UsedGb = 12, TotalGb = 3726 },
    new() { Name = "E:", UsedGb = 386, TotalGb = 1908 },
};
MetricsSnapshot metrics = new() { CpuName = "AMD Ryzen 7 9800X3D 8-Core Processor", GpuName = "NVIDIA GeForce RTX 3060 Ti", CpuUsage = 47, GpuUsage = 99, GpuMemoryUsage = 56, MemoryUsage = 52, MemoryUsedGb = 16.6, MemoryTotalGb = 32, CpuTemperature = 58, GpuTemperature = 70, CpuPower = 88, GpuPower = 215, GpuMemoryUsedGb = 4.5, GpuMemoryTotalGb = 8, Disks = disks, Uptime = TimeSpan.FromHours(28.4), Timestamp = DateTime.Now };
string wallpaperPath = Environment.GetEnvironmentVariable("MYCANVAS_TEST_WALLPAPER");
Bitmap[] companions = (Environment.GetEnvironmentVariable("MYCANVAS_TEST_COMPANIONS") ?? "")
    .Split(';', StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).Select(path => new Bitmap(path)).ToArray();
WallpaperSnapshot wallpaper = File.Exists(wallpaperPath) ? new() { Image = new Bitmap(wallpaperPath), Id = "test", Title = "Taking the plunge, one lesson at a time", Companions = companions } : null;
Check(DualLayoutRenderer.FormatUptime(TimeSpan.FromHours(28.5)) == "1d 04h" && DualLayoutRenderer.FormatUptime(TimeSpan.FromMinutes(75)) == "1h 15m", "uptime reads as days/hours or hours/minutes");
Check(WallpaperService.Luminance("#000000") == 0 && Math.Abs(WallpaperService.Luminance("#ffffff") - 1) < 1e-9 && WallpaperService.Luminance("bad") == 0.5, "wallpaper palette colours convert to luminance");
{
    string cache = Path.Combine(scratch, "wallpaper-wallhaven");
    Directory.CreateDirectory(Path.Combine(cache, "desktop"));
    string older = Path.Combine(cache, "aaa111.jpg"), newer = Path.Combine(cache, "bbb222.jpg");
    foreach (string photo in new[] { older, newer })
    {
        using Bitmap sample = new(64, 36);
        sample.Save(photo, ImageFormat.Jpeg);
    }
    File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
    File.WriteAllBytes(Path.Combine(cache, "desktop", "bbb222.png"), new byte[] { 1 });
    using WallpaperService cached = new(scratch);
    cached.SetSource("wallhaven");
    Check(cached.History(8).Select(Path.GetFileName).SequenceEqual(new[] { "bbb222.jpg", "aaa111.jpg" }), "wallpaper history lists cached photos newest first");
    cached.Select(older);
    Check(cached.Current?.SourcePath == older && cached.History(8)[0] == older, "picking a recent photo shows it and keeps it for the next start");
    Check(WallpaperService.DesktopPath(new WallpaperSnapshot { Image = new Bitmap(1, 1), Id = "x", SourcePath = newer }).EndsWith(@"desktop\bbb222.png")
        && WallpaperService.DesktopPath(cached.Current!) == older, "desktop uses the full-resolution copy when there is one");
}
if (Environment.GetEnvironmentVariable("MYCANVAS_TEST_ONLINE") == "1")
{
    string online = Path.Combine(Path.GetTempPath(), "mycanvas-wallpaper-" + Guid.NewGuid().ToString("N"));
    using WallpaperService service = new(online);
    service.SetSource("wallhaven");
    service.Refresh();
    for (int i = 0; i < 120 && service.Current == null; i++) Thread.Sleep(250);
    WallpaperSnapshot fetched = service.Current;
    Check(fetched != null && fetched.Id.StartsWith("wallhaven:") && fetched.Image.Width <= 1920, "hourly scenery downloads and is stored at screen resolution");
    string fullCopy = WallpaperService.DesktopPath(fetched);
    Check(fullCopy != fetched.SourcePath, "a full-resolution copy is kept for the desktop");
    File.Delete(fullCopy);
    service.FetchDesktopCopy(fetched);
    for (int i = 0; i < 120 && WallpaperService.DesktopPath(fetched) == fetched.SourcePath; i++) Thread.Sleep(250);
    Check(WallpaperService.DesktopPath(fetched) != fetched.SourcePath, "a missing full-resolution copy is fetched again by id");
    if (fetched != null && args.Length > 0)
    {
        Directory.CreateDirectory(args[0]);
        fetched.Image.Save(Path.Combine(args[0], "wallhaven-live.jpg"));
    }
}
Check(FrameSendPolicy.LongMusicIntervalMilliseconds(12) == 40 && FrameSendPolicy.LongMusicIntervalMilliseconds(100) == 150 && FrameSendPolicy.LongMusicIntervalMilliseconds(400) == 200, "long screen music cadence follows measured send time within bounds");
Check(DualLayoutRenderer.ShortName("AMD Ryzen 7 9800X3D 8-Core Processor") == "AMD Ryzen 7 9800X3D" && DualLayoutRenderer.ShortName("Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz") == "Intel Core i7-9700K" && DualLayoutRenderer.ShortName("NVIDIA GeForce RTX 3060 Ti") == "RTX 3060 Ti", "hardware names are shortened for the card header");
Check(new DiskSnapshot { UsedGb = 25, TotalGb = 100 }.Usage == 25, "disk usage is a percentage of capacity");
string coverPath = Environment.GetEnvironmentVariable("MYCANVAS_TEST_COVER");
using Bitmap cover = File.Exists(coverPath) ? new Bitmap(coverPath) : null;
MusicSnapshot music = new() { Available = true, SongId = "test-song", Title = "Sugar", Artist = "Maroon 5", Album = "V", CurrentLyric = "Little love and little sympathy", NextLyric = "Hey, girl, you gotta show me", CurrentTranslation = "一点点爱 一点点怜惜", LyricProgress = 0.3, LyricLineSeconds = 4, Progress = 38, ElapsedSeconds = 84, DurationSeconds = 235, Playing = true, Cover = cover };
WeatherSnapshot weather = new() { Temperature = 18, High = 22, Low = 14, Humidity = 68, Code = 2 };
DualLayoutRenderer renderer = new(config);
using (Bitmap progressFrame = renderer.RenderSquare("music", metrics, music))
{
    int y = (int)DualLayoutRenderer.MusicProgressY + 3;
    Color played = progressFrame.GetPixel(60, y), remaining = progressFrame.GetPixel(400, y);
    Check(played.R + played.G + played.B + 120 < remaining.R + remaining.G + remaining.B, "music progress uses percentage units without overflowing");
}
Check(DualLayoutRenderer.Tokenize("Little love  and 小情歌").Select(t => t.Text).SequenceEqual(new[] { "Little", "love", "and", "小", "情", "歌" }), "lyric tokens split latin words and CJK characters");
Check(WeatherService.ParseLocation("31.23, 121.47") is (31.23, 121.47) && WeatherService.ParseLocation("abc") == null, "weather location override parses lat,lon");
Check(WeatherService.Describe(2, false) == "多云" && WeatherService.Describe(95, true) == "Thunderstorm", "weather codes map to readable conditions");
foreach (string mode in new[] { "hardware", "music", "clock" })
{
    using Bitmap square = renderer.RenderSquare(mode, metrics, music, weather, wallpaper);
    Check(square.Width == 480 && square.Height == 480, "square layout " + mode);
    using Bitmap longFrame = renderer.RenderLong(metrics, music, mode, wallpaper);
    Check(longFrame.Width == 1920 && longFrame.Height == 462, "long layout " + mode);
    if (args.Length > 0)
    {
        Directory.CreateDirectory(args[0]);
        square.Save(Path.Combine(args[0], "square-" + mode + ".png"));
        longFrame.Save(Path.Combine(args[0], "long-" + mode + ".png"));
    }
}
using (DualLayoutRenderer plain = new(config))
{
    MusicSnapshot chinese = new() { Available = true, SongId = "cjk", Title = "晴天", Artist = "周杰伦", CurrentLyric = "刮风这天 我试过握着你手", LyricProgress = 0.5, Progress = 20, Playing = true };
    using Bitmap fallbackLong = plain.RenderLong(new MetricsSnapshot { CpuUsage = 12, MemoryUsage = 40 }, null, "hardware");
    using Bitmap fallbackSquare = plain.RenderSquare("clock", null, null);
    using Bitmap chineseLong = plain.RenderLong(metrics, chinese, "music");
    MusicSnapshot instrumental = new() { Available = true, SongId = "no-lyrics", Title = "Champagne", Artist = "Cavetown", Album = "Lemon Boy", CurrentLyric = "♪  Champagne", Progress = 10, Playing = true };
    using Bitmap instrumentalLong = plain.RenderLong(metrics, instrumental, "music");
    if (args.Length > 0) instrumentalLong.Save(Path.Combine(args[0], "long-music-nolyrics.png"));
    MusicSnapshot nextLine = new() { Available = true, SongId = "no-lyrics", Title = "Champagne", Artist = "Cavetown", CurrentLyric = "Wish I could be anywhere but here", LyricLineSeconds = 4, Progress = 11, Playing = true };
    Thread.Sleep(30);
    plain.RenderLong(metrics, nextLine, "music").Dispose();
    Thread.Sleep(160);
    using Bitmap transitionLong = plain.RenderLong(metrics, nextLine, "music");
    if (args.Length > 0) transitionLong.Save(Path.Combine(args[0], "long-music-transition.png"));
    MusicSnapshot lineA = new() { Available = true, SongId = "scroll", Title = "Sugar", Artist = "Maroon 5", CurrentLyric = "I'm hurting, baby, I'm broken down", NextLyric = "I need your loving, loving, I need it now", LyricProgress = 0.95, LyricLineSeconds = 4, Playing = true, Cover = cover };
    MusicSnapshot lineB = new() { Available = true, SongId = "scroll", Title = "Sugar", Artist = "Maroon 5", CurrentLyric = "I need your loving, loving, I need it now", NextLyric = "When I'm without you", LyricProgress = 0.02, LyricLineSeconds = 4, Playing = true, Cover = cover };
    plain.RenderLong(metrics, lineA, "music").Dispose();
    plain.RenderLong(metrics, lineB, "music").Dispose();
    Thread.Sleep(170);
    using Bitmap scrollLong = plain.RenderLong(metrics, lineB, "music");
    Check(scrollLong.Height == 462, "lyric scroll frame renders mid-transition");
    if (args.Length > 0) scrollLong.Save(Path.Combine(args[0], "long-music-scroll.png"));
    Check(fallbackLong.Width == 1920 && fallbackSquare.Width == 480 && chineseLong.Height == 462, "screens render without cover, metrics or weather");
    if (args.Length > 0)
    {
        fallbackLong.Save(Path.Combine(args[0], "fallback-long-hardware.png"));
        fallbackSquare.Save(Path.Combine(args[0], "fallback-square-clock.png"));
        chineseLong.Save(Path.Combine(args[0], "long-music-cjk.png"));
    }
}
string secondCoverPath = coverPath == null ? null : Path.Combine(Path.GetDirectoryName(coverPath)!, "real1.jpg");
using Bitmap secondCover = File.Exists(secondCoverPath) ? new Bitmap(secondCoverPath) : null;
foreach (string style in AppConfig.LyricStyles)
{
    config.LyricStyle = style;
    using DualLayoutRenderer styled = new(config);
    MusicSnapshot steady = new() { Available = true, SongId = "steady", Title = "Sugar", Artist = "Maroon 5", CurrentLyric = "I need your loving, loving, I need it now", NextLyric = "When I'm without you", LyricProgress = 0.4, LyricLineSeconds = 4, Playing = true, Cover = cover };
    MusicSnapshot before = new() { Available = true, SongId = "steady", Title = "Sugar", Artist = "Maroon 5", CurrentLyric = "I'm hurting, baby, I'm broken down", NextLyric = steady.CurrentLyric, LyricProgress = 0.9, LyricLineSeconds = 4, Playing = true, Cover = cover };
    styled.RenderLong(metrics, before, "music").Dispose();
    styled.RenderLong(metrics, steady, "music").Dispose();
    Thread.Sleep(180);
    using Bitmap mid = styled.RenderLong(metrics, steady, "music");
    Thread.Sleep(450);
    using Bitmap settled = styled.RenderLong(metrics, steady, "music");
    MusicSnapshot translated = new() { Available = true, SongId = "translated", Title = "Sugar", Artist = "Maroon 5", CurrentLyric = "Little love and little sympathy", CurrentTranslation = "一点点爱 一点点怜惜", LyricProgress = 0.3, LyricLineSeconds = 4, Playing = true, Cover = cover };
    styled.RenderLong(metrics, translated, "music").Dispose();
    Thread.Sleep(650);
    using Bitmap withTranslation = styled.RenderLong(metrics, translated, "music");
    MusicSnapshot cjk = new() { Available = true, SongId = "cjk-styled", Title = "晴天", Artist = "周杰伦", CurrentLyric = "刮风这天 我试过握着你手", NextLyric = "但偏偏雨渐渐 大到我看你不见", LyricProgress = 0.5, LyricLineSeconds = 4, Playing = true, Cover = secondCover ?? cover,
        AllLyrics = new[] { "故事的小黄花", "从出生那年就飘着", "刮风这天 我试过握着你手", "但偏偏雨渐渐 大到我看你不见", "还要多久 我才能在你身边", "等到放晴的那天" },
        LyricIndex = 2,
        CurrentWords = new[]
        {
            new LyricWord { Text = "刮风", Start = 0, Duration = 0.6 },
            new LyricWord { Text = "这天 ", Start = 0.6, Duration = 0.7 },
            new LyricWord { Text = "我试过", Start = 1.5, Duration = 0.9 },
            new LyricWord { Text = "握着", Start = 2.4, Duration = 0.6 },
            new LyricWord { Text = "你手", Start = 3.0, Duration = 0.8 },
        } };
    styled.RenderLong(metrics, cjk, "music").Dispose();
    Thread.Sleep(650);
    var timer = System.Diagnostics.Stopwatch.StartNew();
    for (int frame = 0; frame < 5; frame++) styled.RenderLong(metrics, cjk, "music").Dispose();
    Console.WriteLine($"  {style}: {timer.Elapsed.TotalMilliseconds / 5:0.0} ms per long frame");
    using Bitmap chineseStyled = styled.RenderLong(metrics, cjk, "music");
    Check(settled.Width == 1920 && mid.Height == 462, "lyric style " + style + " renders");
    if (args.Length > 0)
    {
        settled.Save(Path.Combine(args[0], "lyrics-" + style + ".png"));
        mid.Save(Path.Combine(args[0], "lyrics-" + style + "-scroll.png"));
        withTranslation.Save(Path.Combine(args[0], "lyrics-" + style + "-translation.png"));
        chineseStyled.Save(Path.Combine(args[0], "lyrics-" + style + "-cjk.png"));
    }
}
using (DualLayoutRenderer switching = new(config))
{
    MusicSnapshot song = new() { Available = true, SongId = "switch", Title = "Sugar", CurrentLyric = "I need your loving", LyricProgress = 0.4, LyricLineSeconds = 4, Playing = true, Cover = cover };
    foreach (string style in new[] { "apple", "bigtype", "unknown", "spotify" })
    {
        config.LyricStyle = style;
        switching.RenderLong(metrics, song, "music").Dispose();
    }
    Check(true, "lyric style switches on a live renderer");
}
config.LyricStyle = "apple";
if (System.IO.Ports.SerialPort.GetPortNames().Length == 0)
    Console.WriteLine("SKIP: long screen is not attached, its COM port cannot be resolved");
else
    Check(SerialJpegDisplay.FindPort() != null, "verified USB serial device resolves without a hardcoded COM number");
using (NeteaseCdpBridge bridge = new(1))
{
    bridge.Dispose(); // Event decoding is independent of a live player.
    var handle = typeof(NeteaseCdpBridge).GetMethod("HandlePlaybackPayload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    void Feed(string payload) => handle.Invoke(bridge, new object[] { payload });
    Feed("{\"eventName\":\"progress\",\"args\":[\"3395189970_RANDOM\",42.5,1]}");
    var playback = bridge.GetSnapshot();
    Check(playback.SongId == "3395189970" && playback.HasPosition && playback.Playing && playback.PositionSeconds >= 42.5, "native progress identifies song and playback time");
    Feed("{\"eventName\":\"state\",\"args\":[\"3395189970_RANDOM\",\"opaque-operation-id\",2]}");
    Check(!bridge.GetSnapshot().Playing, "NetEase 3.x pause reads third state argument");
    Feed("{\"eventName\":\"state\",\"args\":[\"3395189970_RANDOM\",\"opaque-operation-id\",1]}");
    Check(bridge.GetSnapshot().Playing, "NetEase 3.x resume reads third state argument");
    Feed("{\"eventName\":\"state\",\"args\":[\"3395189970_RANDOM\",\"pause\"]}");
    Check(!bridge.GetSnapshot().Playing, "legacy two-argument pause remains supported");
    Feed("{\"eventName\":\"load\",\"args\":[\"123_NEW\",{\"duration\":180000}]}");
    playback = bridge.GetSnapshot();
    Check(playback.SongId == "123" && playback.DurationSeconds == 180 && playback.PositionSeconds == 0, "song change resets progress and normalizes duration");
}
(Color Color, double Luminance) AmbientOf(Color fill)
{
    using Bitmap solid = new(32, 32);
    using (Graphics g = Graphics.FromImage(solid)) g.Clear(fill);
    return DominantColor.Ambient(solid);
}
var night = AmbientOf(Color.FromArgb(0x1C, 0x26, 0x40));
Check(night.Color.B == 255 && night.Color.R < night.Color.B && night.Luminance < 0.2, "night-blue wallpaper gives a dim blue light");
var snow = AmbientOf(Color.FromArgb(0xEE, 0xF0, 0xF2));
Check(snow.Color.R > 230 && snow.Color.G > 230 && snow.Luminance > 0.85, "white wallpaper gives a bright white light");
double Contrast(Color a, Color b)
{
    double x = SystemAccent.RelativeLuminance(a), y = SystemAccent.RelativeLuminance(b);
    return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
}
Color appSurface = Color.FromArgb(0x0D, 0x18, 0x23);
foreach (Color led in new[] { Color.FromArgb(0x00, 0x41, 0xFF), Color.FromArgb(0x1C, 0x1C, 0xFF), Color.FromArgb(0xFF, 0xE0, 0x30),
             Color.FromArgb(0x30, 0xFF, 0x60), Color.FromArgb(0xFF, 0x50, 0x20), Color.FromArgb(0xF0, 0xF2, 0xF5) })
{
    static double Chroma(Color c) { int max = Math.Max(c.R, Math.Max(c.G, c.B)); return max == 0 ? 0 : (max - Math.Min(c.R, Math.Min(c.G, c.B))) / (double)max; }
    Color dark = SystemAccent.ForUi(led, SystemAccent.SystemToneFor(0.1));
    Color pale = SystemAccent.ForUi(led, SystemAccent.SystemToneFor(0.6));
    Color app = SystemAccent.ForUi(led, SystemAccent.AppTone);
    bool hueKept = Chroma(led) < 0.1 || Math.Abs(pale.GetHue() - led.GetHue()) is < 8 or > 352;
    Check(Contrast(Color.White, dark) >= 10.5 && Contrast(Color.Black, pale) >= 10 && Chroma(dark) <= 0.46 && Chroma(pale) <= 0.32 &&
          hueKept && Contrast(app, appSurface) >= 6,
        $"light #{led.R:X2}{led.G:X2}{led.B:X2} gives a muted accent: dark photo #{dark.R:X2}{dark.G:X2}{dark.B:X2}, bright photo #{pale.R:X2}{pale.G:X2}{pale.B:X2}, highlight #{app.R:X2}{app.G:X2}{app.B:X2}");
}
string wallpapers = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\app\data\wallpaper-wallhaven"));
if (args.Length > 0 && Directory.Exists(wallpapers))
{
    string[] photos = Directory.GetFiles(wallpapers, "*.jpg").OrderByDescending(File.GetLastWriteTime).Take(8).ToArray();
    using Bitmap sheet = new(1000, 100 * photos.Length);
    using Graphics sg = Graphics.FromImage(sheet);
    sg.Clear(Color.Black);
    using Font label = new("Segoe UI", 11);
    for (int i = 0; i < photos.Length; i++)
    {
        using Bitmap photo = new(photos[i]);
        var (tone, luminance) = DominantColor.Ambient(photo);
        double level = Math.Clamp(0.3 + 1.2 * luminance, 0.3, 1.0);
        sg.DrawImage(photo, new Rectangle(0, i * 100, 160, 90));
        using SolidBrush led = new(Color.FromArgb((int)(tone.R * level), (int)(tone.G * level), (int)(tone.B * level)));
        sg.FillRectangle(led, 170, i * 100, 140, 90);
        sg.DrawString($"#{tone.R:X2}{tone.G:X2}{tone.B:X2}  lum {luminance:0.00}  level {level:0.00}", label, Brushes.White, 320, i * 100 + 34);
        Color system = SystemAccent.ForUi(tone, SystemAccent.SystemToneFor(luminance)), app = SystemAccent.ForUi(tone, SystemAccent.AppTone);
        using (SolidBrush accentFill = new(system)) sg.FillRectangle(accentFill, 570, i * 100, 200, 90);
        sg.DrawString($"Windows #{system.R:X2}{system.G:X2}{system.B:X2}", label,
            SystemAccent.RelativeLuminance(system) > 0.3 ? Brushes.Black : Brushes.White, 580, i * 100 + 34);
        using (SolidBrush surface = new(appSurface)) sg.FillRectangle(surface, 780, i * 100, 220, 90);
        using (SolidBrush text = new(app)) sg.DrawString($"高亮 #{app.R:X2}{app.G:X2}{app.B:X2}", label, text, 790, i * 100 + 34);
    }
    sheet.Save(Path.Combine(args[0], "wallpaper-light.png"), ImageFormat.Png);
}
Console.WriteLine($"{passed} checks passed.");
