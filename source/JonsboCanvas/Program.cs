using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace JonsboCanvas
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                EmbeddedRuntime.Initialize();
            }
            catch (Exception exception)
            {
                MessageBox.Show("无法展开单文件运行资源：" + exception.Message,
                    "Jonsbo Canvas", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string configPath = EmbeddedRuntime.ConfigPath;
            AppConfig config = AppConfig.Load(configPath);
            bool autoStarted = Array.Exists(args, delegate(string value)
            {
                return string.Equals(value, "--autostart", StringComparison.OrdinalIgnoreCase);
            });

            if (config.StartWithWindows)
                StartupManager.EnsureRegistered(Application.ExecutablePath);

            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs errorArgs)
            {
                Log.Write("UI error: " + errorArgs.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs errorArgs)
            {
                Log.Write("Unhandled error: " + errorArgs.ExceptionObject);
            };

            Application.Run(new MainForm(configPath, config, autoStarted));
        }
    }

    internal static class StartupManager
    {
        private const string TaskName = "Jonsbo Canvas AutoStart";

        public static bool EnsureRegistered(string executablePath)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
                string taskCommand = "\\\"" + executablePath + "\\\" --autostart";
                info.Arguments = "/Create /TN \"" + TaskName + "\" /TR \"" + taskCommand +
                    "\" /SC ONLOGON /DELAY 0000:15 /RL HIGHEST /F";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);
                    bool success = process.ExitCode == 0;
                    Log.Write("Startup task registration: " + success + " " + output + " " + error);
                    return success;
                }
            }
            catch (Exception exception)
            {
                Log.Write("Startup task registration failed: " + exception);
                return false;
            }
        }
    }

    internal static class Log
    {
        private static readonly object Sync = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(
                        EmbeddedRuntime.LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine);
                }
            }
            catch
            {
            }
        }
    }
}
