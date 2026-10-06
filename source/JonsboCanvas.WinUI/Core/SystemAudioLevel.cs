using System.Runtime.InteropServices;

namespace JonsboCanvas_WinUI;

// Reads the current master output peak level via WASAPI's IAudioMeterInformation.
// This lets the case lighting pulse with whatever is playing, regardless of source.
internal static class SystemAudioLevel
{
    private static IAudioMeterInformation? _meter;
    private static DateTime _retryAfter;

    public static float GetPeak()
    {
        if (DateTime.UtcNow < _retryAfter) return 0f;
        try
        {
            _meter ??= CreateMeter();
            if (_meter == null) throw new InvalidOperationException("No audio output meter");
            Marshal.ThrowExceptionForHR(_meter.GetPeakValue(out float peak));
            return peak;
        }
        catch (Exception ex) { _meter = null; _retryAfter = DateTime.UtcNow.AddSeconds(2); LightingController.LogInfo("audio meter retry: " + ex.Message); return 0f; }
    }

    private static IAudioMeterInformation? CreateMeter()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 0, out IMMDevice device)); // eRender, eConsole
        Guid iid = typeof(IAudioMeterInformation).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 1, IntPtr.Zero, out object activated)); // CLSCTX_INPROC_SERVER
        return activated as IAudioMeterInformation;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float pfPeak);
        [PreserveSig] int GetMeteringChannelCount(out int pnChannelCount);
        [PreserveSig] int GetChannelsPeakValues(int u32ChannelCount, [In, Out] float[] afPeakValues);
        [PreserveSig] int QueryHardwareSupport(out int pdwHardwareSupportMask);
    }
}
