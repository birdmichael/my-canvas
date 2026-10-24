using System.Xml.Linq;

namespace JonsboCanvas_WinUI;

// ROG Live Service publishes the wireless keyboard's presence here. Reading a
// small local XML is cheap and keeps MyCanvas from polling the HID transport.
internal static class KeyboardExternalPresence
{
    internal const string DefaultPath = @"C:\ProgramData\ASUS\ROG Live Service\UnifyDGDevice.xml";

    internal static bool IsKeyboardPresent(string path = DefaultPath)
    {
        try
        {
            if (!File.Exists(path)) return false;
            XDocument document = XDocument.Load(path);
            return document.Descendants("device_id")
                .Where(element => string.Equals((string?)element.Attribute("key"), "0b051c12", StringComparison.OrdinalIgnoreCase))
                .Select(element => (string?)element.Element("status"))
                .Any(value => value == "1");
        }
        catch
        {
            return false;
        }
    }
}
