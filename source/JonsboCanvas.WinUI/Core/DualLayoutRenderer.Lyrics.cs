using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

// Long-screen lyrics. Three looks share the layout, karaoke wipe and scroll
// animation:
//   apple   - slowly flowing blur of the cover's colours, white type, the
//             previous and next lines small and dim around the current one;
//   spotify - a flat colour from the cover, sung words white, the rest dark;
//   bigtype - one line at a time, as large as the screen allows, outlined
//             until it is sung.
internal sealed partial class DualLayoutRenderer
{
    // Read from the config once per frame so the settings page can switch it
    // while a frame is being drawn.
    private string LyricStyle { get; set; } = "apple";

    private string _songId = "";
    private string _layoutStyle = "";
    private string _currentLyric = "";
    private string _nextLyric = "";
    private DateTime _lyricChangedAt = DateTime.MinValue;
    private bool _titleOnly;
    private bool _scrolledFromNext;
    private LyricLayout? _olderLayout;
    private LyricLayout? _previousLayout;
    private LyricLayout? _currentLayout;
    private LyricLayout? _nextLayout;
    private Bitmap? _flowBase;
    private Bitmap? _flowOverlay;
    private readonly Stopwatch _flowClock = Stopwatch.StartNew();

    private const float ScrollSeconds = 0.55f;
    private const float NeighbourScale = 0.42f;
    private const float LyricCentre = 231;
    private const float TranslationSize = 42;
    private const float TranslationGap = 70;
    private const int FlowMargin = 240;

    private sealed record LyricLook(string Family, FontStyle Style, float ScaleX, bool Upper, float MaxSize, float MinSize,
        float Left, float Width, float MaxBlock, float CapRatio, float LineRatio);

    private LyricLook Look(bool cjk) => (LyricStyle, cjk) switch
    {
        ("bigtype", true) => new(CjkFamily, FontStyle.Bold, 1f, false, 300, 110, 84, 1750, 330, 0.8f, 1.16f),
        ("bigtype", false) => new("Impact", FontStyle.Regular, 1f, true, 340, 110, 84, 1750, 370, 0.8f, 0.98f),
        (_, true) => new(CjkFamily, FontStyle.Bold, 1f, false, 104, 56, 110, 1480, 230, 0.72f, 1.24f),
        _ => new(LatinLyricFamily, FontStyle.Regular, 0.88f, false, 104, 56, 110, 1480, 230, 0.72f, 1.06f),
    };

    private bool ShowsNeighbours => LyricStyle != "bigtype";

    private (Color Sung, Color Unsung, Color Past, Color Upcoming, Color Translation) LyricPalette => LyricStyle switch
    {
        "spotify" => (White, Color.FromArgb(165, 10, 10, 14), Color.FromArgb(175, White), Color.FromArgb(150, 10, 10, 14),
            Color.FromArgb(225, White)),
        "bigtype" => (White, Color.FromArgb(150, White), Color.FromArgb(150, White), Color.FromArgb(120, White), HighlightColor),
        _ => (White, Color.FromArgb(110, White), Color.FromArgb(100, White), Color.FromArgb(100, White), HighlightColor),
    };

    // ---------- backgrounds ----------

    private Bitmap RenderLyricLong(MusicSnapshot? music)
    {
        string style = AppConfig.NormalizeLyricStyle(_config.LyricStyle);
        if (style != LyricStyle)
        {
            LyricStyle = style;
            DisposeMusicLayers();
        }
        DateTime now = DateTime.UtcNow;
        if (IsFoliaStyle(LyricStyle)) return RenderFolia(music, now);
        Bitmap image = new(LongWidth, LongHeight, PixelFormat.Format32bppArgb);
        using Graphics g = Prepare(image);
        if (LyricStyle == "apple") DrawFlow(g);
        else g.DrawImageUnscaled(LyricLongLayer(), 0, 0);
        SyncLyric(g, music, now);
        float since = _lyricChangedAt == DateTime.MinValue ? float.MaxValue : (float)(now - _lyricChangedAt).TotalSeconds;
        DrawLyrics(g, music, EaseOutCubic(since / ScrollSeconds));
        DrawGrain(g, LongWidth, LongHeight);
        return image;
    }

