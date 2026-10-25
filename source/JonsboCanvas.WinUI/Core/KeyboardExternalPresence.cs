using System.Xml.Linq;

namespace JonsboCanvas_WinUI;

// ROG Live Service publishes the wireless keyboard's presence here. Reading a
// small local XML is cheap and keeps MyCanvas from polling the HID transport.
internal static class KeyboardExternalPresence
{
    internal const string DefaultPath = @"C:\ProgramData\ASUS\ROG Live Service\UnifyDGDevice.xml";

    internal static bool IsKeyboardPresent(string path = DefaultPath)
        => TryGetKeyboardPresence(out bool present, path) && present;

    internal static bool TryGetKeyboardPresence(out bool present, string path = DefaultPath)
    {
        present = false;
        try
        {
            if (!File.Exists(path)) return false;
            XDocument document = XDocument.Load(path);
            present = document.Descendants("device_id")
                .Where(element => string.Equals((string?)element.Attribute("key"), "0b051c12", StringComparison.OrdinalIgnoreCase))
                .Select(element => (string?)element.Element("status"))
                .Any(value => value == "1");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
