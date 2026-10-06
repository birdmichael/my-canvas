using System;

namespace JonsboCanvas
{
    internal static class AppLanguage
    {
        internal static string Normalize(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                value.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return "en-US";
            return "zh-CN";
        }
    }
}
