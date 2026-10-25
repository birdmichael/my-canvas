using System.Diagnostics;
using JonsboCanvas_WinUI;

internal static class KeyboardWakeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        KeyboardActivitySignal activity = new(0);
        check(!KeyboardActivitySignal.IsTargetKeyboard(0, @"HID\VID_0B05&PID_1AD0") &&
              !KeyboardActivitySignal.IsTargetKeyboard(1, @"HID\VID_1234&PID_5678") &&
              KeyboardActivitySignal.IsTargetKeyboard(1, @"\\?\hid#vid_0b05&pid_1ad0&mi_00#keyboard"),
            "mouse and unrelated devices cannot wake the keyboard lighting");
        check(!activity.IsIdle(29_999) && activity.IsIdle(30_000),
            "startup grace expires without keyboard activity");
        activity.RecordKeyboardInput(31_000);
        check(activity.TryTakeActivity(out bool woke) && woke &&
              !activity.TryTakeActivity(out _) && !activity.IsIdle(31_001),
            "a real keyboard wake is consumed once");
        activity.RecordKeyboardInput(32_000);
        check(activity.TryTakeActivity(out woke) && !woke && activity.IsIdle(62_000),
            "ordinary typing can resume a failed request without repeated wake restores");
        activity.RecordReconnect(65_000);
        check(!activity.TryTakeActivity(out _) && !activity.IsIdle(94_999) && activity.IsIdle(95_000),
            "a real reconnect grants bounded setup time without fabricating keyboard input");

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

        await Until(() => !keyboard.SessionNeedsLight);
        keyboard.SuspendUntilActivity();
        await Task.Delay(100);
        writes = device.Writes;
        reads = device.LightingReads;
        int uploads = device.Uploads;
        KeyboardLight next = new(0, 130, 255, 50);
        for (int i = 0; i < 20; i++) keyboard.SetLightColor(next.R, next.G, next.B, 128);
        bool uploadedWhileIdle = await keyboard.ApplyWallpaperAsync(new byte[KeyboardDisplay.PayloadLength], "new-idle-image");
        await Task.Delay(200);
        check(keyboard.WaitingForActivity && !uploadedWhileIdle &&
              device.Writes == writes && device.LightingReads == reads && device.Uploads == uploads,
            "idle suppresses lighting, verification, and OLED transfers while keeping the latest desired state");
        keyboard.NotifyWake();
        await Until(() => device.Light == next);
        check(!keyboard.WaitingForActivity,
            "real activity restores the latest colour accumulated during idle");

        device.FailWrites = true;
        keyboard.SetLightColor(5, 6, 7, 128);
        await Until(() => keyboard.WaitingForActivity);
        int failedAttempts = device.WriteAttempts;
        keyboard.SetLightColor(8, 9, 10, 128);
        await keyboard.ApplyWallpaperAsync(new byte[KeyboardDisplay.PayloadLength], "sleeping-image");
        await Task.Delay(250);
        check(device.WriteAttempts == failedAttempts,
            "a sleeping or rejecting keyboard is not probed again without activity");

        string unknownPresence = Path.GetTempFileName();
        try
        {
            File.WriteAllText(unknownPresence, "<root>");
            check(!KeyboardExternalPresence.TryGetKeyboardPresence(out _, unknownPresence),
                "a partially written presence file is unknown rather than a disconnect");
        }
        finally { File.Delete(unknownPresence); }
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
        private int _writes, _reads, _uploads, _writeAttempts;
        public volatile bool FailWrites;
        public volatile bool ResetOnNextLightingRead;
        public KeyboardLight Light { get { lock (_sync) return _light; } }
        public int Writes => Volatile.Read(ref _writes);
        public int LightingReads => Volatile.Read(ref _reads);
        public int Uploads => Volatile.Read(ref _uploads);
        public int WriteAttempts => Volatile.Read(ref _writeAttempts);

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
            Interlocked.Increment(ref _writeAttempts);
            if (FailWrites) throw new IOException("Simulated sleeping keyboard");
            lock (_sync) _light = new(r, g, b, brightness);
            Interlocked.Increment(ref _writes);
        }
        public void UploadOledImage(byte[] imageData) => Interlocked.Increment(ref _uploads);
        public void Dispose() { }
    }
}
