using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Ports;
using Microsoft.Win32;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

// The long screen is a USB serial device. When it stops answering, opening or
// writing the port can block for minutes inside the driver, so all port I/O
// runs on its own thread: the render loop, the square screen and the lighting
// only ever hand over the latest frame and never wait on the port.
internal sealed class SerialJpegDisplay : IDisposable
{
    private const string DeviceKey = @"SYSTEM\CurrentControlSet\Enum\USB\VID_33C3&PID_F101";
    private static readonly TimeSpan ResetInterval = TimeSpan.FromMinutes(10);
    private static DateTime _lastResetUtc = DateTime.MinValue;

    private readonly object _sync = new();
    private Worker? _worker;

    public bool Connected => _worker?.Connected == true;
    // The device stopped answering USB requests; only a power cycle brings it back.
    public bool NotResponding => _worker?.NotResponding == true;
    public string PortName => _worker?.PortName ?? "";
    // Goes up on every (re)connection, so the caller resends an unchanged frame.
    public int Generation => _worker?.Generation ?? 0;
    public double WriteMilliseconds => _worker?.WriteMilliseconds ?? 130;
    public event Action<string>? StatusChanged;

    internal static string? FindPort()
    {
        // Match the verified display interface, rather than whichever COM port happens to be first.
        using RegistryKey? devices = Registry.LocalMachine.OpenSubKey(DeviceKey + "&MI_00");
        if (devices == null) return null;
        foreach (string instance in devices.GetSubKeyNames())
        {
            using RegistryKey? parameters = devices.OpenSubKey(instance + @"\Device Parameters");
            string? port = parameters?.GetValue("PortName") as string;
            if (port != null && SerialPort.GetPortNames().Contains(port, StringComparer.OrdinalIgnoreCase))
                return port;
        }
        return null;
    }

    // Returns at once; the port is opened (and reopened) in the background.
    public void Start(string configuredPort)
    {
        lock (_sync)
        {
            _worker?.Cancel();
            _worker = new Worker(this, configuredPort);
        }
    }

    public bool WaitConnected(TimeSpan timeout) => _worker?.WaitConnected(timeout) == true;

    // Never waits: a worker stuck in the driver closes its port when the call returns.
    public void Stop()
    {
        lock (_sync)
        {
            _worker?.Cancel();
            _worker = null;
        }
    }

    // Queues the frame for the port; an older frame not yet written is dropped.
    // The frame is rotated in place, so the caller must not use it afterwards.
    // Called from one thread only: the encoding stream is reused.
    public bool Post(Bitmap frame, int rotation)
    {
        Worker? worker = _worker;
        if (worker == null || !worker.Connected) return false;
        Validate(frame, rotation);
        frame.RotateFlip(rotation == 270 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.Rotate90FlipNone);
        _encoded.SetLength(0);
        frame.Save(_encoded, JpegCodec, JpegQuality);
        // A JPEG of the long screen is a few hundred KB; pooled buffers keep it off the large object heap.
        int length = (int)_encoded.Length;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        Buffer.BlockCopy(_encoded.GetBuffer(), 0, buffer, 0, length);
        worker.Post(new JpegFrame(buffer, length));
        return true;
    }

