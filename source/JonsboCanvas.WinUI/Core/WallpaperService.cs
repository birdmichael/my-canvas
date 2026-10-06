using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

internal sealed class WallpaperSnapshot
{
    private readonly object _sync = new();
    private Bitmap? _image;
    private IReadOnlyList<Bitmap>? _companions;

    public required string Id { get; init; }
    public string Title { get; init; } = "";
    // The main photo's file; empty for snapshots not loaded from disk.
    public string SourcePath { get; init; } = "";
    // Files of the further photos for the long screen's paper and black panels.
    public IReadOnlyList<string> CompanionPaths { get; init; } = Array.Empty<string>();

    // Decoded photos are freed once the screens have read them (DisposeImages)
    // and decoded again from SourcePath and CompanionPaths if needed later.
    public Bitmap Image
    {
        get { lock (_sync) return _image ??= WallpaperService.LoadImage(SourcePath); }
        init => _image = value;
    }

    public IReadOnlyList<Bitmap> Companions
    {
        get { lock (_sync) return _companions ??= CompanionPaths.Select(WallpaperService.LoadImage).ToList(); }
        init => _companions = value;
    }

    public int CompanionCount => Math.Max(CompanionPaths.Count, _companions?.Count ?? 0);

    public void DisposeImages()
    {
        lock (_sync)
        {
            _image?.Dispose();
            _image = null;
            foreach (Bitmap companion in _companions ?? Array.Empty<Bitmap>()) companion.Dispose();
            _companions = null;
        }
    }
}

// Photo for the hardware screens: Bing's image of the day, an hourly scenery
// pick from Wallhaven, or a custom file. Downloads are cached on disk so a
// photo is available immediately after start-up and while offline.
internal sealed class WallpaperService : IDisposable
{
    private static readonly TimeSpan BingCheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    // A render copies the snapshot within milliseconds of it changing.
    private static readonly TimeSpan DisposeDelay = TimeSpan.FromSeconds(30);
    private const string BingHost = "https://cn.bing.com";
    private const int CachedWallhavenImages = 48;
    private const int StoredWidth = 1920;
    private static readonly string[] WallhavenQueries = { "landscape", "mountains", "space", "forest", "lake", "night sky" };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _bingDirectory;
    private readonly string _wallhavenDirectory;
    private readonly Random _random = new();
    private DateTime _nextCheckUtc = DateTime.MinValue;
    private bool _forceNext;
    private int _busy;
    private int _generation;
    private string _source = "bing";
    private volatile WallpaperSnapshot? _current;
    private volatile WallpaperSnapshot? _custom;

    public WallpaperService(string dataDirectory)
    {
        _bingDirectory = Path.Combine(dataDirectory, "wallpaper-en");
        _wallhavenDirectory = Path.Combine(dataDirectory, "wallpaper-wallhaven");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 MyCanvas/1.2");
    }

    public WallpaperSnapshot? Current => _custom ?? _current;

    // "bing" or "wallhaven"; custom images are layered on top with UseCustom.
    public void SetSource(string source)
    {
        source = source == "wallhaven" ? "wallhaven" : "bing";
        if (source == _source) return;
        _source = source;
        Interlocked.Increment(ref _generation);
        Replace(ref _current, null);
        _nextCheckUtc = DateTime.MinValue;
    }

