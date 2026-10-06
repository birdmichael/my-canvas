using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

// Night-sky lyric modes in the manner of folia-major's visualizers (drawn
// from scratch here; that project is AGPL): a dark backdrop with slowly
// drifting outline shapes, serif glyphs placed one by one, and the glyph being
// sung lit in a warm highlight with a soft glow.
//   lumiere - one line, each glyph slightly tilted, the sung glyph enlarged;
//   fume    - a wall of the song's lines that slides to the current one;
//   partita - the line broken into phrases set on stepped guide lines;
//   cadenza - glyphs of varied sizes scattered, the sung one ringed;
//   tilt    - two centred lines, the second slanted in the highlight colour.
internal sealed partial class DualLayoutRenderer
{
    private static bool IsFoliaStyle(string style) => style is "lumiere" or "fume" or "partita" or "cadenza" or "tilt";

    private static readonly string SerifFamily = InstalledFamily("Noto Serif SC", CjkFamily);
    private static readonly string ScriptFamily = InstalledFamily("Ink Free", SerifFamily);
    private static readonly Color Gold = ColorTranslator.FromHtml("#F3C350");
    private const float FoliaExitSeconds = 0.5f;
    private const float FoliaSubtitleBaseline = 438;

    private sealed class FoliaUnit : IDisposable
    {
        public string Text = "";
        public bool SpaceAfter;
        public LyricSpan Span;
        public GraphicsPath? Path;
        public float Width, Size, X, Baseline, Tilt, Shear;
        public int Row;
        public int Seed;
        public void Dispose() => Path?.Dispose();
    }

    private sealed class FoliaLine : IDisposable
    {
        public string Text = "";
        public string Translation = "";
        public bool Cjk, TitleOnly;
        public string Mode = "";
        public float Size;
        public List<FoliaUnit> Units = new();
        public void Dispose() { foreach (FoliaUnit unit in Units) unit.Dispose(); }
    }

    private readonly record struct FoliaShape(int Kind, float X, float Y, float Size, float Vx, float Vy,
        float Angle, float Spin, float Alpha);

    private static readonly FoliaShape[] FoliaShapes = BuildFoliaShapes();

    private string _foliaSong = "";
    private string _foliaMode = "";
    private FoliaLine? _foliaCurrent;
    private FoliaLine? _foliaPrevious;
    private DateTime _foliaChangedAt;
    private Bitmap? _foliaBase;
    private Color? _foliaHighlight;

    private Bitmap RenderFolia(MusicSnapshot? music, DateTime now)
    {
        Bitmap image = new(LongWidth, LongHeight, PixelFormat.Format32bppArgb);
        using Graphics g = Prepare(image);
        g.DrawImageUnscaled(_foliaBase ??= BuildFoliaBase(), 0, 0);
        float clock = (float)_flowClock.Elapsed.TotalSeconds;
        DrawFoliaShapes(g, clock);
        SyncFolia(g, music, now);
        float since = (float)(now - _foliaChangedAt).TotalSeconds;
        float t = LineElapsed(music);
        FoliaLine current = _foliaCurrent!;
        if (LyricStyle == "fume" && !current.TitleOnly && music?.AllLyrics is { Length: > 0 } && music.LyricIndex >= 0)
        {
            DrawFume(g, music, current, t, since);
        }
        else
        {
            if (_foliaPrevious != null) DrawFoliaLine(g, _foliaPrevious, float.MaxValue, since, Math.Clamp(since / FoliaExitSeconds, 0, 1));
            DrawFoliaLine(g, current, t, since, 0);
            DrawFoliaSubtitle(g, current.Translation, since);
        }
        DrawGrain(g, LongWidth, LongHeight);
        return image;
    }

    private Color FoliaHighlight
    {
        get
        {
            _foliaHighlight ??= _accent.GetSaturation() < 0.25f ? Gold
                : Mix(Gold, FromHsl(_accent.GetHue(), 0.82f, 0.64f), 0.4f);
            return _foliaHighlight.Value;
        }
    }

    private void DisposeFolia()
    {
        _foliaBase?.Dispose(); _foliaBase = null;
        _foliaHighlight = null;
        _foliaCurrent?.Dispose(); _foliaCurrent = null;
        _foliaPrevious?.Dispose(); _foliaPrevious = null;
        _foliaSong = "";
    }

    // ---------- backdrop ----------

