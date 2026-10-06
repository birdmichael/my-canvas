namespace JonsboCanvas_WinUI;

internal static class LaunchPolicy
{
    internal static bool IsAutoStarted(IEnumerable<string> arguments)
    {
        return arguments.Any(argument =>
            string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase));
    }

    // Started by the watchdog task, which relaunches the app after a crash.
    internal static bool IsWatchdog(IEnumerable<string> arguments)
    {
        return arguments.Any(argument =>
            string.Equals(argument, "--watchdog", StringComparison.OrdinalIgnoreCase));
    }
}
