namespace JonsboCanvas_WinUI;

internal static class ScreenModePolicy
{
    public static string Resolve(string mode, bool available, bool playing) =>
        mode == "auto" ? (available && playing ? "music" : "hardware") : mode;
}
