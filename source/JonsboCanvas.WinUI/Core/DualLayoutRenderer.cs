using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

// Renderer for the two passive cooler displays. Music screens are composed over
// the album art, hardware screens over the daily wallpaper (both fall back to a
// generated scene). Image layers are rebuilt only when their source changes;
// frames only add text on top.
internal sealed partial class DualLayoutRenderer : IDisposable
{
    private const int LongWidth = 1920;
    private const int LongHeight = 462;
    private const int SquareSize = 480;
    private const string NumberFamily = "Impact";
    private const string LabelFamily = "Bahnschrift SemiBold";
    private const string LatinLyricFamily = "Segoe UI Black";
    private const string CjkFamily = "Microsoft YaHei UI";

    private static readonly Color Ink = ColorTranslator.FromHtml("#141821");
    private static readonly Color Paper = ColorTranslator.FromHtml("#EEEAE2");
    private static readonly Color Night = ColorTranslator.FromHtml("#0C0E14");
    private static readonly Color Label = ColorTranslator.FromHtml("#35578D");
    private static readonly Color White = ColorTranslator.FromHtml("#F6F3EE");
    private static readonly Color Slate = ColorTranslator.FromHtml("#1C2638");
    private static readonly Color DefaultAccent = ColorTranslator.FromHtml("#F38BAF");

    private static readonly StringFormat Typographic = CreateTypographic();

    private readonly AppConfig _config;
    private Bitmap? _art;
    private string _artId = "";
    private Color _accent = DefaultAccent;
    private Bitmap? _musicSquareLayer;
    private Bitmap? _lyricLongLayer;

    public DualLayoutRenderer(AppConfig config) => _config = config;

    private bool English => AppConfig.NormalizeLanguage(_config.Language) == "en-US";
    private string T(string zh, string en) => English ? en : zh;
    internal Color Accent => _accent;

    public Bitmap RenderLong(MetricsSnapshot? metrics, MusicSnapshot? music, string mode,
        WallpaperSnapshot? wallpaper = null)
    {
        if (mode == "music")
        {
            EnsureArt(music);
            return RenderLyricLong(music);
        }
        EnsureWallpaper(wallpaper);
        return RenderHardwareLong(metrics);
    }

    public Bitmap RenderSquare(string mode, MetricsSnapshot? metrics, MusicSnapshot? music,
        WeatherSnapshot? weather = null, WallpaperSnapshot? wallpaper = null)
    {
        if (mode == "music")
        {
            EnsureArt(music);
            return RenderMusicSquare(music);
        }
        EnsureWallpaper(wallpaper);
        return RenderClockSquare(weather);
    }

    // ---------- art & cached layers ----------

    private void EnsureArt(MusicSnapshot? music)
    {
        if (music?.Cover != null && !string.IsNullOrEmpty(music.SongId) && music.SongId != _artId)
        {
            ReplaceArt(new Bitmap(music.Cover), music.SongId);
        }
        else if (_art == null)
        {
            ReplaceArt(BuildFallbackArt(), "fallback");
        }
    }

    private void ReplaceArt(Bitmap art, string id)
    {
        _art?.Dispose();
        _art = art;
        _artId = id;
        Color[] palette = id == "fallback" ? Array.Empty<Color>() : DominantColor.FromBitmapMulti(art, 4);
        _accent = palette.Length > 0 ? palette.MaxBy(c => c.GetSaturation() * (1 - Math.Abs(c.GetBrightness() - 0.55f))) : DefaultAccent;
        DisposeMusicLayers();
    }

    private void DisposeMusicLayers()
    {
        _musicSquareLayer?.Dispose(); _musicSquareLayer = null;
        _lyricLongLayer?.Dispose(); _lyricLongLayer = null;
        DisposeLyricLayers();
    }

    private static Bitmap BuildFallbackArt()
    {
        const int size = 600;
        Bitmap art = new(size, size, PixelFormat.Format32bppArgb);
        using Graphics g = Prepare(art);
        Rectangle full = new(0, 0, size, size);
        using (LinearGradientBrush sky = new(full, Color.Black, Color.Black, 90f))
        {
            sky.InterpolationColors = new ColorBlend
            {
                Colors = new[] { ColorTranslator.FromHtml("#4A6FA8"), ColorTranslator.FromHtml("#2C426E"), ColorTranslator.FromHtml("#151B2C") },
                Positions = new[] { 0f, 0.55f, 1f },
            };
            g.FillRectangle(sky, full);
        }
        using (GraphicsPath glow = new())
        {
            glow.AddEllipse(-120, 330, size + 240, 380);
            using PathGradientBrush brush = new(glow)
            {
                CenterColor = Color.FromArgb(150, 236, 72, 128),
                SurroundColors = new[] { Color.FromArgb(0, 236, 72, 128) },
            };
            g.FillPath(brush, glow);
        }
        using (Brush ridge = new SolidBrush(Color.FromArgb(150, 24, 38, 66)))
            g.FillPolygon(ridge, new PointF[] { new(0, 330), new(90, 290), new(210, 318), new(330, 270), new(470, 312), new(600, 286), new(600, 380), new(0, 380) });
        using (Brush ground = new SolidBrush(Color.FromArgb(120, 196, 54, 104)))
            g.FillRectangle(ground, 0, 470, size, 130);
        return art;
    }

