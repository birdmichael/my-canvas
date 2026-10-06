using JonsboCanvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using static JonsboCanvas_WinUI.UiText;

namespace JonsboCanvas_WinUI;

public sealed partial class LightingPage : Page, ILivePage
{
    private static readonly string[] Presets =
        { "#FF6B35", "#F5A524", "#35C8B8", "#3B82F6", "#8B5CF6", "#EC4899", "#EF4444", "#22C55E", "#FFFFFF" };

    private readonly CanvasEngine _engine = App.Engine;
    private bool _syncing = true;

    public LightingPage()
    {
        InitializeComponent();
        AppConfig config = _engine.Config;
        HardwareSource.SetOptions(("auto", L("Lighting.FromWallpaper")), ("fixed", L("Lighting.Fixed")));
        MusicSource.SetOptions(("auto", L("Lighting.FromCover")), ("fixed", L("Lighting.Fixed")));
        HardwareSource.Selected = config.LightingWallpaperColor ? "auto" : "fixed";
        MusicSource.Selected = config.LightingCoverColor ? "auto" : "fixed";
        HardwareSource.SelectionChanged += source =>
        {
            _engine.Config.LightingWallpaperColor = source == "auto";
            _engine.LightingChanged();
            UpdateParts();
        };
        MusicSource.SelectionChanged += source =>
        {
            _engine.Config.LightingCoverColor = source == "auto";
            _engine.LightingChanged(coverChanged: true);
            UpdateParts();
        };
        EnabledToggle.IsOn = config.LightingEnabled;
        BeatToggle.IsOn = config.LightingBeatSync;
        BrightnessSlider.Value = config.LightingBrightness;
        BrightnessText.Text = config.LightingBrightness + "%";
        _syncing = false;
        UpdateParts();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        System.Drawing.Color light = _engine.LightColor;
        bool on = _engine.Config.LightingEnabled;
        CurrentSwatch.Background = new SolidColorBrush(on && !light.IsEmpty
            ? Windows.UI.Color.FromArgb(255, light.R, light.G, light.B)
            : Windows.UI.Color.FromArgb(255, 0x2B, 0x2B, 0x2B));
        CurrentText.Text = on ? LF("Lighting.Current", $"#{light.R:X2}{light.G:X2}{light.B:X2}") : L("Lighting.Enable.Hint");
    }

    private void UpdateParts()
    {
        AppConfig config = _engine.Config;
        HardwareColors.Visibility = config.LightingWallpaperColor ? Visibility.Collapsed : Visibility.Visible;
        MusicColors.Visibility = config.LightingCoverColor ? Visibility.Collapsed : Visibility.Visible;
        BuildSwatches(HardwareSwatches, config.LightingHardwareColor, music: false);
        BuildSwatches(MusicSwatches, config.LightingMusicColor, music: true);
    }

    private void BuildSwatches(StackPanel host, string selected, bool music)
    {
        host.Children.Clear();
        bool matched = false;
        foreach (string hex in Presets)
        {
            bool isSelected = string.Equals(hex, selected, StringComparison.OrdinalIgnoreCase);
            matched |= isSelected;
            host.Children.Add(Swatch(hex, isSelected, () => SetColor(hex, music)));
        }

        // The custom colour button shows the current colour when it is not a preset.
        System.Drawing.Color current = _engine.Config.GetColor(selected, System.Drawing.Color.White);
        ColorPicker picker = new()
        {
            Color = Windows.UI.Color.FromArgb(255, current.R, current.G, current.B),
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true,
        };
        Button custom = new()
        {
            Height = 36,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(18),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE790", FontSize = 14 },
                    new TextBlock { Text = L("Lighting.Custom"), VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Flyout = new Flyout { Content = picker },
        };
        if (!matched)
        {
            custom.BorderBrush = (Brush)Application.Current.Resources["CanvasAccentBrush"];
            custom.BorderThickness = new Thickness(2);
        }
        custom.Flyout.Closed += (_, _) =>
        {
            Windows.UI.Color c = picker.Color;
            SetColor($"#{c.R:X2}{c.G:X2}{c.B:X2}", music);
        };
        host.Children.Add(custom);
    }

    private static Button Swatch(string hex, bool selected, Action pick)
    {
        System.Drawing.Color color = System.Drawing.ColorTranslator.FromHtml(hex);
        Button swatch = new()
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, color.R, color.G, color.B)),
            BorderBrush = selected
                ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
                : (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(selected ? 3 : 1),
        };
        // Keep the fill when hovered; the default button template would grey it out.
        swatch.Resources["ButtonBackgroundPointerOver"] = swatch.Background;
        swatch.Resources["ButtonBackgroundPressed"] = swatch.Background;
        ToolTipService.SetToolTip(swatch, hex);
        swatch.Click += (_, _) => pick();
        return swatch;
    }

    private void SetColor(string hex, bool music)
    {
        if (music) _engine.Config.LightingMusicColor = hex;
        else _engine.Config.LightingHardwareColor = hex;
        _engine.LightingChanged();
        UpdateParts();
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _engine.Config.LightingEnabled = EnabledToggle.IsOn;
        _engine.LightingChanged();
        Refresh();
    }

    private void BeatToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _engine.Config.LightingBeatSync = BeatToggle.IsOn;
        _engine.LightingChanged(beatChanged: true);
    }

    private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        int value = (int)Math.Round(e.NewValue);
        BrightnessText.Text = value + "%";
        _engine.SetBrightness(value);
        BrightnessSave.Schedule(_engine);
    }
}
