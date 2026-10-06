using System.Text.Json;
using System.Text.Json.Serialization;

namespace System.Web.Script.Serialization;

// Keeps the proven collectors source-compatible while the WinUI shell runs on modern .NET.
internal sealed class JavaScriptSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true
        };
        // System.Text.Json normally materializes values declared as object as
        // JsonElement. The legacy serializer returned CLR strings/numbers,
        // arrays and dictionaries; collectors rely on those runtime types.
        options.Converters.Add(new InferredObjectConverter());
        // The legacy serializer coerced numbers/booleans into string properties
        // (Netease returns song ids as JSON numbers). System.Text.Json is strict,
        // so restore that leniency for string members.
        options.Converters.Add(new LenientStringConverter());
        return options;
    }

    public int MaxJsonLength { get; set; } = int.MaxValue;

    public T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    public object? DeserializeObject(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ConvertElement(document.RootElement);
    }

    public string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    private static object? ConvertElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                Dictionary<string, object?> dictionary = new(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in element.EnumerateObject())
                    dictionary[property.Name] = ConvertElement(property.Value);
                return dictionary.ToDictionary(pair => pair.Key, pair => pair.Value!);

            case JsonValueKind.Array:
                return element.EnumerateArray().Select(ConvertElement).ToArray();

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                if (element.TryGetInt64(out long integer))
                    return integer;
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            default:
                return null;
        }
    }

    private sealed class InferredObjectConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return ConvertElement(document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, object value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }

    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String: return reader.GetString();
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long integer)) return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case JsonTokenType.True: return "true";
                case JsonTokenType.False: return "false";
                default: return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}
