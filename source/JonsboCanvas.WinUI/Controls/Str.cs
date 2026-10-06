using Microsoft.UI.Xaml.Markup;

namespace JonsboCanvas_WinUI;

// {local:Str Key=Nav.Home} in XAML; pages are rebuilt when the language changes.
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class Str : MarkupExtension
{
    public string Key { get; set; } = "";

    protected override object ProvideValue() => Localization.Get(Key);
}
