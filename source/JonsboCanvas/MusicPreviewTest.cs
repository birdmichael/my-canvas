using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace JonsboCanvas
{
    internal static class MusicPreviewTest
    {
        public static int Main()
        {
            AppConfig config = AppConfig.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"));
            using (NeteaseMediaCollector media = new NeteaseMediaCollector(config.NeteaseDebugPort))
            using (DashboardRenderer renderer = new DashboardRenderer(config))
            {
                MusicSnapshot snapshot = media.Collect();
                using (Bitmap frame = renderer.RenderMusic(snapshot))
                    frame.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-music.png"), ImageFormat.Png);
                Console.WriteLine("AVAILABLE=" + snapshot.Available);
                Console.WriteLine("TITLE=" + snapshot.Title);
                Console.WriteLine("ARTIST=" + snapshot.Artist);
                Console.WriteLine("LYRIC=" + snapshot.CurrentLyric);
                Console.WriteLine("COVER=" + (snapshot.Cover != null));
            }
            return 0;
        }
    }
}
