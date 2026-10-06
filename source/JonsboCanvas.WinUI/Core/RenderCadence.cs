namespace JonsboCanvas_WinUI;

internal static class RenderCadence
{
    private const int MinimumSchedulerMilliseconds = 25;
    private const int IdleSchedulerMilliseconds = 500;

    internal static int SchedulerIntervalMilliseconds(int frameIntervalMilliseconds, bool outputActive)
    {
        int interval = Math.Max(MinimumSchedulerMilliseconds, frameIntervalMilliseconds);
        return outputActive ? interval : Math.Max(IdleSchedulerMilliseconds, interval);
    }

    internal static bool ShouldUpdateLocalPreview(bool previewEnabled, bool windowVisible)
    {
        return previewEnabled && windowVisible;
    }
}
