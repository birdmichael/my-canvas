using System.Diagnostics;
using System.Reflection;
using JonsboCanvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static JonsboCanvas_WinUI.UiText;

namespace JonsboCanvas_WinUI;

public sealed partial class SettingsPage : Page
{
    private readonly CanvasEngine _engine = App.Engine;
    private bool _syncing = true;

    public SettingsPage()
    {
        InitializeComponent();
        AppConfig config = _engine.Config;
        LanguagePicker.SelectedIndex = Localization.IsEnglish ? 1 : 0;
        StartupToggle.IsOn = config.StartWithWindows;
        HiddenToggle.IsOn = config.StartHiddenOnAutoStart;
        HiddenToggle.IsEnabled = config.StartWithWindows;
        TrayToggle.IsOn = config.MinimizeToTray;
        string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        AboutCard.Description = LF("Settings.About", version);
        _syncing = false;
    }

    private void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LanguagePicker.SelectedItem is not ComboBoxItem { Tag: string language }) return;
        if (AppConfig.NormalizeLanguage(language) == _engine.Config.Language) return;
        _engine.SetLanguage(language);
        DispatcherQueue.TryEnqueue(App.Current.ApplyLanguage);
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        bool enabled = StartupToggle.IsOn;
        HiddenToggle.IsEnabled = enabled;
        _engine.Config.StartWithWindows = enabled;
        _engine.SaveConfig();

        string? executable = Environment.ProcessPath;
        bool updated = executable != null && await Task.Run(() => enabled
            ? StartupTaskManager.EnsureRegistered(executable, allowElevationPrompt: true)
            : StartupTaskManager.RemoveRegistered(allowElevationPrompt: true));
        if (!updated && enabled)
        {
            _engine.Config.StartWithWindows = false;
            _engine.SaveConfig();
            _syncing = true;
            StartupToggle.IsOn = false;
            HiddenToggle.IsEnabled = false;
            _syncing = false;
            App.Current.ShowNotice(L("Status.StartupFailed"));
        }
        else if (!updated)
        {
            App.Current.ShowNotice(L("Status.StartupCleanupFailed"));
        }
    }

    private void HiddenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _engine.Config.StartHiddenOnAutoStart = HiddenToggle.IsOn;
        _engine.SaveConfig();
    }

    private void TrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _engine.Config.MinimizeToTray = TrayToggle.IsOn;
        _engine.SaveConfig();
    }

    private void DataButton_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + EmbeddedRuntime.DataDirectory + "\"") { UseShellExecute = true });
}
