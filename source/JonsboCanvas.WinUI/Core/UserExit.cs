using JonsboCanvas;

namespace JonsboCanvas_WinUI;

// Written when the user quits, so the watchdog leaves the app closed until the
// next logon or manual start; a crash leaves no marker.
internal static class UserExit
{
    private static string MarkerPath => Path.Combine(EmbeddedRuntime.DataDirectory, "exited-by-user");

    internal static bool Marked => File.Exists(MarkerPath);

    internal static void Mark()
    {
        if (Environment.GetEnvironmentVariable("JONSBO_CANVAS_CAPTURE") == "1") return;
        try { File.WriteAllText(MarkerPath, DateTime.Now.ToString("O")); } catch { }
    }

    internal static void Clear()
    {
        try { File.Delete(MarkerPath); } catch { }
    }
}
