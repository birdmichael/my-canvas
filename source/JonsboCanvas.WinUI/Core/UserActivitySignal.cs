using System.Runtime.InteropServices;

namespace JonsboCanvas_WinUI;

// GetLastInputInfo is a cheap local timestamp and does not inspect keystrokes.
// It gives MyCanvas a wake signal even when ROG Live Service is stopped and its
// device-presence XML is no longer being refreshed.
internal static class UserActivitySignal
{
    internal static bool TryGetLastInputTick(out uint tick)
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input))
        {
            tick = 0;
            return false;
        }

        tick = input.Time;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo input);
}