    private readonly MemoryStream _encoded = new(512 * 1024);
    private static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders().Single(x => x.MimeType == "image/jpeg");
    private static readonly EncoderParameters JpegQuality = new(1)
    {
        Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L) },
    };

    internal static byte[] Encode(Bitmap frame, int rotation)
    {
        Validate(frame, rotation);
        using Bitmap physical = new(frame);
        physical.RotateFlip(rotation == 270 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.Rotate90FlipNone);
        using MemoryStream stream = new();
        physical.Save(stream, JpegCodec, JpegQuality);
        return stream.ToArray();
    }

    private static void Validate(Bitmap frame, int rotation)
    {
        if (frame.Width != 1920 || frame.Height != 462)
            throw new ArgumentException("长条屏画布必须为 1920 × 462");
        if (rotation is not (90 or 270))
            throw new ArgumentOutOfRangeException(nameof(rotation));
    }

    private sealed record JpegFrame(byte[] Buffer, int Length)
    {
        public void Release() => ArrayPool<byte>.Shared.Return(Buffer);
    }

    // 5 s, 10 s, 20 s, 40 s, then once a minute.
    internal static TimeSpan RetryDelay(int failures) =>
        TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Max(0, failures - 1))));

    // ERROR_SEM_TIMEOUT: the device stopped answering USB requests.
    internal static bool DeviceHung(Exception error) =>
        error is TimeoutException || (error is IOException && (error.HResult & 0xFFFF) == 121);

    // Re-enumerates the screen's USB device, like unplugging it, which brings a
    // hung display back. Only once every ten minutes, and only with the port closed.
    private static void ResetDevice()
    {
        if (DateTime.UtcNow - _lastResetUtc < ResetInterval) return;
        _lastResetUtc = DateTime.UtcNow;
        try
        {
            using RegistryKey? devices = Registry.LocalMachine.OpenSubKey(DeviceKey);
            foreach (string instance in devices?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using Process? pnputil = Process.Start(new ProcessStartInfo("pnputil.exe", $"/restart-device \"USB\\VID_33C3&PID_F101\\{instance}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                });
                if (pnputil == null) continue;
                pnputil.StandardOutput.ReadToEnd();
                bool exited = pnputil.WaitForExit(30000);
                Log.Write($"Long display USB reset {instance}: {(exited ? "exit " + pnputil.ExitCode : "timed out")}");
            }
        }
        catch (Exception error)
        {
            Log.Write("Long display USB reset failed: " + error.Message);
        }
    }

    public void Dispose() => Stop();

    private sealed class Worker
    {
        private readonly SerialJpegDisplay _owner;
        private readonly string _configuredPort;
        private readonly AutoResetEvent _wake = new(false);
        private readonly ManualResetEventSlim _connected = new(false);
        private volatile bool _cancelled;
        private JpegFrame? _pending;
        private SerialPort? _port;
        private int _failures;

        public bool Connected => _connected.IsSet && !_cancelled;
        public volatile bool NotResponding;
        public volatile string PortName = "";
        public int Generation;
        public double WriteMilliseconds = 130;

        public Worker(SerialJpegDisplay owner, string configuredPort)
        {
            _owner = owner;
            _configuredPort = configuredPort;
            new Thread(Run) { IsBackground = true, Name = "Long screen serial" }.Start();
        }

        public void Cancel() { _cancelled = true; _wake.Set(); }

        public bool WaitConnected(TimeSpan timeout) => _connected.Wait(timeout) && !_cancelled;

        public void Post(JpegFrame jpeg)
        {
            Interlocked.Exchange(ref _pending, jpeg)?.Release();
            _wake.Set();
        }

        private void Run()
        {
            DateTime nextAttempt = DateTime.MinValue;
            try
            {
                while (!_cancelled)
                {
                    if (_port == null)
                    {
                        TimeSpan wait = nextAttempt - DateTime.UtcNow;
                        if (wait > TimeSpan.Zero) { _wake.WaitOne(wait); continue; }
                        if (!Open()) nextAttempt = DateTime.UtcNow + RetryDelay(_failures);
                        continue;
                    }
                    JpegFrame? jpeg = Interlocked.Exchange(ref _pending, null);
                    if (jpeg == null) { _wake.WaitOne(1000); continue; }
                    bool written = Write(jpeg);
                    jpeg.Release();
                    if (!written) nextAttempt = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                }
            }
            finally { Close(); }
        }

        private bool Open()
        {
            try
            {
                string? name = string.IsNullOrWhiteSpace(_configuredPort) ? FindPort() : _configuredPort;
                if (name == null) throw new IOException("长条屏未找到，请检查 USB 连接");
                var port = new SerialPort(name, 9600, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None, ReadTimeout = 500, WriteTimeout = 1500
                };
                port.Open();
                if (_cancelled) { port.Dispose(); return false; }
                _port = port;
                PortName = name;
                if (_failures > 0) Log.Write($"Long display reconnected after {_failures} failed attempts");
                _failures = 0;
                NotResponding = false;
                Interlocked.Increment(ref Generation);
                _connected.Set();
                _owner.StatusChanged?.Invoke("长条屏已连接：1920 × 462 · " + name);
                return true;
            }
            catch (Exception error)
            {
                Fail("connection", error);
                return false;
            }
        }

        private bool Write(JpegFrame jpeg)
        {
            try
            {
                long started = Stopwatch.GetTimestamp();
                _port!.BaseStream.Write(jpeg.Buffer, 0, jpeg.Length);
                _port.BaseStream.Flush();
                WriteMilliseconds = WriteMilliseconds * 0.8 + Stopwatch.GetElapsedTime(started).TotalMilliseconds * 0.2;
                return true;
            }
            catch (Exception error)
            {
                Fail("send", error);
                return false;
            }
        }

        private void Fail(string what, Exception error)
        {
            Close();
            if (_cancelled) return;
            _failures++;
            // The first failure of a run gets the full trace; after that one line now and then.
            if (_failures == 1) Log.Write($"Long display {what} failed: {error}");
            else if (_failures % 10 == 0) Log.Write($"Long display still unavailable after {_failures} attempts: {error.Message}");
            if (_failures == 1) _owner.StatusChanged?.Invoke((what == "send" ? "长条屏发送失败：" : "长条屏连接失败：") + error.Message);
            if (DeviceHung(error) && _failures == 3)
            {
                NotResponding = true;
                Log.Write("Long display is not answering; it needs a power cycle.");
                _owner.StatusChanged?.Invoke("长条屏没有响应：请拔插它的 USB，或关机后断电 30 秒再开机");
            }
            // A hung device holds its port, so access denied after a hang means the same.
            if (DeviceHung(error) || (error is UnauthorizedAccessException && _failures >= 3))
                ResetDevice();
        }

        private void Close()
        {
            _connected.Reset();
            SerialPort? port = _port;
            _port = null;
            try { port?.Dispose(); } catch { }
        }
    }
}
