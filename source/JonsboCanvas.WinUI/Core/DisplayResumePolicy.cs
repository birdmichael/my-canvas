namespace JonsboCanvas_WinUI;

internal static class DisplayResumePolicy
{
    internal static readonly TimeSpan MinimumSuspendGap = TimeSpan.FromSeconds(5);

    public static bool ShouldRestartDisplay(
        DateTime previousTickUtc,
        DateTime currentTickUtc,
        bool connectionRequested)
    {
        if (!connectionRequested || previousTickUtc == DateTime.MinValue ||
            currentTickUtc <= previousTickUtc)
        {
            return false;
        }

        return currentTickUtc - previousTickUtc >= MinimumSuspendGap;
    }
}
