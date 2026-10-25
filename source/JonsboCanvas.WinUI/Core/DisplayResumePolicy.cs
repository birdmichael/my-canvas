namespace JonsboCanvas_WinUI;

internal static class DisplayResumePolicy
{
    internal static readonly TimeSpan MinimumSuspendGap = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan RecoveryCooldown = TimeSpan.FromSeconds(15);

    public static bool ShouldRestartDisplay(
        DateTime previousTickUtc,
        DateTime currentTickUtc,
        bool connectionRequested,
        DateTime recoveryStartedUtc = default,
        bool? screensConnected = null)
    {
        if (!connectionRequested || previousTickUtc == DateTime.MinValue ||
            currentTickUtc <= previousTickUtc)
        {
            return false;
        }

        if (currentTickUtc - previousTickUtc < MinimumSuspendGap) return false;
        // A missing value keeps the legacy behavior used by callers that do
        // not have a live device probe. An explicit online result suppresses
        // recovery because the display session is still healthy.
        if (screensConnected == true) return false;
        return recoveryStartedUtc == default ||
            currentTickUtc - recoveryStartedUtc >= RecoveryCooldown;
    }
}
