using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace JonsboCanvas
{
    internal static class NeteaseLauncher
    {
        public static bool IsRunning()
        {
            try { return Process.GetProcessesByName("cloudmusic").Length > 0; }
            catch { return false; }
        }

        public static string StartRealtime(int debugPort)
        {
            if (IsRunning())
            {
                // An instance started normally does not expose the CDP port. Reuse an
                // instance that already has the port, otherwise restart it here so the
                // user does not have to quit NetEase by hand first.
                if (IsDebugPortOpen(debugPort))
                    return "网易云已运行，正在连接实时同步";
                return RestartRealtime(debugPort);
            }

            string executable = FindExecutable();
            if (string.IsNullOrWhiteSpace(executable))
                return "未找到网易云音乐，请先安装或手动启动播放器";

            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = executable;
                info.Arguments = "--remote-debugging-port=" + debugPort +
                    " --remote-debugging-address=127.0.0.1";
                info.UseShellExecute = true;
                Process.Start(info);
                return "已启动网易云精确同步，正在等待播放信息…";
            }
            catch (Exception exception)
            {
                Log.Write("Unable to launch Netease realtime: " + exception);
                return "网易云启动失败：" + exception.Message;
            }
        }

        private static bool IsDebugPortOpen(int debugPort)
        {
            try
            {
                using System.Net.Sockets.TcpClient client = new();
                System.Threading.Tasks.Task task = client.ConnectAsync("127.0.0.1", debugPort);
                return task.Wait(250);
            }
            catch
            {
                return false;
            }
        }

        public static string RestartRealtime(int debugPort)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("cloudmusic");
                foreach (Process process in processes)
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                            process.CloseMainWindow();
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                DateTime deadline = DateTime.UtcNow.AddSeconds(8);
                while (IsRunning() && DateTime.UtcNow < deadline)
                    System.Threading.Thread.Sleep(200);

                if (IsRunning())
                    return "网易云没有正常退出；请手动退出网易云后再点“启用同步”";

                return StartRealtime(debugPort);
            }
            catch (Exception exception)
            {
                Log.Write("Unable to restart Netease realtime: " + exception);
                return "网易云重启失败：" + exception.Message;
            }
        }

        private static string FindExecutable()
        {
            List<string> candidates = new List<string>();
            AddCandidate(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddCandidate(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, "NetEase", "CloudMusic", "cloudmusic.exe"));

            foreach (string path in candidates)
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            return "";
        }

        private static void AddCandidate(List<string> candidates, string programFiles)
        {
            if (!string.IsNullOrWhiteSpace(programFiles))
                candidates.Add(Path.Combine(programFiles, "NetEase", "CloudMusic", "cloudmusic.exe"));
        }
    }
}
