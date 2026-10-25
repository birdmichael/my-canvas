using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace JonsboCanvas
{
    internal static class NeteaseLauncher
    {
        private static readonly object LaunchSync = new object();

        public static bool IsRunning()
        {
            Process[] processes = Process.GetProcessesByName("cloudmusic");
            try
            {
                using Process current = Process.GetCurrentProcess();
                foreach (Process process in processes)
                    try { if (process.SessionId == current.SessionId && !process.HasExited) return true; }
                    catch { }
                return false;
            }
            catch { return false; }
            finally { foreach (Process process in processes) process.Dispose(); }
        }

        internal sealed class RunningInstance
        {
            public int ProcessId;
            public string Identity = "";
            public string Executable = "";
        }

        internal static RunningInstance GetRunningInstance()
        {
            Process[] processes = Process.GetProcessesByName("cloudmusic");
            try
            {
                using Process current = Process.GetCurrentProcess();
                Process oldest = null;
                foreach (Process process in processes)
                {
                    try
                    {
                        if (process.SessionId == current.SessionId && !process.HasExited &&
                            (oldest == null || process.StartTime < oldest.StartTime))
                            oldest = process;
                    }
                    catch { }
                }
                if (oldest == null) return null;
                return new RunningInstance
                {
                    ProcessId = oldest.Id,
                    Identity = oldest.Id + ":" + oldest.StartTime.ToUniversalTime().Ticks,
                    Executable = oldest.MainModule.FileName
                };
            }
            catch { return null; }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }

        // Recheck under the same lock as the manual button: a slow launch or a
        // simultaneous click must not restart an already-connected player.
        internal static void AutoConnect(int debugPort, string expectedIdentity, CancellationToken cancel)
        {
            while (!Monitor.TryEnter(LaunchSync, 200))
                if (cancel.IsCancellationRequested) return;
            try
            {
                if (cancel.IsCancellationRequested || IsDebugPortOpen(debugPort)) return;
                RunningInstance instance = GetRunningInstance();
                if (instance == null || instance.Identity != expectedIdentity) return;
                Log.Write("Netease auto-connect: attaching to ordinary launch " + instance.Identity);
                string result = RestartCore(debugPort, instance, true, cancel);
                if (!string.IsNullOrEmpty(result))
                    Log.Write("Netease auto-connect result: " + result);
            }
            finally { Monitor.Exit(LaunchSync); }
        }

        public static string StartRealtime(int debugPort)
        {
            lock (LaunchSync) return StartCore(debugPort);
        }

        private static string StartCore(int debugPort, string executable = null)
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

            executable = executable ?? FindExecutable();
            if (string.IsNullOrWhiteSpace(executable))
                return "未找到网易云音乐，请先安装或手动启动播放器";

            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = executable;
                info.Arguments = "--remote-debugging-port=" + debugPort +
                    " --remote-debugging-address=127.0.0.1";
                info.UseShellExecute = true;
                Process.Start(info)?.Dispose();
                return "已启动网易云精确同步，正在等待播放信息…";
            }
            catch (Exception exception)
            {
                Log.Write("Unable to launch Netease realtime: " + exception);
                return "网易云启动失败：" + exception.Message;
            }
        }

        internal static bool IsDebugPortOpen(int debugPort)
        {
            try
            {
                using System.Net.Sockets.TcpClient client = new();
                using CancellationTokenSource timeout = new(250);
                client.ConnectAsync(System.Net.IPAddress.Loopback, debugPort, timeout.Token)
                    .AsTask().GetAwaiter().GetResult();
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        public static string RestartRealtime(int debugPort)
        {
            lock (LaunchSync)
            {
                if (IsDebugPortOpen(debugPort)) return "网易云已运行，正在连接实时同步";
                RunningInstance instance = GetRunningInstance();
                if (instance == null)
                    return IsRunning() ? "无法访问网易云进程，请退出网易云后重试" : StartCore(debugPort);
                return RestartCore(debugPort, instance, false, CancellationToken.None);
            }
        }

        private static string RestartCore(int debugPort, RunningInstance instance,
            bool allowTrayExit, CancellationToken cancel)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("cloudmusic");
                foreach (Process process in processes)
                {
                    try
                    {
                        if (process.Id == instance.ProcessId && process.MainWindowHandle != IntPtr.Zero)
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
                    if (cancel.WaitHandle.WaitOne(200)) return "";

                if (cancel.IsCancellationRequested) return "";
                RunningInstance remaining = GetRunningInstance();
                if (remaining != null && remaining.Identity != instance.Identity)
                    return "网易云进程已更换，等待重新检测";
                if (remaining != null && allowTrayExit)
                {
                    // Closing the window often only hides NetEase in the tray.
                    // Only terminate the exact instance observed before the close.
                    using Process main = Process.GetProcessById(instance.ProcessId);
                    if (main.Id + ":" + main.StartTime.ToUniversalTime().Ticks != instance.Identity)
                        return "";
                    Log.Write("Netease auto-connect: exiting tray process " + instance.ProcessId);
                    main.Kill(entireProcessTree: true);
                    if (!main.WaitForExit(5000)) return "网易云退出超时";
                    deadline = DateTime.UtcNow.AddSeconds(3);
                    while (IsRunning() && DateTime.UtcNow < deadline)
                        if (cancel.WaitHandle.WaitOne(100)) return "";
                }

                if (IsRunning())
                    return "网易云没有正常退出；请手动退出网易云后再点“启用同步”";

                if (cancel.IsCancellationRequested) return "";
                string result = StartCore(debugPort, instance.Executable);
                Log.Write("Netease auto-connect restart: " + result);
                return result;
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

    // Each observed process gets one automatic attempt, with a startup grace
    // period. Remember our replacement too, even when its endpoint fails.
    internal sealed class NeteaseAutoConnectPolicy
    {
        private string _candidate = "";
        private string _attempted = "";
        private DateTime _seenAt;

        public bool ShouldRestart(string identity, bool portOpen, DateTime now)
        {
            if (string.IsNullOrEmpty(identity))
            {
                _candidate = "";
                return false;
            }
            if (_candidate != identity)
            {
                _candidate = identity;
                _seenAt = now;
            }
            if (portOpen)
            {
                _attempted = identity;
                return false;
            }
            if (_attempted == identity || now - _seenAt < TimeSpan.FromSeconds(8))
                return false;
            _attempted = identity;
            return true;
        }

        public void RememberReplacement(string identity)
        {
            if (!string.IsNullOrEmpty(identity)) _attempted = identity;
        }
    }
}
