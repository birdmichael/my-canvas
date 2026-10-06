using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace JonsboCanvas_WinUI;

// The Windows accent colour of the current user. It is set through the same
// unnamed uxtheme export the Settings app uses, so Windows derives the palette
// and the Start / taskbar colours itself.
internal static class SystemAccent
{
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    // "start,accent,palette" in hex: enough to put the user's own colours back exactly.
    public static string Snapshot()
    {
        var preference = new Preference();
        if (GetUserColorPreference(ref preference, true) != 0) return "";
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AccentKey);
        string palette = key?.GetValue("AccentPalette") is byte[] bytes ? Convert.ToHexString(bytes) : "";
        return $"{preference.Start:X8},{preference.Accent:X8},{palette}";
    }

    public static bool Set(Color color)
    {
        var preference = new Preference();
        if (GetUserColorPreference(ref preference, true) != 0) return false;
        preference.Accent = 0xFF000000u | (uint)(color.B << 16 | color.G << 8 | color.R);
        return SetUserColorPreference(ref preference, true) == 0;
    }

    public static bool Restore(string snapshot)
    {
        string[] parts = snapshot.Split(',');
        if (parts.Length != 3 ||
            !uint.TryParse(parts[0], NumberStyles.HexNumber, null, out uint start) ||
            !uint.TryParse(parts[1], NumberStyles.HexNumber, null, out uint accent))
            return false;
        var preference = new Preference { Start = start, Accent = accent };
        if (SetUserColorPreference(ref preference, true) != 0) return false;
        if (parts[2].Length == 64)
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(AccentKey, writable: true))
                key?.SetValue("AccentPalette", Convert.FromHexString(parts[2]), RegistryValueKind.Binary);
            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x0002, 3000, out _);
        }
        return true;
    }

    // A full-screen game, video or presentation is in front.
    public static bool UserBusy() =>
        SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4;

    // The Windows accent is never vivid: a near-black tint for dark photos and a
    // near-white tint for bright ones. Only the case lighting stays bright.
    public static readonly Tone SystemDarkTone = new(0.02, 0.04, 0.28);
    public static readonly Tone SystemLightTone = new(0.55, 0.70, 0.3);
    public const double BrightPhoto = 0.35;
    // The app's highlight text sits on near-black surfaces.
    public static readonly Tone AppTone = new(0.35, 0.55, 0.4);
    public static readonly Tone AppStrongTone = new(0.22, 0.35, 0.4);

    public static Tone SystemToneFor(double photoLuminance) =>
        photoLuminance >= BrightPhoto ? SystemLightTone : SystemDarkTone;

    public readonly record struct Tone(double MinLuminance, double MaxLuminance, double MaxSaturation);

    // The light colour is tuned for LEDs (full value, extra saturation); pure
    // blue at full value is almost black to the eye. Keep its hue, cap the
    // saturation and bring its perceived brightness into the tone's range.
    public static Color ForUi(Color light, Tone tone)
    {
        int max = Math.Max(light.R, Math.Max(light.G, light.B)), min = Math.Min(light.R, Math.Min(light.G, light.B));
        // Chroma, not HSL saturation: a pale photo (#CEFFC5) must stay pale.
        double hue = light.GetHue() / 360, saturation = Math.Min(max == 0 ? 0 : (max - min) / (double)max, tone.MaxSaturation);
        double luminance = Math.Clamp(RelativeLuminance(FromHsl(hue, saturation, 0.5)), tone.MinLuminance, tone.MaxLuminance);
        double low = 0, high = 1;
        for (int i = 0; i < 24; i++)
        {
            double mid = (low + high) / 2;
            if (RelativeLuminance(FromHsl(hue, saturation, mid)) < luminance) low = mid; else high = mid;
        }
        return FromHsl(hue, saturation, (low + high) / 2);
    }

    public static double RelativeLuminance(Color c)
    {
        static double Linear(int v) { double s = v / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    private static Color FromHsl(double hue, double saturation, double l)
    {
        double q = l < 0.5 ? l * (1 + saturation) : l + saturation - l * saturation, p = 2 * l - q;
        int Channel(double t)
        {
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            double v = t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p;
            return (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
        return Color.FromArgb(Channel(hue + 1 / 3.0), Channel(hue), Channel(hue - 1 / 3.0));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Preference
    {
        public uint Start;
        public uint Accent;
        // Room in case a Windows build reads more than the two colours.
        public uint Reserved1;
        public uint Reserved2;
    }

    [DllImport("uxtheme.dll", EntryPoint = "#120")]
    private static extern int GetUserColorPreference(ref Preference preference, [MarshalAs(UnmanagedType.Bool)] bool forceReload);

    [DllImport("uxtheme.dll", EntryPoint = "#122")]
    private static extern int SetUserColorPreference(ref Preference preference, [MarshalAs(UnmanagedType.Bool)] bool forceCommit);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