    private Bitmap BuildFoliaBase()
    {
        Bitmap layer = new(LongWidth, LongHeight, PixelFormat.Format32bppArgb);
        using Graphics g = Prepare(layer);
        Rectangle all = new(0, 0, LongWidth, LongHeight);
        using (LinearGradientBrush sky = new(all, Mix(ColorTranslator.FromHtml("#151D36"), _accent, 0.1f),
                   Mix(ColorTranslator.FromHtml("#090D1B"), _accent, 0.05f), LinearGradientMode.Vertical))
            g.FillRectangle(sky, all);
        using (GraphicsPath glow = new())
        {
            glow.AddEllipse(160, -130, 1600, 720);
            using PathGradientBrush brush = new(glow)
            {
                CenterColor = Color.FromArgb(40, Mix(_accent, FoliaHighlight, 0.3f)),
                SurroundColors = new[] { Color.FromArgb(0, _accent) },
                CenterPoint = new PointF(960, 210),
            };
            g.FillPath(brush, glow);
        }
        using (Pen stripe = new(Color.FromArgb(7, 255, 255, 255), 1))
            for (int x = 0; x < LongWidth; x += 7) g.DrawLine(stripe, x, 0, x, LongHeight);
        return layer;
    }

    private static FoliaShape[] BuildFoliaShapes()
    {
        Random random = new(20261005);
        FoliaShape[] shapes = new FoliaShape[17];
        for (int i = 0; i < shapes.Length; i++)
        {
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
            float Signed(float min, float max) => Range(min, max) * (random.Next(2) == 0 ? -1 : 1);
            shapes[i] = new FoliaShape(i % 6, Range(0, LongWidth + 240), Range(20, LongHeight - 20), Range(26, 96),
                Signed(4, 14), Signed(1, 5), Range(0, 360), Signed(3, 14), Range(0.55f, 1f));
        }
        return shapes;
    }

    private void DrawFoliaShapes(Graphics g, float clock)
    {
        Color tone = Mix(ColorTranslator.FromHtml("#4C70A2"), FoliaHighlight, 0.18f);
        foreach (FoliaShape shape in FoliaShapes)
        {
            float span = LongWidth + 240, height = LongHeight + 160;
            float x = Mod(shape.X + shape.Vx * clock, span) - 120;
            float y = Mod(shape.Y + 80 + shape.Vy * clock, height) - 80;
            GraphicsState state = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(shape.Angle + shape.Spin * clock);
            float s = shape.Size, h = s / 2;
            using Brush fill = new SolidBrush(Color.FromArgb((int)(46 * shape.Alpha), tone));
            using Pen line = new(Color.FromArgb((int)(70 * shape.Alpha), tone), 2f) { LineJoin = LineJoin.Round };
            switch (shape.Kind)
            {
                case 0: // cross
                    g.FillRectangle(fill, -h, -s * 0.13f, s, s * 0.26f);
                    g.FillRectangle(fill, -s * 0.13f, -h, s * 0.26f, s);
                    break;
                case 1: // triangle
                    g.FillPolygon(fill, new[] { new PointF(0, -h), new PointF(h * 0.87f, h * 0.5f), new PointF(-h * 0.87f, h * 0.5f) });
                    break;
                case 2:
                    g.DrawEllipse(line, -h, -h, s, s);
                    break;
                case 3:
                    g.DrawRectangle(line, -h * 0.8f, -h * 0.8f, s * 0.8f, s * 0.8f);
                    break;
                case 4:
                    g.FillEllipse(fill, -h * 0.6f, -h * 0.6f, s * 0.6f, s * 0.6f);
                    break;
                default: // four-pointed star
                    PointF[] star = new PointF[8];
                    for (int k = 0; k < 8; k++)
                    {
                        float r = k % 2 == 0 ? h : h * 0.32f, a = MathF.PI / 4 * k;
                        star[k] = new PointF(r * MathF.Cos(a), r * MathF.Sin(a));
                    }
                    g.DrawPolygon(line, star);
                    break;
            }
            g.Restore(state);
        }
        // A few faint stars twinkle.
        for (int i = 0; i < 34; i++)
        {
            float sx = Hash(i, 1) * LongWidth, sy = Hash(i, 2) * LongHeight;
            float twinkle = 0.5f + 0.5f * MathF.Sin(clock * (0.6f + Hash(i, 3) * 1.4f) + Hash(i, 4) * 6.3f);
            using Brush dot = new SolidBrush(Color.FromArgb((int)(20 + 70 * twinkle), 220, 228, 255));
            float r = 0.8f + Hash(i, 5) * 1.3f;
            g.FillEllipse(dot, sx - r, sy - r, 2 * r, 2 * r);
        }
    }

    private static float Mod(float value, float span) => ((value % span) + span) % span;