    private Bitmap MusicSquareLayer()
    {
        if (_musicSquareLayer != null) return _musicSquareLayer;
        Bitmap layer = new(SquareSize, SquareSize, PixelFormat.Format32bppArgb);
        using Graphics g = Prepare(layer);
        DrawArt(g, _art!, new Rectangle(0, 0, SquareSize, SquareSize), 0.5f);
        List<PointF> panel = new(TornLine(new PointF(0, MusicPanelTop + 4), new PointF(SquareSize, MusicPanelTop), 47))
        {
            new(SquareSize, SquareSize), new(0, SquareSize),
        };
        using (Brush fill = new SolidBrush(PanelColor))
            g.FillPolygon(fill, panel.ToArray());
        return _musicSquareLayer = layer;
    }

    private Color PanelColor => Mix(_accent, Color.White, 0.52f);
    private Color HighlightColor => Mix(_accent, Color.White, 0.62f);

    // ---------- music ----------

    private const int MusicPanelTop = 296;
    internal const float MusicProgressY = 412;

    private Bitmap RenderMusicSquare(MusicSnapshot? music)
    {
        Bitmap image = new(MusicSquareLayer());
        using Graphics g = Prepare(image);
        Color ink = Color.FromArgb(28, 22, 34);
        const float left = 28, width = 424;

        string title = music?.Title ?? T("等待音乐", "Waiting for music");
        using Font titleFont = Fit(g, title, CjkFamily, FontStyle.Bold, 40, 30, width);
        DrawAt(g, Ellipsize(g, title, titleFont, width), titleFont, ink, left, 350);

        using Font artist = new(CjkFamily, 23, FontStyle.Bold, GraphicsUnit.Pixel);
        string artistText = Ellipsize(g, music?.Artist ?? "", artist, width);
        float artistEnd = DrawAt(g, artistText, artist, Color.FromArgb(230, ink), left, 386);
        string album = music?.Album ?? "";
        if (album.Length > 0 && artistEnd < left + width - 60)
        {
            using Font albumFont = new(CjkFamily, 21, FontStyle.Regular, GraphicsUnit.Pixel);
            string albumText = Ellipsize(g, "  ·  " + album, albumFont, left + width - artistEnd);
            DrawAt(g, albumText, albumFont, Color.FromArgb(165, ink), artistEnd, 386);
        }

        float progress = (float)Math.Clamp((music?.Progress ?? 0) / 100.0, 0, 1);
        const float barHeight = 6;
        using (GraphicsPath track = RoundedRect(new RectangleF(left, MusicProgressY, width, barHeight), barHeight / 2))
        using (Brush brush = new SolidBrush(Color.FromArgb(55, ink)))
            g.FillPath(brush, track);
        float filled = Math.Max(barHeight, width * progress);
        using (GraphicsPath bar = RoundedRect(new RectangleF(left, MusicProgressY, filled, barHeight), barHeight / 2))
        using (Brush brush = new SolidBrush(ink))
            g.FillPath(brush, bar);
        using (Brush knob = new SolidBrush(ink))
            g.FillEllipse(knob, left + filled - 7, MusicProgressY + barHeight / 2 - 7, 14, 14);

        using Font times = new(LabelFamily, 20, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawAt(g, FormatSeconds(music?.ElapsedSeconds ?? 0), times, ink, left, 456);
        string duration = FormatSeconds(music?.DurationSeconds ?? 0);
        DrawAt(g, duration, times, ink, left + width - Measure(g, duration, times), 456);
        DrawPlaybackGlyph(g, music?.Playing == true, ink, SquareSize / 2f, 449);
        DrawGrain(g, SquareSize, SquareSize);
        return image;
    }

    private static void DrawPlaybackGlyph(Graphics g, bool playing, Color color, float cx, float cy)
    {
        using Brush brush = new SolidBrush(color);
        if (playing)
        {
            // Equaliser bars read as "playing" without suggesting a button.
            float[] heights = { 10, 18, 13 };
            for (int i = 0; i < heights.Length; i++)
                g.FillRectangle(brush, cx - 11 + i * 8, cy + 9 - heights[i], 5, heights[i]);
        }
        else
        {
            g.FillRectangle(brush, cx - 8, cy - 9, 5, 18);
            g.FillRectangle(brush, cx + 3, cy - 9, 5, 18);
        }
    }

    // ---------- drawing helpers ----------

    private static Graphics Prepare(Bitmap bitmap)
    {
        Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        return g;
    }

    private static StringFormat CreateTypographic()
    {
        StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return format;
    }

    private static float Ascent(Font font)
        => font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);

