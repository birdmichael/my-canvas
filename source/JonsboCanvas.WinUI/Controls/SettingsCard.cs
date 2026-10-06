using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JonsboCanvas_WinUI;

// Its look is the implicit style in App.xaml.
public sealed class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata("", (d, _) => ((SettingsCard)d).UpdateParts()));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsCard), new PropertyMetadata("", (d, _) => ((SettingsCard)d).UpdateParts()));

    private FrameworkElement? _description;
    private FrameworkElement? _icon;

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    // A Segoe Fluent Icons glyph; empty leaves no icon column.
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _description = GetTemplateChild("DescriptionPart") as FrameworkElement;
        _icon = GetTemplateChild("IconPart") as FrameworkElement;
        UpdateParts();
    }

    private void UpdateParts()
    {
        if (_description != null)
            _description.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        if (_icon != null)
            _icon.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;
    }
}
