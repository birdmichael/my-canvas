using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace JonsboCanvas_WinUI;

// The Windows desktop background of the current user.
internal static class DesktopWallpaper
{
    private const uint SetDesktopWallpaper = 0x0014;
    private const uint UpdateIniFile = 0x01;
    private const uint SendChange = 0x02;

    public static string Current()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return key?.GetValue("WallPaper") as string ?? "";
    }

    // Shows the image filled to the screen; returns false if Windows refused it.
    public static bool Set(string path)
    {
        if (!File.Exists(path)) return false;
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
        {
            key?.SetValue("WallpaperStyle", "10");
            key?.SetValue("TileWallpaper", "0");
        }
        return SystemParametersInfo(SetDesktopWallpaper, 0, path, UpdateIniFile | SendChange);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
}
