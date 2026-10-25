using JonsboCanvas;

internal static class NeteaseAutoConnectTests
{
    public static void Run(Action<bool, string> check)
    {
        DateTime now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        NeteaseAutoConnectPolicy policy = new();
        check(!policy.ShouldRestart("", false, now), "auto-connect never launches a closed player");
        check(!policy.ShouldRestart("ordinary:1", false, now) &&
              !policy.ShouldRestart("ordinary:1", false, now.AddSeconds(7)),
            "ordinary launches get a startup grace period");
        check(policy.ShouldRestart("ordinary:1", false, now.AddSeconds(8)) &&
              !policy.ShouldRestart("ordinary:1", false, now.AddMinutes(1)),
            "ordinary launches are attached once without a restart loop");
        policy.RememberReplacement("replacement:2");
        check(!policy.ShouldRestart("replacement:2", false, now.AddSeconds(10)) &&
              !policy.ShouldRestart("replacement:2", false, now.AddMinutes(2)),
            "a failed replacement endpoint cannot cause repeated restarts");
        check(!policy.ShouldRestart("", false, now.AddMinutes(3)) &&
              !policy.ShouldRestart("manual:3", false, now.AddMinutes(4)) &&
              policy.ShouldRestart("manual:3", false, now.AddMinutes(4).AddSeconds(8)),
            "closing and manually reopening the player triggers a fresh attachment");
        check(!policy.ShouldRestart("ready:4", false, now.AddMinutes(5)) &&
              !policy.ShouldRestart("ready:4", true, now.AddMinutes(5).AddSeconds(4)) &&
              !policy.ShouldRestart("ready:4", false, now.AddMinutes(6)),
            "an existing sync endpoint and temporary disconnect never restart the player");
        check(!policy.ShouldRestart("manual:3-new-start-time", false, now.AddMinutes(7)) &&
              policy.ShouldRestart("manual:3-new-start-time", false, now.AddMinutes(7).AddSeconds(8)),
            "reused process IDs remain distinct across player launches");

        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        check(NeteaseLauncher.IsDebugPortOpen(port), "sync port probe detects a listening local endpoint");
        listener.Stop();
        check(!NeteaseLauncher.IsDebugPortOpen(port), "sync port probe handles connection failure without throwing");
    }
}
