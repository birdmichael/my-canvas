namespace JonsboCanvas_WinUI;

// A disconnected keyboard must not be probed every few seconds: each failed
// exchange wakes the receiver and can keep the keyboard out of idle sleep.
internal static class KeyboardRetryBackoffPolicy
{
    internal static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(5);

    internal static TimeSpan NextDelay(int failedAttempts)
    {
        if (failedAttempts <= 1) return InitialDelay;
        int exponent = Math.Max(0, Math.Min(failedAttempts - 2, 10));
        int seconds = (int)InitialDelay.TotalSeconds * (1 << exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumDelay.TotalSeconds));
    }
}
