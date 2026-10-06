using System.Text;

namespace JonsboCanvas;

internal static class Log
{
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(EmbeddedRuntime.DataDirectory);
                RotateIfNeeded();
                File.AppendAllText(
                    EmbeddedRuntime.LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch
        {
        }
    }

    private static void RotateIfNeeded()
    {
        string path = EmbeddedRuntime.LogPath;
        if (!File.Exists(path) || !LogRotationPolicy.ShouldRotate(new FileInfo(path).Length))
            return;

        string previousPath = Path.Combine(
            Path.GetDirectoryName(path) ?? EmbeddedRuntime.DataDirectory,
            Path.GetFileNameWithoutExtension(path) + ".previous.log");
        if (File.Exists(previousPath))
            File.Delete(previousPath);
        File.Move(path, previousPath);
    }
}
