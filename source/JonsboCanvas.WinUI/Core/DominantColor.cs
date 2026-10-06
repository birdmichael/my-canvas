using System.Drawing;
using System.Drawing.Drawing2D;

namespace JonsboCanvas_WinUI;

// Extracts a vibrant, representative color from album art, following the common
// industry approach (Android Palette / Vibrant.js): downscale, quantize into
// buckets, then score each bucket by saturation weighted by how often it appears.
internal static class DominantColor
{
    public static Color FromBitmap(Bitmap? source)
    {
        if (source == null || source.Width < 2 || source.Height < 2) return Color.Empty;
        try
        {
            const int size = 40;
            using var small = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(source, 0, 0, size, size);
            }

            var buckets = new Dictionary<int, (int count, long r, long g, long b)>();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color c = small.GetPixel(x, y);
                    if (c.A < 128) continue;
                    int key = ((c.R >> 4) << 8) | ((c.G >> 4) << 4) | (c.B >> 4);
                    buckets.TryGetValue(key, out var cur);
                    buckets[key] = (cur.count + 1, cur.r + c.R, cur.g + c.G, cur.b + c.B);
                }

            Color best = Color.Empty;
            double bestScore = -1;
            Color mostCommon = Color.Empty;
            int mostCommonCount = -1;
            foreach (var bucket in buckets.Values)
            {
                int count = bucket.count;
                byte r = (byte)(bucket.r / count), g = (byte)(bucket.g / count), b = (byte)(bucket.b / count);
                if (count > mostCommonCount) { mostCommonCount = count; mostCommon = Color.FromArgb(r, g, b); }
                ToHsv(r, g, b, out double h, out double s, out double v);
                // Favor saturated, mid-bright colors; strongly avoid near-black washes.
                double score = count * (0.15 + s) * (v < 0.12 ? 0.1 : 1.0) * (v > 0.95 && s < 0.1 ? 0.2 : 1.0);
                if (score > bestScore) { bestScore = score; best = Color.FromArgb(r, g, b); }
            }

            Color pick = bestScore > 0 ? best : mostCommon;
            if (pick.IsEmpty) return Color.Empty;
            return BoostForLeds(pick);
        }
        catch { return Color.Empty; }
    }

    // Extracts up to 'count' visually distinct, vibrant colors for music-reactive
    // lighting. Colors must differ in hue by >= 25 degrees so switching between
    // them is clearly visible even at ~10 FPS controller cadence.
    public static Color[] FromBitmapMulti(Bitmap? source, int count = 3)
    {
        if (source == null || source.Width < 2 || source.Height < 2 || count <= 0)
            return Array.Empty<Color>();
        try
        {
            const int size = 40;
            using var small = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(source, 0, 0, size, size);
            }

            var buckets = new Dictionary<int, (int count, long r, long g, long b)>();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color c = small.GetPixel(x, y);
                    if (c.A < 128) continue;
                    int key = ((c.R >> 4) << 8) | ((c.G >> 4) << 4) | (c.B >> 4);
                    buckets.TryGetValue(key, out var cur);
                    buckets[key] = (cur.count + 1, cur.r + c.R, cur.g + c.G, cur.b + c.B);
                }

            // Score every bucket the same way as the single-color pick, then take
            // the best candidates whose hues are mutually far enough apart.
            var candidates = new List<(double score, double hue, Color color)>();
            foreach (var bucket in buckets.Values)
            {
                int n = bucket.count;
                byte r = (byte)(bucket.r / n), g = (byte)(bucket.g / n), b = (byte)(bucket.b / n);
                ToHsv(r, g, b, out double h, out double s, out double v);
                if (v < 0.12 || (v > 0.95 && s < 0.1) || s < 0.15) continue;
                double score = n * (0.15 + s);
                candidates.Add((score, h, BoostForLeds(Color.FromArgb(r, g, b))));
            }
            candidates.Sort((a, b) => b.score.CompareTo(a.score));

            var picked = new List<Color>();
            foreach (var candidate in candidates)
            {
                if (picked.Count >= count) break;
                bool distinct = true;
                foreach (Color existing in picked)
                {
                    ToHsv(existing.R, existing.G, existing.B, out double eh, out _, out _);
                    double delta = Math.Abs(candidate.hue - eh);
                    if (delta > 180) delta = 360 - delta;
                    if (delta < 25) { distinct = false; break; }
                }
                if (distinct) picked.Add(candidate.color);
            }
            return picked.ToArray();
        }
        catch { return Array.Empty<Color>(); }
    }

    // The overall tone of a photo for ambient lighting: the hue and saturation of
    // its chroma-weighted mean at full LED value (so a grey scene gives a cool
    // white rather than a forced vivid colour), and its mean luminance 0..1.
    public static (Color Color, double Luminance) Ambient(Bitmap? source)
    {
        if (source == null || source.Width < 2 || source.Height < 2) return (Color.Empty, 0);
        try
        {
            const int size = 48;
            using var small = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(source, 0, 0, size, size);
            }
            double luminance = 0, weight = 0, r = 0, gr = 0, b = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color c = small.GetPixel(x, y);
                    luminance += (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
                    ToHsv(c.R, c.G, c.B, out _, out double s, out double v);
                    double w = 0.05 + s * v;
                    weight += w; r += c.R * w; gr += c.G * w; b += c.B * w;
                }
            ToHsv((byte)(r / weight), (byte)(gr / weight), (byte)(b / weight), out double hue, out double saturation, out _);
            return (FromHsv(hue, Math.Clamp(saturation * 1.3, 0, 1), 1), luminance / (size * size));
        }
        catch { return (Color.Empty, 0); }
    }

    // LEDs look dull with desaturated or dark colors; lift saturation/value so the
    // album color reads clearly on the case lighting.
    private static Color BoostForLeds(Color c)
    {
        ToHsv(c.R, c.G, c.B, out double h, out double s, out double v);
        s = Math.Clamp(s * 1.25 + 0.25, 0.35, 1.0);
        v = Math.Clamp(v * 0.7 + 0.35, 0.55, 1.0);
        return FromHsv(h, s, v);
    }

    private static void ToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
        double d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == rd) h = ((gd - bd) / d) % 6;
        else if (max == gd) h = (bd - rd) / d + 2;
        else h = (rd - gd) / d + 4;
        h *= 60; if (h < 0) h += 360;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        int i = (int)Math.Floor(h / 60) % 6;
        double f = h / 60 - Math.Floor(h / 60);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        (double r, double g, double b) = i switch
        {
            0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t),
            3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q),
        };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}
