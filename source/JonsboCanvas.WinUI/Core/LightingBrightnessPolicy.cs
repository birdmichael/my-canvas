namespace JonsboCanvas_WinUI;

internal static class LightingBrightnessPolicy
{
    internal static byte FromPercent(int percent, double sceneLevel = 1) =>
        (byte)Math.Clamp(Math.Round(Math.Clamp(percent, 0, 100) * 255 / 100.0 * Math.Clamp(sceneLevel, 0, 1)), 0, 255);

    internal static byte ScaleEnvelope(byte envelope, int percent) =>
        (byte)Math.Clamp(Math.Round(envelope * Math.Clamp(percent, 0, 100) / 100.0), 0, 255);
}
