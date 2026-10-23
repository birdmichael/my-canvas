namespace JonsboCanvas_WinUI;

internal readonly record struct KeyboardLight(
    byte R,
    byte G,
    byte B,
    byte Brightness);

// Static lighting is state, not a heartbeat. Re-sending an unchanged colour
// keeps the keyboard HID endpoint active and can prevent its idle sleep.
internal static class KeyboardLightDedupPolicy
{
    internal static bool ShouldApply(
        KeyboardLight requested,
        KeyboardLight? applied,
        KeyboardLight? pending) =>
        applied != requested || pending != null;
}
