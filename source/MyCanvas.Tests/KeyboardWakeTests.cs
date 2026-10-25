using System.Diagnostics;
using JonsboCanvas_WinUI;

internal static class KeyboardWakeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        FakeKeyboard device = new();
        using KeyboardHidController keyboard = new(() => device, TimeSpan.FromMilliseconds(50));
        KeyboardLight desired = new(20, 110, 230, 50);
        keyboard.SetLightColor(desired.R, desired.G, desired.B, 128);
        await Until(() => device.Light == desired && device.LightingReads >= 2);
        check(device.Writes == 1 && keyboard.Diagnostics.Sessions == 1,
            "the first requested colour survives opening the keyboard session");
        int writes = device.Writes, reads = device.LightingReads;
        for (int i = 0; i < 10; i++) keyboard.SetLightColor(desired.R, desired.G, desired.B, 128);
        await Task.Delay(150);
        check(device.Writes == writes && device.LightingReads == reads,
            "verified static keyboard lighting stays idle without periodic HID traffic");

        string presencePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(presencePath, "<root><device_id key=\"0b051c12\"><status>255</status></device_id></root>");
            keyboard.CheckExternalReconnect(presencePath);
            File.WriteAllText(presencePath, "<root><device_id key=\"0b051c12\"><status>1</status></device_id></root>");
            await Task.Delay(2100);
            device.ResetToRed();
            keyboard.CheckExternalReconnect(presencePath);
            await Until(() => device.Light == desired && device.LightingReads >= reads + 2);
            check(keyboard.Diagnostics.Sessions == 1,
                "a wireless keyboard returning on the same receiver session still restores its colour");
            reads = device.LightingReads;
        }
        finally { File.Delete(presencePath); }

        await keyboard.ApplyWallpaperAsync(new byte[KeyboardDisplay.PayloadLength], "unchanged-image");
        device.ResetToRed();
        keyboard.NotifyWake();
        await Until(() => device.Light == desired && device.LightingReads >= reads + 2);
        check(keyboard.AppliedWallpaperId == "unchanged-image" && device.Uploads == 1,
            "wake restores unchanged lighting without reuploading an unchanged OLED image");

        reads = device.LightingReads;
        writes = device.Writes;
        device.ResetOnNextLightingRead = true;
        keyboard.NotifyWake();
        await Until(() => device.LightingReads >= reads + 2 && device.Light == desired);
        check(device.Writes >= writes + 2,
            "delayed readback repairs a firmware colour reset after the wake write");

        device.ResetToRed();
        keyboard.RequestReconnect();
        await Until(() => device.Light == desired && keyboard.Diagnostics.Sessions == 2);
        check(device.Light == desired, "reconnecting replays the desired colour without another caller update");

        desired = new KeyboardLight(180, 0, 150, 25);
        device.FailWrites = true;
        keyboard.SetLightColor(desired.R, desired.G, desired.B, 64);
        await Until(() => keyboard.Diagnostics.Failures > 0);
        device.FailWrites = false;
        Stopwatch recovery = Stopwatch.StartNew();
        keyboard.NotifyWake();
        await Until(() => device.Light == desired);
        check(recovery.Elapsed < TimeSpan.FromSeconds(6),
            "wake interrupts failure backoff and restores the latest requested colour");

        check(!KeyboardLightDedupPolicy.MatchesReadback(desired, new byte[5]),
            "truncated lighting readback cannot be mistaken for a successful restore");
    }

    private static async Task Until(Func<bool> condition)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(6))
                throw new Exception("Keyboard wake regression timed out");
            await Task.Delay(10);
        }
    }

    private sealed class FakeKeyboard : IKeyboardDeviceSession
    {
        private readonly object _sync = new();
        private KeyboardLight _light = new(255, 0, 0, 100);
        private int _writes, _reads, _uploads;
        public volatile bool FailWrites;
        public volatile bool ResetOnNextLightingRead;
        public KeyboardLight Light { get { lock (_sync) return _light; } }
        public int Writes => Volatile.Read(ref _writes);
        public int LightingReads => Volatile.Read(ref _reads);
        public int Uploads => Volatile.Read(ref _uploads);

        public void ResetToRed() { lock (_sync) _light = new(255, 0, 0, 100); }
        public byte[] ReadState(byte command, byte subCommand)
        {
            byte[] state = new byte[64];
            if (command != 0x25) { state[4] = 6; return state; }
            lock (_sync)
            {
                if (ResetOnNextLightingRead)
                {
                    _light = new(255, 0, 0, 100);
                    ResetOnNextLightingRead = false;
                }
                state[5] = _light.Brightness;
                state[9] = _light.R;
                state[10] = _light.G;
                state[11] = _light.B;
            }
            Interlocked.Increment(ref _reads);
            return state;
        }

        public void SetStaticColor(byte r, byte g, byte b, byte brightness)
        {
            if (FailWrites) throw new IOException("Simulated sleeping keyboard");
            lock (_sync) _light = new(r, g, b, brightness);
            Interlocked.Increment(ref _writes);
        }
        public void UploadOledImage(byte[] imageData) => Interlocked.Increment(ref _uploads);
        public void Dispose() { }
    }
}
