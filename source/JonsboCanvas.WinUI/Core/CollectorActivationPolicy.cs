namespace JonsboCanvas_WinUI;

internal static class CollectorActivationPolicy
{
    internal static bool RequiresMetrics(string mode)
    {
        return mode is "auto" or "hardware";
    }

    internal static bool RequiresMedia(string mode, bool playerRunning)
    {
        return playerRunning && mode is "auto" or "music";
    }
}
