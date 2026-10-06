using JonsboCanvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static JonsboCanvas_WinUI.UiText;

namespace JonsboCanvas_WinUI;

public sealed partial class ContentPage : Page, ILivePage
{
    private static readonly int[] FrameRates = { 5, 8, 10, 15, 20 };
    private static readonly int[] RefreshIntervals = { 500, 900, 2000 };
    private readonly CanvasEngine _engine = App.Engine;
    private bool _syncing = true;

    public ContentPage()
    {
        InitializeComponent();
        AppConfig config = _engine.Config;
        foreach (string style in AppConfig.LyricStyles)
            LyricPicker.Items.Add(new ComboBoxItem { Content = L("Lyrics." + style), Tag = style });
        foreach (int fps in FrameRates)
            FpsPicker.Items.Add(new ComboBoxItem { Content = L("Fps." + fps), Tag = fps });
        foreach (int interval in RefreshIntervals)
            RefreshPicker.Items.Add(new ComboBoxItem { Content = L("Refresh." + interval), Tag = interval });

        LyricPicker.SelectedIndex = Math.Max(0, Array.IndexOf(AppConfig.LyricStyles, config.LyricStyle));
        FpsPicker.SelectedIndex = Array.FindLastIndex(FrameRates, fps => fps <= config.MusicFrameRate) is int f and >= 0 ? f : 1;
        RefreshPicker.SelectedIndex = Array.FindIndex(RefreshIntervals, ms => ms >= config.RefreshMilliseconds) is int r and >= 0 ? r : 2;
        Clock24Toggle.IsOn = config.Use24HourClock;
        _syncing = false;
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        MusicLink state = _engine.MusicState;
        SyncState.Text = MusicState(state);
        SyncDot.Fill = StateBrush(state switch
        {
            MusicLink.Synced => ScreenState.Connected,
            MusicLink.Off => ScreenState.Off,
            _ => ScreenState.Searching,
        });
        SyncButton.Content = MusicAction(state);
        SyncButton.Visibility = state is MusicLink.Off or MusicLink.WaitingForTrack && !_engine.CaptureMode
            ? Visibility.Visible : Visibility.Collapsed;
        var music = _engine.Music;
        SyncCard.Description = state == MusicLink.Synced && music?.Available == true
            ? music.Title + " · " + music.Artist
            : L("Content.Sync.Hint");
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        Task work = EnsureMusicAsync(XamlRoot);
        Refresh();
        await work;
        Refresh();
    }

    private void LyricPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LyricPicker.SelectedItem is not ComboBoxItem { Tag: string style }) return;
        _engine.Config.LyricStyle = style;
        _engine.ContentChanged();
    }

    private void FpsPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || FpsPicker.SelectedItem is not ComboBoxItem { Tag: int fps }) return;
        _engine.Config.MusicFrameRate = fps;
        _engine.ContentChanged();
    }

    private void RefreshPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || RefreshPicker.SelectedItem is not ComboBoxItem { Tag: int interval }) return;
        _engine.Config.RefreshMilliseconds = interval;
        _engine.ContentChanged();
    }

    private void Clock24Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _engine.Config.Use24HourClock = Clock24Toggle.IsOn;
        _engine.ContentChanged();
    }
}
