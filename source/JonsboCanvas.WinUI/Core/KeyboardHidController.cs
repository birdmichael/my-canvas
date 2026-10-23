using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace JonsboCanvas_WinUI;

// The ROG Azoth 96 HE uses the same private command transport as Gear Link's
// WebHID page. The receiver exposes the keyboard as a 64-byte control report;
// both lighting and the OLED image stream travel over that report.
internal sealed class KeyboardHidController : IDisposable
{
    private const ushort AsusVendorId = 0x0B05;
    private const ushort ReceiverProductId = 0x1AD0;
    private const ushort KeyboardWirelessProductId = 0x1C12;
    private const ushort ReceiverControlUsagePage = 0xFF02;
    private const ushort ReceiverEventUsagePage = 0xFFC2;
    private const ushort KeyboardControlUsagePage = 0xFF00;
    private const ushort KeyboardEventUsagePage = 0xFFC0;
    private const int OledPayloadLength = KeyboardDisplay.PayloadLength;
    private const int LightIntervalMilliseconds = 45;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint ErrorIoPending = 997;

    private static readonly Guid HidInterfaceGuid =
        new("4D1E55B2-F16F-11CF-88CB-001111000030");

    private readonly object _deviceSync = new();
    private readonly object _lightSync = new();
    private readonly SemaphoreSlim _wallpaperGate = new(1, 1);
    private DeviceSession? _session;
    private DateTime _connectRetryAfter;
    private volatile bool _disposed;
    private KeyboardLight? _pendingLight;
    private KeyboardLight? _latestLight;
    private bool _lightWorkerRunning;
    private KeyboardLight? _appliedLight;
    private string _appliedWallpaperId = "";
    private DateTime _lastFailureLog;
    private DateTime _lastLightSuccessLog;
    private int _sessionCount, _lightWrites, _uploads, _failures, _consecutiveFailures;
    private volatile string _lastDisplayError = "";
    private volatile string _lastLightError = "";
    internal string LastError => _lastDisplayError.Length > 0 ? _lastDisplayError : _lastLightError;
    private int _state;

    public string AppliedWallpaperId => Volatile.Read(ref _appliedWallpaperId);
    public bool IsUploading => _wallpaperGate.CurrentCount == 0;
    public ScreenState State => (ScreenState)Volatile.Read(ref _state);
    internal (int Sessions, int LightWrites, int Uploads, int Failures) Diagnostics =>
        (Volatile.Read(ref _sessionCount), Volatile.Read(ref _lightWrites),
         Volatile.Read(ref _uploads), Volatile.Read(ref _failures));
    internal int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    public void RequestReconnect()
    {
        DropSession();
        lock (_lightSync) _appliedLight = null;
        Volatile.Write(ref _appliedWallpaperId, "");
        Volatile.Write(ref _state, (int)ScreenState.Searching);
    }

    internal Task<byte[]> ReadStateAsync(byte command, byte subCommand) => Task.Run(() =>
    {
        lock (_deviceSync) return EnsureSession().ReadState(command, subCommand);
    });

    public void SetLightColor(byte r, byte g, byte b, byte brightness)
    {
        if (_disposed) return;
        byte percent = (byte)Math.Clamp(Math.Round(brightness * 100 / 255.0), 0, 100);
        var next = new KeyboardLight(r, g, b, percent);
        lock (_lightSync)
        {
            if (!KeyboardLightDedupPolicy.ShouldApply(next, _appliedLight, _pendingLight)) return;
            _pendingLight = next;
            _latestLight = _pendingLight;
            if (_lightWorkerRunning) return;
            _lightWorkerRunning = true;
        }
        _ = Task.Run(LightLoop);
    }

    // The beat loop calls this at 15 ms; the worker keeps only the newest frame
    // and writes it at a rate the HID receiver can sustain.
    public void Pulse(byte r, byte g, byte b, byte brightness) =>
        SetLightColor(r, g, b, brightness);