    // A null or empty path clears the custom image. Returns false when the file could not be loaded.
    public bool UseCustom(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Replace(ref _custom, null);
            return true;
        }
        WallpaperSnapshot? loaded = Load(path, "custom:" + path + ":" + File.GetLastWriteTimeUtc(path).Ticks, withTitle: false);
        if (loaded == null) return false;
        Replace(ref _custom, loaded);
        return true;
    }

    public bool Busy => Volatile.Read(ref _busy) == 1;

    // Fetches a different photo now: a new Wallhaven pick, or another recent
    // Bing day (kept until tomorrow's check). False for custom images or while busy.
    public bool Next()
    {
        if (_custom != null || Busy) return false;
        _forceNext = true;
        _nextCheckUtc = DateTime.MinValue;
        Refresh();
        return true;
    }

    // Cached photos of the current source, newest first.
    public IReadOnlyList<string> History(int count)
    {
        string directory = _source == "wallhaven" ? _wallhavenDirectory : _bingDirectory;
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        return Directory.EnumerateFiles(directory, "*.jpg").OrderByDescending(File.GetLastWriteTimeUtc).Take(count).ToList();
    }

    // Shows a cached photo; it becomes the newest so it is also the one restored at start-up.
    public void Select(string path)
    {
        if (_custom != null || !File.Exists(path)) return;
        bool bing = _source != "wallhaven";
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }
        Publish(_generation, LoadWithCompanions(path, bing ? _bingDirectory : _wallhavenDirectory, bing));
        if (bing) _nextCheckUtc = DateTime.Today.AddDays(1).ToUniversalTime();
    }

    // The full-resolution copy kept for the desktop, else the stored photo.
    public static string DesktopPath(WallpaperSnapshot snapshot)
    {
        string? directory = Path.GetDirectoryName(snapshot.SourcePath);
        if (string.IsNullOrEmpty(directory)) return snapshot.SourcePath;
        string full = Path.Combine(directory, DesktopFolder);
        if (!Directory.Exists(full)) return snapshot.SourcePath;
        string name = Path.GetFileNameWithoutExtension(snapshot.SourcePath);
        return Directory.EnumerateFiles(full, name + ".*").FirstOrDefault() ?? snapshot.SourcePath;
    }

    private const string DesktopFolder = "desktop";
    private readonly HashSet<string> _desktopFetches = new(StringComparer.OrdinalIgnoreCase);

    // Wallhaven photos cached before full-resolution copies were kept get one on demand.
    public void FetchDesktopCopy(WallpaperSnapshot snapshot)
    {
        string? directory = Path.GetDirectoryName(snapshot.SourcePath);
        string name = Path.GetFileNameWithoutExtension(snapshot.SourcePath);
        if (!string.Equals(directory, _wallhavenDirectory, StringComparison.OrdinalIgnoreCase) || !_desktopFetches.Add(name)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(await _http.GetStringAsync("https://wallhaven.cc/api/v1/w/" + Uri.EscapeDataString(name)));
                string? url = document.RootElement.GetProperty("data").GetProperty("path").GetString();
                if (url != null) await SaveDesktopCopyAsync(_wallhavenDirectory, name, url);
            }
            catch (Exception exception)
            {
                Log.Write("Desktop wallpaper copy failed (" + name + "): " + exception.Message);
            }
        });
    }

    public void Refresh()
    {
        if (_custom != null || DateTime.UtcNow < _nextCheckUtc || Interlocked.Exchange(ref _busy, 1) == 1) return;
        int generation = _generation;
        string source = _source;
        bool next = _forceNext;
        _forceNext = false;
        _ = Task.Run(async () =>
        {
            try
            {
                if (source == "wallhaven")
                {
                    Directory.CreateDirectory(_wallhavenDirectory);
                    if (_current == null) Publish(generation, LoadNewestCached(_wallhavenDirectory));
                    await RotateWallhavenAsync(generation);
                    _nextCheckUtc = NextHourUtc();
                }
                else if (next)
                {
                    Directory.CreateDirectory(_bingDirectory);
                    await DownloadBingAsync(generation, randomDay: true);
                    _nextCheckUtc = DateTime.Today.AddDays(1).ToUniversalTime();
                }
                else
                {
                    Directory.CreateDirectory(_bingDirectory);
                    if (_current == null) Publish(generation, LoadNewestCached(_bingDirectory));
                    await DownloadBingAsync(generation, randomDay: false);
                    _nextCheckUtc = DateTime.UtcNow + BingCheckInterval;
                }
            }
            catch (Exception exception)
            {
                Log.Write("Wallpaper refresh failed (" + source + "): " + exception.Message);
                _nextCheckUtc = DateTime.UtcNow + RetryInterval;
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }

    private static DateTime NextHourUtc()
    {
        DateTime now = DateTime.Now;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Local).AddHours(1).ToUniversalTime();
    }

    private void Publish(int generation, WallpaperSnapshot? snapshot)
    {
        if (snapshot == null) return;
        if (generation != _generation) { snapshot.DisposeImages(); return; }
        Replace(ref _current, snapshot);
    }

    private static void Replace(ref WallpaperSnapshot? slot, WallpaperSnapshot? next)
    {
        WallpaperSnapshot? previous = Interlocked.Exchange(ref slot, next);
        if (previous == null || ReferenceEquals(previous, next)) return;
        _ = Task.Delay(DisposeDelay).ContinueWith(_ => previous.DisposeImages(), TaskScheduler.Default);
    }

    // Main photo plus two other cached photos from the same folder: the newest
    // ones for Bing (the previous days), a random pick for Wallhaven.
    private WallpaperSnapshot? LoadWithCompanions(string mainPath, string directory, bool bing)
    {
        WallpaperSnapshot? main = Load(mainPath, IdFor(mainPath, bing), withTitle: bing);
        if (main == null) return null;
        IEnumerable<string> others = Directory.EnumerateFiles(directory, "*.jpg")
            .Where(path => !string.Equals(path, mainPath, StringComparison.OrdinalIgnoreCase));
        others = bing ? others.OrderByDescending(Path.GetFileName, StringComparer.Ordinal) : others.OrderBy(_ => _random.Next());
        List<Bitmap> companions = new();
        List<string> companionPaths = new();
        List<string> ids = new() { main.Id };
        foreach (string path in others)
        {
            if (companions.Count == 2) break;
            WallpaperSnapshot? companion = Load(path, "", withTitle: false);
            if (companion == null) continue;
            companions.Add(companion.Image);
            companionPaths.Add(path);
            ids.Add(Path.GetFileNameWithoutExtension(path));
        }
        return new WallpaperSnapshot
        {
            Image = main.Image, Id = string.Join('|', ids), Title = main.Title, SourcePath = mainPath,
            Companions = companions, CompanionPaths = companionPaths,
        };
    }

    private static string IdFor(string path, bool bing)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return bing ? name : "wallhaven:" + name;
    }

    // ---------- Bing ----------

    private async Task DownloadBingAsync(int generation, bool randomDay)
    {
        // Today's image is the main photo; the two days before fill the other panels.
        // "Next" instead takes another of the last eight days.
        using JsonDocument document = JsonDocument.Parse(await _http.GetStringAsync(
            BingHost + "/HPImageArchive.aspx?format=js&idx=0&n=" + (randomDay ? 8 : 3) + "&mkt=en-US&ensearch=1"));
        List<JsonElement> images = document.RootElement.GetProperty("images").EnumerateArray().ToList();
        if (images.Count == 0) throw new InvalidDataException("no Bing images");
        if (randomDay)
        {
            string? shown = _current?.Id.Split('|')[0];
            List<JsonElement> others = images.Where(image => image.GetProperty("startdate").GetString() != shown).ToList();
            images = new() { others.Count > 0 ? others[_random.Next(others.Count)] : images[0] };
        }
        string? mainPath = null;
        bool downloaded = false;
        foreach (JsonElement image in images)
        {
            string date = image.GetProperty("startdate").GetString() ?? DateTime.Now.ToString("yyyyMMdd");
            string path = Path.Combine(_bingDirectory, date + ".jpg");
            mainPath ??= path;
            if (File.Exists(path)) continue;
            string title = image.TryGetProperty("title", out JsonElement t) ? t.GetString() ?? "" : "";
            string urlBase = image.GetProperty("urlbase").GetString() ?? throw new InvalidDataException("missing urlbase");
            byte[] data = await _http.GetByteArrayAsync(BingHost + urlBase + "_1920x1080.jpg");
            await File.WriteAllBytesAsync(path, data);
            await File.WriteAllTextAsync(Path.ChangeExtension(path, ".txt"), title);
            await SaveDesktopCopyAsync(_bingDirectory, date, BingHost + urlBase + "_UHD.jpg");
            Log.Write("Wallpaper downloaded: " + date + " " + title);
            downloaded = true;
        }
        if (mainPath == null) throw new InvalidDataException("no Bing images");
        if (randomDay) File.SetLastWriteTimeUtc(mainPath, DateTime.UtcNow);
        Prune(_bingDirectory, randomDay ? 4 : 3);
        if (!downloaded && _current?.Id.Split('|')[0] == Path.GetFileNameWithoutExtension(mainPath) && _current.CompanionCount == 2) return;
        Publish(generation, LoadWithCompanions(mainPath, _bingDirectory, bing: true));
    }

    // The desktop gets the photo at full resolution; failing that it falls back to the stored one.
    private async Task SaveDesktopCopyAsync(string directory, string name, string url, byte[]? data = null)
    {
        try
        {
            data ??= await _http.GetByteArrayAsync(url);
            string full = Path.Combine(directory, DesktopFolder);
            Directory.CreateDirectory(full);
            string extension = Path.GetExtension(new Uri(url).AbsolutePath);
            await File.WriteAllBytesAsync(Path.Combine(full, name + (string.IsNullOrEmpty(extension) ? ".jpg" : extension)), data);
        }
        catch (Exception exception)
        {
            Log.Write("Desktop wallpaper copy failed: " + exception.Message);
        }
    }

    // ---------- Wallhaven ----------

    private sealed record WallhavenImage(string Id, string Url, double Luminance);

    private async Task RotateWallhavenAsync(int generation)
    {
        HashSet<string> recent = Directory.EnumerateFiles(_wallhavenDirectory, "*.jpg")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet();
        string query = WallhavenQueries[(int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerHour % WallhavenQueries.Length)];
        List<WallhavenImage> candidates = await SearchWallhavenAsync(query, _random.Next(1, 4));
        candidates = candidates.Where(c => !recent.Contains(c.Id)).ToList();
        // Darker pictures keep the white figures on the screens readable.
        List<WallhavenImage> dark = candidates.Where(c => c.Luminance <= 0.42).ToList();
        List<WallhavenImage> pool = dark.Count > 0 ? dark : candidates;
        if (pool.Count == 0) throw new InvalidDataException("no new Wallhaven results for " + query);

        foreach (WallhavenImage pick in pool.OrderBy(_ => _random.Next()).Take(3))
        {
            string path = Path.Combine(_wallhavenDirectory, pick.Id + ".jpg");
            byte[] data = await _http.GetByteArrayAsync(pick.Url);
            if (!SaveScaled(data, path))
            {
                Log.Write("Wallpaper skipped (too bright): wallhaven " + pick.Id);
                continue;
            }
            await SaveDesktopCopyAsync(_wallhavenDirectory, pick.Id, pick.Url, data);
            Log.Write("Wallpaper downloaded: wallhaven " + pick.Id + " (" + query + ")");
            Prune(_wallhavenDirectory, CachedWallhavenImages);
            Publish(generation, LoadWithCompanions(path, _wallhavenDirectory, bing: false));
            return;
        }
        throw new InvalidDataException("Wallhaven results were all too bright for " + query);
    }

    private async Task<List<WallhavenImage>> SearchWallhavenAsync(string query, int page)
    {
        string url = "https://wallhaven.cc/api/v1/search?q=" + Uri.EscapeDataString(query) +
            "&categories=100&purity=100&sorting=toplist&topRange=1y&atleast=1920x1080&ratios=16x9,16x10,21x9&page=" +
            page.ToString(CultureInfo.InvariantCulture);
        using JsonDocument document = JsonDocument.Parse(await _http.GetStringAsync(url));
        List<WallhavenImage> results = new();
        foreach (JsonElement item in document.RootElement.GetProperty("data").EnumerateArray())
        {
            string? id = item.GetProperty("id").GetString();
            string? path = item.GetProperty("path").GetString();
            long size = item.TryGetProperty("file_size", out JsonElement s) ? s.GetInt64() : 0;
            if (id == null || path == null || size > 12_000_000) continue;
            double[] colours = item.TryGetProperty("colors", out JsonElement c)
                ? c.EnumerateArray().Select(e => Luminance(e.GetString())).Take(3).ToArray()
                : Array.Empty<double>();
            results.Add(new WallhavenImage(id, path, colours.Length > 0 ? colours.Average() : 0.5));
        }
        return results;
    }

    internal static double Luminance(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return 0.5;
        int value = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (0.2126 * (value >> 16 & 0xFF) + 0.7152 * (value >> 8 & 0xFF) + 0.0722 * (value & 0xFF)) / 255;
    }

    // Stores the image at screen resolution; returns false if it is too bright overall.
    private static bool SaveScaled(byte[] data, string path)
    {
        using MemoryStream stream = new(data);
        using Image source = Image.FromStream(stream);
        int width = Math.Min(StoredWidth, source.Width);
        int height = (int)Math.Round(source.Height * (width / (double)source.Width));
        using Bitmap scaled = new(width, height, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, width, height);
        }
        if (MeanLuminance(scaled) > 0.55) return false;
        ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().Single(e => e.MimeType == "image/jpeg");
        using EncoderParameters parameters = new(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
        scaled.Save(path, codec, parameters);
        return true;
    }

    private static double MeanLuminance(Bitmap image)
    {
        using Bitmap small = new(32, 18, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(image, 0, 0, 32, 18);
        }
        double total = 0;
        for (int y = 0; y < small.Height; y++)
            for (int x = 0; x < small.Width; x++)
            {
                Color c = small.GetPixel(x, y);
                total += (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
            }
        return total / (small.Width * small.Height);
    }

    // ---------- cache ----------

    private WallpaperSnapshot? LoadNewestCached(string directory)
    {
        string? newest = Directory.EnumerateFiles(directory, "*.jpg").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (newest == null) return null;
        return LoadWithCompanions(newest, directory, bing: directory.EndsWith("wallpaper-en", StringComparison.Ordinal));
    }

    private static WallpaperSnapshot? Load(string path, string id, bool withTitle)
    {
        try
        {
            using Image image = Image.FromFile(path);
            string titlePath = Path.ChangeExtension(path, ".txt");
            return new WallpaperSnapshot
            {
                Image = ScreenSized(image),
                Id = id,
                SourcePath = path,
                Title = withTitle && File.Exists(titlePath) ? File.ReadAllText(titlePath) : "",
            };
        }
        catch (Exception exception)
        {
            Log.Write("Wallpaper load failed: " + exception.Message);
            return null;
        }
    }

    internal static Bitmap LoadImage(string path)
    {
        try
        {
            using Image image = Image.FromFile(path);
            return ScreenSized(image);
        }
        catch (Exception exception)
        {
            // The cache may have been pruned since the snapshot was made.
            Log.Write("Wallpaper reload failed: " + exception.Message);
            Bitmap blank = new(StoredWidth, StoredWidth * 9 / 16, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(blank);
            g.Clear(Color.Black);
            return blank;
        }
    }

    // Custom pictures can be camera-sized; the screens never need more than the stored width.
    private static Bitmap ScreenSized(Image image)
    {
        if (image.Width <= StoredWidth) return new Bitmap(image);
        int height = (int)Math.Round(image.Height * (StoredWidth / (double)image.Width));
        Bitmap scaled = new(StoredWidth, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(image, 0, 0, StoredWidth, height);
        return scaled;
    }

    private static void Prune(string directory, int keep)
    {
        foreach (string old in Directory.EnumerateFiles(directory, "*.jpg").OrderByDescending(File.GetLastWriteTimeUtc).Skip(keep))
        {
            try { File.Delete(old); File.Delete(Path.ChangeExtension(old, ".txt")); }
            catch { }
        }
        string full = Path.Combine(directory, DesktopFolder);
        if (!Directory.Exists(full)) return;
        foreach (string copy in Directory.EnumerateFiles(full))
        {
            if (File.Exists(Path.Combine(directory, Path.GetFileNameWithoutExtension(copy) + ".jpg"))) continue;
            try { File.Delete(copy); } catch { }
        }
    }

    public void Dispose() => _http.Dispose();
}
