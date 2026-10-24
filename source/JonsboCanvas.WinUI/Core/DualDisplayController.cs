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

    private readonly FrameMailbox _longMail = new();
    private readonly FrameMailbox _squareMail = new();
    private Thread? _longSender;
    private Thread? _squareSender;
    private volatile bool _disposed;
    private long _longSendStartedTicks;
    private long _squareSendStartedTicks;
    private bool _longStallLogged;
    private bool _squareStallLogged;

    // Takes ownership of both frames and returns at once. Each screen has its
    // own sender so a slow serial write cannot delay the USB display.
    public void Post(Bitmap longFrame, Bitmap squareFrame, string longMode, string squareMode)
    {
        if (_disposed) { longFrame.Dispose(); squareFrame.Dispose(); return; }
        if (_longMail.Post(new ScreenFrame(longFrame, longMode)))
        {
            _longSender = new Thread(LongSendLoop) { IsBackground = true, Name = "Long screen sender" };
            _longSender.Start();
        }
        if (_squareMail.Post(new ScreenFrame(squareFrame, squareMode)))
        {
            _squareSender = new Thread(SquareSendLoop) { IsBackground = true, Name = "Square screen sender" };
            _squareSender.Start();
        }
        CheckStall(ref _longSendStartedTicks, ref _longStallLogged, "Long screen");
        CheckStall(ref _squareSendStartedTicks, ref _squareStallLogged, "Square screen");
    }

    private static void CheckStall(ref long startedTicks, ref bool logged, string name)
    {
        long started = Interlocked.Read(ref startedTicks);
        if (started != 0 && !logged && DateTime.UtcNow.Ticks - started > TimeSpan.FromSeconds(15).Ticks)
        {
            logged = true;
            Log.Write(name + " driver has not returned for 15 s; frames are being dropped until it does.");
        }
    }

    private void LongSendLoop()
    {
        while (!_disposed)
        {
            ScreenFrame? frame = _longMail.Take();
            if (frame is not { } current) return;
            Interlocked.Exchange(ref _longSendStartedTicks, DateTime.UtcNow.Ticks);
            try { SendLong(current); }
            catch (Exception error) { Log.Write("Long screen send failed: " + error.Message); }
            finally
            {
                Interlocked.Exchange(ref _longSendStartedTicks, 0);
                if (_longStallLogged) { _longStallLogged = false; Log.Write("Long screen driver answered again."); }
                current.Bitmap.Dispose();
            }
        }
    }

    private void SquareSendLoop()
    {
        while (!_disposed)
        {
            ScreenFrame? frame = _squareMail.Take();
            if (frame is not { } current) return;
            Interlocked.Exchange(ref _squareSendStartedTicks, DateTime.UtcNow.Ticks);
            try { SendSquare(current); }
            catch (Exception error) { Log.Write("Square screen send failed: " + error.Message); }
            finally
            {
                Interlocked.Exchange(ref _squareSendStartedTicks, 0);
                if (_squareStallLogged) { _squareStallLogged = false; Log.Write("Square screen driver answered again."); }
                current.Bitmap.Dispose();
            }
        }
    }

    private void SendLong(ScreenFrame frame)
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
        double longInterval = frame.Mode == "music"
            ? FrameSendPolicy.LongMusicIntervalMilliseconds(_longSendMilliseconds)
            : _config.RefreshMilliseconds;
        if (_long.Connected && (now - _lastLongSend).TotalMilliseconds >= longInterval)
        {
            _lastLongSend = now;
            ulong signature = Signature(frame.Bitmap);
            bool changed = !_hasLongSignature || signature != _longSignature;
            if (FrameSendPolicy.ShouldSend(changed, _lastLongSent, now))
            {
                if (_long.Post(frame.Bitmap, _config.LongRotationDegrees))
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
        }
    }

    private void SendSquare(ScreenFrame frame)
    {
        lock (_sync)
        {
        DateTime now = DateTime.UtcNow;
        double squareInterval = frame.Mode == "music" ? Math.Max(50, 1000 / _config.MusicFrameRate) : frame.Mode == "clock" ? 1000 : _config.RefreshMilliseconds;
        if (_square.Connected && (now - _lastSquareSend).TotalMilliseconds >= squareInterval)
        {
            _lastSquareSend = now;
            ulong signature = Signature(frame.Bitmap);
            bool changed = !_hasSquareSignature || signature != _squareSignature;
            if (!FrameSendPolicy.ShouldSend(changed, _lastSquareSent, now)) return;
            int result = _square.Send(frame.Bitmap, _config.SquareRotationDegrees);
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
        _longMail.Stop();
        _squareMail.Stop();
        Stop();
        _square.Dispose();
        _long.Dispose();
    }

    private sealed record ScreenFrame(Bitmap Bitmap, string Mode);

    private sealed class FrameMailbox
    {
        private readonly object _sync = new();
        private readonly AutoResetEvent _wake = new(false);
        private ScreenFrame? _frame;
        private bool _running;
        private bool _stopped;

        // Returns true when the caller must start the sender thread.
        public bool Post(ScreenFrame frame)
        {
            lock (_sync)
            {
                if (_stopped) { frame.Bitmap.Dispose(); return false; }
                Interlocked.Exchange(ref _frame, frame)?.Bitmap.Dispose();
                _wake.Set();
                if (_running) return false;
                _running = true;
                return true;
            }
        }

        public ScreenFrame? Take()
        {
            _wake.WaitOne(1000);
            lock (_sync)
            {
                if (_stopped) return null;
                ScreenFrame? frame = Interlocked.Exchange(ref _frame, null);
                if (frame != null) return frame;
                _running = false;
                return null;
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _stopped = true;
                Interlocked.Exchange(ref _frame, null)?.Bitmap.Dispose();
                _wake.Set();
            }
        }
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
