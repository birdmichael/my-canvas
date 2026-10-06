using System;
using System.Drawing;
using System.IO;
using System.Threading;

namespace JonsboCanvas
{
    internal static class DeviceFrameTest
    {
        public static int Main()
        {
            string resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_test.txt");
            try
            {
                AppConfig config = AppConfig.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"));
                using (NativeDisplay display = new NativeDisplay(DashboardRenderer.CanvasWidth, DashboardRenderer.CanvasHeight))
                using (DashboardRenderer renderer = new DashboardRenderer(config))
                {
                    int start = display.Start();
                    DateTime deadline = DateTime.Now.AddSeconds(6);
                    while (!display.Connected && DateTime.Now < deadline)
                        Thread.Sleep(100);
                    if (start != 0 || !display.Connected)
                    {
                        File.WriteAllText(resultPath, string.Format("START={0}; CONNECTED={1}", start, display.Connected));
                        return 2;
                    }
                    MetricsSnapshot sample = new MetricsSnapshot
                    {
                        Timestamp = DateTime.Now,
                        CpuUsage = 42,
                        CpuTemperature = 58,
                        CpuPower = 65,
                        MemoryUsage = 61,
                        MemoryUsedGb = 19.2,
                        MemoryTotalGb = 31.3,
                        GpuUsage = 64,
                        GpuTemperature = 52,
                        GpuMemoryUsage = 55,
                        GpuMemoryUsedGb = 4.4,
                        GpuMemoryTotalGb = 8.0,
                        GpuPower = 88,
                        CpuName = "Intel Core Ultra 5 245K",
                        GpuName = "NVIDIA GeForce RTX 5060",
                        Uptime = TimeSpan.FromHours(16)
                    };
                    using (Bitmap frame = renderer.Render(sample))
                    {
                        int send = display.Send(frame, config.RotationDegrees);
                        File.WriteAllText(resultPath, string.Format("START={0}; CONNECTED={1}; MODE={2}; SEND={3}",
                            start, display.Connected, display.ActiveResolution, send));
                        Thread.Sleep(1800);
                        return send == 0 ? 0 : 3;
                    }
                }
            }
            catch (Exception exception)
            {
                File.WriteAllText(resultPath, exception.ToString());
                return 1;
            }
        }
    }
}
