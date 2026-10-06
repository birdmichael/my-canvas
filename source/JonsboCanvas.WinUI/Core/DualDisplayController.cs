using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

internal enum ScreenState { Off, Searching, Connected, NotResponding }

internal sealed class DualDisplayController : IDisposable
{
    private readonly AppConfig _config;
    private readonly NativeDisplay _square = new(480, 480);
    private readonly SerialJpegDisplay _long = new();
    private bool _requested;
    private DateTime _lastLongSend, _lastSquareSend, _lastLongSent, _lastSquareSent;
    private int _longFrames, _squareFrames, _longGeneration;
    private double _longSendMilliseconds = 130;
    private readonly object _sync = new();
    private ulong _longSignature, _squareSignature;
    private bool _hasLongSignature, _hasSquareSignature;
    public event Action<string>? StatusChanged;
    public bool Connected => _square.Connected || _long.Connected;
    public int ConnectedCount => (_square.Connected ? 1 : 0) + (_long.Connected ? 1 : 0);
    public string LongPortName => _long.PortName;

    public ScreenState LongState =>
        !_requested ? ScreenState.Off
        : _long.Connected ? ScreenState.Connected
        : _long.NotResponding ? ScreenState.NotResponding
        : ScreenState.Searching;

    public ScreenState SquareState =>
        !_requested ? ScreenState.Off : _square.Connected ? ScreenState.Connected : ScreenState.Searching;

    public DualDisplayController(AppConfig config)
    {
        _config = config;
        _square.StatusChanged += status => { if (!_square.Connected) _hasSquareSignature = false; Log.Write("Square: " + status); StatusChanged?.Invoke(status); };
        _long.StatusChanged += status => StatusChanged?.Invoke(status);
    }

    public int Start()
    {
        int result;
        lock (_sync)
        {
        _requested = true;
        _lastLongSend = _lastSquareSend = DateTime.MinValue;
        _hasLongSignature = _hasSquareSignature = false;
        _long.Start(_config.SerialPortName);
        result = _square.Start();
        }
        return result == 0 || _long.WaitConnected(TimeSpan.FromSeconds(2)) ? 0 : result;
    }

    private readonly object _mailSync = new();
    private readonly AutoResetEvent _mailWake = new(false);
    private (Bitmap Long, Bitmap Square, string LongMode, string SquareMode)? _mail;
    private Thread? _sender;
    private volatile bool _disposed;
    private long _sendStartedTicks;
    private bool _stallLogged;

    // Takes ownership of both frames and returns at once; a screen whose driver
    // stops answering only stalls the display thread. Frames that pile up
    // meanwhile are dropped, keeping the newest.
    public void Post(Bitmap longFrame, Bitmap squareFrame, string longMode, string squareMode)
    {
        if (_disposed) { longFrame.Dispose(); squareFrame.Dispose(); return; }
        var mail = (longFrame, squareFrame, longMode, squareMode);
        (Bitmap Long, Bitmap Square, string, string)? dropped;
        lock (_mailSync)
        {
            dropped = _mail;
            _mail = mail;
            if (_sender == null)
            {
                _sender = new Thread(SendLoop) { IsBackground = true, Name = "Screen sender" };
                _sender.Start();
            }
        }
        dropped?.Long.Dispose();
        dropped?.Square.Dispose();
        _mailWake.Set();

        long started = Interlocked.Read(ref _sendStartedTicks);
        if (started != 0 && !_stallLogged && DateTime.UtcNow.Ticks - started > TimeSpan.FromSeconds(15).Ticks)
        {
            _stallLogged = true;
            Log.Write("Screen driver has not returned for 15 s; frames are being dropped until it does.");
        }
    }

    private void SendLoop()
    {
        while (!_disposed)
        {
            _mailWake.WaitOne(1000);
            (Bitmap Long, Bitmap Square, string LongMode, string SquareMode)? mail;
            lock (_mailSync) { mail = _mail; _mail = null; }
            if (mail is not { } frame) continue;
            Interlocked.Exchange(ref _sendStartedTicks, DateTime.UtcNow.Ticks);
            try { Send(frame.Long, frame.Square, frame.LongMode, frame.SquareMode); }
            catch (Exception error) { Log.Write("Screen send failed: " + error.Message); }
            finally
            {
                Interlocked.Exchange(ref _sendStartedTicks, 0);
                if (_stallLogged) { _stallLogged = false; Log.Write("Screen driver answered again."); }
                frame.Long.Dispose();
                frame.Square.Dispose();
            }
        }
    }

    private void Send(Bitmap longFrame, Bitmap squareFrame, string longMode, string squareMode)
    {
        lock (_sync)
        {
        DateTime now = DateTime.UtcNow;
        if (_long.Generation != _longGeneration)
        {
            _longGeneration = _long.Generation;
            _hasLongSignature = false;
        }
        // The serial screen is capped independently so a fast music page on the USB screen
        // does not flood its JPEG transport. Each screen retains its own cadence.
        double longInterval = longMode == "music"
            ? FrameSendPolicy.LongMusicIntervalMilliseconds(_longSendMilliseconds)
            : _config.RefreshMilliseconds;
        double squareInterval = squareMode == "music" ? Math.Max(50, 1000 / _config.MusicFrameRate) : squareMode == "clock" ? 1000 : _config.RefreshMilliseconds;
        if (_long.Connected && (now - _lastLongSend).TotalMilliseconds >= longInterval)
        {
            _lastLongSend = now;
            ulong signature = Signature(longFrame);
            bool changed = !_hasLongSignature || signature != _longSignature;
            if (FrameSendPolicy.ShouldSend(changed, _lastLongSent, now))
            {
                if (_long.Post(longFrame, _config.LongRotationDegrees))
                {
                    _longSendMilliseconds = _long.WriteMilliseconds;
                    _longSignature = signature;
                    _hasLongSignature = true;
                    _lastLongSent = now;
                    if (++_longFrames % 30 == 1)
                        Log.Write($"DUAL_LONG_SENT={_longFrames} 1920x462 avg_send_ms={_longSendMilliseconds:0}");
                }
            }
        }
        if (_square.Connected && (now - _lastSquareSend).TotalMilliseconds >= squareInterval)
        {
            _lastSquareSend = now;
            ulong signature = Signature(squareFrame);
            bool changed = !_hasSquareSignature || signature != _squareSignature;
            if (!FrameSendPolicy.ShouldSend(changed, _lastSquareSent, now)) return;
            int result = _square.Send(squareFrame, _config.SquareRotationDegrees);
            if (result == 0)
            {
                _squareSignature = signature;
                _hasSquareSignature = true;
                _lastSquareSent = now;
                if (++_squareFrames % 30 == 1) Log.Write("DUAL_SQUARE_SENT=" + _squareFrames + " 480x480");
            }
            else StatusChanged?.Invoke("方屏发送失败，错误码 " + result);
        }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
        _requested = false;
        _long.Stop();
        _square.Stop();
        StatusChanged?.Invoke("两块屏幕连接已释放");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _mailWake.Set();
        Stop();
        _square.Dispose();
        _long.Dispose();
    }

    internal static unsafe ulong Signature(Bitmap frame)
    {
        BitmapData data = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                for (int y = 0; y < frame.Height; y++)
                {
                    uint* row = (uint*)((byte*)data.Scan0 + y * data.Stride);
                    for (int x = 0; x < frame.Width; x++) { hash ^= row[x]; hash *= 1099511628211UL; }
                }
                return hash;
            }
        }
        finally { frame.UnlockBits(data); }
    }
}