    private Bitmap LyricLongLayer() => _lyricLongLayer ??= LyricStyle == "spotify" ? SpotifyLayer() : BigTypeLayer();

    // The cover squeezed into a few pixels and stretched back up: large soft
    // fields of its colours, scaled to a fixed mean luminance so white type
    // reads on light and dark covers alike. Two such fields drift against
    // each other.
    private Bitmap ColourField(int columns, int rows, bool flipped, float luminance, float saturation, float alpha)
    {
        using Bitmap tiny = new(columns, rows, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(tiny))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            if (flipped) { g.TranslateTransform(columns, rows); g.RotateTransform(180); }
            g.DrawImage(_art!, new Rectangle(0, 0, columns, rows));
        }
        float mean = (float)ReadPixels(tiny).Average(p => 0.2126 * (p >> 16 & 0xFF) + 0.7152 * (p >> 8 & 0xFF) + 0.0722 * (p & 0xFF)) / 255;
        float brightness = Math.Clamp(luminance / Math.Max(0.02f, mean), 0.35f, 2.2f);
        Bitmap field = new(LongWidth + 2 * FlowMargin, LongHeight + FlowMargin, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(field))
        using (ImageAttributes attributes = new())
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            attributes.SetColorMatrix(Saturate(saturation, brightness, alpha));
            g.DrawImage(tiny, new Rectangle(0, 0, field.Width, field.Height), 0, 0, columns, rows, GraphicsUnit.Pixel, attributes);
        }
        return field;
    }

    private static ColorMatrix Saturate(float saturation, float brightness, float alpha)
    {
        const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
        float s = saturation, k = brightness;
        return new ColorMatrix(new[]
        {
            new[] { (lr * (1 - s) + s) * k, lr * (1 - s) * k, lr * (1 - s) * k, 0f, 0f },
            new[] { lg * (1 - s) * k, (lg * (1 - s) + s) * k, lg * (1 - s) * k, 0f, 0f },
            new[] { lb * (1 - s) * k, lb * (1 - s) * k, (lb * (1 - s) + s) * k, 0f, 0f },
            new[] { 0f, 0f, 0f, alpha, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
    }

    private void DrawFlow(Graphics g)
    {
        _flowBase ??= ColourField(5, 3, false, 0.3f, 1.75f, 1f);
        _flowOverlay ??= ColourField(4, 3, true, 0.34f, 1.9f, 0.55f);
        float t = (float)_flowClock.Elapsed.TotalSeconds;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(_flowBase, (int)(-FlowMargin * (1 + MathF.Sin(t * 0.11f))), (int)(-FlowMargin / 2f * (1 + MathF.Sin(t * 0.07f))));
        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawImageUnscaled(_flowOverlay, (int)(-FlowMargin * (1 + MathF.Cos(t * 0.083f))), (int)(-FlowMargin / 2f * (1 + MathF.Cos(t * 0.13f))));
        using Brush veil = new SolidBrush(Color.FromArgb(28, 0, 0, 0));
        g.FillRectangle(veil, 0, 0, LongWidth, LongHeight);
    }

    private Bitmap SpotifyLayer()
    {
        Bitmap layer = new(LongWidth, LongHeight, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(layer);
        float saturation = Math.Clamp(_accent.GetSaturation(), 0.42f, 0.72f);
        using Brush fill = new SolidBrush(FromHsl(_accent.GetHue(), saturation, 0.37f));
        g.FillRectangle(fill, 0, 0, LongWidth, LongHeight);
        return layer;
    }

    private Bitmap BigTypeLayer()
    {
        Bitmap layer = new(LongWidth, LongHeight, PixelFormat.Format32bppArgb);
        using Graphics g = Prepare(layer);
        using (Brush fill = new SolidBrush(Mix(ColorTranslator.FromHtml("#09090C"), _accent, 0.08f)))
            g.FillRectangle(fill, 0, 0, LongWidth, LongHeight);
        using Bitmap field = ColourField(5, 3, false, 0.26f, 1.7f, 0.45f);
        g.DrawImageUnscaled(field, -FlowMargin, -FlowMargin / 2);
        return layer;
    }

    private void DisposeLyricLayers()
    {
        _flowBase?.Dispose(); _flowBase = null;
        _flowOverlay?.Dispose(); _flowOverlay = null;
        DisposeFolia();
    }

    private static Color FromHsl(float hue, float saturation, float lightness)
    {
        float c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        float h = hue / 60f, x = c * (1 - Math.Abs(h % 2 - 1)), m = lightness - c / 2;
        (float r, float g, float b) = h switch
        {
            < 1 => (c, x, 0f), < 2 => (x, c, 0f), < 3 => (0f, c, x), < 4 => (0f, x, c), < 5 => (x, 0f, c), _ => (c, 0f, x),
        };
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    // ---------- placement and animation ----------

    private void DrawLyrics(Graphics g, MusicSnapshot? music, float e)
    {
        var palette = LyricPalette;
        if (_currentLayout == null) return;

        if (ShowsNeighbours)
        {
            // The line before last leaves over the top while the last line
            // shrinks into the slot above the current one.
            if (e < 1 && _olderLayout != null && _previousLayout != null)
            {
                float top = PreviousTop(_previousLayout, _olderLayout) - e * 60;
                DrawLyric(g, _olderLayout, top, NeighbourScale, (1 - e) * EdgeFade(_olderLayout, top, NeighbourScale), 1, palette.Past, palette.Past);
            }
            if (_previousLayout != null)
            {
                float top = Lerp(CurrentTop(_previousLayout), PreviousTop(_currentLayout, _previousLayout), e);
                float scale = Lerp(1, NeighbourScale, e);
                Color colour = Blend(palette.Sung, palette.Past, e);
                float baseline = DrawLyric(g, _previousLayout, top, scale, EdgeFade(_previousLayout, top, scale), 1, colour, colour);
                if (e < 1) DrawTranslation(g, _previousLayout, baseline, 1 - e);
            }
        }
        else if (_previousLayout != null && e < 1)
        {
            float top = CurrentTop(_previousLayout) - e * (BlockHeight(_previousLayout) * 0.6f + 140);
            float fade = MathF.Pow(1 - e, 3);
            DrawLyric(g, _previousLayout, top, 1, fade, 1, palette.Sung, palette.Unsung, outline: true);
        }

        float currentTop = CurrentTop(_currentLayout), currentScale = 1, opacity = 1;
        if (e < 1 && _scrolledFromNext && _previousLayout != null)
        {
            currentTop = Lerp(NextTop(_previousLayout), currentTop, e);
            currentScale = Lerp(NeighbourScale, 1, e);
        }
        else if (e < 1)
        {
            currentTop += (1 - e) * (ShowsNeighbours ? 40 : 90);
            opacity = ShowsNeighbours ? e : e * e;
        }
        Color unsung = _scrolledFromNext ? Blend(palette.Upcoming, palette.Unsung, e) : palette.Unsung;
        float lastBaseline = DrawLyric(g, _currentLayout, currentTop, currentScale, opacity, 1, palette.Sung, unsung,
            outline: LyricStyle == "bigtype", fills: _titleOnly ? null : TokenFills(music, _currentLayout));

        if (_titleOnly)
        {
            string caption = string.Join("  ·  ", new[] { music?.Artist, music?.Album }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            using Font captionFont = new(CjkFamily, 36, FontStyle.Bold, GraphicsUnit.Pixel);
            DrawAt(g, Ellipsize(g, caption, captionFont, _currentLayout.Width), captionFont,
                Color.FromArgb((int)(palette.Translation.A * opacity), palette.Translation), _currentLayout.Left + 4, lastBaseline + 70);
        }
        else if (_currentLayout.Translation.Length > 0)
        {
            DrawTranslation(g, _currentLayout, lastBaseline, e < 1 && _scrolledFromNext ? e : opacity);
        }
        else if (_nextLayout != null && ShowsNeighbours)
        {
            float top = NextTop(_currentLayout) + (1 - e) * 40;
            DrawLyric(g, _nextLayout, top, NeighbourScale, e * EdgeFade(_nextLayout, top, NeighbourScale), 0, palette.Upcoming, palette.Upcoming);
        }
    }

    private void DrawTranslation(Graphics g, LyricLayout layout, float lastBaseline, float opacity)
    {
        if (layout.Translation.Length == 0 || opacity <= 0.01f) return;
        Color colour = LyricPalette.Translation;
        using Font font = new(CjkFamily, TranslationSize, FontStyle.Bold, GraphicsUnit.Pixel);
        DrawAt(g, Ellipsize(g, layout.Translation, font, layout.Width), font,
            Color.FromArgb((int)(colour.A * opacity), colour), layout.Left + 4, lastBaseline + TranslationGap);
    }

    // Neighbour lines fade out towards the top and bottom edges.
    private static float EdgeFade(LyricLayout layout, float top, float scale)
    {
        float bottom = top + BlockHeight(layout) * scale;
        return Math.Min(Smooth(-40, 30, top), Smooth(LongHeight + 30, LongHeight - 30, bottom));
    }

    private static float BlockHeight(LyricLayout layout) => layout.CapHeight + (layout.Lines - 1) * layout.LineHeight;

    // The line and its translation are centred together. Chinese glyphs hang
    // well below the baseline, so their block counts that descent.
    private float CurrentTop(LyricLayout layout) =>
        LyricCentre - (_titleOnly ? 36 : 0) - (BlockHeight(layout) + (layout.Cjk ? layout.Size * 0.3f : 0)
            + (layout.Translation.Length > 0 ? TranslationGap : 0)) / 2;

    private float NextTop(LyricLayout current) => CurrentTop(current) + BlockHeight(current) + 52;

    private float PreviousTop(LyricLayout current, LyricLayout previous) =>
        CurrentTop(current) - 46 - BlockHeight(previous) * NeighbourScale;

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.A + (to.A - from.A) * amount), (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));

    private static float EaseOutCubic(float t)
    {
        t = Math.Clamp(t, 0, 1);
        return 1 - (1 - t) * (1 - t) * (1 - t);
    }

    private void SyncLyric(Graphics g, MusicSnapshot? music, DateTime now)
    {
        string songId = music?.SongId ?? "";
        // The collector reports "♪  title" for songs without lyrics and before the first line.
        _titleOnly = IsPlaceholder(music?.CurrentLyric);
        string lyric = _titleOnly ? music?.Title ?? T("等待音乐", "Waiting for music") : music!.CurrentLyric;
        string translation = _titleOnly ? "" : (music!.CurrentTranslation ?? "").Trim();
        // A translated line keeps the space below for its translation.
        string next = _titleOnly || translation.Length > 0 || IsPlaceholder(music?.NextLyric) ? "" : music!.NextLyric.Trim();
        if (songId != _songId || LyricStyle != _layoutStyle)
        {
            _songId = songId;
            _layoutStyle = LyricStyle;
            _olderLayout = _previousLayout = _nextLayout = null;
            _nextLyric = "";
            _currentLayout = LayoutLyric(g, lyric, Reserve(translation));
            _currentLyric = lyric;
            _lyricChangedAt = DateTime.MinValue;
        }
        else if (!string.Equals(lyric, _currentLyric, StringComparison.Ordinal) || _currentLayout == null)
        {
            _scrolledFromNext = _nextLayout != null && string.Equals(lyric.Trim(), _nextLyric, StringComparison.Ordinal);
            _olderLayout = _previousLayout;
            _previousLayout = _titleOnly || IsPlaceholder(_currentLyric) ? null : _currentLayout;
            _currentLayout = _scrolledFromNext ? _nextLayout! : LayoutLyric(g, lyric, Reserve(translation));
            _currentLyric = lyric;
            if (_scrolledFromNext) { _nextLayout = null; _nextLyric = ""; }
            _lyricChangedAt = now;
        }
        if (_currentLayout.Reserve != Reserve(translation))
            _currentLayout = LayoutLyric(g, lyric, Reserve(translation));
        _currentLayout.Translation = translation;
        if (!string.Equals(next, _nextLyric, StringComparison.Ordinal))
        {
            _nextLyric = next;
            _nextLayout = next.Length == 0 ? null : LayoutLyric(g, next, 0);
        }
    }

    // Height a translated line gives up for its translation; the neighbour
    // styles keep it below the block anyway.
    private float Reserve(string translation) => translation.Length > 0 && !ShowsNeighbours ? TranslationGap + 20 : 0;

    private static bool IsPlaceholder(string? lyric) => string.IsNullOrWhiteSpace(lyric) || lyric.TrimStart().StartsWith('♪');

    private static float[] TokenFills(MusicSnapshot? music, LyricLayout layout)
    {
        LyricSpan[] spans = TokenSpans(layout.Tokens.Select(t => t.Text).ToList(), music, layout.Cjk);
        float elapsed = LineElapsed(music);
        return spans.Select(span => span.Fill(elapsed)).ToArray();
    }

    // ---------- layout ----------

    // Offset is the token's start along the line's ink, ignoring spaces, so the
    // karaoke wipe advances at an even speed.
    private sealed record LyricToken(string Text, float X, int Line, float Width, float Offset);
    private sealed record LyricLayout(List<LyricToken> Tokens, int Lines, LyricLook Look, float Size)
    {
        public int TokenCount => Tokens.Count;
        public float InkWidth => Tokens.Count == 0 ? 0 : Tokens[^1].Offset + Tokens[^1].Width;
        public bool Cjk => Look.Family == CjkFamily;
        public float LineHeight => Size * Look.LineRatio;
        public float CapHeight => Size * Look.CapRatio;
        public float Left => Look.Left;
        public float Width => Look.Width;
        public string Translation { get; set; } = "";
        public float Reserve { get; init; }
    }

    internal static List<(string Text, bool SpaceAfter)> Tokenize(string value)
    {
        List<(string, bool)> tokens = new();
        System.Text.StringBuilder word = new();
        void Flush(bool space)
        {
            if (word.Length > 0) { tokens.Add((word.ToString(), space)); word.Clear(); }
            else if (space && tokens.Count > 0) tokens[^1] = (tokens[^1].Item1, true);
        }
        foreach (char c in value.Trim())
        {
            if (char.IsWhiteSpace(c)) Flush(true);
            else if (IsCjk(c)) { Flush(false); tokens.Add((c.ToString(), false)); }
            else word.Append(c);
        }
        Flush(false);
        return tokens;
    }

    private static bool IsCjk(char c) =>
        c is >= '\u3000' and <= '\u9FFF' or >= '\uAC00' and <= '\uD7AF' or >= '\uFF00' and <= '\uFFEF';

    // The largest size at which the line fits in two lines within the look's
    // width and height.
    private LyricLayout LayoutLyric(Graphics g, string value, float reserve)
    {
        LyricLook look = Look(value.Any(IsCjk));
        var tokens = Tokenize(look.Upper ? value.ToUpperInvariant() : value);
        for (float size = look.MaxSize; ; size -= 4)
        {
            using Font font = new(look.Family, size, look.Style, GraphicsUnit.Pixel);
            float space = Measure(g, " ", font) * look.ScaleX;
            float[] widths = tokens.Select(t => Measure(g, t.Text, font) * look.ScaleX).ToArray();
            float[] gaps = tokens.Select(t => t.SpaceAfter ? space : 0).ToArray();
            List<int> breaks = Wrap(widths, gaps, look.Width);
            if (breaks.Count == 1)
                breaks = BalanceTwoLines(widths, gaps, breaks[0], look.Width);
            float block = size * look.CapRatio + breaks.Count * size * look.LineRatio;
            if ((breaks.Count <= 1 && block <= look.MaxBlock - reserve) || size <= look.MinSize)
                return Place(tokens, widths, gaps, breaks, look, size) with { Reserve = reserve };
        }
    }

    // Greedy wrap; returns the token indices that start a new line.
    private static List<int> Wrap(float[] widths, float[] gaps, float maxWidth)
    {
        List<int> breaks = new();
        float x = 0;
        for (int i = 0; i < widths.Length; i++)
        {
            if (x > 0 && x + widths[i] > maxWidth) { breaks.Add(i); x = 0; }
            x += widths[i] + gaps[i];
        }
        return breaks;
    }

    // Picks the two-line break with the most even lines, leaning towards a
    // shorter first line, and preferring breaks at spaces so Chinese phrases
    // separated by a space stay whole.
    private static List<int> BalanceTwoLines(float[] widths, float[] gaps, int greedyBreak, float maxWidth)
    {
        float LineWidth(int from, int to)
        {
            float w = 0;
            for (int i = from; i < to; i++) w += widths[i] + (i < to - 1 ? gaps[i] : 0);
            return w;
        }
        int best = greedyBreak;
        float bestScore = float.MaxValue;
        for (int b = 1; b <= greedyBreak; b++)
        {
            float first = LineWidth(0, b), second = LineWidth(b, widths.Length);
            if (Math.Max(first, second) > maxWidth) continue;
            float score = Math.Max(first * 1.45f, second) + (gaps[b - 1] > 0 ? 0 : maxWidth);
            if (score < bestScore) { best = b; bestScore = score; }
        }
        return new List<int> { best };
    }

    private static LyricLayout Place(List<(string Text, bool SpaceAfter)> tokens, float[] widths, float[] gaps,
        List<int> breaks, LyricLook look, float size)
    {
        List<LyricToken> placed = new();
        int line = 0;
        float x = 0, offset = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (breaks.Contains(i)) { line++; x = 0; }
            placed.Add(new LyricToken(tokens[i].Text, x, line, widths[i], offset));
            x += widths[i] + gaps[i];
            offset += widths[i];
        }
        return new LyricLayout(placed, line + 1, look, size);
    }

    // ---------- drawing ----------

    // Draws the layout with its first cap top at top, scaled about its left edge.
    // fills is each token's sung fraction; without it every token is sung
    // (sung 1) or unsung (sung 0). outline draws unsung words as outlines.
    // Returns the last baseline.
    private float DrawLyric(Graphics g, LyricLayout layout, float top, float scale, float opacity, float sung,
        Color sungColour, Color unsungColour, bool outline = false, float[]? fills = null)
    {
        float lastBaseline = top + BlockHeight(layout) * scale;
        if (opacity <= 0.01f) return lastBaseline;
        LyricLook look = layout.Look;
        using Font font = new(look.Family, layout.Size, look.Style, GraphicsUnit.Pixel);
        float ascent = Ascent(font);
        float wiped = sung * layout.InkWidth;
        bool shadow = LyricStyle == "apple";
        using Brush bright = new SolidBrush(Color.FromArgb((int)(sungColour.A * opacity), sungColour));
        using Brush faded = new SolidBrush(Color.FromArgb((int)(unsungColour.A * opacity), unsungColour));
        using Brush shade = new SolidBrush(Color.FromArgb((int)(60 * opacity), 0, 0, 0));
        using Pen stroke = new(Color.FromArgb((int)(unsungColour.A * opacity), unsungColour), 3f) { LineJoin = LineJoin.Round };
        GraphicsState outer = g.Save();
        g.TranslateTransform(layout.Left, top);
        g.ScaleTransform(scale, scale);
        for (int index = 0; index < layout.Tokens.Count; index++)
        {
            LyricToken token = layout.Tokens[index];
            float baseline = layout.CapHeight + token.Line * layout.LineHeight;
            float fill = fills != null && index < fills.Length ? fills[index]
                : Math.Clamp((wiped - token.Offset) / Math.Max(1, token.Width), 0, 1);
            GraphicsState state = g.Save();
            g.TranslateTransform(token.X, baseline - ascent);
            g.ScaleTransform(look.ScaleX, 1);
            using GraphicsPath? path = outline ? new GraphicsPath() : null;
            path?.AddString(token.Text, font.FontFamily, (int)font.Style, layout.Size, PointF.Empty, Typographic);
            if (shadow) g.DrawString(token.Text, font, shade, 0, 3, Typographic);
            if (fill < 1)
            {
                if (path != null) g.DrawPath(stroke, path);
                else g.DrawString(token.Text, font, faded, 0, 0, Typographic);
            }
            if (fill > 0)
            {
                if (fill < 1)
                    g.SetClip(new RectangleF(-4, -layout.Size, (token.Width / look.ScaleX + 8) * fill, layout.Size * 3));
                if (path != null) g.FillPath(bright, path);
                else g.DrawString(token.Text, font, bright, 0, 0, Typographic);
            }
            g.Restore(state);
        }
        g.Restore(outer);
        return lastBaseline;
    }
}
