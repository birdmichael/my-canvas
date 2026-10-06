namespace JonsboCanvas_WinUI;

internal static class AppAssetPaths
{
    internal static string IconPath(string baseDirectory)
    {
        return Path.GetFullPath(Path.Combine(baseDirectory, "Assets", "AppIcon.ico"));
    }
}
