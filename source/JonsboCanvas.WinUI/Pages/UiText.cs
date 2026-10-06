using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace JonsboCanvas_WinUI;

// Wording and dialogs shared by several pages.
internal static class UiText
{
    public static string L(string key) => Localization.Get(key);
    public static string LF(string key, params object[] args) => Localization.Format(key, args);

    public static string ScreenState(ScreenState state, string port = "") => state switch
    {
        JonsboCanvas_WinUI.ScreenState.Connected => string.IsNullOrEmpty(port) ? L("State.Connected") : LF("State.ConnectedOn", port),
        JonsboCanvas_WinUI.ScreenState.Searching => L("State.Searching"),
        JonsboCanvas_WinUI.ScreenState.NotResponding => L("State.NotResponding"),
        _ => App.Engine.CaptureMode ? L("State.Demo") : L("State.Off"),
    };

    public static Brush StateBrush(ScreenState state) => (Brush)Application.Current.Resources[state switch
    {
        JonsboCanvas_WinUI.ScreenState.Connected => "CanvasHealthyBrush",
        JonsboCanvas_WinUI.ScreenState.Searching => "CanvasWarningBrush",
        JonsboCanvas_WinUI.ScreenState.NotResponding => "CanvasErrorBrush",
        _ => "TextFillColorTertiaryBrush",
    }];

    public static string MusicState(MusicLink state) => L(state switch
    {
        MusicLink.Synced => "Music.State.Synced",
        MusicLink.Connecting => "Music.State.Connecting",
        MusicLink.WaitingForTrack => "Music.State.Waiting",
        _ => "Music.State.Off",
    });

    public static string MusicAction(MusicLink state) => L(state == MusicLink.WaitingForTrack ? "Music.Recheck" : "Music.Connect");

    public static string ModeName(string mode) => L(mode switch
    {
        "music" => "Mode.Music",
        "clock" => "Mode.Clock",
        "auto" => "Mode.Auto",
        _ => "Mode.Hardware",
    });

    public static Task ConnectAsync(XamlRoot root) => App.Engine.ConnectAsync(() => ConfirmAsync(root,
        L("Connection.Takeover.Title"), L("Connection.Takeover.Content"), L("Connection.Takeover.Primary"), L("Common.Cancel")));

    public static Task EnsureMusicAsync(XamlRoot root) => App.Engine.EnsureMusicRealtimeAsync(() => ConfirmAsync(root,
        L("Music.Restart.Title"), L("Music.Restart.Content"), L("Music.Restart.Primary"), L("Common.NotNow")));

    private static async Task<bool> ConfirmAsync(XamlRoot root, string title, string content, string primary, string close)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