    private static float Hash(int seed, int salt)
    {
        uint h = unchecked((uint)(seed * 374761393 + salt * 668265263 + 1442695041));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    // ---------- line state ----------

    private void SyncFolia(Graphics g, MusicSnapshot? music, DateTime now)
    {
        bool titleOnly = IsPlaceholder(music?.CurrentLyric);
        string text = titleOnly ? music?.Title ?? T("等待音乐", "Waiting for music") : music!.CurrentLyric.Trim();
        string translation = titleOnly
            ? string.Join("  ·  ", new[] { music?.Artist, music?.Album }.Where(part => !string.IsNullOrWhiteSpace(part)))
            : (music!.CurrentTranslation ?? "").Trim();
        string song = music?.SongId ?? "";
        if (song != _foliaSong || LyricStyle != _foliaMode || _foliaCurrent == null)
        {
            _foliaCurrent?.Dispose();
            _foliaPrevious?.Dispose(); _foliaPrevious = null;
            _foliaSong = song;
            _foliaMode = LyricStyle;
            _foliaCurrent = BuildFoliaLine(g, text, titleOnly, music);
            _foliaChangedAt = now;
        }
        else if (text != _foliaCurrent.Text || titleOnly != _foliaCurrent.TitleOnly)
        {
            _foliaPrevious?.Dispose();
            _foliaPrevious = _foliaCurrent;
            _foliaCurrent = BuildFoliaLine(g, text, titleOnly, music);
            _foliaChangedAt = now;
        }
        else if (_foliaPrevious != null && (now - _foliaChangedAt).TotalSeconds > FoliaExitSeconds)
        {
            _foliaPrevious.Dispose();
            _foliaPrevious = null;
        }
        _foliaCurrent.Translation = translation;
    }

    private FoliaLine BuildFoliaLine(Graphics g, string text, bool titleOnly, MusicSnapshot? music)
    {
        FoliaLine line = new() { Text = text, TitleOnly = titleOnly, Cjk = text.Any(IsCjk), Mode = LyricStyle };
        var tokens = Tokenize(text);
        LyricSpan[] spans = titleOnly
            ? tokens.Select(_ => new LyricSpan(-10, -10)).ToArray()
            : TokenSpans(tokens.Select(t => t.Text).ToList(), music, line.Cjk);
        int seed = text.Aggregate(17, (h, c) => unchecked(h * 31 + c));
        for (int i = 0; i < tokens.Count; i++)
            line.Units.Add(new FoliaUnit { Text = tokens[i].Text, SpaceAfter = tokens[i].SpaceAfter, Span = spans[i], Seed = seed + i * 7919 });
        string mode = titleOnly || LyricStyle == "fume" && music?.LyricIndex < 0 ? "lumiere" : LyricStyle;
        line.Mode = mode;
        switch (mode)
        {
            case "fume": LayoutFumeLine(g, line); break;
            case "partita": LayoutPartita(g, line); break;
            case "cadenza": LayoutCadenza(g, line); break;
            case "tilt": LayoutTilt(g, line); break;
            default: LayoutLumiere(g, line); break;
        }
        return line;
    }

    // ---------- glyphs ----------

    private static string InstalledFamily(string name, string fallback)
    {
        try { using FontFamily family = new(name); return name; }
        catch (ArgumentException) { return fallback; }
    }

    private static FontStyle Available(FontFamily family, FontStyle style) =>
        family.IsStyleAvailable(style) ? style : FontStyle.Regular;

    // Outline of the unit's text with its left end at x 0 and baseline at y 0.
    private static void ShapeUnit(Graphics g, FoliaUnit unit, string family, FontStyle style, float size)
    {
        unit.Path?.Dispose();
        using FontFamily fontFamily = new(family);
        style = Available(fontFamily, style);
        using Font font = new(fontFamily, size, style, GraphicsUnit.Pixel);
        float ascent = size * fontFamily.GetCellAscent(style) / fontFamily.GetEmHeight(style);
        unit.Size = size;
        unit.Width = Measure(g, unit.Text, font);
        // Noto's outlines overlap (serifs over stems); alternate filling would
        // punch holes where they cross.
        unit.Path = new GraphicsPath(FillMode.Winding);
        unit.Path.AddString(unit.Text, fontFamily, (int)style, size, new PointF(0, -ascent), Typographic);
    }

    private static float SpaceAfter(FoliaUnit unit, bool cjk) => unit.SpaceAfter ? unit.Size * (cjk ? 0.45f : 0.28f) : cjk ? unit.Size * 0.02f : 0;

    // Draws the unit at (x, baseline), rotated and scaled about its centre
    // (or its left end), with a glow in glowColour of the given strength.
    private static void DrawUnit(Graphics g, FoliaUnit unit, float x, float baseline, float scale, float rotation,
        Color fill, float opacity, float glow, Color glowColour, bool pivotLeft = false)
    {
        if (unit.Path == null || opacity <= 0.01f) return;
        GraphicsState state = g.Save();
        float px = pivotLeft ? 0 : unit.Width / 2, py = pivotLeft ? 0 : -unit.Size * 0.36f;
        g.TranslateTransform(x + px, baseline + py);
        if (rotation != 0) g.RotateTransform(rotation);
        if (scale != 1) g.ScaleTransform(scale, scale);
        if (unit.Shear != 0)
        {
            using Matrix shear = new(1, 0, unit.Shear, 1, 0, 0);
            g.MultiplyTransform(shear);
        }
        g.TranslateTransform(-px, -py);
        DrawGlow(g, unit.Path, glowColour, glow * opacity, unit.Size);
        using Brush brush = new SolidBrush(Fade(fill, opacity));
        g.FillPath(brush, unit.Path);
        g.Restore(state);
    }

    private static void DrawGlow(Graphics g, GraphicsPath path, Color colour, float strength, float size)
    {
        if (strength <= 0.02f) return;
        ReadOnlySpan<float> widths = stackalloc float[] { 0.36f, 0.23f, 0.13f, 0.06f };
        ReadOnlySpan<int> alphas = stackalloc int[] { 8, 14, 24, 40 };
        for (int i = 0; i < widths.Length; i++)
        {
            using Pen pen = new(Color.FromArgb(Math.Clamp((int)(alphas[i] * strength), 0, 255), colour), Math.Max(1, size * widths[i]))
            {
                LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round,
            };
            g.DrawPath(pen, path);
        }
    }

    private static Color Fade(Color colour, float opacity) =>
        Color.FromArgb(Math.Clamp((int)(colour.A * opacity), 0, 255), colour.R, colour.G, colour.B);

    // How lit a unit is: rises as its singing starts, stays while it lasts and
    // fades out over a third of a second after.
    private static float Activity(LyricSpan span, float t) =>
        t < span.Start - 0.05f ? 0 : t <= span.End ? Smooth(span.Start - 0.05f, span.Start + 0.07f, t)
            : MathF.Exp(-(t - span.End) / 0.32f);

    private static readonly Color Unsung = Color.FromArgb(86, White);

    // Splits units into rows no wider than width, breaking near the middle at a
    // space when a split is needed. Returns the index each row starts at.
    private static List<int> SplitRows(FoliaLine line, float width, int maxRows)
    {
        float total = RowWidth(line, 0, line.Units.Count);
        if (total <= width || maxRows < 2 || line.Units.Count < 2) return new List<int> { 0 };
        int best = line.Units.Count / 2;
        float bestScore = float.MaxValue;
        for (int b = 1; b < line.Units.Count; b++)
        {
            float first = RowWidth(line, 0, b), second = RowWidth(line, b, line.Units.Count);
            float score = Math.Max(first, second) + (line.Units[b - 1].SpaceAfter ? 0 : line.Cjk ? width * 0.08f : width);
            if (score < bestScore) { best = b; bestScore = score; }
        }
        return new List<int> { 0, best };
    }

    private static float RowWidth(FoliaLine line, int from, int to)
    {
        float w = 0;
        for (int i = from; i < to; i++) w += line.Units[i].Width + (i < to - 1 ? SpaceAfter(line.Units[i], line.Cjk) : 0);
        return w;
    }

    private static void ShapeAll(Graphics g, FoliaLine line, string family, FontStyle style, float size)
    {
        foreach (FoliaUnit unit in line.Units) ShapeUnit(g, unit, family, style, size);
        line.Size = size;
    }

    // Shapes the line at the largest size up to max that fits within width in
    // at most maxRows rows, and places the rows centred about centreY.
    private static List<int> FitRows(Graphics g, FoliaLine line, string family, FontStyle style, float max, float min,
        float width, int maxRows)
    {
        ShapeAll(g, line, family, style, max);
        float single = RowWidth(line, 0, line.Units.Count);
        float size = single <= width ? max : max * width / single;
        if (size >= max * 0.72f || maxRows < 2)
        {
            size = Math.Max(min, size);
            ShapeAll(g, line, family, style, size);
            return new List<int> { 0 };
        }
        List<int> rows = SplitRows(line, width, maxRows);
        float widest = Math.Max(RowWidth(line, 0, rows[1]), RowWidth(line, rows[1], line.Units.Count));
        size = Math.Max(min, Math.Min(max * 0.86f, max * width / widest));
        ShapeAll(g, line, family, style, size);
        return rows;
    }

    private static void PlaceRows(FoliaLine line, List<int> rows, float centreY, float rowGap)
    {
        float cap = line.Size * 0.8f;
        float firstBaseline = centreY - (rows.Count - 1) * rowGap / 2 + cap / 2;
        for (int r = 0; r < rows.Count; r++)
        {
            int from = rows[r], to = r + 1 < rows.Count ? rows[r + 1] : line.Units.Count;
            float x = (LongWidth - RowWidth(line, from, to)) / 2;
            for (int i = from; i < to; i++)
            {
                FoliaUnit unit = line.Units[i];
                unit.X = x;
                unit.Baseline = firstBaseline + r * rowGap;
                unit.Row = r;
                x += unit.Width + SpaceAfter(unit, line.Cjk);
            }
        }
    }

    private float Centre(FoliaLine line) => line.Translation.Length > 0 || line.TitleOnly ? 196 : 214;

    // ---------- modes ----------

    private void DrawFoliaLine(Graphics g, FoliaLine line, float t, float since, float exit)
    {
        switch (line.Mode)
        {
            case "partita": DrawPartita(g, line, t, since, exit); break;
            case "cadenza": DrawCadenza(g, line, t, since, exit); break;
            case "tilt": DrawTilt(g, line, t, since, exit); break;
            case "fume": DrawFumeAlone(g, line, t, since, exit); break;
            default: DrawLumiere(g, line, t, since, exit); break;
        }
    }

    private void LayoutLumiere(Graphics g, FoliaLine line)
    {
        List<int> rows = FitRows(g, line, SerifFamily, FontStyle.Bold, line.Cjk ? 136 : 124, 60, 1720, 2);
        PlaceRows(line, rows, Centre(line), line.Size * 1.2f);
        foreach (FoliaUnit unit in line.Units)
        {
            unit.Tilt = (Hash(unit.Seed, 1) - 0.5f) * 13;
            unit.Baseline += (Hash(unit.Seed, 2) - 0.5f) * unit.Size * 0.12f;
        }
    }

    private void DrawLumiere(Graphics g, FoliaLine line, float t, float since, float exit)
    {
        Color highlight = FoliaHighlight;
        for (int i = 0; i < line.Units.Count; i++)
        {
            FoliaUnit unit = line.Units[i];
            float enter = EaseOutCubic((since - i * 0.035f) / 0.38f);
            float leave = exit <= 0 ? 0 : Math.Clamp(exit * 1.5f - i * 0.025f, 0, 1);
            float fill = unit.Span.Fill(t), act = exit > 0 ? 0 : Activity(unit.Span, t);
            Color colour = Mix(Blend(Unsung, White, fill), highlight, act);
            float y = unit.Baseline + (1 - enter) * 26 - leave * 44 - act * 8;
            DrawUnit(g, unit, unit.X, y, 1 + 0.17f * act, unit.Tilt, colour, enter * (1 - leave), act, highlight);
        }
    }

    private void LayoutTilt(Graphics g, FoliaLine line)
    {
        ShapeAll(g, line, SerifFamily, FontStyle.Bold, 100);
        int split = line.Units.Count >= 4 ? SplitRows(line, 0, 2)[^1] : 0;
        string secondFamily = line.Cjk ? SerifFamily : ScriptFamily;
        float firstSize = line.Cjk ? 112 : 104, secondSize = line.Cjk ? 118 : 128;
        for (int pass = 0; pass < 8; pass++)
        {
            for (int i = 0; i < line.Units.Count; i++)
            {
                FoliaUnit unit = line.Units[i];
                bool second = i >= split;
                ShapeUnit(g, unit, second ? secondFamily : SerifFamily, second ? FontStyle.Regular : FontStyle.Bold,
                    second ? secondSize : firstSize);
                unit.Row = second ? 1 : 0;
                unit.Shear = second ? -0.16f : 0;
            }
            float widest = Math.Max(TrackedWidth(line, 0, split), TrackedWidth(line, split, line.Units.Count));
            if (widest <= 1640 || firstSize < 62) break;
            float k = Math.Max(0.6f, 1640 / widest);
            firstSize *= k; secondSize *= k;
        }
        line.Size = secondSize;
        float centre = Centre(line);
        float firstBaseline = split == 0 ? 0 : centre - secondSize * 0.15f;
        float secondBaseline = split == 0 ? centre + secondSize * 0.38f : firstBaseline + secondSize * 1.08f;
        for (int r = 0; r < 2; r++)
        {
            int from = r == 0 ? 0 : split, to = r == 0 ? split : line.Units.Count;
            if (from >= to) continue;
            float x = (LongWidth - TrackedWidth(line, from, to)) / 2;
            for (int i = from; i < to; i++)
            {
                FoliaUnit unit = line.Units[i];
                unit.X = x;
                unit.Baseline = r == 0 ? firstBaseline : secondBaseline + (Hash(unit.Seed, 3) - 0.5f) * unit.Size * 0.16f;
                unit.Tilt = r == 0 ? 0 : (i % 2 == 0 ? -1 : 1) * (2 + Hash(unit.Seed, 4) * 4);
                x += unit.Width + Tracking(unit, line);
            }
        }
    }

    private static float Tracking(FoliaUnit unit, FoliaLine line) =>
        SpaceAfter(unit, line.Cjk) + (unit.Row == 1 ? unit.Size * (line.Cjk ? 0.1f : 0.04f) : 0);

    private static float TrackedWidth(FoliaLine line, int from, int to)
    {
        float w = 0;
        for (int i = from; i < to; i++) w += line.Units[i].Width + (i < to - 1 ? Tracking(line.Units[i], line) : 0);
        return w;
    }

    private void DrawTilt(Graphics g, FoliaLine line, float t, float since, float exit)
    {
        Color highlight = FoliaHighlight;
        float enter = EaseOutCubic(since / 0.5f);
        for (int i = 0; i < line.Units.Count; i++)
        {
            FoliaUnit unit = line.Units[i];
            bool second = unit.Row == 1;
            float fill = unit.Span.Fill(t), act = exit > 0 ? 0 : Activity(unit.Span, t);
            Color sung = second ? highlight : White;
            Color colour = Blend(Fade(sung, 0.32f), sung, fill);
            float slide = (1 - enter) * (second ? 40 : -40);
            DrawUnit(g, unit, unit.X + slide, unit.Baseline - act * 10 - exit * 26, 1 + 0.06f * act, unit.Tilt, colour,
                enter * (1 - exit), act * (second ? 0.7f : 0.45f), second ? highlight : White);
        }
    }

    private void LayoutCadenza(Graphics g, FoliaLine line)
    {
        float baseSize = line.Cjk ? 128 : 112;
        List<int> rows = new() { 0 };
        for (int pass = 0; pass < 10; pass++)
        {
            foreach (FoliaUnit unit in line.Units)
                ShapeUnit(g, unit, SerifFamily, FontStyle.Bold, baseSize * (line.Cjk ? 0.72f + 0.6f * Hash(unit.Seed, 5) : 0.85f + 0.3f * Hash(unit.Seed, 5)));
            rows = line.Units.Count > (line.Cjk ? 9 : 6) ? SplitRows(line, 0, 2) : new List<int> { 0 };
            float widest = rows.Count == 1 ? CadenzaWidth(line, 0, line.Units.Count)
                : Math.Max(CadenzaWidth(line, 0, rows[1]), CadenzaWidth(line, rows[1], line.Units.Count));
            float limit = rows.Count == 1 ? 1600 : 1680;
            if (widest <= limit || baseSize < 58) break;
            baseSize *= Math.Max(0.6f, limit / widest);
        }
        line.Size = baseSize;
        float centre = Centre(line);
        float gap = baseSize * 1.22f;
        for (int r = 0; r < rows.Count; r++)
        {
            int from = rows[r], to = r + 1 < rows.Count ? rows[r + 1] : line.Units.Count;
            float x = (LongWidth - CadenzaWidth(line, from, to)) / 2;
            float baseline = centre + baseSize * 0.36f + (r - (rows.Count - 1) / 2f) * gap;
            for (int i = from; i < to; i++)
            {
                FoliaUnit unit = line.Units[i];
                unit.X = x;
                unit.Baseline = baseline + (Hash(unit.Seed, 6) - 0.5f) * baseSize * (rows.Count == 1 ? 0.7f : 0.3f);
                unit.Row = r;
                x += unit.Width + CadenzaGap(unit, line);
            }
        }
    }

    private static float CadenzaGap(FoliaUnit unit, FoliaLine line) => unit.Size * (line.Cjk ? 0.2f : 0.3f) + SpaceAfter(unit, line.Cjk);

    private static float CadenzaWidth(FoliaLine line, int from, int to)
    {
        float w = 0;
        for (int i = from; i < to; i++) w += line.Units[i].Width + (i < to - 1 ? CadenzaGap(line.Units[i], line) : 0);
        return w;
    }

    private void DrawCadenza(Graphics g, FoliaLine line, float t, float since, float exit)
    {
        Color highlight = FoliaHighlight;
        foreach (FoliaUnit unit in line.Units)
        {
            float enter = EaseOutCubic((since - Hash(unit.Seed, 7) * 0.35f) / 0.4f);
            float fill = unit.Span.Fill(t), act = exit > 0 ? 0 : Activity(unit.Span, t);
            // A few sung glyphs keep a touch of the highlight.
            Color sung = Hash(unit.Seed, 8) < 0.2f ? Mix(White, highlight, 0.75f) : White;
            Color colour = Mix(Blend(Color.FromArgb(70, White), sung, fill), highlight, act);
            float cx = unit.X + unit.Width / 2, cy = unit.Baseline - unit.Size * 0.36f;
            float drift = exit * 60 * Math.Sign(cx - LongWidth / 2f);
            float opacity = enter * (1 - exit);
            DrawUnit(g, unit, unit.X + drift, unit.Baseline, (0.75f + 0.25f * enter) * (1 + 0.12f * act), 0, colour, opacity, act, highlight);
            if (act > 0.02f && opacity > 0.02f)
            {
                float radius = Math.Max(unit.Width, unit.Size * 0.82f) * 0.66f * (1 + 0.4f * (1 - act));
                using Pen ring = new(Color.FromArgb((int)(170 * act * opacity), highlight), 1.8f);
                g.DrawEllipse(ring, cx + drift - radius, cy - radius, 2 * radius, 2 * radius);
                using Pen halo = new(Color.FromArgb((int)(60 * act * opacity), highlight), 1.2f);
                float outer = radius * 1.32f;
                g.DrawEllipse(halo, cx + drift - outer, cy - outer, 2 * outer, 2 * outer);
            }
        }
    }

    // Phrases of up to three characters (words in Latin script) on three steps.
    private void LayoutPartita(Graphics g, FoliaLine line)
    {
        List<FoliaUnit> chunks = new();
        int i = 0;
        while (i < line.Units.Count)
        {
            int run = i;
            if (line.Cjk)
                while (run < line.Units.Count && line.Units[run].Text.Length == 1 && IsCjk(line.Units[run].Text[0])
                       && (run == i || !line.Units[run - 1].SpaceAfter))
                    run++;
            int length = Math.Max(1, run - i);
            if (line.Cjk && length > 3) length = length == 4 ? 2 : length % 3 == 1 ? 2 : 3;
            FoliaUnit first = line.Units[i], last = line.Units[i + length - 1];
            chunks.Add(new FoliaUnit
            {
                Text = string.Concat(line.Units.Skip(i).Take(length).Select(u => u.Text)),
                SpaceAfter = last.SpaceAfter,
                Span = LyricSpan.Union(first.Span, last.Span),
                Seed = first.Seed,
            });
            i += length;
        }
        line.Dispose();
        line.Units = chunks;
        float size = line.Cjk ? 92 : 82;
        for (int pass = 0; pass < 10; pass++)
        {
            ShapeAll(g, line, SerifFamily, FontStyle.Bold, size);
            float x = 0;
            foreach (FoliaUnit chunk in line.Units) x += chunk.Width + size * 0.42f;
            if (x <= 1640 || size < 54) break;
            size *= Math.Max(0.6f, 1640 / x);
        }
        float centre = Centre(line);
        float[] steps = { centre - size * 0.78f, centre + size * 0.32f, centre + size * 1.42f };
        float total = line.Units.Sum(c => c.Width + size * 0.42f) - size * 0.42f;
        float left = Math.Max(90, (LongWidth - total) / 2 - 40);
        for (int k = 0; k < line.Units.Count; k++)
        {
            FoliaUnit chunk = line.Units[k];
            chunk.X = left;
            chunk.Row = line.Units.Count <= 2 ? k + 1 : k % 3;
            chunk.Baseline = steps[chunk.Row];
            left += chunk.Width + size * 0.42f;
        }
    }

    private void DrawPartita(Graphics g, FoliaLine line, float t, float since, float exit)
    {
        Color highlight = FoliaHighlight;
        for (int i = 0; i < line.Units.Count; i++)
        {
            FoliaUnit chunk = line.Units[i];
            float enter = EaseOutCubic((since - i * 0.06f) / 0.4f);
            float reached = Smooth(chunk.Span.Start - 0.35f, chunk.Span.Start, t);
            float fill = chunk.Span.Fill(t), act = exit > 0 ? 0 : Activity(chunk.Span, t);
            float opacity = enter * (1 - exit) * (0.22f + 0.78f * reached);
            float x = chunk.X - (1 - reached) * 22 - exit * 40;
            // The guide line under the phrase, with ticks at both ends.
            Color guide = Mix(Color.FromArgb(90, White), Color.FromArgb(200, highlight), act);
            using (Pen pen = new(Fade(guide, opacity), 1.4f))
            {
                float y = chunk.Baseline + chunk.Size * 0.16f, x0 = x - 26, x1 = x + chunk.Width * (1 + 0.45f * act) + 70;
                g.DrawLine(pen, x0, y, x1, y);
                g.DrawLine(pen, x1, y - 12, x1, y + 12);
                g.DrawLine(pen, x0, y - 6, x0, y + 6);
            }
            Color colour = Mix(Blend(Color.FromArgb(150, White), White, fill), highlight, act);
            DrawUnit(g, chunk, x, chunk.Baseline, 1 + 0.45f * act, 0, colour, opacity, act, highlight, pivotLeft: true);
        }
    }

    // ---------- fume: the wall of lines ----------

    private const float FumeSize = 94, FumeSmall = 50;

    private void LayoutFumeLine(Graphics g, FoliaLine line)
    {
        ShapeAll(g, line, SerifFamily, FontStyle.Bold, FumeSize);
        float width = RowWidth(line, 0, line.Units.Count);
        if (width > 1720) ShapeAll(g, line, SerifFamily, FontStyle.Bold, Math.Max(56, FumeSize * 1720 / width));
        float x = 0;
        foreach (FoliaUnit unit in line.Units)
        {
            unit.X = x;
            x += unit.Width + SpaceAfter(unit, line.Cjk);
        }
    }

    private static float FumeIndent(int index) => (int)(Hash(index, 9) * 3) * 150;

    private float FumeBaseline(float r, bool translated)
    {
        float centre = translated ? 214 : 236;
        return r >= 0
            ? centre + Math.Min(r, 1) * (translated ? 150 : 108) + Math.Max(r - 1, 0) * 64
            : centre + Math.Max(r, -1) * 112 + Math.Min(r + 1, 0) * 64;
    }

    private void DrawFume(Graphics g, MusicSnapshot music, FoliaLine current, float t, float since)
    {
        Color highlight = FoliaHighlight;
        float e = EaseOutCubic(since / 0.6f);
        bool translated = current.Translation.Length > 0;
        int index = music.LyricIndex;
        for (int k = -4; k <= 5; k++)
        {
            int at = index + k;
            if (at < 0 || at >= music.AllLyrics.Length) continue;
            float r = k + (1 - e);
            float near = Math.Min(Math.Abs(r), 1);
            float alpha = Math.Abs(r) <= 1 ? Lerp(1, 0.34f, near) : Math.Max(0, 0.34f - (Math.Abs(r) - 1) * 0.09f);
            if (alpha <= 0.01f) continue;
            float baseline = FumeBaseline(r, translated);
            float x = 96 + FumeIndent(at) * near;
            if (k == 0)
            {
                float scale = Lerp(current.Size, FumeSmall, near) / current.Size;
                GraphicsState state = g.Save();
                g.TranslateTransform(x, baseline);
                g.ScaleTransform(scale, scale);
                DrawFumeUnits(g, current, t, alpha, highlight);
                g.Restore(state);
                if (translated)
                {
                    using Font font = new(CjkFamily, 32, FontStyle.Bold, GraphicsUnit.Pixel);
                    DrawAt(g, Ellipsize(g, current.Translation, font, 1700), font,
                        Fade(Mix(highlight, White, 0.35f), e * 0.85f), x + 4, baseline + 58);
                }
                continue;
            }
            float size = Lerp(FumeSize, FumeSmall, near);
            using Font rowFont = new(SerifFamily, size, FontStyle.Bold, GraphicsUnit.Pixel);
            string text = Ellipsize(g, music.AllLyrics[at], rowFont, LongWidth - x - 40);
            DrawAt(g, text, rowFont, Fade(k < 0 ? Mix(White, highlight, 0.5f * (1 - near)) : White, alpha * 0.9f), x, baseline);
        }
    }

    private void DrawFumeUnits(Graphics g, FoliaLine line, float t, float opacity, Color highlight)
    {
        foreach (FoliaUnit unit in line.Units)
        {
            float fill = unit.Span.Fill(t), act = Activity(unit.Span, t);
            Color colour = Blend(Color.FromArgb(225, White), highlight, fill);
            DrawUnit(g, unit, unit.X, 0, 1, 0, colour, opacity, Math.Max(act, fill * 0.25f), highlight);
            if (act > 0.02f)
            {
                using Brush bar = new SolidBrush(Fade(highlight, act * opacity * 0.9f));
                g.FillRectangle(bar, unit.X, unit.Size * 0.14f, unit.Width * Math.Max(0.15f, fill), unit.Size * 0.07f);
            }
        }
    }

    // Fume without a song timeline (before the first line): the line alone.
    private void DrawFumeAlone(Graphics g, FoliaLine line, float t, float since, float exit)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(96, 236 - exit * 60);
        DrawFumeUnits(g, line, t, EaseOutCubic(since / 0.4f) * (1 - exit), FoliaHighlight);
        g.Restore(state);
    }

    private void DrawFoliaSubtitle(Graphics g, string text, float since)
    {
        if (text.Length == 0) return;
        using Font font = new(CjkFamily, 32, FontStyle.Bold, GraphicsUnit.Pixel);
        string fitted = Ellipsize(g, text, font, 1700);
        float width = Measure(g, fitted, font);
        DrawAt(g, fitted, font, Fade(Mix(FoliaHighlight, White, 0.4f), 0.82f * EaseOutCubic(since / 0.45f)),
            (LongWidth - width) / 2, FoliaSubtitleBaseline);
    }
}
