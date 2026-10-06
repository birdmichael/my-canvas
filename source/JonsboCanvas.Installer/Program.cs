using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Jonsbo Canvas Setup")]
[assembly: AssemblyDescription("Installs and launches Jonsbo Canvas")]
[assembly: AssemblyCompany("JonsboCanvas")]
[assembly: AssemblyProduct("Jonsbo Canvas")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace JonsboCanvasInstaller
{
    internal static class Program
    {
        private const string PayloadResourceName = "JonsboCanvas.Payload";

        [STAThread]
        private static int Main()
        {
            string stagingDirectory = Path.Combine(
                Path.GetTempPath(),
                "JonsboCanvas-Install-" + Guid.NewGuid().ToString("N"));
            string payloadPath = Path.Combine(stagingDirectory, "payload.zip");

            try
            {
                Directory.CreateDirectory(stagingDirectory);
                ExtractPayload(payloadPath);
                ZipFile.ExtractToDirectory(payloadPath, stagingDirectory);

                string sourceDirectory = Path.Combine(stagingDirectory, "JonsboCanvas-WinUI");
                string sourceExecutable = Path.Combine(sourceDirectory, "JonsboCanvas.WinUI.exe");
                if (!File.Exists(sourceExecutable))
                    throw new InvalidDataException("安装包中缺少 JonsboCanvas.WinUI.exe。请重新下载安装器。");

                StopRunningApplication();

                string installDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "JonsboCanvas");
                Directory.CreateDirectory(installDirectory);
                MirrorDirectory(sourceDirectory, installDirectory);

                string installedExecutable = Path.Combine(installDirectory, "JonsboCanvas.WinUI.exe");
                CreateShortcuts(installedExecutable, installDirectory);

                Process.Start(new ProcessStartInfo
                {
                    FileName = installedExecutable,
                    WorkingDirectory = installDirectory,
                    UseShellExecute = true
                });

                MessageBox.Show(
                    "Jonsbo Canvas v1.2.0 已安装并启动。\r\n\r\n桌面和开始菜单快捷方式已经创建。",
                    "安装完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "安装失败：\r\n" + exception.Message,
                    "Jonsbo Canvas 安装程序",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingDirectory))
                        Directory.Delete(stagingDirectory, true);
                }
                catch
                {
                }
            }
        }

        private static void ExtractPayload(string payloadPath)
        {
            using (Stream resource = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(PayloadResourceName))
            {
                if (resource == null)
                    throw new InvalidDataException("安装器内嵌资源损坏。请重新下载安装器。");
                using (FileStream output = File.Create(payloadPath))
                    resource.CopyTo(output);
            }
        }

        private static void StopRunningApplication()
        {
            Process[] processes = Process.GetProcessesByName("JonsboCanvas.WinUI");
            foreach (Process process in processes)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void MirrorDirectory(string sourceDirectory, string targetDirectory)
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "robocopy.exe"),
                Arguments = Quote(sourceDirectory) + " " + Quote(targetDirectory) +
                    " /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process process = Process.Start(info))
            {
                if (process == null)
                    throw new InvalidOperationException("无法启动 Windows 文件复制工具。");
                process.WaitForExit();
                if (process.ExitCode > 7)
                    throw new IOException("复制应用文件失败，错误码 " + process.ExitCode + "。");
            }
        }

        private static void CreateShortcuts(string executablePath, string workingDirectory)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "Jonsbo Canvas");
            Directory.CreateDirectory(startMenu);

            CreateShortcut(Path.Combine(desktop, "Jonsbo Canvas.lnk"), executablePath, workingDirectory);
            CreateShortcut(Path.Combine(startMenu, "Jonsbo Canvas.lnk"), executablePath, workingDirectory);
        }

        private static void CreateShortcut(string shortcutPath, string executablePath, string workingDirectory)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;

            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut,
                    new object[] { executablePath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                    new object[] { workingDirectory });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut,
                    new object[] { executablePath + ",0" });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut,
                    new object[] { "Jonsbo Canvas USB 小屏控制器" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                    Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell))
                    Marshal.FinalReleaseComObject(shell);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
