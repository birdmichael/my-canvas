namespace JonsboCanvas_WinUI;

// Input after an idle period restores lighting even when the OLED is unchanged.
// Image uploads still only retry content known to be stale.
internal static class KeyboardWallpaperWakePolicy
{
    internal const long QuietPeriodMilliseconds = 30_000;

    internal static bool IsWakeTransition(long now, long lastInputAt) =>
        lastInputAt >= 0 && now - lastInputAt >= QuietPeriodMilliseconds;

    internal static bool NeedsResync(string desiredId, string appliedId) =>
        desiredId.Length > 0 &&
        !string.Equals(desiredId, appliedId, StringComparison.Ordinal);
}
