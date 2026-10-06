using System;
using System.IO;

namespace JonsboCanvas
{
    internal static class SensorProbe
    {
        public static int Main()
        {
            using (MetricsCollector collector = new MetricsCollector(true))
            {
                MetricsSnapshot snapshot = collector.Collect();
                string result = "CPU_TEMP=" + (snapshot.CpuTemperature.HasValue
                    ? snapshot.CpuTemperature.Value.ToString("0.0") : "N/A") + Environment.NewLine +
                    "CPU_LOAD=" + snapshot.CpuUsage.ToString("0.0");
                Console.WriteLine(result);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "sensor_test.txt"), result);
            }
            return 0;
        }
    }
}
