namespace JonsboCanvas_WinUI;

// Only device identity and activity time are retained, never key contents.
internal sealed class KeyboardActivitySignal
{
    private readonly long _startedAt;
    private readonly object _sync = new();
    private long _lastInputAt = -1;
    private bool _activityPending, _wakePending;

    internal KeyboardActivitySignal(long startedAt) => _startedAt = startedAt;

    internal static bool IsTargetKeyboard(uint deviceType, string path) =>
        deviceType == 1 &&
        (path.Contains("VID_0B05&PID_1AD0", StringComparison.OrdinalIgnoreCase) ||
         path.Contains("VID_0B05&PID_1C12", StringComparison.OrdinalIgnoreCase));

    internal void RecordKeyboardInput(long now)
    {
        lock (_sync)
        {
            if (_lastInputAt < 0 || now - _lastInputAt >= KeyboardWallpaperWakePolicy.QuietPeriodMilliseconds)
                _wakePending = true;
            _lastInputAt = now;
            _activityPending = true;
        }
    }

    internal bool TryTakeActivity(out bool afterIdle)
    {
        lock (_sync)
        {
            bool pending = _activityPending;
            afterIdle = _wakePending;
            _activityPending = _wakePending = false;
            return pending;
        }
    }

    internal void RecordReconnect(long now)
    {
        lock (_sync) _lastInputAt = now;
    }

    internal bool IsIdle(long now)
    {
        lock (_sync)
            return now - (_lastInputAt >= 0 ? _lastInputAt : _startedAt) >=
                KeyboardWallpaperWakePolicy.QuietPeriodMilliseconds;
    }
}
