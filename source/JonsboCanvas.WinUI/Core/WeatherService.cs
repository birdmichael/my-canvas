using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using JonsboCanvas;

namespace JonsboCanvas_WinUI;

internal sealed class WeatherSnapshot
{
    public double Temperature { get; init; }
    public double High { get; init; }
    public double Low { get; init; }
    public int Humidity { get; init; }
    public int Code { get; init; }
    public DateTime FetchedAt { get; init; }
}

// Open-Meteo needs no API key. Without a configured "lat,lon" the location is
// resolved once per session from the public IP.
internal sealed class WeatherService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(3);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _configuredLocation;
    private (double Latitude, double Longitude)? _coordinates;
    private DateTime _nextAttemptUtc = DateTime.MinValue;
    private int _busy;
    private volatile WeatherSnapshot? _current;

    public WeatherService(string? configuredLocation)
    {
        _configuredLocation = configuredLocation ?? "";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MyCanvas/1.2");
    }

    public WeatherSnapshot? Current => _current;

    public void Refresh()
    {
        if (DateTime.UtcNow < _nextAttemptUtc || Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                _coordinates ??= ParseLocation(_configuredLocation) ?? await LocateAsync();
                _current = await FetchAsync(_coordinates.Value.Latitude, _coordinates.Value.Longitude);
                _nextAttemptUtc = DateTime.UtcNow + RefreshInterval;
            }
            catch (Exception exception)
            {
                Log.Write("Weather refresh failed: " + exception.Message);
                _nextAttemptUtc = DateTime.UtcNow + RetryInterval;
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }

    internal static (double, double)? ParseLocation(string value)
    {
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude) &&
            Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180)
            return (latitude, longitude);
        return null;
    }

    private async Task<(double, double)> LocateAsync()
    {
        using JsonDocument document = JsonDocument.Parse(
            await _http.GetStringAsync("http://ip-api.com/json/?fields=status,lat,lon"));
        JsonElement root = document.RootElement;
        if (root.GetProperty("status").GetString() != "success")
            throw new InvalidOperationException("IP geolocation unavailable");
        return (root.GetProperty("lat").GetDouble(), root.GetProperty("lon").GetDouble());
    }

    private async Task<WeatherSnapshot> FetchAsync(double latitude, double longitude)
    {
        string url = FormattableString.Invariant(
            $"https://api.open-meteo.com/v1/forecast?latitude={latitude:0.###}&longitude={longitude:0.###}") +
            "&current=temperature_2m,relative_humidity_2m,weather_code" +
            "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";
        using JsonDocument document = JsonDocument.Parse(await _http.GetStringAsync(url));
        JsonElement current = document.RootElement.GetProperty("current");
        JsonElement daily = document.RootElement.GetProperty("daily");
        return new WeatherSnapshot
        {
            Temperature = current.GetProperty("temperature_2m").GetDouble(),
            Humidity = (int)Math.Round(current.GetProperty("relative_humidity_2m").GetDouble()),
            Code = current.GetProperty("weather_code").GetInt32(),
            High = daily.GetProperty("temperature_2m_max")[0].GetDouble(),
            Low = daily.GetProperty("temperature_2m_min")[0].GetDouble(),
            FetchedAt = DateTime.UtcNow,
        };
    }

    // WMO weather interpretation codes as used by Open-Meteo.
    public static string Describe(int code, bool english) => code switch
    {
        0 => english ? "Clear" : "晴",
        1 => english ? "Mostly clear" : "晴间多云",
        2 => english ? "Partly cloudy" : "多云",
        3 => english ? "Overcast" : "阴",
        45 or 48 => english ? "Fog" : "雾",
        >= 51 and <= 57 => english ? "Drizzle" : "毛毛雨",
        61 or 80 => english ? "Light rain" : "小雨",
        63 or 81 => english ? "Rain" : "中雨",
        65 or 82 => english ? "Heavy rain" : "大雨",
        66 or 67 => english ? "Freezing rain" : "冻雨",
        71 or 85 => english ? "Light snow" : "小雪",
        73 => english ? "Snow" : "中雪",
        75 or 86 => english ? "Heavy snow" : "大雪",
        77 => english ? "Snow grains" : "米雪",
        95 => english ? "Thunderstorm" : "雷阵雨",
        96 or 99 => english ? "Hail storm" : "雷阵雨伴冰雹",
        _ => english ? "Unknown" : "未知",
    };

    public void Dispose() => _http.Dispose();
}
