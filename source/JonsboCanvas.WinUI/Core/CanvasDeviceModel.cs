namespace JonsboCanvas_WinUI;

[Flags]
internal enum CanvasDeviceCapabilities
{
    None = 0,
    Display = 1,
    Lighting = 2,
}

internal enum CanvasDeviceKind
{
    LongScreen,
    SquareScreen,
    Keyboard,
    MotherboardLighting,
}

// A physical device can expose more than one capability. The keyboard is one
// device with both an OLED display and RGB lighting, rather than two devices.
internal sealed record CanvasDeviceSnapshot(
    CanvasDeviceKind Kind,
    string Name,
    ScreenState State,
    CanvasDeviceCapabilities Capabilities,
    string Detail,
    string LastError = "");

internal static class CanvasDeviceCatalog
{
    internal static IReadOnlyList<CanvasDeviceSnapshot> CreateSnapshot(
        ScreenState longScreen, ScreenState squareScreen, ScreenState keyboard, ScreenState motherboard,
        string keyboardError = "", string motherboardError = "") => new[]
    {
        new CanvasDeviceSnapshot(CanvasDeviceKind.LongScreen, "Long screen", longScreen,
            CanvasDeviceCapabilities.Display, "1920x462 USB serial"),
        new CanvasDeviceSnapshot(CanvasDeviceKind.SquareScreen, "Square screen", squareScreen,
            CanvasDeviceCapabilities.Display, "480x480 display SDK"),
        new CanvasDeviceSnapshot(CanvasDeviceKind.Keyboard, "ROG Azoth 96 HE", keyboard,
            CanvasDeviceCapabilities.Display | CanvasDeviceCapabilities.Lighting,
            "184x97 OLED + RGB receiver", keyboardError),
        new CanvasDeviceSnapshot(CanvasDeviceKind.MotherboardLighting, "Motherboard ARGB", motherboard,
            CanvasDeviceCapabilities.Lighting, "ITE IT5711", motherboardError),
    };
}
