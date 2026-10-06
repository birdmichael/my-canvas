using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace JonsboCanvas
{
    internal sealed class UsbDriverStatus
    {
        public bool Ready;
        public bool DriverStorePresent;
        public bool RepairPackagePresent;
        public string Message;
    }

    internal sealed class UsbDriverInstallResult
    {
        public bool Success;
        public string Message;
    }

    internal static class UsbDriverManager
    {
        private const int ErrorSuccess = 0;
        private const int ErrorNoMoreItems = 259;
        private const int ErrorSuccessRebootInitiated = 1641;
        private const int ErrorSuccessRebootRequired = 3010;
        private const string DriverInfName = "msusbdisplay.inf";
        private const string ServiceRegistryPath = @"SYSTEM\CurrentControlSet\Services\libusb0";

        private static string PackagedInfPath
        {
            get
            {
                return Path.Combine(EmbeddedRuntime.RuntimeDirectory,
                    "drivers", "MSUSBDisplay", DriverInfName);
            }
        }

        public static UsbDriverStatus Check()
        {
            UsbDriverStatus status = new UsbDriverStatus();
            string systemDriver = Path.Combine(Environment.SystemDirectory, "drivers", "libusb0.sys");
            bool servicePresent = false;

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath, false))
                    servicePresent = key != null;
            }
            catch (Exception exception)
            {
                Log.Write("Unable to inspect libusb0 service: " + exception.Message);
            }

            status.Ready = servicePresent && File.Exists(systemDriver);
            status.RepairPackagePresent = File.Exists(PackagedInfPath) &&
                File.Exists(Path.Combine(Path.GetDirectoryName(PackagedInfPath), "MSUSBDisplay.cat")) &&
                File.Exists(Path.Combine(Path.GetDirectoryName(PackagedInfPath), "amd64", "libusb0.sys"));
            status.DriverStorePresent = status.Ready || IsPackageInDriverStore();

            if (status.Ready)
                status.Message = "USB 驱动正常（MS USB Display / libusb0）";
            else if (status.DriverStorePresent)
                status.Message = "USB 驱动包已在 Windows 中；请重新插入屏幕完成匹配";
            else if (status.RepairPackagePresent)
                status.Message = "USB 驱动缺失；可点击右侧按钮安装";
            else
                status.Message = "USB 驱动缺失，且程序目录没有修复包";

            return status;
        }

        public static UsbDriverInstallResult InstallOrRepair()
        {
            UsbDriverInstallResult result = new UsbDriverInstallResult();
            if (!File.Exists(PackagedInfPath))
            {
                result.Message = "找不到随程序提供的驱动文件：" + PackagedInfPath;
                return result;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
                info.Arguments = "/add-driver \"" + PackagedInfPath + "\" /install";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(60000))
                    {
                        try { process.Kill(); }
                        catch { }
                        result.Message = "驱动安装超时，请稍后重新检查。";
                        return result;
                    }

                    int exitCode = process.ExitCode;
                    Log.Write("pnputil exit " + exitCode + ": " + output + " " + error);
                    return InterpretInstallResult(exitCode, Check(), output, error);
                }
            }
            catch (Exception exception)
            {
                Log.Write("Driver repair failed: " + exception);
                result.Message = "无法启动 Windows 驱动安装工具：" + exception.Message;
                return result;
            }
        }

        internal static UsbDriverInstallResult InterpretInstallResult(
            int exitCode, UsbDriverStatus status, string output, string error)
        {
            UsbDriverInstallResult result = new UsbDriverInstallResult();
            if (exitCode == ErrorSuccess)
            {
                result.Success = true;
                result.Message = "Windows 已接收并安装签名驱动。若屏幕仍未识别，请拔插一次 USB。";
                return result;
            }

            if (exitCode == ErrorSuccessRebootRequired ||
                exitCode == ErrorSuccessRebootInitiated)
            {
                result.Success = true;
                result.Message = "Windows 已安装签名驱动；请重启电脑以完成安装。";
                return result;
            }

            // PnPUtil defines 259 as "no matching device" or "the target is
            // already using a better/newer driver". It is not a generic
            // installation failure. Verify the resulting state so the UI can
            // distinguish an already healthy/staged driver from a missing device.
            if (exitCode == ErrorNoMoreItems)
            {
                if (status != null && status.Ready)
                {
                    result.Success = true;
                    result.Message = "当前设备已在使用此驱动或更新版本，无需重复安装。";
                }
                else if (status != null && status.DriverStorePresent)
                {
                    result.Success = true;
                    result.Message = "驱动包已在 Windows 中；未找到需要更新的设备。若屏幕未识别，请重新插入 USB。";
                }
                else
                {
                    result.Message = "没有检测到与此驱动匹配的设备。请连接屏幕后重试（代码 259）。";
                }
                return result;
            }

            string detail = !string.IsNullOrWhiteSpace(error) ? error.Trim() :
                (!string.IsNullOrWhiteSpace(output) ? output.Trim() : string.Empty);
            result.Message = "驱动安装失败（代码 " + exitCode + "）。";
            if (detail.Length > 0)
                result.Message += Environment.NewLine + detail;
            return result;
        }

        private static bool IsPackageInDriverStore()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
                info.Arguments = "/enum-drivers";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10000);
                    return output.IndexOf(DriverInfName, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception exception)
            {
                Log.Write("Unable to inspect Driver Store: " + exception.Message);
                return false;
            }
        }
    }
}