    private static float Measure(Graphics g, string text, Font font)
        => g.MeasureString(text, font, PointF.Empty, Typographic).Width;

    // Draws text with its baseline at the given y and returns the x where it ends.
    private static float DrawAt(Graphics g, string text, Font font, Color color, float x, float baseline)
    {
        using Brush brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, baseline - Ascent(font), Typographic);
        return x + Measure(g, text, font);
    }

    // Like DrawAt with extra space between characters.
    private static float DrawTracked(Graphics g, string text, Font font, Color color, float x, float baseline, float tracking)
    {
        using Brush brush = new SolidBrush(color);
        float y = baseline - Ascent(font);
        for (int i = 0; i < text.Length; i++)
        {
            string glyph = text[i].ToString();
            g.DrawString(glyph, font, brush, x, y, Typographic);
            x += Measure(g, glyph, font) + (i < text.Length - 1 ? tracking : 0);
        }
        return x;
    }

    private static Font Fit(Graphics g, string text, string family, FontStyle style, float max, float min, float width)
    {
        for (float size = max; ; size -= 2)
        {
            Font font = new(family, size, style, GraphicsUnit.Pixel);
            if (size <= min || Measure(g, text, font) <= width) return font;
            font.Dispose();
        }
    }

    private static string Ellipsize(Graphics g, string text, Font font, float width)
    {
        if (Measure(g, text, font) <= width) return text;
        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text[..length].TrimEnd() + "…";
            if (Measure(g, candidate, font) <= width) return candidate;
        }
        return "…";
    }

    private static string Clamp(double value, double min = 0, double max = 100)
        => Math.Clamp(value, min, max).ToString("0", CultureInfo.InvariantCulture);

    private static string FormatSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "-:--";
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private static Color Mix(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    // Centre-crops the art to the destination aspect; focusY moves the crop vertically.
    private static void DrawArt(Graphics g, Bitmap art, Rectangle destination, float focusY,
        ImageAttributes? attributes = null)
    {
        float destinationRatio = destination.Width / (float)destination.Height;
        float sourceRatio = art.Width / (float)art.Height;
        float cropWidth = sourceRatio > destinationRatio ? art.Height * destinationRatio : art.Width;
        float cropHeight = sourceRatio > destinationRatio ? art.Height : art.Width / destinationRatio;
        float sourceX = (art.Width - cropWidth) / 2;
        float sourceY = (art.Height - cropHeight) * focusY;
        using ImageAttributes owned = new();
        ImageAttributes used = attributes ?? owned;
        used.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(art, destination, sourceX, sourceY, cropWidth, cropHeight, GraphicsUnit.Pixel, used);
    }

    private static readonly Lazy<Bitmap> Grain = new(() => BuildGrain(LongWidth, LongHeight + SquareSize));

    // Fine film grain, drawn over the finished frame so type and paper share
    // the same print texture.
    private static Bitmap BuildGrain(int width, int height)
    {
        Bitmap grain = new(width, height, PixelFormat.Format32bppArgb);
        Random random = new(7);
        int[] pixels = new int[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            int value = random.Next(256);
            int alpha = random.Next(14);
            pixels[i] = (alpha << 24) | (value << 16) | (value << 8) | value;
        }
        BitmapData data = grain.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
        finally { grain.UnlockBits(data); }
        return grain;
    }

    private static void DrawGrain(Graphics g, int width, int height)
    {
        Rectangle area = new(0, 0, width, height);
        g.DrawImage(Grain.Value, area, area, GraphicsUnit.Pixel);
    }

    // A jagged edge from a to b, like torn print stock.
    private static IEnumerable<PointF> TornLine(PointF a, PointF b, int seed)
    {
        Random random = new(seed);
        float length = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        int steps = Math.Max(2, (int)(length / 7));
        float nx = -(b.Y - a.Y) / length, ny = (b.X - a.X) / length;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float jitter = i == 0 || i == steps ? 0 : (float)(random.NextDouble() * 5 - 2.5)
                + (random.Next(9) == 0 ? (float)(random.NextDouble() * 8 - 4) : 0);
            yield return new PointF(a.X + (b.X - a.X) * t + nx * jitter, a.Y + (b.Y - a.Y) * t + ny * jitter);
        }
    }

    public void Dispose()
    {
        DisposeMusicLayers();
        DisposeHardwareLayers();
        _art?.Dispose();
        _art = null;
    }
}
