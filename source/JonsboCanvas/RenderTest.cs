using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace JonsboCanvas
{
    internal static class RenderTest
    {
        public static int Main()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                AppConfig config = AppConfig.Load(configPath);
                MetricsSnapshot snapshot = new MetricsSnapshot
                {
                    Timestamp = DateTime.Now,
                    CpuUsage = 36,
                    CpuTemperature = 58,
                    MemoryUsage = 51,
                    GpuUsage = 22,
                    GpuTemperature = 47
                };
                string[] presets = { "cyber-cyan", "molten-amber", "aurora-violet" };
                foreach (string preset in presets)
                {
                    config.ApplyPreset(preset);
                    using (DashboardRenderer renderer = new DashboardRenderer(config))
                    using (Bitmap frame = renderer.Render(snapshot))
                        frame.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                            "preview-" + preset + ".png"), ImageFormat.Png);
                }
                Console.WriteLine("theme previews created");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
    }
}
