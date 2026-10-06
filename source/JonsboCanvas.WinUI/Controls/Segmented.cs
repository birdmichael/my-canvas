using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace JonsboCanvas_WinUI;

// A row of mutually exclusive options, like the segmented control in Windows 11.
public sealed class Segmented : UserControl
{
    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly string _group = "segmented-" + Guid.NewGuid().ToString("N");
    private string _selected = "";
    private bool _updating;

    // Raised when the user picks an option, with its tag.
    public event Action<string>? SelectionChanged;

    public Segmented()
    {
        Content = new Border
        {
            Background = (Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            Child = _panel,
        };
    }

    public void SetOptions(params (string Tag, string Label)[] options)
    {
        _panel.Children.Clear();
        foreach (var (tag, label) in options)
        {
            RadioButton option = new()
            {
                Content = label,
                Tag = tag,
                GroupName = _group,
                Style = (Style)Application.Current.Resources["SegmentStyle"],
                IsChecked = tag == _selected,
            };
            option.Checked += Option_Checked;
            _panel.Children.Add(option);
        }
    }

    // Setting it never raises SelectionChanged.
    public string Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            _updating = true;
            foreach (RadioButton option in _panel.Children.OfType<RadioButton>())
                option.IsChecked = (string)option.Tag == value;
            _updating = false;
        }
    }

    private void Option_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        _selected = tag;
        if (!_updating) SelectionChanged?.Invoke(tag);
    }
}
