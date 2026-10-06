using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace JonsboCanvas
{
    public sealed class UsageQuotaWindow
    {
        public bool Available;
        public double UsedPercent;
        public int WindowMinutes;
        public DateTime ResetAtLocal;
    }

    public sealed class CodingUsageSnapshot
    {
        public string Provider = "";
        public string Model = "";
        public string Plan = "";
        public string Status = "";
        public bool Installed;
        public bool Connected;
        public DateTime UpdatedAtLocal;
        public double ContextUsedPercent;
        public long SessionTokens;
        public UsageQuotaWindow ShortWindow = new UsageQuotaWindow();
        public UsageQuotaWindow WeeklyWindow = new UsageQuotaWindow();
    }

    public sealed class VibeCodingSnapshot
    {
        public CodingUsageSnapshot Codex = new CodingUsageSnapshot { Provider = "CODEX" };
        public CodingUsageSnapshot Claude = new CodingUsageSnapshot { Provider = "CLAUDE" };
        public DateTime CollectedAtLocal = DateTime.Now;
    }

    internal sealed class VibeCodingCollector
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public VibeCodingSnapshot Collect()
        {
            VibeCodingSnapshot snapshot = new VibeCodingSnapshot();
            snapshot.Codex = CollectCodex();
            snapshot.Claude = CollectClaude();
            snapshot.CollectedAtLocal = DateTime.Now;
            return snapshot;
        }

        private CodingUsageSnapshot CollectCodex()
        {
            CodingUsageSnapshot result = new CodingUsageSnapshot { Provider = "CODEX" };
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(codexHome))
                codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            string sessions = Path.Combine(codexHome, "sessions");
            result.Installed = Directory.Exists(codexHome);
            if (!Directory.Exists(sessions))
            {
                result.Status = result.Installed ? "尚无本地会话记录" : "未检测到 Codex";
                return result;
            }

            try
            {
                List<FileInfo> files = Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(8)
                    .ToList();
                CodingUsageSnapshot tokenFallback = null;
                foreach (FileInfo file in files)
                {
                    string[] lines = ReadTailLines(file.FullName, 2 * 1024 * 1024);
                    for (int index = lines.Length - 1; index >= 0; index--)
                    {
                        Dictionary<string, object> root = DeserializeDictionary(lines[index]);
                        if (GetString(root, "type") != "event_msg")
                            continue;
                        Dictionary<string, object> payload = GetDictionary(root, "payload");
                        if (GetString(payload, "type") != "token_count")
                            continue;

                        CodingUsageSnapshot parsed = ParseCodexTokenEvent(payload, file.LastWriteTime);
                        parsed.Installed = true;
                        if (tokenFallback == null)
                            tokenFallback = parsed;
                        if (parsed.ShortWindow.Available || parsed.WeeklyWindow.Available)
                        {
                            parsed.Connected = true;
                            parsed.Status = "本机额度快照";
                            return parsed;
                        }
                    }
                }
                if (tokenFallback != null)
                {
                    tokenFallback.Status = "有会话数据，暂无码率快照";
                    return tokenFallback;
                }
                result.Status = "尚未找到额度事件";
            }
            catch (Exception exception)
            {
                result.Status = "读取失败";
                Log.Write("Codex usage read failed: " + exception.Message);
            }
            return result;
        }

        private CodingUsageSnapshot ParseCodexTokenEvent(Dictionary<string, object> payload, DateTime updated)
        {
            CodingUsageSnapshot result = new CodingUsageSnapshot
            {
                Provider = "CODEX",
                UpdatedAtLocal = updated
            };
            Dictionary<string, object> info = GetDictionary(payload, "info");
            Dictionary<string, object> total = GetDictionary(info, "total_token_usage");
            result.SessionTokens = GetLong(total, "total_tokens");

            Dictionary<string, object> rateLimits = GetDictionary(payload, "rate_limits");
            result.Model = GetString(rateLimits, "limit_name");
            result.Plan = GetString(rateLimits, "plan_type");
            AssignCodexWindow(result, GetDictionary(rateLimits, "primary"));
            AssignCodexWindow(result, GetDictionary(rateLimits, "secondary"));
            return result;
        }

        private static void AssignCodexWindow(CodingUsageSnapshot result, Dictionary<string, object> source)
        {
            if (source == null || source.Count == 0)
                return;
            int minutes = (int)GetLong(source, "window_minutes");
            UsageQuotaWindow target = minutes > 0 && minutes <= 360
                ? result.ShortWindow : result.WeeklyWindow;
            target.Available = true;
            target.WindowMinutes = minutes;
            target.UsedPercent = GetDouble(source, "used_percent");
            target.ResetAtLocal = UnixToLocal(GetLong(source, "resets_at"));
        }

        private CodingUsageSnapshot CollectClaude()
        {
            CodingUsageSnapshot result = new CodingUsageSnapshot { Provider = "CLAUDE" };
            string claudeHome = ClaudeStatusBridge.GetClaudeHome();
            result.Installed = Directory.Exists(claudeHome);
            string cache = ClaudeStatusBridge.CachePath;
            if (!File.Exists(cache))
            {
                result.Status = result.Installed ? "点击控制器启用同步" : "未检测到 Claude Code";
                return result;
            }

            try
            {
                Dictionary<string, object> data = DeserializeDictionary(File.ReadAllText(cache, Encoding.UTF8));
                result.Model = GetString(data, "model");
                result.Plan = GetString(data, "version");
                result.ContextUsedPercent = GetDouble(data, "context_used");
                result.ShortWindow = ParseClaudeWindow(data, "five_hour", 300);
                result.WeeklyWindow = ParseClaudeWindow(data, "seven_day", 10080);
                result.UpdatedAtLocal = UnixToLocal(GetLong(data, "captured_at"));
                result.Connected = result.ShortWindow.Available || result.WeeklyWindow.Available;
                result.Status = result.Connected ? "Claude 状态栏同步" : "等待 Claude 首次响应";
                result.Installed = true;
            }
            catch (Exception exception)
            {
                result.Status = "同步数据损坏";
                Log.Write("Claude usage read failed: " + exception.Message);
            }
            return result;
        }

        private static UsageQuotaWindow ParseClaudeWindow(Dictionary<string, object> data, string prefix, int minutes)
        {
            UsageQuotaWindow result = new UsageQuotaWindow();
            if (!data.ContainsKey(prefix + "_used") || data[prefix + "_used"] == null)
                return result;
            result.Available = true;
            result.WindowMinutes = minutes;
            result.UsedPercent = GetDouble(data, prefix + "_used");
            result.ResetAtLocal = UnixToLocal(GetLong(data, prefix + "_reset"));
            return result;
        }

        private Dictionary<string, object> DeserializeDictionary(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try { return _json.DeserializeObject(json) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static string[] ReadTailLines(string path, int maximumBytes)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0, stream.Length - maximumBytes);
                stream.Position = start;
                int length = (int)Math.Min(maximumBytes, stream.Length - start);
                byte[] buffer = new byte[length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int amount = stream.Read(buffer, read, buffer.Length - read);
                    if (amount <= 0) break;
                    read += amount;
                }
                string text = Encoding.UTF8.GetString(buffer, 0, read);
                if (start > 0)
                {
                    int firstNewline = text.IndexOf('\n');
                    text = firstNewline >= 0 ? text.Substring(firstNewline + 1) : "";
                }
                return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        internal static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null)
                return null;
            return source[key] as Dictionary<string, object>;
        }

        internal static string GetString(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null)
                return "";
            return Convert.ToString(source[key], CultureInfo.InvariantCulture) ?? "";
        }

        internal static long GetLong(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null)
                return 0;
            try { return Convert.ToInt64(source[key], CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        internal static double GetDouble(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null)
                return 0;
            try { return Convert.ToDouble(source[key], CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static DateTime UnixToLocal(long seconds)
        {
            if (seconds <= 0) return DateTime.MinValue;
            try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime(); }
            catch { return DateTime.MinValue; }
        }
    }

    internal static class ClaudeStatusBridge
    {
        public static string CachePath
        {
            get { return Path.Combine(EmbeddedRuntime.DataDirectory, "claude-status.json"); }
        }

        public static string GetClaudeHome()
        {
            string configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
                : Path.GetFullPath(configured);
        }

        public static bool HasExistingStatusLine(out bool isOurBridge)
        {
            isOurBridge = false;
            string path = Path.Combine(GetClaudeHome(), "settings.json");
            if (!File.Exists(path)) return false;
            try
            {
                JavaScriptSerializer json = new JavaScriptSerializer();
                Dictionary<string, object> settings = json.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                Dictionary<string, object> status = VibeCodingCollector.GetDictionary(settings, "statusLine");
                string command = VibeCodingCollector.GetString(status, "command");
                if (string.IsNullOrWhiteSpace(command)) return false;
                isOurBridge = command.IndexOf("jonsbo-claude-status", StringComparison.OrdinalIgnoreCase) >= 0;
                return true;
            }
            catch { return false; }
        }

        public static string Install(bool replaceExisting)
        {
            string home = GetClaudeHome();
            Directory.CreateDirectory(home);
            string settingsPath = Path.Combine(home, "settings.json");
            JavaScriptSerializer json = new JavaScriptSerializer();
            Dictionary<string, object> settings = new Dictionary<string, object>();
            if (File.Exists(settingsPath))
            {
                string existingText = File.ReadAllText(settingsPath, Encoding.UTF8);
                Dictionary<string, object> existing = json.DeserializeObject(existingText) as Dictionary<string, object>;
                if (existing != null) settings = existing;
                if (settings.ContainsKey("statusLine") && !replaceExisting)
                    return "已有 Claude 状态栏配置，未覆盖";
                string backup = settingsPath + ".jonsbo-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(settingsPath, backup, false);
            }

            string scriptPath = Path.Combine(EmbeddedRuntime.DataDirectory, "jonsbo-claude-status.ps1");
            File.WriteAllText(scriptPath, BuildPowerShellScript(CachePath), new UTF8Encoding(false));
            string commandPath = scriptPath.Replace('\\', '/');
            settings["statusLine"] = new Dictionary<string, object>
            {
                { "type", "command" },
                { "command", "powershell -NoProfile -ExecutionPolicy Bypass -File \"" + commandPath + "\"" },
                { "refreshInterval", 5 }
            };
            File.WriteAllText(settingsPath, json.Serialize(settings), new UTF8Encoding(false));
            return "Claude 余量同步已配置；发送下一条消息后生效";
        }

        private static string BuildPowerShellScript(string cachePath)
        {
            string escapedCache = cachePath.Replace("'", "''");
            return "$raw = [Console]::In.ReadToEnd()\r\n" +
                "if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }\r\n" +
                "try {\r\n" +
                "  $d = $raw | ConvertFrom-Json\r\n" +
                "  $safe = [ordered]@{\r\n" +
                "    model = $d.model.display_name\r\n" +
                "    version = $d.version\r\n" +
                "    context_used = $d.context_window.used_percentage\r\n" +
                "    five_hour_used = $d.rate_limits.five_hour.used_percentage\r\n" +
                "    five_hour_reset = $d.rate_limits.five_hour.resets_at\r\n" +
                "    seven_day_used = $d.rate_limits.seven_day.used_percentage\r\n" +
                "    seven_day_reset = $d.rate_limits.seven_day.resets_at\r\n" +
                "    captured_at = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()\r\n" +
                "  }\r\n" +
                "  $safe | ConvertTo-Json -Compress | Set-Content -Encoding UTF8 -LiteralPath '" + escapedCache + "'\r\n" +
                "  $parts = @()\r\n" +
                "  if ($null -ne $safe.five_hour_used) { $parts += ('5h {0:N0}%' -f $safe.five_hour_used) }\r\n" +
                "  if ($null -ne $safe.seven_day_used) { $parts += ('7d {0:N0}%' -f $safe.seven_day_used) }\r\n" +
                "  if ($parts.Count -gt 0) { Write-Output ('[Claude] ' + ($parts -join '  ')) } else { Write-Output '[Claude]' }\r\n" +
                "} catch { Write-Output '[Claude]' }\r\n";
        }
    }
}