    public async Task<bool> ApplyWallpaperAsync(byte[] payload, string wallpaperId, bool force = false)
    {
        if (_disposed || payload.Length != OledPayloadLength) return false;
        await _wallpaperGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return false;
            if (!force && string.Equals(wallpaperId, AppliedWallpaperId, StringComparison.Ordinal))
                return true;

            Exception? lastError = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        lock (_deviceSync)
                        {
                            DeviceSession session = EnsureSession();
                            session.UploadOledImage(payload);
                            // Upload mode can override the visible RGB effect. Restore
                            // before releasing the shared transport, even if static
                            // lighting was deduplicated while the image was uploading.
                            RestoreLightingAfterUpload(session);
                        }
                    }).ConfigureAwait(false);
                    Volatile.Write(ref _appliedWallpaperId, wallpaperId);
                    Interlocked.Increment(ref _uploads);
                    Volatile.Write(ref _consecutiveFailures, 0);
                    _lastDisplayError = "";
                    LogInfo($"wallpaper {wallpaperId} uploaded directly to keyboard");
                    return true;
                }
                catch (Exception exception)
                {
                    _lastDisplayError = exception.Message;
                    LogInfo($"wallpaper attempt {attempt + 1} failed: {exception.Message}");
                    lastError = exception;
                    Interlocked.Increment(ref _failures);
                    DropSession();
                    if (attempt == 0) await Task.Delay(1000).ConfigureAwait(false);
                }
            }

            LogFailure("wallpaper upload", lastError ?? new IOException("unknown upload error"));
            Interlocked.Increment(ref _consecutiveFailures);
            return false;
        }
        finally
        {
            _wallpaperGate.Release();
        }
    }

    // Caller holds _deviceSync; do not fail/re-upload a completed OLED image
    // merely because the following independent lighting command needs a retry.
    private void RestoreLightingAfterUpload(DeviceSession session)
    {
        KeyboardLight? light;
        lock (_lightSync) light = _latestLight;
        if (light is not { } latest) return;
        try
        {
            session.SetStaticColor(latest.R, latest.G, latest.B, latest.Brightness);
            Interlocked.Increment(ref _lightWrites);
            lock (_lightSync) _appliedLight = latest;
            _lastLightError = "";
            LogInfo($"lighting restored after OLED upload rgb={latest.R},{latest.G},{latest.B} brightness={latest.Brightness}");
        }
        catch (Exception exception)
        {
            _lastLightError = exception.Message;
            Interlocked.Increment(ref _failures);
            LogFailure("post-upload lighting restore", exception);
            lock (_lightSync)
            {
                _appliedLight = null;
                _pendingLight = _latestLight;
                if (_lightWorkerRunning) return;
                _lightWorkerRunning = true;
            }
            _ = Task.Run(LightLoop);
        }
    }

    private void LightLoop()
    {
        Stopwatch sinceWrite = Stopwatch.StartNew();
        while (!_disposed)
        {
            KeyboardLight? request;
            lock (_lightSync)
            {
                request = _pendingLight;
                _pendingLight = null;
            }

            if (request == null)
            {
                Thread.Sleep(20);
                lock (_lightSync)
                {
                    if (_pendingLight == null)
                    {
                        _lightWorkerRunning = false;
                        return;
                    }
                }
                continue;
            }

            try
            {
                lock (_deviceSync)
                {
                    lock (_lightSync)
                    {
                        request = _latestLight ?? request;
                        _pendingLight = null;
                    }
                    DeviceSession session = EnsureSession();
                    sinceWrite.Restart();
                    session.SetStaticColor(request.Value.R, request.Value.G, request.Value.B, request.Value.Brightness);
                }
                Interlocked.Increment(ref _lightWrites);
                Volatile.Write(ref _consecutiveFailures, 0);
                _lastLightError = "";
                lock (_lightSync) _appliedLight = request;
                if ((DateTime.UtcNow - _lastLightSuccessLog).TotalSeconds >= 10)
                {
                    _lastLightSuccessLog = DateTime.UtcNow;
                    LogInfo(
                        $"lighting rgb={request.Value.R},{request.Value.G},{request.Value.B} brightness={request.Value.Brightness}");
                }
            }
            catch (Exception exception)
            {
                _lastLightError = exception.Message;
                LogFailure("lighting", exception);
                Interlocked.Increment(ref _failures);
                Interlocked.Increment(ref _consecutiveFailures);
                DropSession();
                lock (_lightSync)
                {
                    _appliedLight = null;
                    _pendingLight ??= request;
                }
                Thread.Sleep((int)Math.Min(
                    KeyboardRetryBackoffPolicy.NextDelay(ConsecutiveFailures).TotalMilliseconds,
                    int.MaxValue));
                continue;
            }

            int wait = LightIntervalMilliseconds - (int)sinceWrite.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep(wait);
        }
    }

    // Caller holds _deviceSync.
    private DeviceSession EnsureSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session != null) return _session;
        int retryDelay = (int)Math.Ceiling((_connectRetryAfter - DateTime.UtcNow).TotalMilliseconds);
        if (retryDelay > 0) Thread.Sleep(Math.Min(retryDelay, 5000));
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            Volatile.Write(ref _state, (int)ScreenState.Searching);
            _session = DeviceSession.Open();
            byte[] animation = _session.ReadState(0x21, 0);
            if (animation[4] != 6) Volatile.Write(ref _appliedWallpaperId, "");
            Interlocked.Increment(ref _sessionCount);
            _connectRetryAfter = DateTime.MinValue;
            Volatile.Write(ref _state, (int)ScreenState.Connected);
            LogInfo("ROG AZOTH 96 HE HID session opened");
            return _session;
        }
        catch
        {
            Volatile.Write(ref _state, (int)ScreenState.NotResponding);
            _connectRetryAfter = DateTime.UtcNow.AddSeconds(5);
            throw;
        }
    }

    private void DropSession()
    {
        lock (_deviceSync)
        {
            // Waiting for a retry must not move the deadline forward again.
            if (_session == null) return;
            _session?.Dispose();
            _session = null;
            Volatile.Write(ref _state, (int)ScreenState.Searching);
            // A new HID session has no OLED state. Force the current content
            // through the uploader again after an unplug or receiver reset.
            Volatile.Write(ref _appliedWallpaperId, "");
            _connectRetryAfter = DateTime.UtcNow.AddSeconds(2);
        }
    }

    private static void LogInfo(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyCanvas", "keyboard.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch
        {
        }
    }

    private void LogFailure(string area, Exception exception)
    {
        if ((DateTime.UtcNow - _lastFailureLog).TotalSeconds < 5) return;
        _lastFailureLog = DateTime.UtcNow;
        LogInfo($"{area} failed: {exception.Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lightSync)
        {
            _pendingLight = null;
        }
        lock (_deviceSync)
        {
            _session?.Dispose();
            _session = null;
            Volatile.Write(ref _state, (int)ScreenState.Off);
        }
        // In-flight uploads release this gate after noticing disposal.
    }

    private sealed class DeviceSession : IDisposable
    {
        private const int OledPacketDataLength = 57;
        private const int OledPacketsPerBatch = 20;

        private readonly HidInterface _receiver;
        private readonly HidInterface _keyboard;
        private readonly IntPtr _receiverControl;
        private readonly IntPtr _receiverRead;
        private readonly IntPtr _receiverEvent;
        private readonly IntPtr _keyboardControl;
        private readonly IntPtr _keyboardRead;
        private readonly IntPtr _keyboardEvent;
        private readonly BlockingCollection<byte[]> _responses = new(new ConcurrentQueue<byte[]>());
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread _receiverReader;
        private readonly Thread _keyboardReader;
        private readonly IntPtr _receiverOverlapped;
        private readonly IntPtr _keyboardOverlapped;
        private readonly IntPtr _receiverIoEvent;
        private readonly IntPtr _keyboardIoEvent;
        private bool _disposed;

        private DeviceSession(
            HidInterface receiver,
            HidInterface receiverEvents,
            HidInterface keyboard,
            HidInterface keyboardEvents,
            IntPtr receiverControl,
            IntPtr receiverRead,
            IntPtr receiverEvent,
            IntPtr keyboardControl,
            IntPtr keyboardRead,
            IntPtr keyboardEvent)
        {
            _receiver = receiver;
            _keyboard = keyboard;
            _receiverControl = receiverControl;
            _receiverRead = receiverRead;
            _receiverEvent = receiverEvent;
            _keyboardControl = keyboardControl;
            _keyboardRead = keyboardRead;
            _keyboardEvent = keyboardEvent;
            _receiverOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
            _keyboardOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
            _receiverIoEvent = CreateEventW(IntPtr.Zero, true, false, null);
            _keyboardIoEvent = CreateEventW(IntPtr.Zero, true, false, null);
            if (_receiverIoEvent == IntPtr.Zero || _keyboardIoEvent == IntPtr.Zero)
                throw new IOException("Unable to create ROG HID I/O events");
            _receiverReader = StartReader(
                _receiverRead,
                receiver.InputReportByteLength,
                "receiver",
                _receiverOverlapped,
                _receiverIoEvent);
            _keyboardReader = StartReader(
                _keyboardRead,
                keyboard.InputReportByteLength,
                "keyboard",
                _keyboardOverlapped,
                _keyboardIoEvent);
        }

        public static DeviceSession Open()
        {
            List<HidInterface> interfaces = EnumerateInterfaces();
            HidInterface receiver = interfaces.FirstOrDefault(item =>
                item.ProductId == ReceiverProductId &&
                item.UsagePage == ReceiverControlUsagePage &&
                item.Usage == 1)
                ?? throw new IOException("ROG receiver control interface not found");
            HidInterface receiverEvents = interfaces.FirstOrDefault(item =>
                item.ProductId == ReceiverProductId &&
                item.UsagePage == ReceiverEventUsagePage &&
                item.Usage == 1)
                ?? throw new IOException("ROG receiver event interface not found");
            HidInterface keyboard = interfaces.FirstOrDefault(item =>
                item.ProductId == ReceiverProductId &&
                item.UsagePage == KeyboardControlUsagePage &&
                item.Usage == 1)
                ?? throw new IOException("ROG keyboard proxy interface not found");
            HidInterface keyboardEvents = interfaces.FirstOrDefault(item =>
                item.ProductId == ReceiverProductId &&
                item.UsagePage == KeyboardEventUsagePage &&
                item.Usage == 1)
                ?? throw new IOException("ROG keyboard event interface not found");

            IntPtr receiverControl = OpenInterface(receiver.Path, write: true, required: false);
            IntPtr receiverRead = OpenInterface(receiver.Path, write: false, required: false, overlapped: true);
            IntPtr receiverEvent = OpenInterface(receiverEvents.Path, write: false, required: false);
            IntPtr keyboardControl = OpenInterface(keyboard.Path, write: true, required: false);
            IntPtr keyboardRead = OpenInterface(keyboard.Path, write: false, required: false, overlapped: true);
            IntPtr keyboardEvent = OpenInterface(keyboardEvents.Path, write: false, required: false);
            if (receiverControl == IntPtr.Zero || receiverRead == IntPtr.Zero ||
                receiverEvent == IntPtr.Zero ||
                keyboardControl == IntPtr.Zero || keyboardRead == IntPtr.Zero ||
                keyboardEvent == IntPtr.Zero)
            {
                Close(receiverControl);
                Close(receiverRead);
                Close(receiverEvent);
                Close(keyboardControl);
                Close(keyboardRead);
                Close(keyboardEvent);
                throw new IOException("Unable to open ROG keyboard HID interfaces");
            }

            DeviceSession? session = null;
            try
            {
                session = new DeviceSession(
                    receiver,
                    receiverEvents,
                    keyboard,
                    keyboardEvents,
                    receiverControl,
                    receiverRead,
                    receiverEvent,
                    keyboardControl,
                    keyboardRead,
                    keyboardEvent);
                session.FindKeyboardReportId();
                return session;
            }
            catch
            {
                if (session != null)
                {
                    session.Dispose();
                }
                else
                {
                    Close(receiverControl);
                    Close(receiverRead);
                    Close(receiverEvent);
                    Close(keyboardControl);
                    Close(keyboardRead);
                    Close(keyboardEvent);
                }
                throw;
            }
        }

        private void FindKeyboardReportId()
        {
            byte receiverReportId = _receiver.OutputReportIds.FirstOrDefault();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                byte[] scan = SendAndWait(
                    _receiverControl,
                    _receiver.OutputReportByteLength,
                    receiverReportId,
                    new byte[] { 0xA0, 0x00, 0x00 },
                    2,
                    3000);
                if (scan.Length >= 4)
                {
                    int count = scan[2] | scan[3] << 8;
                    for (int index = 0; index < count; index++)
                    {
                        int offset = 4 + index * 4;
                        if (offset + 4 > scan.Length) break;
                        ushort pid = (ushort)(scan[offset] | scan[offset + 1] << 8);
                        if (pid != KeyboardWirelessProductId) continue;
                        KeyboardReportId = scan[offset + 2];
                        if (KeyboardReportId != _keyboard.OutputReportIds.FirstOrDefault())
                            LogInfo($"receiver reports keyboard report 0x{KeyboardReportId:X2}");
                        Thread.Sleep(120);
                        return;
                    }
                }
                Thread.Sleep(250);
            }
            throw new IOException("ROG AZOTH 96 HE is not paired to the receiver");
        }

        public byte KeyboardReportId { get; private set; }

        public byte[] ReadState(byte command, byte subCommand)
        {
            byte[] response = SendAndWait(_keyboardControl, _keyboard.OutputReportByteLength,
                KeyboardReportId, new byte[] { command, subCommand, 0, 0 }, 4, 2000);
            if (response.Length < 5) throw new IOException("ROG state read timed out");
            return response;
        }

        public void SetStaticColor(byte r, byte g, byte b, byte brightness)
        {
            byte[] payload =
            {
                0x51, 0x2C, 0x00, 0x00,
                0x01, brightness, 0x00,
                0xFF, 0xFF,
                r, g, b,
                0x00, 0x00, 0x00,
                0x00, 0x00, 0x00
            };
            Exception? lastError = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    byte[] response = SendAndWait(
                        _keyboardControl,
                        _keyboard.OutputReportByteLength,
                        KeyboardReportId,
                        payload,
                        4,
                        1500);
                    if (response.Length > 0) return;
                    lastError = new IOException("ROG keyboard lighting response timed out");
                }
                catch (IOException exception)
                {
                    lastError = exception;
                }
                Thread.Sleep(180);
            }
            throw lastError ?? new IOException("ROG keyboard lighting failed");
        }

        public void UploadOledImage(byte[] imageData)
        {
            int packetCount = (imageData.Length + OledPacketDataLength - 1) / OledPacketDataLength;
            byte[] start = new byte[8];
            start[0] = 0x61;
            start[1] = 0x01;
            start[4] = (byte)packetCount;
            start[5] = (byte)(packetCount >> 8);
            start[6] = (byte)(packetCount >> 16);
            start[7] = (byte)(packetCount >> 24);
            byte[] response = SendAndWait(
                _keyboardControl,
                _keyboard.OutputReportByteLength,
                KeyboardReportId,
                start,
                2,
                5000);
            if (IsFailure(response)) throw new IOException("ROG OLED upload start failed");

            for (int batchStart = 0; batchStart < packetCount; batchStart += OledPacketsPerBatch)
            {
                int batchEnd = Math.Min(batchStart + OledPacketsPerBatch, packetCount);
                var payloads = new List<byte[]>(batchEnd - batchStart);
                for (int packetIndex = batchStart; packetIndex < batchEnd; packetIndex++)
                {
                    int imageOffset = packetIndex * OledPacketDataLength;
                    int remainingIndex = packetCount - packetIndex - 1;
                    byte[] payload = new byte[63];
                    payload[0] = 0x61;
                    payload[1] = 0x02;
                    payload[2] = (byte)remainingIndex;
                    payload[3] = (byte)(remainingIndex >> 8);
                    payload[4] = (byte)(remainingIndex >> 16);
                    payload[5] = (byte)(remainingIndex >> 24);
                    int copyLength = Math.Min(OledPacketDataLength, imageData.Length - imageOffset);
                    Array.Copy(imageData, imageOffset, payload, 6, copyLength);
                    payloads.Add(payload);
                }

                byte[] batchResponse = Array.Empty<byte>();
                for (int attempt = 0; attempt <= 3; attempt++)
                {
                    batchResponse = SendBatchAndWait(payloads, 5000);
                    if (!IsFailure(batchResponse)) break;
                }
                if (IsFailure(batchResponse))
                    throw new IOException($"ROG OLED batch {batchStart}-{batchEnd - 1} failed after retries");
            }

            byte[] end = SendAndWait(
                _keyboardControl,
                _keyboard.OutputReportByteLength,
                KeyboardReportId,
                new byte[] { 0x61, 0x03, 0x00, 0x00 },
                2,
                10000);
            if (IsFailure(end)) throw new IOException("ROG OLED upload end failed");
            byte[] animation = SendAndWait(
                _keyboardControl, _keyboard.OutputReportByteLength, KeyboardReportId,
                new byte[] { 0x21, 0x00, 0x00, 0x00 }, 4, 2000);
            if (animation.Length < 5 || animation[4] != 6)
                throw new IOException("ROG OLED did not enter custom-image mode");
        }

        private byte[] SendAndWait(
            IntPtr handle,
            ushort outputReportLength,
            byte reportId,
            byte[] payload,
            int matchBytes,
            int timeoutMs)
        {
            while (_responses.TryTake(out _))
            {
            }
            if (!WritePayload(handle, outputReportLength, reportId, payload))
                throw new IOException("ROG HID write failed");

            int actualMatch = Math.Min(matchBytes, payload.Length);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (!_responses.TryTake(out byte[]? response, Math.Max(1, (int)remaining.TotalMilliseconds)))
                    break;

                byte[] data = response;
                if (data.Length <= 1 || data[0] != reportId) continue;
                data = data.Skip(1).ToArray();
                if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xAA)
                    throw new IOException("ROG keyboard rejected the command");
                if (data.Length >= actualMatch && data.Take(actualMatch).SequenceEqual(payload.Take(actualMatch)))
                    return data;
            }
            return Array.Empty<byte>();
        }

        private byte[] SendBatchAndWait(IReadOnlyList<byte[]> payloads, int timeoutMs)
        {
            while (_responses.TryTake(out _))
            {
            }
            foreach (byte[] payload in payloads)
            {
                if (!WritePayload(_keyboardControl, _keyboard.OutputReportByteLength, KeyboardReportId, payload))
                    return Array.Empty<byte>();
            }

            byte[] match = payloads[^1];
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (!_responses.TryTake(out byte[]? response, Math.Max(1, (int)remaining.TotalMilliseconds)))
                    break;
                byte[] data = response;
                if (data.Length <= 1 || data[0] != KeyboardReportId) continue;
                data = data.Skip(1).ToArray();
                // A delayed ACK for the previous batch is not completion of this one.
                if (data.Length >= 8 && data.AsSpan(0, 6).SequenceEqual(match.AsSpan(0, 6)))
                    return data;
            }
            return Array.Empty<byte>();
        }

        private static bool WritePayload(
            IntPtr handle,
            ushort outputReportLength,
            byte reportId,
            byte[] payload)
        {
            int prefixLength = reportId == 0 ? 0 : 1;
            int capacity = Math.Max(0, outputReportLength - prefixLength);
            byte[] report = new byte[outputReportLength];
            if (reportId != 0) report[0] = reportId;
            Array.Copy(payload, 0, report, prefixLength, Math.Min(payload.Length, capacity));
            return WriteFile(handle, report, (uint)report.Length, out uint written, IntPtr.Zero) &&
                   written == report.Length;
        }

        private static bool IsFailure(byte[] response) =>
            response.Length == 0 ||
            (response.Length > 7 && response[6] == 0xFF && response[7] == 0xAA);

        private Thread StartReader(
            IntPtr handle,
            ushort reportLength,
            string name,
            IntPtr overlapped,
            IntPtr ioEvent)
        {
            var thread = new Thread(() =>
            {
                // Overlapped I/O outlives ReadFile's P/Invoke call. Its destination
                // must stay at one address until Windows completes the read.
                IntPtr buffer = Marshal.AllocHGlobal(reportLength);
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        Marshal.StructureToPtr(new NativeOverlapped { EventHandle = ioEvent }, overlapped, false);
                        ResetEvent(ioEvent);
                        if (!ReadFile(handle, buffer, reportLength, IntPtr.Zero, overlapped))
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (error != ErrorIoPending)
                            {
                                if (!_stop.IsCancellationRequested)
                                    LogInfo($"{name} HID read failed: {error}");
                                break;
                            }
                            // Poll only the completion event, never the device. Cancel
                            // even if disposal raced with issuance of this read.
                            while (WaitForSingleObject(ioEvent, 100) == 258)
                                if (_stop.IsCancellationRequested) CancelIoEx(handle, overlapped);
                        }

                        if (!GetOverlappedResult(handle, overlapped, out uint read, true))
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (!_stop.IsCancellationRequested)
                                LogInfo($"{name} HID overlapped read failed: {error}");
                            break;
                        }
                        if (read > 0)
                        {
                            byte[] report = new byte[read];
                            Marshal.Copy(buffer, report, 0, report.Length);
                            _responses.Add(report);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            })
            {
                IsBackground = true,
                Name = "ROG keyboard " + name
            };
            thread.Start();
            return thread;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            CancelIoEx(_receiverRead, _receiverOverlapped);
            CancelIoEx(_keyboardRead, _keyboardOverlapped);
            bool receiverStopped = _receiverReader.Join(1000);
            bool keyboardStopped = _keyboardReader.Join(1000);
            Close(_keyboardRead);
            Close(_keyboardControl);
            Close(_keyboardEvent);
            Close(_receiverRead);
            Close(_receiverControl);
            Close(_receiverEvent);
            if (receiverStopped && keyboardStopped)
            {
                Close(_receiverIoEvent);
                Close(_keyboardIoEvent);
                Marshal.FreeHGlobal(_receiverOverlapped);
                Marshal.FreeHGlobal(_keyboardOverlapped);
                _stop.Dispose();
            }
        }
    }

    private sealed class HidInterface
    {
        public string Path { get; set; } = "";
        public ushort ProductId { get; set; }
        public ushort Usage { get; set; }
        public ushort UsagePage { get; set; }
        public ushort InputReportByteLength { get; set; }
        public ushort OutputReportByteLength { get; set; }
        public ushort FeatureReportByteLength { get; set; }
        public List<byte> InputReportIds { get; set; } = new();
        public List<byte> OutputReportIds { get; set; } = new();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public uint Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(
        out uint length,
        ref Guid interfaceClassGuid,
        string? deviceId,
        uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(
        ref Guid interfaceClassGuid,
        string? deviceId,
        char[] buffer,
        uint bufferLength,
        uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr file,
        IntPtr buffer,
        uint bytesToRead,
        IntPtr bytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr file,
        byte[] buffer,
        uint bytesToWrite,
        out uint bytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(IntPtr file, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(
        IntPtr file,
        IntPtr overlapped,
        out uint bytesTransferred,
        bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEventW(
        IntPtr eventAttributes,
        bool manualReset,
        bool initialState,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ResetEvent(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetEvent(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(IntPtr file, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(IntPtr file, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, ref HidpCaps capabilities);

    [DllImport("hid.dll")]
    private static extern int HidP_GetValueCaps(
        int reportType,
        IntPtr valueCaps,
        ref ushort valueCapsLength,
        IntPtr preparsedData);

    private static List<HidInterface> EnumerateInterfaces()
    {
        Guid guid = HidInterfaceGuid;
        int status = CM_Get_Device_Interface_List_SizeW(out uint length, ref guid, null, 0);
        if (status != 0) throw new Win32Exception(status);
        char[] buffer = new char[length];
        status = CM_Get_Device_Interface_ListW(ref guid, null, buffer, length, 0);
        if (status != 0) throw new Win32Exception(status);

        var interfaces = new List<HidInterface>();
        int index = 0;
        while (index < buffer.Length && buffer[index] != '\0')
        {
            int end = Array.IndexOf(buffer, '\0', index);
            if (end < 0) break;
            string path = new(buffer, index, end - index);
            index = end + 1;

            IntPtr handle = OpenInterface(path, write: true, required: false);
            if (handle == IntPtr.Zero) continue;
            try
            {
                var attributes = new HiddAttributes { Size = (uint)Marshal.SizeOf<HiddAttributes>() };
                if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorId != AsusVendorId)
                    continue;
                if (!HidD_GetPreparsedData(handle, out IntPtr preparsed)) continue;
                try
                {
                    var caps = new HidpCaps { Reserved = new ushort[17] };
                    if (HidP_GetCaps(preparsed, ref caps) < 0) continue;
                    if (caps.NumberInputValueCaps > 256 ||
                        caps.NumberOutputValueCaps > 256 ||
                        caps.NumberFeatureValueCaps > 256)
                        continue;
                    interfaces.Add(new HidInterface
                    {
                        Path = path,
                        ProductId = attributes.ProductId,
                        Usage = caps.Usage,
                        UsagePage = caps.UsagePage,
                        InputReportByteLength = caps.InputReportByteLength,
                        OutputReportByteLength = caps.OutputReportByteLength,
                        FeatureReportByteLength = caps.FeatureReportByteLength,
                        InputReportIds = GetValueReportIds(preparsed, 0, caps.NumberInputValueCaps),
                        OutputReportIds = GetValueReportIds(preparsed, 1, caps.NumberOutputValueCaps),
                    });
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        return interfaces;
    }

    private static List<byte> GetValueReportIds(IntPtr preparsed, int reportType, ushort capCount)
    {
        if (capCount == 0) return new List<byte>();
        const int valueCapsSize = 72;
        ushort length = capCount;
        IntPtr buffer = Marshal.AllocHGlobal(checked(length * valueCapsSize));
        try
        {
            if (HidP_GetValueCaps(reportType, buffer, ref length, preparsed) < 0)
                return new List<byte>();
            var ids = new List<byte>();
            for (int index = 0; index < length; index++)
            {
                byte id = Marshal.ReadByte(buffer, index * valueCapsSize + 2);
                if (!ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr OpenInterface(
        string path,
        bool write,
        bool required = true,
        bool overlapped = false)
    {
        uint access = write ? GenericRead | GenericWrite : GenericRead;
        IntPtr handle = CreateFileW(
            path,
            access,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            overlapped ? FileFlagOverlapped : 0,
            IntPtr.Zero);
        if (handle != new IntPtr(-1)) return handle;
        if (required) throw new Win32Exception(Marshal.GetLastWin32Error(), path);
        return IntPtr.Zero;
    }

    private static void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero && handle != new IntPtr(-1)) CloseHandle(handle);
    }
}
