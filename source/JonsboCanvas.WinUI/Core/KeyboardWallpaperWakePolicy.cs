namespace JonsboCanvas_WinUI;

// Input after an idle period is the external signal that the wireless keyboard
// has woken. Only retry when the OLED is known to be showing stale content.
internal static class KeyboardWallpaperWakePolicy
{
    internal const long QuietPeriodMilliseconds = 30_000;

    internal static bool IsWakeTransition(long now, long lastInputAt) =>
        lastInputAt >= 0 && now - lastInputAt >= QuietPeriodMilliseconds;

    internal static bool NeedsResync(string desiredId, string appliedId) =>
        desiredId.Length > 0 &&
        !string.Equals(desiredId, appliedId, StringComparison.Ordinal);
}
