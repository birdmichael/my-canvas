using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

// Hardware mode as a printed poster: a paper panel (CPU), a black panel (GPU)
// and the bare photo (memory) cut from one strip with torn edges. The wallpaper
// runs under all three and bleeds into the panels' lower half through a worn
// print mask, multiplied into the paper and screened into the black. Figures
// have fixed sizes and positions and digits sit in fixed-width cells, so
// changing readings never move the layout.
internal sealed partial class DualLayoutRenderer
{
    private const string FigureFamily = "Impact";
    private const string SecondaryFamily = "Bahnschrift SemiBold";
    private const string CaptionFamily = "Bahnschrift SemiCondensed";
    private static readonly Color PosterPaper = ColorTranslator.FromHtml("#ECE8E0");
    private static readonly Color PosterInk = ColorTranslator.FromHtml("#17191E");
    private static readonly Color PosterBlack = ColorTranslator.FromHtml("#0D0F14");
    private static readonly Color PosterBlue = ColorTranslator.FromHtml("#35578D");
    private static readonly Color PosterWhite = ColorTranslator.FromHtml("#F4F1EC");
    private static readonly Color PhotoTint = ColorTranslator.FromHtml("#0C1222");

    // Torn edges run from the top point to the bottom point.
    private static readonly (PointF Top, PointF Bottom) PaperEdge = (new(616, 0), new(598, LongHeight));
    private static readonly (PointF Top, PointF Bottom) BlackEdge = (new(1318, 0), new(1298, LongHeight));
    private const float CpuX = 74, GpuX = 708, MemoryX = 1384, MemoryRight = 1872;

    private const float LabelBaseline = 72;
    private const float ModelBaseline = 106;
    private const float TemperatureBaseline = 130;
    private const float FigureBaseline = 334;
    private const float FigureSize = 236;
    private const float BottomBaseline = 424;

    private const int ClockPaperBottom = 246;

    private string _wallpaperId = "";
    private Bitmap? _hardwareLongLayer;
    private Bitmap? _clockSquareLayer;

    // The snapshot whose photos the hardware layers were built from.
    internal string WallpaperId => _wallpaperId;

    // Both layers are built as soon as the photo changes, so the full-size photos
    // are only read here and never copied or kept.
    private void EnsureWallpaper(WallpaperSnapshot? wallpaper)
    {
        if (wallpaper != null ? wallpaper.Id == _wallpaperId : _hardwareLongLayer != null) return;
        DisposeHardwareLayers();
        if (wallpaper != null)
        {
            BuildHardwareLayers(wallpaper.Image, wallpaper.Companions);
            _wallpaperId = wallpaper.Id;
        }
        else
        {
            using Bitmap art = BuildFallbackArt();
            BuildHardwareLayers(art, Array.Empty<Bitmap>());
            _wallpaperId = "fallback";
        }
    }

    private void BuildHardwareLayers(Bitmap photo, IReadOnlyList<Bitmap> panels)
    {
        // The paper and black panels get their own photos and fall back to the main one.
        Bitmap Panel(int index) => index < panels.Count ? panels[index] : photo;
        _hardwareLongLayer = BuildHardwareLongLayer(photo, Panel(0), Panel(1));
        _clockSquareLayer = BuildClockSquareLayer(photo);
    }

    private void DisposeHardwareLayers()
    {
        _hardwareLongLayer?.Dispose(); _hardwareLongLayer = null;
        _clockSquareLayer?.Dispose(); _clockSquareLayer = null;
    }

    // ---------- long screen ----------

