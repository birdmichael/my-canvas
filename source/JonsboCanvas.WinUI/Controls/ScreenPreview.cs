using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace JonsboCanvas_WinUI;

// A device screen drawn as a dark panel with the live frame inside. Its height
// follows its width at the screen's aspect ratio.
public sealed class ScreenPreview : UserControl
{
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly ProgressRing _progress = new() { IsActive = true, Width = 28, Height = 28 };
    private readonly Border _screen;
    private WriteableBitmap? _bitmap;

    public ScreenPreview()
    {
        Grid content = new();
        content.Children.Add(_progress);
        content.Children.Add(_image);
        _screen = new Border
        {
            Background = (Brush)Application.Current.Resources["CanvasScreenBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5),
            Child = new Border { CornerRadius = new CornerRadius(6), Child = content },
        };
        Content = _screen;
        SizeChanged += (_, args) => _screen.Height = args.NewSize.Width * AspectRatio + 10;
    }

    // Height divided by width of the device screen.
    public double AspectRatio { get; set; } = 1;

    // BGRA pixels, top row first.
    internal void Show(byte[] pixels, int width, int height)
    {
        if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height);
            _image.Source = _bitmap;
        }
        using (Stream stream = _bitmap.PixelBuffer.AsStream())
            stream.Write(pixels, 0, Math.Min(pixels.Length, width * height * 4));
        _bitmap.Invalidate();
        _progress.IsActive = false;
        _progress.Visibility = Visibility.Collapsed;
    }
}
