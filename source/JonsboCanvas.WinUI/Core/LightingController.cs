using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JonsboCanvas_WinUI;

// Drives the case ARGB lighting through the motherboard's ITE IT5711 controller
// using Gigabyte's GHidApi.dll. Verified on this machine (X870 EAGLE WIFI7):
// enable built-in effects (CC 32 00), write a static color to every zone
// (0..10) at Mode@11 / Brightness@12 / BGR@14..16, then apply (CC 28 FF 00).
// Zone 7 is the ARGB hub the case fans / AIO / strips are wired to.
internal static class LightingController
{
    private static readonly object Sync = new();
    private static readonly object QueueSync = new();
    private static string _lastKey = "";
    private static bool _resolverHooked;
    private static (byte r, byte g, byte b, byte bri)? _pending;
    private static bool _workerRunning;

    // Static theme color. Deduped and collapsed to the latest value so dragging
    // sliders does not queue a long backlog of HID writes.
    public static void SetColor(byte r, byte g, byte b, byte brightness)
    {
        // Bake brightness into the color channels: the ITE controller fades its
        // dedicated brightness byte over roughly a second, which made slider and
        // beat changes feel sluggish, while color writes snap instantly. Scaling
        // RGB (and pinning the brightness register at 255) keeps every change on
        // the instant path with identical effective output.
        byte sr = Scale(r, brightness), sg = Scale(g, brightness), sb = Scale(b, brightness);
        lock (QueueSync)
        {
            string key = sr + "," + sg + "," + sb;
            if (key == _lastKey) return;
            _lastKey = key;
            _pending = (sr, sg, sb, 255);
            if (_workerRunning) return;
            _workerRunning = true;
            Task.Run(DrainQueue);
        }
    }

    private static byte Scale(byte value, byte level) => (byte)Math.Clamp(
        (int)Math.Round(value * (level / 255.0)), 0, 255);

    // The connection stays open while writes keep coming (the beat loop) and is
    // released after a quiet second, so static colours still leave the device
    // free for Gigabyte Control Center.
    private static void DrainQueue()
    {
        Stopwatch idle = Stopwatch.StartNew();
        while (true)
        {
            (byte r, byte g, byte b, byte bri)? pending;
            bool stop = false;
            lock (QueueSync)
            {
                pending = _pending;
                _pending = null;
                if (pending == null && idle.ElapsedMilliseconds >= 1000)
                    _workerRunning = false;
                stop = !_workerRunning;
            }
            if (pending is { } p)
            {
                WriteAll(p.r, p.g, p.b, p.bri);
                idle.Restart();
            }
            else if (stop)
            {
                lock (Sync) Release();
                return;
            }
            else Thread.Sleep(4);
        }
    }

    // High-rate write used by the music beat loop. Synchronous and throttled so
    // the HID bus is not saturated; called from the beat thread directly.
    public static void Pulse(byte r, byte g, byte b, byte brightness)
    {
        SetColor(r, g, b, brightness);
    }

    // Clear the dedup state so the next SetColor always writes (used when the
    // beat loop hands control back to the static theme).
    public static void Reset() { lock (QueueSync) { _lastKey = ""; _pending = null; } }

    public static void LogPeak(float peak)
    {
        DebugLog($"peak={peak:0.000}");
    }

    public static void LogInfo(string message) => DebugLog(message);

    private static bool _connected;
    private static int _statWrites, _statConnects, _statFailures;
    private static double _statTotalMs, _statMaxMs;
    private static readonly Stopwatch StatClock = Stopwatch.StartNew();

    private static void WriteAll(byte r, byte g, byte b, byte brightness)
    {
        lock (Sync)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                EnsureResolver();
                if (!_connected)
                {
                    var lengths = new ReportLengths[32];
                    if (Connect(0x048D, 0x5711, 0xFF89, 0xCC, lengths) != 1) { DebugLog("connect failed"); return; }
                    var enable = new byte[64];
                    enable[0] = 0xCC; enable[1] = 0x32;
                    Write(0x048D, 0x5711, 0, enable, 64);
                    _connected = true;
                    _statConnects++;
                }

                for (int zone = 0; zone <= 10; zone++)
                {
                    var e = new byte[64];
                    e[0] = 0xCC;
                    e[1] = (byte)(0x20 + zone);
                    BitConverter.GetBytes(1u << zone).CopyTo(e, 2);
                    e[11] = 1;                 // static
                    e[12] = brightness;
                    e[14] = b; e[15] = g; e[16] = r;  // BGR
                    Write(0x048D, 0x5711, 0, e, 64);
                }

                var apply = new byte[64];
                apply[0] = 0xCC; apply[1] = 0x28; apply[2] = 0xFF; apply[3] = 0x00;
                // A failed apply means the handle went stale (another app, sleep):
                // reconnect on the next write.
                if (Write(0x048D, 0x5711, 0, apply, 64) != 1) { _statFailures++; Release(); }
            }
            catch (Exception ex) { DebugLog("error " + ex.Message); Release(); }
            double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _statWrites++;
            _statTotalMs += ms;
            _statMaxMs = Math.Max(_statMaxMs, ms);
            if (StatClock.Elapsed.TotalSeconds >= 5)
            {
                DebugLog($"writes={_statWrites} avg={_statTotalMs / _statWrites:0.0}ms max={_statMaxMs:0.0}ms " +
                    $"connects={_statConnects} failed={_statFailures} last=({r},{g},{b})");
                _statWrites = _statConnects = _statFailures = 0;
                _statTotalMs = _statMaxMs = 0;
                StatClock.Restart();
            }
        }
    }

    // Caller holds Sync.
    private static void Release()
    {
        if (!_connected) return;
        _connected = false;
        try { Disconnect(0x048D, 0x5711, 0); } catch { }
    }

    private static readonly string DebugLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyCanvas", "lighting.log");

    private static void DebugLog(string message)
    {
        try { File.AppendAllText(DebugLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n"); } catch { }
    }

    private static void EnsureResolver()
    {
        if (_resolverHooked) return;
        _resolverHooked = true;
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(LightingController).Assembly, (name, asm, path) =>
            {
                if (name != "GHidApi" && name != "GHidApi.dll") return IntPtr.Zero;
                string local = Path.Combine(AppContext.BaseDirectory, "GHidApi.dll");
                if (File.Exists(local) && NativeLibrary.TryLoad(local, out var h)) return h;
                string gcc = @"C:\Program Files\GIGABYTE\Control Center\GHidApi.dll";
                if (File.Exists(gcc) && NativeLibrary.TryLoad(gcc, out h)) return h;
                return IntPtr.Zero;
            });
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReportLengths { public ushort Feature, Input, Output; }

    [DllImport("GHidApi", EntryPoint = "dllexp_ConnectDevice", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Connect(ushort vid, ushort pid, ushort usagePage, ushort usage, [In, Out] ReportLengths[] reports);

    [DllImport("GHidApi", EntryPoint = "dllexp_DisconnectDevice", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Disconnect(ushort vid, ushort pid, byte index);

    [DllImport("GHidApi", EntryPoint = "dllexp_WriteDataToDevice", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Write(ushort vid, ushort pid, byte index, byte[] data, byte count);
}
