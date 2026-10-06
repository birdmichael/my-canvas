using System.Web.Script.Serialization;
using JonsboCanvas_WinUI;
using JonsboCanvas;

internal static class LegacyJsonSerializerTests
{
    private sealed class PlaybackPayload
    {
        public string eventName = "";
        public object[] args = Array.Empty<object>();
    }

    public static int Main()
    {
        JavaScriptSerializer serializer = new();
        PlaybackPayload? progress = serializer.Deserialize<PlaybackPayload>(
            "{\"eventName\":\"progress\",\"args\":[\"109168_7ECL8L\",72.52,1]}");
        PlaybackPayload? load = serializer.Deserialize<PlaybackPayload>(
            "{\"eventName\":\"load\",\"args\":[\"109168_7ECL8L\",{\"duration\":228000}]}");

        Assert(progress is not null, "progress payload missing");
        Assert(progress!.args.Length == 3, "progress args missing");
        Assert(progress.args[0] is string, "song id was not inferred as string");
        Assert(progress.args[1] is double, "progress was not inferred as double");
        Assert(Math.Abs(Convert.ToDouble(progress.args[1]) - 72.52) < 0.0001,
            "progress value changed");
        Assert(progress.args[2] is long, "load progress was not inferred as integer");
        Assert(load?.args[1] is Dictionary<string, object>,
            "load info was not inferred as a dictionary");
        Dictionary<string, object> info = (Dictionary<string, object>)load!.args[1];
        Assert(Convert.ToInt64(info["duration"]) == 228000, "duration value changed");

        Assert(RenderCadence.SchedulerIntervalMilliseconds(50, outputActive: true) == 50,
            "active 20 FPS output must use a 50 ms scheduler");
        Assert(RenderCadence.SchedulerIntervalMilliseconds(10, outputActive: true) == 25,
            "scheduler must keep a safe minimum interval");
        Assert(RenderCadence.SchedulerIntervalMilliseconds(50, outputActive: false) == 500,
            "inactive output must reduce wakeups");
        Assert(RenderCadence.ShouldUpdateLocalPreview(previewEnabled: true, windowVisible: true),
            "visible enabled preview must update");
        Assert(!RenderCadence.ShouldUpdateLocalPreview(previewEnabled: true, windowVisible: false),
            "hidden window must not copy preview pixels");
        Assert(TrayWindowPolicy.ShouldHideToTray(minimizeToTray: true, exitRequested: false),
            "close and minimize must hide to tray when enabled");
        Assert(!TrayWindowPolicy.ShouldHideToTray(minimizeToTray: true, exitRequested: true),
            "explicit tray exit must close the process");
        Assert(!TrayWindowPolicy.ShouldHideToTray(minimizeToTray: false, exitRequested: false),
            "disabled tray behavior must allow a normal close");
        Assert(LaunchPolicy.IsAutoStarted(new[] { "app.exe", "--AUTOSTART" }),
            "autostart argument must be case-insensitive");
        Assert(!LaunchPolicy.IsAutoStarted(new[] { "app.exe" }),
            "manual launches must remain visible");

        // 120 BPM kicks over a quieter bed, sampled every 15 ms like the beat loop.
        MusicLightEnvelope envelope = new();
        int lightBeats = 0, lightMinimum = 255, lightMaximum = 0;
        for (int tick = 0; tick < 800; tick++)
        {
            double t = tick * 0.015;
            double sinceKick = t % 0.5;
            float peak = (float)(0.18 + (sinceKick < 0.06 ? 0.6 : 0.0));
            MusicLightFrame frame = envelope.Sample(peak, peak, 40, 0.015);
            if (t < 2) continue;
            if (frame.Beat) lightBeats++;
            lightMinimum = Math.Min(lightMinimum, frame.Brightness);
            lightMaximum = Math.Max(lightMaximum, frame.Brightness);
        }
        Assert(lightBeats is >= 15 and <= 22, "kicks at 120 BPM must register as beats, got " + lightBeats);
        Assert(lightMaximum >= 100, "a beat must flash near full brightness");
        Assert(lightMinimum >= 102 * MusicLightEnvelope.Floor - 1, "music lighting must not fall below its floor");
        MusicLightEnvelope silent = new();
        MusicLightFrame quiet = default;
        for (int tick = 0; tick < 200; tick++) quiet = silent.Sample(0, 0, 40, 0.015);
        Assert(!quiet.Beat && quiet.Brightness == (int)Math.Round(102 * MusicLightEnvelope.Floor), "silence must rest on the floor");
        // A mastered track pinned at its ceiling: the full band never moves,
        // only the bass pulses. The kicks must still flash.
        MusicLightEnvelope loud = new();
        int loudBeats = 0;
        for (int tick = 0; tick < 800; tick++)
        {
            double sinceKick = tick * 0.015 % 0.5;
            float bass = (float)(sinceKick < 0.08 ? 0.25 : 0.06);
            if (loud.Sample(bass, 0.3f, 40, 0.015).Beat && tick * 0.015 >= 2) loudBeats++;
        }
        Assert(loudBeats is >= 15 and <= 22, "bass kicks under a constant full band must register, got " + loudBeats);

        FrameRotation.GetTargetSize(960, 376, 90, out int rotatedWidth, out int rotatedHeight);
        Assert(rotatedWidth == 376 && rotatedHeight == 960,
            "90 degree frame dimensions must match the physical panel");
        FrameRotation.MapPixel(0, 0, 960, 376, 90, out int x90, out int y90);
        Assert(x90 == 375 && y90 == 0, "90 degree top-left mapping changed");
        FrameRotation.MapPixel(959, 375, 960, 376, 90, out x90, out y90);
        Assert(x90 == 0 && y90 == 959, "90 degree bottom-right mapping changed");
        FrameRotation.MapPixel(0, 0, 960, 376, 180, out int x180, out int y180);
        Assert(x180 == 959 && y180 == 375, "180 degree mapping changed");
        FrameRotation.MapPixel(0, 0, 960, 376, 270, out int x270, out int y270);
        Assert(x270 == 0 && y270 == 959, "270 degree mapping changed");
        Assert(!FrameSendPolicy.ShouldComputeSignature(musicMode: true, musicPlaying: true),
            "animated music frames must skip redundant signatures");
        Assert(FrameSendPolicy.ShouldComputeSignature(musicMode: true, musicPlaying: false),
            "paused music frames must retain duplicate suppression");
        Assert(FrameSendPolicy.ShouldComputeSignature(musicMode: false, musicPlaying: false),
            "static non-music frames must retain duplicate suppression");
        DateTime sentAt = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        Assert(!FrameSendPolicy.ShouldSend(changed: false, sentAt, sentAt.AddMilliseconds(400)),
            "unchanged frames are suppressed briefly");
        Assert(FrameSendPolicy.ShouldSend(changed: false, sentAt, sentAt.AddSeconds(1)),
            "unchanged frames are resent so static screens do not blank");
        Assert(FrameSendPolicy.ShouldSend(changed: true, sentAt, sentAt.AddMilliseconds(10)),
            "changed frames are always sent");
        Assert(!LogRotationPolicy.ShouldRotate(LogRotationPolicy.MaximumBytes - 1),
            "log must stay in place below the size limit");
        Assert(LogRotationPolicy.ShouldRotate(LogRotationPolicy.MaximumBytes),
            "log must rotate at the size limit");
        string iconPath = AppAssetPaths.IconPath(Path.Combine("C:\\", "Apps", "JonsboCanvas"));
        Assert(Path.IsPathRooted(iconPath), "window icon path must be absolute");
        Assert(iconPath.EndsWith(Path.Combine("Assets", "AppIcon.ico"), StringComparison.OrdinalIgnoreCase),
            "window icon path must resolve under the application directory");
        Assert(CollectorActivationPolicy.RequiresMetrics("hardware"),
            "hardware mode must initialize metrics");
        Assert(!CollectorActivationPolicy.RequiresMetrics("music"),
            "music-only mode must not initialize hardware metrics");
        Assert(!CollectorActivationPolicy.RequiresMedia("music", playerRunning: false),
            "media bridge must stay lazy while the player is absent");
        Assert(CollectorActivationPolicy.RequiresMedia("auto", playerRunning: true),
            "auto mode must initialize media when the player appears");
        DateTime schedulerTick = new(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);
        Assert(!DisplayResumePolicy.ShouldRestartDisplay(
                schedulerTick,
                schedulerTick + DisplayResumePolicy.MinimumSuspendGap - TimeSpan.FromMilliseconds(1),
                connectionRequested: true),
            "ordinary scheduler delay must not restart the display SDK");
        Assert(DisplayResumePolicy.ShouldRestartDisplay(
                schedulerTick,
                schedulerTick + DisplayResumePolicy.MinimumSuspendGap,
                connectionRequested: true),
            "a suspend-sized scheduler gap must restart a requested display session");
        Assert(!DisplayResumePolicy.ShouldRestartDisplay(
                schedulerTick,
                schedulerTick + TimeSpan.FromHours(1),
                connectionRequested: false),
            "a manually disconnected display must stay disconnected after resume");
        Assert(AppLanguage.Normalize("en-GB") == "en-US",
            "English locale variants must normalize to the supported English language");
        Assert(AppLanguage.Normalize("zh-Hans") == "zh-CN",
            "Chinese locale variants must normalize to Simplified Chinese");
        Assert(AppLanguage.Normalize("unsupported") == "zh-CN",
            "unsupported languages must fall back to Simplified Chinese");

        Console.WriteLine("PASS serializer, render cadence, tray, and resume regressions");
        return 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
