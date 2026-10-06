using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JonsboCanvas.SingleLauncher
{
    internal static class Program
    {
        private const string ResourceName = "JonsboCanvas.Package.v4.zip";
        private const string PackageId = "music-sync-v4-637E8CF568E0";
        private const string MainExecutable = "JonsboCanvas.WinUI.exe";

        [STAThread]
        private static int Main(string[] args)
        {
            bool extractOnly = Array.Exists(args, value =>
                string.Equals(value, "--extract-only", StringComparison.OrdinalIgnoreCase));
            if (extractOnly)
            {
                try
                {
                    ExtractPackage();
                    return 0;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LaunchWindow());
            return LaunchWindow.ExitCode;
        }

        private sealed class LaunchWindow : Form
        {
            public static int ExitCode;
            private readonly Label _status;

            public LaunchWindow()
            {
                Text = "Jonsbo Canvas";
                ClientSize = new Size(420, 126);
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = true;
                BackColor = Color.FromArgb(14, 23, 31);
                ForeColor = Color.White;

                _status = new Label
                {
                    AutoSize = false,
                    Bounds = new Rectangle(24, 22, 372, 38),
                    Text = "正在准备 Jonsbo Canvas…",
                    Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Controls.Add(_status);

                ProgressBar progress = new ProgressBar
                {
                    Bounds = new Rectangle(24, 76, 372, 12),
                    Style = ProgressBarStyle.Marquee,
                    MarqueeAnimationSpeed = 24
                };
                Controls.Add(progress);
            }

            protected override async void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try
                {
                    string directory = await Task.Run(() => ExtractPackage());
                    _status.Text = "正在启动主程序…";
                    string executable = Path.Combine(directory, MainExecutable);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = executable,
                        WorkingDirectory = directory,
                        UseShellExecute = true
                    });
                    ExitCode = 0;
                    Close();
                }
                catch (Exception exception)
                {
                    ExitCode = 1;
                    MessageBox.Show(this,
                        "单文件启动失败：\r\n\r\n" + exception.Message,
                        "Jonsbo Canvas", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                }
            }
        }

        private static string ExtractPackage()
        {
            string rootOverride = Environment.GetEnvironmentVariable("JONSBO_CANVAS_SINGLE_ROOT");
            string root = string.IsNullOrWhiteSpace(rootOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "JonsboCanvas", "packages")
                : Path.GetFullPath(rootOverride);
            string target = Path.Combine(root, PackageId);
            string marker = Path.Combine(target, ".complete");
            string executable = Path.Combine(target, MainExecutable);

            Directory.CreateDirectory(root);
            using (Mutex mutex = new Mutex(false, @"Local\JonsboCanvasSingleV4Package"))
            {
                if (!mutex.WaitOne(TimeSpan.FromMinutes(2)))
                    throw new TimeoutException("等待另一份启动程序完成初始化时超时。");
                try
                {
                    if (File.Exists(marker) && File.Exists(executable))
                        return target;

                    Directory.CreateDirectory(target);
                    Assembly assembly = Assembly.GetExecutingAssembly();
                    using (Stream package = assembly.GetManifestResourceStream(ResourceName))
                    {
                        if (package == null)
                            throw new InvalidOperationException("单文件内缺少 WinUI 应用包。");
                        using (ZipArchive archive = new ZipArchive(package, ZipArchiveMode.Read, false))
                        {
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                string relative = StripTopDirectory(entry.FullName);
                                if (string.IsNullOrWhiteSpace(relative))
                                    continue;

                                string destination = Path.GetFullPath(Path.Combine(target, relative));
                                string targetPrefix = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
                                if (!destination.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidDataException("应用包包含不安全的文件路径。");

                                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                                    entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                                {
                                    Directory.CreateDirectory(destination);
                                    continue;
                                }

                                string parent = Path.GetDirectoryName(destination);
                                if (!Directory.Exists(parent))
                                    Directory.CreateDirectory(parent);
                                using (Stream input = entry.Open())
                                using (FileStream output = new FileStream(destination, FileMode.Create,
                                    FileAccess.Write, FileShare.None))
                                    input.CopyTo(output);
                            }
                        }
                    }

                    if (!File.Exists(executable))
                        throw new InvalidDataException("解压完成后没有找到主程序。");
                    File.WriteAllText(marker, PackageId);
                    return target;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static string StripTopDirectory(string path)
        {
            string normalized = (path ?? "").Replace('\\', '/');
            int separator = normalized.IndexOf('/');
            return separator < 0 ? normalized : normalized.Substring(separator + 1);
        }
    }
}
