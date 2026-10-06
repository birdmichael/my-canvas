using System.Runtime.InteropServices;

namespace JonsboCanvas_WinUI;

// The notification-area icon. It owns a hidden top-level window, so the main
// window can be closed while the app keeps running in the background; a
// message-only window would miss the broadcast a second launch sends.
internal sealed class TrayIconController : IDisposable
{
    private const uint NotifyAdd = 0x00000000;
    private const uint NotifyModify = 0x00000001;
    private const uint NotifyDelete = 0x00000002;
    private const uint NotifyMessage = 0x00000001;
    private const uint NotifyIcon = 0x00000002;
    private const uint NotifyTip = 0x00000004;
    private const uint NotifyInfo = 0x00000010;
    private const uint InfoFlagInfo = 0x00000001;
    private const uint CallbackMessage = 0x8001;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x00000010;
    private const uint LeftButtonUp = 0x0202;
    private const uint LeftButtonDoubleClick = 0x0203;
    private const uint RightButtonUp = 0x0205;
    private const uint ContextMenu = 0x007B;
    private const uint MenuString = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackRightButton = 0x0002;
    private const uint TrackReturnCommand = 0x0100;
    private const uint OpenCommand = 1001;
    private const uint ExitCommand = 1002;
    private const uint PopupStyle = 0x80000000;
    private const string ClassName = "MyCanvas.TrayWindow";

    // Windows calls this for the window class for as long as the process lives.
    private static readonly WindowProcedure Procedure = Dispatch;
    private static TrayIconController? _instance;

    private readonly IntPtr _windowHandle;
    private readonly uint _taskbarCreatedMessage;
    private readonly uint _restoreRequestedMessage;
    private string _tooltip;
    private IntPtr _iconHandle;
    private bool _disposed;

    internal event Action? OpenRequested;
    internal event Action? ExitRequested;

    internal TrayIconController(string iconPath, string tooltip)
    {
        _tooltip = tooltip;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _restoreRequestedMessage = RegisterWindowMessage(SingleInstanceCoordinator.RestoreMessageName);
        _instance = this;

        IntPtr module = GetModuleHandle(null);
        WindowClass windowClass = new()
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = module,
            ClassName = ClassName,
        };
        RegisterClassEx(ref windowClass);
        _windowHandle = CreateWindowEx(0, ClassName, "My Canvas", PopupStyle, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
        if (_windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("Unable to create the tray window.");

        // The autostart task runs elevated; a normal launch (desktop shortcut) must
        // still be able to restore it, which UIPI blocks unless the message is let in.
        ChangeWindowMessageFilterEx(_windowHandle, _restoreRequestedMessage, MessageFilterAllow, IntPtr.Zero);
        ChangeWindowMessageFilterEx(_windowHandle, _taskbarCreatedMessage, MessageFilterAllow, IntPtr.Zero);
        _iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 32, 32, LoadFromFile);
        if (_iconHandle == IntPtr.Zero)
            throw new InvalidOperationException("Unable to load tray icon: " + iconPath);

        AddIcon();
    }

    internal void ShowInfo(string title, string message)
    {
        if (_disposed)
            return;

        NotifyIconData data = CreateData();
        data.Flags = NotifyInfo;
        data.InfoTitle = title;
        data.Info = message;
        data.InfoFlags = InfoFlagInfo;
        data.TimeoutOrVersion = 2200;
        ShellNotifyIcon(NotifyModify, ref data);
    }

    internal void UpdateTooltip(string tooltip)
    {
        if (_disposed)
            return;
        _tooltip = tooltip;
        NotifyIconData data = CreateData();
        data.Flags = NotifyTip;
        data.Tip = _tooltip;
        ShellNotifyIcon(NotifyModify, ref data);
    }

    private void AddIcon()
    {
        NotifyIconData data = CreateData();
        data.Flags = NotifyMessage | NotifyIcon | NotifyTip;
        data.CallbackMessage = CallbackMessage;
        data.IconHandle = _iconHandle;
        data.Tip = _tooltip;
        if (!ShellNotifyIcon(NotifyAdd, ref data))
            throw new InvalidOperationException("Windows rejected the tray icon.");
    }

    private NotifyIconData CreateData()
    {
        return new NotifyIconData
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = 1,
            Tip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty
        };
    }

    private static IntPtr Dispatch(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        TrayIconController? tray = _instance;
        if (tray != null && !tray._disposed && tray.HandleWindowMessage(message, lParam))
            return IntPtr.Zero;
        return DefWindowProc(window, message, wParam, lParam);
    }

    private bool HandleWindowMessage(uint message, IntPtr lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            AddIcon();
            return true;
        }

        if (message == _restoreRequestedMessage)
        {
            OpenRequested?.Invoke();
            return true;
        }

        if (message != CallbackMessage)
            return false;

        uint mouseMessage = unchecked((uint)lParam.ToInt64());
        if (mouseMessage is LeftButtonUp or LeftButtonDoubleClick)
            OpenRequested?.Invoke();
        else if (mouseMessage is RightButtonUp or ContextMenu)
            ShowContextMenu();
        return true;
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            AppendMenu(menu, MenuString, OpenCommand, Localization.Get("Tray.Open"));
            AppendMenu(menu, MenuSeparator, 0, null);
            AppendMenu(menu, MenuString, ExitCommand, Localization.Get("Tray.Exit"));
            GetCursorPos(out Point cursor);
            SetForegroundWindow(_windowHandle);
            uint command = TrackPopupMenu(
                menu,
                TrackRightButton | TrackReturnCommand,
                cursor.X,
                cursor.Y,
                0,
                _windowHandle,
                IntPtr.Zero);
            if (command == OpenCommand)
                OpenRequested?.Invoke();
            else if (command == ExitCommand)
                ExitRequested?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        NotifyIconData data = CreateData();
        ShellNotifyIcon(NotifyDelete, ref data);
        DestroyWindow(_windowHandle);
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal int Size;
        internal IntPtr WindowHandle;
        internal uint Id;
        internal uint Flags;
        internal uint CallbackMessage;
        internal IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State;
        internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid ItemGuid;
        internal IntPtr BalloonIconHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal IntPtr Procedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal IntPtr Instance;
        internal IntPtr Icon;
        internal IntPtr Cursor;
        internal IntPtr Background;
        internal string? MenuName;
        internal string ClassName;
        internal IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint load);

    private const uint MessageFilterAllow = 1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr filterStatus);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string value);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint item, string? text);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
