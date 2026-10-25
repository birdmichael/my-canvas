using System.Diagnostics;
using System.Drawing;
using JonsboCanvas_WinUI;

// Opt-in hardware soak; never runs as part of the normal render tests.
internal static class KeyboardHardwareChecks
{
    public static async Task CheckWakeLightingAsync()
    {
        using KeyboardHidController keyboard = new();
        byte[] original = await keyboard.ReadStateAsync(0x25, 0x0E);
        Console.WriteLine($"INITIAL_LIGHT rgb={original[9]},{original[10]},{original[11]} brightness={original[5]}");
        try
        {
            keyboard.SetLightColor(180, 0, 150, 64);
            await ExpectLight(keyboard, new KeyboardLight(180, 0, 150, 25));
            await Task.Delay(2200);
            // Simulate firmware restoring its red profile while MyCanvas still
            // has a valid session and a cached magenta colour.
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object gate = typeof(KeyboardHidController).GetField("_deviceSync", flags)!.GetValue(keyboard)!;
            IKeyboardDeviceSession session = (IKeyboardDeviceSession)typeof(KeyboardHidController)
                .GetField("_session", flags)!.GetValue(keyboard)!;
            lock (gate) session.SetStaticColor(255, 0, 0, 40);
            byte[] reset = await keyboard.ReadStateAsync(0x25, 0x0E);
            Console.WriteLine($"SIMULATED_WAKE_RESET rgb={reset[9]},{reset[10]},{reset[11]} brightness={reset[5]}");
            if (!KeyboardLightDedupPolicy.MatchesReadback(new KeyboardLight(255, 0, 0, 40), reset))
                throw new Exception("Unable to establish the red firmware-reset test state");
            keyboard.NotifyWake();
            await ExpectLight(keyboard, new KeyboardLight(180, 0, 150, 25));
            await Task.Delay(2300);
            byte[] restored = await keyboard.ReadStateAsync(0x25, 0x0E);
            if (!KeyboardLightDedupPolicy.MatchesReadback(new KeyboardLight(180, 0, 150, 25), restored))
                throw new Exception("Wake lighting did not remain restored");
            Console.WriteLine($"WAKE_LIGHTING_PASS rgb={restored[9]},{restored[10]},{restored[11]} brightness={restored[5]} diagnostics={keyboard.Diagnostics}");
        }
        finally
        {
            byte brightness = (byte)Math.Clamp(Math.Round(original[5] * 255 / 100.0), 0, 255);
            keyboard.SetLightColor(original[9], original[10], original[11], brightness);
            await ExpectLight(keyboard, new KeyboardLight(original[9], original[10], original[11], original[5]));
        }
    }

    private static async Task ExpectLight(KeyboardHidController keyboard, KeyboardLight expected)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(100);
            byte[] state = await keyboard.ReadStateAsync(0x25, 0x0E);
            if (KeyboardLightDedupPolicy.MatchesReadback(expected, state)) return;
        }
        throw new Exception("Keyboard lighting readback mismatch: " + expected);
    }

    public static async Task CheckUploadLightingAsync(string imagePath)
    {
        using Bitmap image = new(imagePath);
        byte[] payload = KeyboardDisplay.BuildPayload(image);
        using KeyboardHidController keyboard = new();
        // A non-green colour makes an unexpected green override unambiguous.
        foreach (byte brightness in new byte[] { 64, 128, 0 })
        {
            keyboard.SetLightColor(180, 0, 150, brightness);
            byte expected = (byte)Math.Clamp(Math.Round(brightness * 100 / 255.0), 0, 100);
            byte[] before = Array.Empty<byte>();
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(100);
                before = await keyboard.ReadStateAsync(0x25, 0x0E);
                if (before[9] == 180 && before[10] == 0 && before[11] == 150 && before[5] == expected) break;
            }
            if (before.Length < 12 || before[9] != 180 || before[10] != 0 || before[11] != 150 || before[5] != expected)
                throw new Exception("Pre-upload static lighting readback mismatch");
            Stopwatch clock = Stopwatch.StartNew();
            if (!await keyboard.ApplyWallpaperAsync(payload, $"lighting-check-{brightness}", force: true))
                throw new Exception("OLED upload failed: " + keyboard.LastError);
            byte[] after = await keyboard.ReadStateAsync(0x25, 0x0E);
            Console.WriteLine($"POST_UPLOAD elapsedMs={clock.ElapsedMilliseconds} rgb={after[9]},{after[10]},{after[11]} brightness={after[5]} expected={expected}");
            if (after[9] != 180 || after[10] != 0 || after[11] != 150 || after[5] != expected)
                throw new Exception("Post-upload lighting colour/brightness mismatch");
        }
        if (keyboard.Diagnostics.Failures != 0) throw new Exception("Lighting restoration failures: " + keyboard.Diagnostics);
        Console.WriteLine("UPLOAD_LIGHTING_PASS " + keyboard.Diagnostics);
    }

    public static async Task RunAsync(string imagePath, int seconds)
    {
        using Bitmap image = new(imagePath);
        byte[] payload = KeyboardDisplay.BuildPayload(image);
        using KeyboardHidController keyboard = new();
        using CancellationTokenSource stop = new();
        Task colors = Task.Run(async () =>
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!stop.IsCancellationRequested)
            {
                double t = clock.Elapsed.TotalSeconds;
                keyboard.Pulse((byte)(100 + 100 * Math.Sin(t)), 85,
                    (byte)(100 + 100 * Math.Cos(t)),
                    (byte)(90 + 85 * (0.5 + 0.5 * Math.Sin(t * 7))));
                await Task.Delay(15);
            }
        });
        Task collections = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                await Task.Delay(100);
            }
        });
        Stopwatch elapsed = Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                Stopwatch upload = Stopwatch.StartNew();
                if (!await keyboard.ApplyWallpaperAsync(payload, $"soak-{i}", force: true))
                    throw new Exception($"OLED upload failed during GC/lighting stress: {keyboard.Diagnostics} {keyboard.LastError}");
                Console.WriteLine($"UPLOAD {i} elapsedMs={upload.ElapsedMilliseconds} state={keyboard.Diagnostics}");
                byte[] mode = await keyboard.ReadStateAsync(0x21, 0);
                if (mode[4] != 6) throw new Exception("OLED mode readback is not custom image");
                if (i == 1)
                {
                    keyboard.RequestReconnect();
                    Console.WriteLine("RECONNECT requested while colors continue");
                }
                await Task.Delay(8000);
            }
            while (elapsed.Elapsed.TotalSeconds < seconds)
            {
                Console.WriteLine($"SOAK elapsed={elapsed.Elapsed.TotalSeconds:0}s state={keyboard.Diagnostics}");
                await Task.Delay(10000);
            }
        }
        finally
        {
            stop.Cancel();
            await Task.WhenAll(colors, collections);
        }

        keyboard.SetLightColor(25, 125, 225, 128);
        await Task.Delay(1500);
        byte[] light = await keyboard.ReadStateAsync(0x25, 0x0E);
        if (light[9] != 25 || light[10] != 125 || light[11] != 225 || light[5] != 50)
            throw new Exception("Final static color/brightness readback mismatch");
        var result = keyboard.Diagnostics;
        if (result.Uploads != 3 || result.Sessions != 2 || result.LightWrites < 100 || result.Failures != 0)
            throw new Exception($"Insufficient successful hardware operations: {result}");
        Console.WriteLine($"HARDWARE_PASS duration={elapsed.Elapsed.TotalSeconds:0}s state={result}");
    }
}
