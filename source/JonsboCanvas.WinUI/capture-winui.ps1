param(
    [string]$Configuration = 'Debug',
    [ValidateSet('auto','hardware','music','codex','claude')]
    [string]$Mode = 'codex',
    [string]$ExecutablePath = '',
    [switch]$OpenSettings
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class Win32Capture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
'@

[Win32Capture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$projectRoot = $PSScriptRoot
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $projectRoot)
$exe = if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    Join-Path $projectRoot "bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\JonsboCanvas.WinUI.exe"
} else {
    [IO.Path]::GetFullPath($ExecutablePath)
}
if (-not (Test-Path $exe)) {
    throw "WinUI executable not found: $exe"
}

$env:JONSBO_CANVAS_CAPTURE = '1'
$env:JONSBO_CANVAS_CAPTURE_MODE = $Mode
$env:JONSBO_CANVAS_DATA_DIR = Join-Path $repositoryRoot 'work\winui-capture-data'
$env:__COMPAT_LAYER = 'RunAsInvoker'

$process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)

    if ($process.HasExited) {
        throw "WinUI app exited before its window appeared (exit code $($process.ExitCode))."
    }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
        throw 'Timed out waiting for the WinUI window.'
    }

    [Win32Capture]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    [Win32Capture]::BringWindowToTop($process.MainWindowHandle) | Out-Null
    [Win32Capture]::SetWindowPos($process.MainWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
    [Win32Capture]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    if ($OpenSettings) {
        Add-Type -AssemblyName UIAutomationClient
        Start-Sleep -Seconds 1
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $settingsItem = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
                'SettingsItem')))
        if ($null -eq $settingsItem) {
            throw 'Unable to find the Settings navigation item.'
        }
        $selection = $settingsItem.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
    }
    Start-Sleep -Seconds 4

    $rect = New-Object Win32Capture+RECT
    if (-not [Win32Capture]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) {
        throw 'Unable to read WinUI window bounds.'
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object Drawing.Bitmap($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        $captureName = if ($OpenSettings) { 'settings' } else { $Mode }
        $target = Join-Path $projectRoot ('winui-implementation-' + $captureName + '.png')
        $bitmap.Save($target, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    Write-Output $target
}
finally {
    if (-not $process.HasExited) {
        [Win32Capture]::SetWindowPos($process.MainWindowHandle, [IntPtr](-2), 0, 0, 0, 0, 0x0013) | Out-Null
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) {
            $process.Kill()
        }
    }
}
