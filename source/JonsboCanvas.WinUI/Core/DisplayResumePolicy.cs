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
        bool screensConnected = true)
    {
        if (!connectionRequested || previousTickUtc == DateTime.MinValue ||
            currentTickUtc <= previousTickUtc)
        {
            return false;
        }

        if (currentTickUtc - previousTickUtc < MinimumSuspendGap) return false;
        if (screensConnected) return false;
        return recoveryStartedUtc == default ||
            currentTickUtc - recoveryStartedUtc >= RecoveryCooldown;
    }
}
