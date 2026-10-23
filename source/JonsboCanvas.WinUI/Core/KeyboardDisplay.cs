using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

// The ROG Azoth 96 HE shows a 184×97 landscape picture. The Gear Link protocol
// accepts the same little-endian RGB565 stream used by its WebHID uploader.
internal static class KeyboardDisplay
{
    public const int Width = 184;
    public const int Height = 97;
    public const int PayloadLength = 4 + Width * Height * 2;

    internal static bool UsesMusic(string mode, bool musicPlaying, bool longMusic, bool squareMusic) => mode switch
    {
        "wallpaper" => false,
        "music" => true,
        _ => musicPlaying || longMusic || squareMusic,
    };

    internal static string ContentKey(WallpaperSnapshot snapshot) =>
        snapshot.Id.Split('|')[0];

    internal static (Image Image, string Id)? SelectContent(
        bool musicMode, MusicSnapshot? music, WallpaperSnapshot? wallpaper)
    {
        if (musicMode)
        {
            // Keep the previous image while the new song's cover is loading.
            if (music?.Available != true || music.Cover == null) return null;
            string track = string.IsNullOrWhiteSpace(music.SongId)
                ? $"{music.Title}|{music.Artist}|{music.Album}" : music.SongId;
            return (music.Cover, "music-cover:" + track);
        }
        return wallpaper == null ? null : (wallpaper.Image, "wallpaper:" + ContentKey(wallpaper));
    }

    public static Bitmap Render(Image source)
    {
        Bitmap target = new(Width, Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Black);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;

        double scale = Math.Max(Width / (double)source.Width, Height / (double)source.Height);
        int drawWidth = Math.Max(Width, (int)Math.Ceiling(source.Width * scale));
        int drawHeight = Math.Max(Height, (int)Math.Ceiling(source.Height * scale));
        graphics.DrawImage(source, new Rectangle(
            (int)Math.Floor((Width - drawWidth) / 2.0),
            (int)Math.Floor((Height - drawHeight) / 2.0),
            drawWidth,
            drawHeight));
        return target;
    }

    public static byte[] BuildPayload(Image source)
    {
        using Bitmap render = Render(source);
        return EncodeFrame(render);
    }

    internal static byte[] EncodeFrame(Bitmap render)
    {
        if (render.Width != Width || render.Height != Height)
            throw new ArgumentException($"Keyboard frame must be {Width}×{Height}.", nameof(render));
        BitmapData data = render.LockBits(
            new Rectangle(0, 0, Width, Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[Width * Height * 4];
        try
        {
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            render.UnlockBits(data);
        }

        byte[] payload = new byte[PayloadLength];
        payload[0] = 0x01;
        payload[1] = 0x00;
        payload[2] = 0xE8;
        payload[3] = 0x03;
        int output = 4;
        for (int x = 0; x < Width; x++)
        {
            for (int y = Height - 1; y >= 0; y--)
            {
                int input = (y * Width + x) * 4;
                double alpha = pixels[input + 3] / 255.0;
                int r = (int)(pixels[input + 2] * alpha);
                int g = (int)(pixels[input + 1] * alpha);
                int b = (int)(pixels[input] * alpha);
                ushort rgb565 = (ushort)((r >> 3) << 11 | (g >> 2) << 5 | (b >> 3));
                payload[output++] = (byte)rgb565;
                payload[output++] = (byte)(rgb565 >> 8);
            }
        }
        return payload;
    }
}
