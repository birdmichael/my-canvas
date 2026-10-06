namespace JonsboCanvas;

internal static class LogRotationPolicy
{
    internal const long MaximumBytes = 5L * 1024 * 1024;

    internal static bool ShouldRotate(long length)
    {
        return length >= MaximumBytes;
    }
}
