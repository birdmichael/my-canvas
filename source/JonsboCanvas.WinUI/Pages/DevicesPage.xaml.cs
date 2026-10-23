using System.Diagnostics;
using JonsboCanvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static JonsboCanvas_WinUI.UiText;

namespace JonsboCanvas_WinUI;

public sealed partial class DevicesPage : Page, ILivePage
{
    private static readonly int[] LongRotations = { 90, 270 };
    private static readonly int[] SquareRotations = { 0, 90, 180, 270 };
    private readonly CanvasEngine _engine = App.Engine;
    private bool _syncing = true;

    public DevicesPage()
    {
        InitializeComponent();
        AppConfig config = _engine.Config;
        foreach (int degrees in LongRotations) LongRotation.Items.Add(new ComboBoxItem { Content = LF("Devices.Rotation", degrees), Tag = degrees });
        foreach (int degrees in SquareRotations) SquareRotation.Items.Add(new ComboBoxItem { Content = LF("Devices.Rotation", degrees), Tag = degrees });
        LongRotation.SelectedIndex = Array.IndexOf(LongRotations, config.LongRotationDegrees);
        SquareRotation.SelectedIndex = Array.IndexOf(SquareRotations, config.SquareRotationDegrees);
        AutoConnectToggle.IsOn = config.AutoConnect;
        TakeoverToggle.IsOn = config.AutoTakeOverOriginalApp;
        _syncing = false;
        Loaded += async (_, _) =>
        {
            Refresh();
            await RefreshDriverAsync();
        };
    }

    public void Refresh()
    {
        ScreenState longState = _engine.LongState, squareState = _engine.SquareState;
        ScreenState keyboardState = _engine.KeyboardState;
        var devices = _engine.Devices;
        int online = devices.Count(device => device.State == ScreenState.Connected);
        ConnectionTitle.Text = _engine.CaptureMode ? L("State.Demo") : LF("Home.Online", online, devices.Count);
        CapabilitiesText.Text = LF("Devices.Capabilities",
            devices.Count(d => d.Capabilities.HasFlag(CanvasDeviceCapabilities.Display)),
            devices.Count(d => d.Capabilities.HasFlag(CanvasDeviceCapabilities.Lighting)));
        LongState.Text = ScreenState(longState, _engine.LongPortName);
        LongDot.Fill = StateBrush(longState);
        SquareState.Text = ScreenState(squareState);
        SquareDot.Fill = StateBrush(squareState);
        KeyboardDot.Fill = StateBrush(keyboardState);
        KeyboardState.Text = ScreenState(keyboardState) + " · " + (_engine.KeyboardUploading ? L("State.Uploading") :
            _engine.Config.KeyboardWallpaperSync ? ModeName(_engine.KeyboardContentName) : L("State.SyncOff"));
        KeyboardCard.Description = _engine.KeyboardLastError.Length > 0 ? LF("State.Error", _engine.KeyboardLastError) : L("Devices.Keyboard.Hint");
        CanvasDeviceSnapshot? motherboard = devices.FirstOrDefault(device => device.Kind == CanvasDeviceKind.MotherboardLighting);
        MotherboardState.Text = motherboard == null ? L("State.Off") : ScreenState(motherboard.State);
        MotherboardDot.Fill = StateBrush(motherboard?.State ?? ScreenState.Off);
        MotherboardCard.Description = motherboard?.LastError.Length > 0 ? LF("State.Error", motherboard.LastError) : L("Devices.Motherboard.Hint");
        var diagnostics = _engine.KeyboardDiagnostics;
        KeyboardDiagnostics.Text = LF("Devices.Keyboard.Diagnostics", diagnostics.Sessions, diagnostics.Uploads,
            diagnostics.LightWrites, diagnostics.Failures);
        ReconnectKeyboardButton.IsEnabled = !_engine.CaptureMode && !_engine.KeyboardUploading;
        LongCard.Description = L("Devices.Long.Hint");
        SquareCard.Description = L("Devices.Square.Hint");

        bool busy = _engine.ConnectBusy;
        bool requested = _engine.ConnectionRequested;
        ConnectButton.IsEnabled = !busy && !_engine.CaptureMode && _engine.StartupError == null;
        ConnectButton.Style = (Style)Application.Current.Resources[requested ? "DefaultButtonStyle" : "AccentButtonStyle"];
        ConnectLabel.Text = L(requested ? "Connection.Disconnect" : "Connection.Connect");
        ConnectProgress.IsActive = busy;
        ConnectProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        ProblemBar.IsOpen = longState == ScreenState.NotResponding;
        ProblemBar.Message = L("State.NotRespondingHint");
    }

    private void ReconnectKeyboardButton_Click(object sender, RoutedEventArgs e)
    {
        _engine.ReconnectKeyboard();
        Refresh();
    }

    private async Task RefreshDriverAsync()
    {
        UsbDriverStatus status = _engine.CaptureMode
            ? new UsbDriverStatus { Ready = true, Message = L("Driver.Ready") }
            : await Task.Run(UsbDriverManager.Check);
        DriverCard.Description = status.Ready ? L("Driver.Ready") : status.Message;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        Task work = _engine.ConnectionRequested ? _engine.DisconnectAsync() : UiText.ConnectAsync(XamlRoot);
        Refresh();
        await work;
        Refresh();
    }

    private void LongRotation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LongRotation.SelectedItem is not ComboBoxItem { Tag: int degrees }) return;
        _engine.Config.LongRotationDegrees = degrees;
        _engine.ContentChanged();
    }

    private void SquareRotation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || SquareRotation.SelectedItem is not ComboBoxItem { Tag: int degrees }) return;
        _engine.Config.SquareRotationDegrees = degrees;
        _engine.ContentChanged();
    }

    private void AutoConnectToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _engine.Config.AutoConnect = AutoConnectToggle.IsOn;
        _engine.SaveConfig();
    }

    private void TakeoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _engine.Config.AutoTakeOverOriginalApp = TakeoverToggle.IsOn;
        _engine.SaveConfig();
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        RepairButton.IsEnabled = false;
        RepairProgress.IsActive = true;
        RepairProgress.Visibility = Visibility.Visible;
        DriverCard.Description = L("Driver.Repairing");
        try
        {
            UsbDriverInstallResult result = await Task.Run(UsbDriverManager.InstallOrRepair);
            App.Current.ShowNotice(result.Message);
            await RefreshDriverAsync();
        }
        catch (Exception exception)
        {
            Log.Write("Unable to repair USB driver: " + exception);
            DriverCard.Description = LF("Driver.RepairFailed", exception.Message);
        }
        finally
        {
            RepairProgress.IsActive = false;
            RepairProgress.Visibility = Visibility.Collapsed;
            RepairButton.IsEnabled = true;
        }
    }

    private async void SaveFrameButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = await _engine.SaveFrameAsync();
            App.Current.ShowNotice(LF("Devices.Saved", path));
        }
        catch (Exception exception)
        {
            App.Current.ShowNotice(LF("Devices.SaveFailed", exception.Message));
        }
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + EmbeddedRuntime.DataDirectory + "\"") { UseShellExecute = true });
}
