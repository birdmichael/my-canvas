using JonsboCanvas;

static class DriverInstallResultTests
{
    public static int Main()
    {
        SuccessIsReported();
        RebootRequiredIsReportedAsSuccess();
        Code259WithReadyDriverIsNotFailure();
        Code259WithStagedDriverIsNotFailure();
        Code259WithoutDriverIsActionable();
        FailureUsesStandardOutputWhenStandardErrorIsEmpty();
        Console.WriteLine("UsbDriverManager regression tests passed.");
        return 0;
    }

    private static void SuccessIsReported()
    {
        UsbDriverInstallResult result = Interpret(0, ready: true, stored: true);
        Assert(result.Success, "Exit code 0 must be successful.");
    }

    private static void RebootRequiredIsReportedAsSuccess()
    {
        UsbDriverInstallResult result = Interpret(3010, ready: false, stored: true);
        Assert(result.Success, "Exit code 3010 must be successful.");
        Assert(result.Message.Contains("重启"), "Exit code 3010 must request a reboot.");
    }

    private static void Code259WithReadyDriverIsNotFailure()
    {
        UsbDriverInstallResult result = Interpret(259, ready: true, stored: true);
        Assert(result.Success, "Exit code 259 must not fail when the driver is ready.");
        Assert(result.Message.Contains("无需重复安装"), "Ready code 259 needs an already-installed message.");
    }

    private static void Code259WithStagedDriverIsNotFailure()
    {
        UsbDriverInstallResult result = Interpret(259, ready: false, stored: true);
        Assert(result.Success, "Exit code 259 must not fail when the package is staged.");
        Assert(result.Message.Contains("驱动包已在 Windows 中"), "Staged code 259 needs a staged-driver message.");
    }

    private static void Code259WithoutDriverIsActionable()
    {
        UsbDriverInstallResult result = Interpret(259, ready: false, stored: false);
        Assert(!result.Success, "Exit code 259 must remain unsuccessful when no driver state is present.");
        Assert(result.Message.Contains("连接屏幕"), "Unmatched code 259 needs device guidance.");
    }

    private static void FailureUsesStandardOutputWhenStandardErrorIsEmpty()
    {
        UsbDriverStatus status = new UsbDriverStatus();
        UsbDriverInstallResult result = UsbDriverManager.InterpretInstallResult(
            5, status, "Access denied", string.Empty);
        Assert(!result.Success, "Unexpected exit codes must fail.");
        Assert(result.Message.Contains("Access denied"), "PnPUtil stdout details must be preserved.");
    }

    private static UsbDriverInstallResult Interpret(int exitCode, bool ready, bool stored)
    {
        UsbDriverStatus status = new UsbDriverStatus
        {
            Ready = ready,
            DriverStorePresent = stored
        };
        return UsbDriverManager.InterpretInstallResult(exitCode, status, string.Empty, string.Empty);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

namespace JonsboCanvas
{
    internal static class EmbeddedRuntime
    {
        public static string RuntimeDirectory { get; } = AppContext.BaseDirectory;
    }

    internal static class Log
    {
        public static void Write(string message)
        {
        }
    }
}
