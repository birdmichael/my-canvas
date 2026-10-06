using System.Runtime.InteropServices;

namespace JonsboCanvas_WinUI;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    internal const string RestoreMessageName = "MyCanvas.WinUI.RestoreWindow";
    private const string MutexName = @"Local\MyCanvas.WinUI.SingleInstance";
    private static readonly IntPtr BroadcastWindow = new(0xffff);

    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private SingleInstanceCoordinator(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    internal static SingleInstanceCoordinator? TryAcquire()
    {
        Mutex mutex = new(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstanceCoordinator(mutex);
    }

    internal static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out Mutex? mutex)) return false;
        mutex.Dispose();
        return true;
    }

    internal static void RequestRestore()
    {
        uint message = RegisterWindowMessage(RestoreMessageName);
        if (message != 0)
            PostMessage(BroadcastWindow, message, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }
        _mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
