using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace JonsboCanvas
{
    internal static class GifRenderTest
    {
        public static int Main()
        {
            AppConfig config = AppConfig.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"));
            config.ApplyPreset("animated-cyan");
            MetricsSnapshot metrics = new MetricsSnapshot
            {
                Timestamp = DateTime.Now,
                CpuUsage = 36,
                CpuTemperature = 58,
                MemoryUsage = 51,
                GpuUsage = 22,
                GpuTemperature = 47
            };
            using (DashboardRenderer renderer = new DashboardRenderer(config))
            {
                using (Bitmap first = renderer.Render(metrics))
                    first.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gif-frame-1.png"), ImageFormat.Png);
                Thread.Sleep(350);
                using (Bitmap second = renderer.Render(metrics))
                    second.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gif-frame-2.png"), ImageFormat.Png);
            }
            return 0;
        }
    }
}
