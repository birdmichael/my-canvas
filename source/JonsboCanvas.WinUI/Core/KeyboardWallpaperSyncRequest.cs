namespace JonsboCanvas_WinUI;

// A manual wallpaper choice grants one sync when the image is ready. Automatic
// rotations never create a request, and a failed download cannot leave one armed.
internal sealed class KeyboardWallpaperSyncRequest
{
    private readonly object _sync = new();
    private string _previousId = "";
    private long _expiresAt;
    private bool _pending, _allowUnchanged;

    internal void Request(string previousId, long now, bool allowUnchanged = false)
    {
        lock (_sync)
        {
            _previousId = previousId;
            _expiresAt = now + 120_000;
            _allowUnchanged = allowUnchanged;
            _pending = true;
        }
    }

    internal void Cancel() { lock (_sync) _pending = false; }

    internal bool TryTake(string currentId, bool busy, bool uploading, long now)
    {
        lock (_sync)
        {
            if (!_pending) return false;
            if (busy || uploading)
            {
                _expiresAt = now + 120_000;
                return false;
            }
            if (now >= _expiresAt) { _pending = false; return false; }
            if (currentId.Length == 0) return false;
            _pending = false;
            return _allowUnchanged || currentId != _previousId;
        }
    }
}
