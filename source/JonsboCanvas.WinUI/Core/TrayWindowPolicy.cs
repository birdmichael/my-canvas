namespace JonsboCanvas_WinUI;

internal static class TrayWindowPolicy
{
    internal static bool ShouldHideToTray(bool minimizeToTray, bool exitRequested)
    {
        return minimizeToTray && !exitRequested;
    }
}
