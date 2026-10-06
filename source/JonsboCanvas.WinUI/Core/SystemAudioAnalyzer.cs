using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace JonsboCanvas_WinUI;

// Listens to whatever the system is playing (WASAPI loopback) and reports the
// RMS of the bass band, where kicks live, and of the full band. The endpoint
// peak meter can't tell a kick from a held chord once a mastered track sits
// at its ceiling, which left the lights frozen through loud songs.
internal static class SystemAudioAnalyzer
{
    private const double BassCutoffHz = 140;
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private static readonly object Sync = new();
    private static FastLoopbackCapture? _capture;
    private static DateTime _retryAfter;
    private static double _alpha, _low1, _low2;
    private static double _bassSum, _fullSum;
    private static int _count;

    // Energy since the previous read. Packets arrive every ~10 ms, so a read
    // that falls between two repeats the last packet rather than reading as a
    // drop to silence; loopback delivers nothing while the system is silent,
    // so a longer gap is zero. False when capture is unavailable.
    public static bool TryRead(out float bass, out float full)
    {
        lock (Sync)
        {
            EnsureCapture();
            if (_count > 0)
            {
                _lastBass = (float)Math.Sqrt(_bassSum / _count);
                _lastFull = (float)Math.Sqrt(_fullSum / _count);
                _lastData = Environment.TickCount64;
                _bassSum = _fullSum = 0;
                _count = 0;
            }
            else if (Environment.TickCount64 - _lastData > 150) _lastBass = _lastFull = 0;
            bass = _lastBass;
            full = _lastFull;
            return _capture != null;
        }
    }

    private static float _lastBass, _lastFull;
    private static long _lastData;

    public static void Stop()
    {
        FastLoopbackCapture? detached;
        lock (Sync) detached = Detach();
        Dispose(detached);
    }

    private static void EnsureCapture()
    {
        if (_capture != null || DateTime.UtcNow < _retryAfter) return;
        FastLoopbackCapture? capture = null;
        try
        {
            capture = new FastLoopbackCapture();
            WaveFormat format = capture.WaveFormat;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible extensible
                && extensible.SubFormat == FloatSubFormat;
            if (!isFloat || format.BitsPerSample != 32)
                throw new NotSupportedException($"loopback format {format.Encoding} {format.BitsPerSample} bit");
            _alpha = 1 - Math.Exp(-2 * Math.PI * BassCutoffHz / format.SampleRate);
            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnStopped;
            capture.StartRecording();
            _capture = capture;
            LightingController.LogInfo($"loopback capture started {format.SampleRate} Hz x{format.Channels}");
        }
        catch (Exception ex)
        {
            capture?.Dispose();
            _retryAfter = DateTime.UtcNow.AddSeconds(5);
            LightingController.LogInfo("loopback capture unavailable: " + ex.Message);
        }
    }

    private static void OnData(object? sender, WaveInEventArgs e)
    {
        lock (Sync)
        {
            if (sender != _capture || _capture == null) return;
            int channels = _capture.WaveFormat.Channels;
            ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
            for (int i = 0; i + channels <= samples.Length; i += channels)
            {
                double mono = 0;
                for (int c = 0; c < channels; c++) mono += samples[i + c];
                mono /= channels;
                _low1 += _alpha * (mono - _low1);
                _low2 += _alpha * (_low1 - _low2);
                _bassSum += _low2 * _low2;
                _fullSum += mono * mono;
                _count++;
            }
        }
    }

    // The device went away or changed format; start again on the next read.
    // Raised on the capture thread, which Dispose joins, so dispose elsewhere.
    private static void OnStopped(object? sender, StoppedEventArgs e)
    {
        FastLoopbackCapture? detached;
        lock (Sync)
        {
            if (sender != _capture) return;
            detached = Detach();
            _retryAfter = DateTime.UtcNow.AddSeconds(2);
        }
        if (e.Exception != null) LightingController.LogInfo("loopback capture stopped: " + e.Exception.Message);
        Task.Run(() => Dispose(detached));
    }

    // Caller holds Sync. Disposing joins the capture thread, which may be
    // waiting on Sync in OnData, so the caller disposes after releasing it.
    private static FastLoopbackCapture? Detach()
    {
        FastLoopbackCapture? capture = _capture;
        _capture = null;
        _bassSum = _fullSum = 0;
        _count = 0;
        if (capture != null)
        {
            capture.DataAvailable -= OnData;
            capture.RecordingStopped -= OnStopped;
        }
        return capture;
    }

    private static void Dispose(FastLoopbackCapture? capture)
    {
        if (capture == null) return;
        try { capture.StopRecording(); } catch { }
        try { capture.Dispose(); } catch { }
    }

    // NAudio's WasapiLoopbackCapture fixes a 100 ms buffer and polls at half
    // of it, so the lights would trail the music by up to 50 ms; a 20 ms
    // buffer delivers a packet every 10 ms.
    private sealed class FastLoopbackCapture : WasapiCapture
    {
        public FastLoopbackCapture()
            : base(new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia), false, 20) { }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback;
    }
}
