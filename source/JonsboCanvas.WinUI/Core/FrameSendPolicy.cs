namespace JonsboCanvas_WinUI;

internal static class FrameSendPolicy
{
    // Both cooler screens blank out when they stop receiving frames, so an
    // unchanged frame is still resent at this interval.
    internal static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(1);

    internal static bool ShouldComputeSignature(bool musicMode, bool musicPlaying)
    {
        return !musicMode || !musicPlaying;
    }

    internal static bool ShouldSend(bool changed, DateTime lastSentUtc, DateTime nowUtc)
    {
        return changed || nowUtc - lastSentUtc >= KeepAlive;
    }

    // Sends to the serial screen block the render thread, so lyric animation
    // runs as fast as the link allows while leaving a third of the time free.
    internal static double LongMusicIntervalMilliseconds(double averageSendMilliseconds)
    {
        return Math.Clamp(averageSendMilliseconds * 1.5, 40, 200);
    }
}