    private static Bitmap BuildHardwareLongLayer(Bitmap mainPhoto, Bitmap paperSource, Bitmap blackSource)
    {
        const int width = LongWidth, height = LongHeight;
        // Each panel gets its own photo, cropped to the panel plus a margin under
        // the torn edges. The bleeds use the lower part of their photos.
        int[] photo = PanelPixels(mainPhoto, new Rectangle((int)BlackEdge.Bottom.X - 50, 0, width - (int)BlackEdge.Bottom.X + 50, height), 0.5f);
        int[] paperPhoto = PanelPixels(paperSource, new Rectangle(0, 0, (int)PaperEdge.Top.X + 50, height), 0.72f);
        int[] blackPhoto = PanelPixels(blackSource, new Rectangle((int)PaperEdge.Bottom.X - 50, 0, (int)(BlackEdge.Top.X - PaperEdge.Bottom.X) + 100, height), 0.72f);
        byte[] paperMask = PolygonMask(width, height, PanelOutline(PaperEdge, 11));
        byte[] blackMask = PolygonMask(width, height, PanelOutline(BlackEdge, 23));
        float[] wear = LongWear.Value;
        float[] mottle = LongMottle.Value;
        // Bright photos (snow, sky) would wash out the white text, so both the bare photo
        // and the black panel's screen bleed are dimmed by how bright that region is.
        float photoGain = Math.Clamp(0.3f / MeanLuminance(photo, width, (int)BlackEdge.Top.X, 0, width, height), 0.3f, 1f);
        float screenGain = 0.9f * Math.Clamp(0.32f / MeanLuminance(blackPhoto, width, (int)PaperEdge.Top.X, height / 2, (int)BlackEdge.Top.X, height), 0.4f, 1f);

        int[] output = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            float depth = Smooth(0.5f, 1f, y / (float)height);
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float bleed = Smooth(0.4f, 0.6f, depth * 1.1f + (wear[i] - 0.5f) * 0.7f);

                // The bare photo, darkened towards the text on its left.
                int p = photo[i];
                float pr = p >> 16 & 0xFF, pg = p >> 8 & 0xFF, pb = p & 0xFF;
                float shade = (0.62f + 0.16f * Smooth(BlackEdge.Top.X, width, x)) * photoGain;
                float r = pr * shade + PhotoTint.R * (1 - shade);
                float gr = pg * shade + PhotoTint.G * (1 - shade);
                float b = pb * shade + PhotoTint.B * (1 - shade);

                if (blackMask[i] > 0)
                {
                    p = blackPhoto[i];
                    pr = p >> 16 & 0xFF; pg = p >> 8 & 0xFF; pb = p & 0xFF;
                    if (bleed > 0) Punch(ref pr, ref pg, ref pb);
                    float m = blackMask[i] / 255f, tone = 1 + (mottle[i] - 0.5f) * 0.5f, amount = bleed * 0.78f;
                    float dr = PosterBlack.R * tone, dg = PosterBlack.G * tone, db = PosterBlack.B * tone;
                    r = Lerp(r, Lerp(dr, Screen(dr, pr * screenGain), amount), m);
                    gr = Lerp(gr, Lerp(dg, Screen(dg, pg * screenGain), amount), m);
                    b = Lerp(b, Lerp(db, Screen(db, pb * screenGain), amount), m);
                }
                if (paperMask[i] > 0)
                {
                    p = paperPhoto[i];
                    pr = p >> 16 & 0xFF; pg = p >> 8 & 0xFF; pb = p & 0xFF;
                    if (bleed > 0) Punch(ref pr, ref pg, ref pb);
                    float m = paperMask[i] / 255f, tone = 1 + (mottle[i] - 0.5f) * 0.07f, amount = bleed * 0.92f;
                    float sr = PosterPaper.R * tone, sg = PosterPaper.G * tone, sb = PosterPaper.B * tone;
                    // Lightening the photo first keeps ink on the bleed readable.
                    r = Lerp(r, Lerp(sr, sr * (pr * 0.72f + 71) / 255f, amount), m);
                    gr = Lerp(gr, Lerp(sg, sg * (pg * 0.72f + 71) / 255f, amount), m);
                    b = Lerp(b, Lerp(sb, sb * (pb * 0.72f + 71) / 255f, amount), m);
                }
                output[i] = unchecked((int)0xFF000000) | ToByte(r) << 16 | ToByte(gr) << 8 | ToByte(b);
            }
        }
        return WritePixels(output, width, height);
    }

    private Bitmap RenderHardwareLong(MetricsSnapshot? metrics)
    {
        Bitmap image = new(_hardwareLongLayer!);
        using Graphics g = Prepare(image);
        DrawProcessor(g, "CPU", ShortName(metrics?.CpuName), metrics?.CpuTemperature, metrics?.CpuUsage,
            "UPTIME", metrics == null ? "--" : FormatUptime(metrics.Uptime).ToUpperInvariant(),
            CpuX, PaperEdge.Top.X - 44, PosterInk, PosterBlue, Color.FromArgb(185, PosterInk), PosterPaper);
        string vram = metrics?.GpuMemoryTotalGb is > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{metrics.GpuMemoryUsedGb:0.0} / {metrics.GpuMemoryTotalGb:0.0} GB")
            : "--";
        DrawProcessor(g, "GPU", ShortName(metrics?.GpuName), metrics?.GpuTemperature, metrics?.GpuUsage,
            "VRAM", vram, GpuX, BlackEdge.Top.X - 44, PosterWhite, Color.FromArgb(235, PosterWhite),
            Color.FromArgb(200, PosterWhite), PosterBlack);
        g.DrawImageUnscaled(FigureWear.Value, 0, 0);
        DrawMemory(g, metrics);
        DrawGrain(g, LongWidth, LongHeight);
        return image;
    }

    private static void DrawProcessor(Graphics g, string name, string model, double? temperature, double? load,
        string extraLabel, string extraValue, float x, float right, Color ink, Color label, Color muted, Color panel)
    {
        // Temperature sits top-right; its left edge bounds the header text.
        using Font temperatureFont = new(FigureFamily, 116, FontStyle.Regular, GraphicsUnit.Pixel);
        using Font degree = new(SecondaryFamily, 44, FontStyle.Regular, GraphicsUnit.Pixel);
        string temperatureText = temperature.HasValue ? Clamp(temperature.Value, 0, 150) : "--";
        float degreeWidth = Measure(g, "°C", degree);
        float temperatureX = right - degreeWidth - 6 - FigureWidth(g, temperatureText, temperatureFont);
        float temperatureEnd = DrawFigureText(g, temperatureText, temperatureFont, ink, temperatureX, TemperatureBaseline);
        DrawAt(g, "°C", degree, ink, temperatureEnd + 6, TemperatureBaseline - 62);

        using Font caption = new(CaptionFamily, 46, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawAt(g, name, caption, label, x + 4, LabelBaseline);
        using Font modelFont = new(CaptionFamily, 26, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawTracked(g, Ellipsize(g, model.ToUpperInvariant(), modelFont, temperatureX - x - 40), modelFont, muted, x + 6, ModelBaseline, 1.5f);

        using Font figure = new(FigureFamily, FigureSize, FontStyle.Regular, GraphicsUnit.Pixel);
        float figureEnd = DrawFigureText(g, load.HasValue ? Clamp(load.Value) : "--", figure, ink, x, FigureBaseline);
        using Font unit = new(FigureFamily, 92, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawAt(g, "%", unit, ink, figureEnd + 12, FigureBaseline);

        // The bottom row sits on the printed photo, so it gets a soft halo in the panel colour.
        using Font bottomLabel = new(CaptionFamily, 26, FontStyle.Regular, GraphicsUnit.Pixel);
        using Font bottomValue = new(SecondaryFamily, 40, FontStyle.Regular, GraphicsUnit.Pixel);
        using Font hint = new(CaptionFamily, 22, FontStyle.Regular, GraphicsUnit.Pixel);
        Color halo = Color.FromArgb(70, panel);
        foreach ((float dx, float dy) in HaloOffsets)
            BottomRow(halo, halo, dx, dy);
        BottomRow(ink, muted, 0, 0);

        void BottomRow(Color main, Color secondary, float dx, float dy)
        {
            float labelEnd = DrawTracked(g, extraLabel, bottomLabel, secondary, x + 6 + dx, BottomBaseline + dy, 2.5f);
            DrawAt(g, extraValue, bottomValue, main, labelEnd + 14 + dx, BottomBaseline + dy);
            if (!temperature.HasValue)
                DrawAt(g, "RUN AS ADMIN FOR TEMP", hint, secondary, right - Measure(g, "RUN AS ADMIN FOR TEMP", hint) + dx, BottomBaseline + dy);
        }
    }

    private static readonly (float X, float Y)[] HaloOffsets =
        { (-3, 0), (3, 0), (0, -3), (0, 3), (-2, -2), (2, -2), (-2, 2), (2, 2) };

    private void DrawMemory(Graphics g, MetricsSnapshot? metrics)
    {
        Color text = Color.FromArgb(240, PosterWhite);
        Color muted = Color.FromArgb(170, PosterWhite);
        Color shadow = Color.FromArgb(120, 0, 0, 0);
        using Font caption = new(CaptionFamily, 46, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawShadowed(g, "RAM", caption, Color.FromArgb(225, 214, 226, 245), shadow, MemoryX + 4, LabelBaseline);
        using Font detail = new(SecondaryFamily, 38, FontStyle.Regular, GraphicsUnit.Pixel);
        string used = metrics == null ? "-- / -- GB"
            : string.Create(CultureInfo.InvariantCulture, $"{metrics.MemoryUsedGb:0.0} / {metrics.MemoryTotalGb:0.0} GB");
        DrawShadowed(g, used, detail, text, shadow, MemoryRight - Measure(g, used, detail), LabelBaseline);

        using Font figure = new(FigureFamily, 176, FontStyle.Regular, GraphicsUnit.Pixel);
        string usage = metrics == null ? "--" : Clamp(metrics.MemoryUsage);
        DrawFigureText(g, usage, figure, shadow, MemoryX + 2, 262 + 3);
        float end = DrawFigureText(g, usage, figure, ColorTranslator.FromHtml("#DCE5F4"), MemoryX, 262);
        using Font percent = new(SecondaryFamily, 54, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawShadowed(g, "%", percent, text, shadow, end + 10, 262);
        using (Brush rule = new SolidBrush(Color.FromArgb(110, PosterWhite)))
            g.FillRectangle(rule, MemoryX + 4, 290, MemoryRight - MemoryX - 4, 2);

        DiskSnapshot[] disks = metrics?.Disks ?? Array.Empty<DiskSnapshot>();
        float column = (MemoryRight - MemoryX) / 3;
        using Font name = new(CaptionFamily, 30, FontStyle.Regular, GraphicsUnit.Pixel);
        using Font value = new(SecondaryFamily, 50, FontStyle.Regular, GraphicsUnit.Pixel);
        using Font unit = new(SecondaryFamily, 30, FontStyle.Regular, GraphicsUnit.Pixel);
        using Font size = new(CaptionFamily, 25, FontStyle.Regular, GraphicsUnit.Pixel);
        for (int i = 0; i < Math.Min(3, disks.Length); i++)
        {
            DiskSnapshot disk = disks[i];
            float cx = MemoryX + 4 + i * column;
            DrawShadowed(g, disk.Name.TrimEnd('\\'), name, muted, shadow, cx, 332);
            string percentText = Clamp(disk.Usage);
            DrawFigureText(g, percentText, value, shadow, cx + 1, 384 + 2);
            float valueEnd = DrawFigureText(g, percentText, value, text, cx, 384);
            DrawShadowed(g, "%", unit, text, shadow, valueEnd + 3, 384);
            DrawShadowed(g, FormatGb(disk.UsedGb) + " / " + FormatGb(disk.TotalGb), size, muted, shadow, cx + 2, BottomBaseline);
        }
    }

    private static void DrawShadowed(Graphics g, string text, Font font, Color color, Color shadow, float x, float baseline)
    {
        DrawAt(g, text, font, shadow, x + 1, baseline + 2);
        DrawAt(g, text, font, color, x, baseline);
    }

    // Worn-print specks over the big figures: paper-coloured on the paper panel,
    // black on the black panel. They are invisible over the panel itself.
    private static readonly Lazy<Bitmap> FigureWear = new(() =>
    {
        Bitmap wear = new(LongWidth, LongHeight, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(wear);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float[] clusters = LongWear.Value;
        Random random = new(19);
        using Brush paper = new SolidBrush(Color.FromArgb(215, PosterPaper));
        using Brush black = new SolidBrush(Color.FromArgb(215, PosterBlack));
        for (int n = 0; n < 14000; n++)
        {
            int x = random.Next(0, (int)BlackEdge.Bottom.X - 50);
            int y = random.Next((int)(FigureBaseline - FigureSize * 0.8f), (int)FigureBaseline + 4);
            float edge = Lerp(PaperEdge.Top.X, PaperEdge.Bottom.X, y / (float)LongHeight);
            if (clusters[y * LongWidth + x] < 0.64f || Math.Abs(x - edge) < 36) continue;
            float s = 0.7f + (float)random.NextDouble() * 1.6f;
            g.FillEllipse(x < edge ? paper : black, x, y, s, s * (0.6f + (float)random.NextDouble() * 0.8f));
        }
        return wear;
    });

    // ---------- clock square ----------

    private static Bitmap BuildClockSquareLayer(Bitmap wallpaper)
    {
        const int size = SquareSize;
        using Bitmap photo = new(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Prepare(photo))
            DrawArt(g, wallpaper, new Rectangle(0, 0, size, size), 0.55f);
        int[] source = ReadPixels(photo);
        List<PointF> outline = new() { new(0, 0), new(size, 0) };
        outline.AddRange(TornLine(new PointF(size, ClockPaperBottom - 6), new PointF(0, ClockPaperBottom + 4), 31));
        byte[] paperMask = PolygonMask(size, size, outline.ToArray());
        float[] wear = SquareWear.Value;
        float photoGain = Math.Clamp(0.3f / MeanLuminance(source, size, 0, ClockPaperBottom, size, size), 0.3f, 1f);
        int[] output = new int[size * size];
        for (int y = 0; y < size; y++)
        {
            float depth = Smooth(196, ClockPaperBottom, y);
            float lower = Smooth(ClockPaperBottom, size, y);
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                int p = source[i];
                float pr = p >> 16 & 0xFF, pg = p >> 8 & 0xFF, pb = p & 0xFF;
                if (y >= 196 && y < ClockPaperBottom + 8) Punch(ref pr, ref pg, ref pb);
                // Darker towards the bottom where the weather sits.
                float shade = (0.78f - 0.3f * lower) * photoGain;
                float r = pr * shade + PhotoTint.R * (1 - shade);
                float gr = pg * shade + PhotoTint.G * (1 - shade);
                float b = pb * shade + PhotoTint.B * (1 - shade);
                if (paperMask[i] > 0)
                {
                    float bleed = Smooth(0.4f, 0.6f, depth * 1.1f + (wear[i] - 0.5f) * 0.7f) * 0.88f;
                    float m = paperMask[i] / 255f;
                    float sr = PosterPaper.R, sg = PosterPaper.G, sb = PosterPaper.B;
                    r = Lerp(r, Lerp(sr, sr * (pr * 0.72f + 71) / 255f, bleed), m);
                    gr = Lerp(gr, Lerp(sg, sg * (pg * 0.72f + 71) / 255f, bleed), m);
                    b = Lerp(b, Lerp(sb, sb * (pb * 0.72f + 71) / 255f, bleed), m);
                }
                output[i] = unchecked((int)0xFF000000) | ToByte(r) << 16 | ToByte(gr) << 8 | ToByte(b);
            }
        }
        return WritePixels(output, size, size);
    }

    private Bitmap RenderClockSquare(WeatherSnapshot? weather)
    {
        Bitmap image = new(_clockSquareLayer!);
        using Graphics g = Prepare(image);
        DateTime now = DateTime.Now;
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        string time = now.ToString(_config.Use24HourClock ? "HH:mm" : "hh:mm", culture);
        using Font clock = new(FigureFamily, 172, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawFigureText(g, time, clock, PosterInk, 26, 166);
        using Font date = new(SecondaryFamily, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawTracked(g, now.ToString("dddd  ·  MMM dd", culture).ToUpperInvariant(), date, Color.FromArgb(215, PosterInk), 30, 206, 2.5f);

        Color text = PosterWhite, shadow = Color.FromArgb(130, 0, 0, 0);
        int code = weather?.Code ?? -1;
        bool night = now.Hour < 6 || now.Hour >= 19;
        DrawWeatherIcon(g, code, night, new RectangleF(24, 282, 96, 96));
        using Font temperature = new(FigureFamily, 112, FontStyle.Regular, GraphicsUnit.Pixel);
        string degrees = weather == null ? "--" : weather.Temperature.ToString("0", CultureInfo.InvariantCulture);
        DrawFigureText(g, degrees, temperature, shadow, 140, 378);
        float end = DrawFigureText(g, degrees, temperature, text, 138, 375);
        using Font unit = new(SecondaryFamily, 34, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawShadowed(g, "°C", unit, text, shadow, end + 8, 312);

        using Font condition = new(SecondaryFamily, 28, FontStyle.Regular, GraphicsUnit.Pixel);
        string conditionText = weather == null ? "Loading weather" : WeatherService.Describe(weather.Code, english: true);
        DrawShadowed(g, Ellipsize(g, conditionText, condition, 424), condition, text, shadow, 28, 420);
        using Font detail = new(SecondaryFamily, 22, FontStyle.Regular, GraphicsUnit.Pixel);
        string range = weather == null ? "H --°   L --°"
            : string.Create(CultureInfo.InvariantCulture, $"H {weather.High:0}°   L {weather.Low:0}°");
        DrawShadowed(g, range, detail, Color.FromArgb(225, text), shadow, 28, 456);
        string humidity = weather == null ? "--%" : weather.Humidity + "%";
        float humidityX = 452 - Measure(g, humidity, detail);
        DrawShadowed(g, humidity, detail, Color.FromArgb(225, text), shadow, humidityX, 456);
        DrawDroplet(g, humidityX - 22, 440, Color.FromArgb(225, 142, 197, 255));
        DrawGrain(g, SquareSize, SquareSize);
        return image;
    }

    // ---------- weather icons ----------

    private static void DrawWeatherIcon(Graphics g, int code, bool night, RectangleF box)
    {
        Color sun = ColorTranslator.FromHtml("#FFC23D");
        Color cloud = Color.FromArgb(245, 246, 248, 252);
        Color dimCloud = Color.FromArgb(235, 196, 204, 218);
        Color rain = ColorTranslator.FromHtml("#8EC5FF");
        float s = box.Width;
        switch (code)
        {
            case 0:
                DrawSunOrMoon(g, night, new PointF(box.X + s * 0.5f, box.Y + s * 0.5f), s * 0.24f, sun);
                break;
            case 1 or 2:
                DrawSunOrMoon(g, night, new PointF(box.X + s * 0.62f, box.Y + s * 0.36f), s * 0.18f, sun);
                DrawCloud(g, new RectangleF(box.X + s * 0.04f, box.Y + s * 0.36f, s * 0.78f, s * 0.46f), cloud);
                break;
            case 3:
                DrawCloud(g, new RectangleF(box.X + s * 0.3f, box.Y + s * 0.18f, s * 0.66f, s * 0.38f), dimCloud);
                DrawCloud(g, new RectangleF(box.X + s * 0.02f, box.Y + s * 0.36f, s * 0.8f, s * 0.46f), cloud);
                break;
            case 45 or 48:
                DrawCloud(g, new RectangleF(box.X + s * 0.1f, box.Y + s * 0.12f, s * 0.8f, s * 0.44f), dimCloud);
                using (Pen pen = new(cloud, s * 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 3; i++)
                    {
                        float y = box.Y + s * (0.66f + i * 0.13f);
                        g.DrawLine(pen, box.X + s * (0.08f + i * 0.08f), y, box.X + s * (0.92f - (2 - i) * 0.06f), y);
                    }
                break;
            case >= 71 and <= 77 or 85 or 86:
                DrawCloud(g, new RectangleF(box.X + s * 0.06f, box.Y + s * 0.1f, s * 0.86f, s * 0.5f), cloud);
                using (Brush flake = new SolidBrush(cloud))
                    foreach ((float fx, float fy) in new[] { (0.28f, 0.74f), (0.5f, 0.86f), (0.72f, 0.74f), (0.39f, 0.96f), (0.61f, 0.96f) })
                        g.FillEllipse(flake, box.X + s * fx - s * 0.04f, box.Y + s * fy - s * 0.04f, s * 0.08f, s * 0.08f);
                break;
            case >= 95:
                DrawCloud(g, new RectangleF(box.X + s * 0.06f, box.Y + s * 0.06f, s * 0.86f, s * 0.5f), dimCloud);
                using (Brush bolt = new SolidBrush(sun))
                    g.FillPolygon(bolt, new PointF[]
                    {
                        new(box.X + s * 0.54f, box.Y + s * 0.5f), new(box.X + s * 0.36f, box.Y + s * 0.78f),
                        new(box.X + s * 0.5f, box.Y + s * 0.78f), new(box.X + s * 0.42f, box.Y + s * 1.0f),
                        new(box.X + s * 0.68f, box.Y + s * 0.68f), new(box.X + s * 0.54f, box.Y + s * 0.68f),
                        new(box.X + s * 0.64f, box.Y + s * 0.5f),
                    });
                break;
            case >= 51 and <= 67 or >= 80 and <= 82:
                DrawCloud(g, new RectangleF(box.X + s * 0.06f, box.Y + s * 0.08f, s * 0.86f, s * 0.5f), cloud);
                using (Pen drop = new(rain, s * 0.055f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 3; i++)
                    {
                        float x = box.X + s * (0.3f + i * 0.2f);
                        g.DrawLine(drop, x, box.Y + s * 0.7f, x - s * 0.07f, box.Y + s * 0.92f);
                    }
                break;
            default:
                DrawCloud(g, new RectangleF(box.X + s * 0.06f, box.Y + s * 0.28f, s * 0.86f, s * 0.5f), dimCloud);
                break;
        }
    }

    private static void DrawSunOrMoon(Graphics g, bool night, PointF centre, float radius, Color sun)
    {
        if (night)
        {
            using GraphicsPath moon = new();
            moon.AddEllipse(centre.X - radius * 1.15f, centre.Y - radius * 1.15f, radius * 2.3f, radius * 2.3f);
            using Region region = new(moon);
            using GraphicsPath bite = new();
            bite.AddEllipse(centre.X - radius * 0.45f, centre.Y - radius * 1.45f, radius * 2.3f, radius * 2.3f);
            region.Exclude(bite);
            using Brush brush = new SolidBrush(ColorTranslator.FromHtml("#FFE3A3"));
            g.FillRegion(brush, region);
            return;
        }
        using (Pen ray = new(sun, radius * 0.26f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                g.DrawLine(ray, centre.X + cos * radius * 1.45f, centre.Y + sin * radius * 1.45f,
                    centre.X + cos * radius * 1.9f, centre.Y + sin * radius * 1.9f);
            }
        using Brush disc = new SolidBrush(sun);
        g.FillEllipse(disc, centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
    }

    private static void DrawCloud(Graphics g, RectangleF r, Color color)
    {
        using GraphicsPath cloud = new();
        cloud.AddEllipse(r.X + r.Width * 0.08f, r.Y + r.Height * 0.36f, r.Width * 0.38f, r.Height * 0.64f);
        cloud.AddEllipse(r.X + r.Width * 0.26f, r.Y, r.Width * 0.46f, r.Height * 0.86f);
        cloud.AddEllipse(r.X + r.Width * 0.54f, r.Y + r.Height * 0.26f, r.Width * 0.4f, r.Height * 0.74f);
        cloud.AddRectangle(new RectangleF(r.X + r.Width * 0.27f, r.Y + r.Height * 0.6f, r.Width * 0.5f, r.Height * 0.4f));
        cloud.FillMode = FillMode.Winding;
        using Brush brush = new SolidBrush(color);
        g.FillPath(brush, cloud);
    }

    private static void DrawDroplet(Graphics g, float x, float y, Color color)
    {
        using GraphicsPath drop = new();
        drop.AddBezier(x + 7, y - 2, x + 3, y + 5, x, y + 9, x + 7, y + 16);
        drop.AddBezier(x + 7, y + 16, x + 14, y + 9, x + 11, y + 5, x + 7, y - 2);
        using Brush brush = new SolidBrush(color);
        g.FillPath(brush, drop);
    }

    // ---------- pixel helpers ----------

    private static readonly Lazy<float[]> LongWear = new(() => ValueNoise(LongWidth, LongHeight, 5, (90, 0.45f), (26, 0.3f), (7, 0.25f)));
    private static readonly Lazy<float[]> LongMottle = new(() => ValueNoise(LongWidth, LongHeight, 9, (160, 0.6f), (40, 0.4f)));
    private static readonly Lazy<float[]> SquareWear = new(() => ValueNoise(SquareSize, SquareSize, 13, (70, 0.45f), (22, 0.3f), (6, 0.25f)));

    // Sum of smoothly interpolated random grids; values in 0..1.
    private static float[] ValueNoise(int width, int height, int seed, params (int Cell, float Weight)[] octaves)
    {
        float[] result = new float[width * height];
        Random random = new(seed);
        foreach ((int cell, float weight) in octaves)
        {
            int gw = width / cell + 2, gh = height / cell + 2;
            float[] grid = new float[gw * gh];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)random.NextDouble();
            for (int y = 0; y < height; y++)
            {
                float fy = y / (float)cell;
                int y0 = (int)fy;
                float ty = fy - y0;
                ty = ty * ty * (3 - 2 * ty);
                for (int x = 0; x < width; x++)
                {
                    float fx = x / (float)cell;
                    int x0 = (int)fx;
                    float tx = fx - x0;
                    tx = tx * tx * (3 - 2 * tx);
                    float top = Lerp(grid[y0 * gw + x0], grid[y0 * gw + x0 + 1], tx);
                    float bottom = Lerp(grid[(y0 + 1) * gw + x0], grid[(y0 + 1) * gw + x0 + 1], tx);
                    result[y * width + x] += Lerp(top, bottom, ty) * weight;
                }
            }
        }
        return result;
    }

    private static PointF[] PanelOutline((PointF Top, PointF Bottom) edge, int seed)
    {
        List<PointF> outline = new() { new(-2, -2) };
        outline.AddRange(TornLine(new PointF(edge.Top.X, -2), new PointF(edge.Bottom.X, LongHeight + 2), seed));
        outline.Add(new PointF(-2, LongHeight + 2));
        return outline.ToArray();
    }

    private static byte[] PolygonMask(int width, int height, PointF[] polygon)
    {
        using Bitmap mask = new(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(mask))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.FillPolygon(Brushes.White, polygon);
        }
        int[] pixels = ReadPixels(mask);
        byte[] alpha = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) alpha[i] = (byte)(pixels[i] >>> 24);
        return alpha;
    }

    private static int[] ReadPixels(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int[] pixels = new int[bitmap.Width * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width, bitmap.Width);
            return pixels;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static Bitmap WritePixels(int[] pixels, int width, int height)
    {
        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width, data.Scan0 + y * data.Stride, width);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // Full-strip pixels with the photo cropped into area; transparent elsewhere.
    private static int[] PanelPixels(Bitmap art, Rectangle area, float focusY)
    {
        using Bitmap strip = new(LongWidth, LongHeight, PixelFormat.Format32bppArgb);
        using (Graphics g = Prepare(strip))
            DrawArt(g, art, area, focusY);
        return ReadPixels(strip);
    }

    private static float MeanLuminance(int[] pixels, int stride, int left, int top, int right, int bottom)
    {
        double sum = 0;
        int count = 0;
        for (int y = top; y < bottom; y += 4)
        for (int x = left; x < right; x += 4)
        {
            int p = pixels[y * stride + x];
            sum += 0.2126 * (p >> 16 & 0xFF) + 0.7152 * (p >> 8 & 0xFF) + 0.0722 * (p & 0xFF);
            count++;
        }
        return count == 0 ? 0.3f : Math.Max(0.01f, (float)(sum / count / 255));
    }

    // Contrast and saturation lift so photo detail survives being printed into a panel.
    private static void Punch(ref float r, ref float g, ref float b)
    {
        float luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        r = Curve(luminance + (r - luminance) * 1.35f);
        g = Curve(luminance + (g - luminance) * 1.35f);
        b = Curve(luminance + (b - luminance) * 1.35f);

        static float Curve(float value)
        {
            float v = Math.Clamp(value / 255f, 0, 1);
            return Lerp(v, v * v * (3 - 2 * v), 0.75f) * 255f;
        }
    }

    private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;
    private static float Screen(float a, float b) => 255 - (255 - a) * (255 - b) / 255;
    private static int ToByte(float value) => Math.Clamp((int)(value + 0.5f), 0, 255);

    // ---------- text helpers ----------

    private static readonly Dictionary<(string, float), float> DigitCells = new();

    private static float DigitCell(Graphics g, Font font)
    {
        lock (DigitCells)
        {
            var key = (font.FontFamily.Name, font.Size);
            if (DigitCells.TryGetValue(key, out float cell)) return cell;
            cell = Enumerable.Range(0, 10).Max(d => Measure(g, d.ToString(CultureInfo.InvariantCulture), font));
            return DigitCells[key] = cell;
        }
    }

    private static float FigureWidth(Graphics g, string text, Font font)
    {
        float cell = DigitCell(g, font);
        return text.Sum(c => char.IsAsciiDigit(c) ? cell : Measure(g, c.ToString(), font));
    }

    // Draws digits centred in equal cells (tabular figures) and returns the end x.
    private static float DrawFigureText(Graphics g, string text, Font font, Color color, float x, float baseline)
    {
        float cell = DigitCell(g, font);
        float y = baseline - Ascent(font);
        using Brush brush = new SolidBrush(color);
        foreach (char c in text)
        {
            string glyph = c.ToString();
            float width = Measure(g, glyph, font);
            float advance = char.IsAsciiDigit(c) ? cell : width;
            g.DrawString(glyph, font, brush, x + (advance - width) / 2, y, Typographic);
            x += advance;
        }
        return x;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        GraphicsPath path = new();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)uptime.TotalDays}d {uptime.Hours:00}h")
        : string.Create(CultureInfo.InvariantCulture, $"{uptime.Hours}h {uptime.Minutes:00}m");

    private static string FormatGb(double gigabytes) => gigabytes >= 1000
        ? (gigabytes / 1024).ToString("0.0", CultureInfo.InvariantCulture) + "T"
        : gigabytes.ToString("0", CultureInfo.InvariantCulture) + "G";

    internal static string ShortName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string value = Regex.Replace(name, @"\((R|TM)\)|\bProcessor\b|\b\d+-Core\b|\bCPU\b.*$|^NVIDIA\s+(GeForce\s+)?", "",
            RegexOptions.IgnoreCase);
        return Regex.Replace(value, @"\s+", " ").Trim();
    }
}
