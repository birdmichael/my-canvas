using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

// The logon task runs with highest privileges so the CPUID driver can read CPU
// temperature. Creating such a task needs administrator rights, so a normal
// session asks for elevation once when the user turns the option on.
internal static class StartupTaskManager
{
    private const string TaskName = "My Canvas Dual Display AutoStart";
    // Relaunches the app within a few minutes if it stops without the user closing it.
    private const string WatchdogTaskName = "My Canvas Watchdog";
    // Bumped whenever the task definition changes, so older registrations are replaced.
    private const string DefinitionTag = "MyCanvas task definition 2";

    internal static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static bool IsRegistered(string executablePath)
    {
        foreach (string name in new[] { TaskName, WatchdogTaskName })
        {
            (bool success, string output) = RunSchtasks(new[] { "/Query", "/TN", name, "/XML" });
            if (!success || !output.Contains(executablePath, StringComparison.OrdinalIgnoreCase) ||
                !output.Contains(DefinitionTag, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    internal static bool EnsureRegistered(string executablePath, bool allowElevationPrompt)
    {
        try
        {
            if (IsRegistered(executablePath))
                return true;
            bool all = true;
            foreach (bool watchdog in new[] { false, true })
            {
                string file = Path.Combine(Path.GetTempPath(), $"mycanvas-task-{(watchdog ? "watchdog" : "logon")}.xml");
                File.WriteAllText(file, TaskXml(executablePath, EmbeddedRuntime.DataDirectory, watchdog), System.Text.Encoding.Unicode);
                string[] create = { "/Create", "/TN", watchdog ? WatchdogTaskName : TaskName, "/XML", file, "/F" };
                (bool success, string output) = RunSchtasks(create);
                Log.Write($"Startup task registration ({(watchdog ? "watchdog" : "logon")}): {success} {output}");
                if (!success && allowElevationPrompt && !IsElevated())
                    success = RunElevated(create);
                all &= success;
            }
            return all;
        }
        catch (Exception exception)
        {
            Log.Write("Startup task registration failed: " + exception);
            return false;
        }
    }

    internal static bool RemoveRegistered(bool allowElevationPrompt = false)
    {
        bool all = true;
        foreach (string name in new[] { TaskName, WatchdogTaskName })
        {
            try
            {
                string[] delete = { "/Delete", "/TN", name, "/F" };
                (bool success, string output) = RunSchtasks(delete);
                bool taskMissing = output.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("找不到", StringComparison.OrdinalIgnoreCase);
                Log.Write("Startup task removal: " + (success || taskMissing) + " " + output);
                if (!(success || taskMissing) && allowElevationPrompt && !IsElevated())
                    success = RunElevated(delete);
                all &= success || taskMissing;
            }
            catch (Exception exception)
            {
                Log.Write("Startup task removal failed: " + exception);
                all = false;
            }
        }
        return all;
    }

    // No run-time limit (the schtasks default ends the app after 72 hours) and
    // no battery rules. The logon task starts the app; the watchdog task checks
    // every five minutes and exits at once unless the app crashed.
    internal static string TaskXml(string executablePath, string dataDirectory, bool watchdog)
    {
        static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? "";
        string user = Escape(WindowsIdentity.GetCurrent().Name);
        string arguments = "--autostart" + (watchdog ? " --watchdog" : "") + " --data-dir=\"" + dataDirectory.TrimEnd('\\') + "\"";
        string trigger = watchdog
            ? "<TimeTrigger><StartBoundary>2026-01-01T00:00:00</StartBoundary><Enabled>true</Enabled>" +
              "<Repetition><Interval>PT5M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition></TimeTrigger>"
            : $"<LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT15S</Delay></LogonTrigger>";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{DefinitionTag}</Description></RegistrationInfo>
              <Triggers>{trigger}</Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(executablePath)}</Command>
                  <Arguments>{Escape(arguments)}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (bool Success, string Output) RunSchtasks(IEnumerable<string> arguments)
    {
        ProcessStartInfo info = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process? process = Process.Start(info);
        if (process == null) return (false, "");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(8000))
        {
            process.Kill(entireProcessTree: true);
            return (false, "timed out");
        }
        return (process.ExitCode == 0, output.Result + " " + error.Result);
    }

    // Runs schtasks through the UAC prompt; output cannot be captured here, so
    // success is judged by the exit code alone.
    private static bool RunElevated(IEnumerable<string> arguments)
    {
        ProcessStartInfo info = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            Arguments = string.Join(" ", arguments.Select(Quote)),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using Process? process = Process.Start(info);
            if (process == null || !process.WaitForExit(120_000)) return false;
            Log.Write("Elevated schtasks exit code: " + process.ExitCode);
            return process.ExitCode == 0;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            Log.Write("Elevation declined by user.");
            return false;
        }
    }

    private static string Quote(string argument)
        => argument.Contains(' ') || argument.Contains('"')
            ? "\"" + argument.Replace("\"", "\\\"") + "\""
            : argument;
}
