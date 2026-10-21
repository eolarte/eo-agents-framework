using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ai_travel_planner.Modules.Weather.Application;
using ai_travel_planner.Modules.Weather.Domain;

namespace ai_travel_planner.Modules.Weather.Infrastructure;

public sealed class OpenMeteoWeatherProvider(HttpClient httpClient) : IWeatherProvider
{
    private const string DailyFields = "temperature_2m_max,temperature_2m_min,precipitation_sum,weather_code";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<IReadOnlyList<WeatherSample>> GetForecastAsync(
        WeatherLocation location, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        GetAsync("forecast", location, start, end, cancellationToken);

    public Task<IReadOnlyList<WeatherSample>> GetArchiveAsync(
        WeatherLocation location, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        GetAsync("archive", location, start, end, cancellationToken);

    private async Task<IReadOnlyList<WeatherSample>> GetAsync(
        string api, WeatherLocation location, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var queryParameters = new Dictionary<string, string>
        {
            ["latitude"] = location.Latitude.ToString(CultureInfo.InvariantCulture),
            ["longitude"] = location.Longitude.ToString(CultureInfo.InvariantCulture),
            ["daily"] = DailyFields,
            ["timezone"] = location.Timezone,
            ["start_date"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end_date"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        var query = string.Join('&', queryParameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        try
        {
            var requestUri = api == "archive"
                ? $"https://archive-api.open-meteo.com/v1/archive?{query}"
                : $"v1/{api}?{query}";
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new WeatherProviderException($"Open-Meteo {api} API returned HTTP {(int)response.StatusCode}.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, JsonOptions, cancellationToken);
            if (payload?.Daily?.Time is null || payload.Daily.Temperature2mMax is null ||
                payload.Daily.Temperature2mMin is null || payload.Daily.PrecipitationSum is null)
                throw new WeatherProviderException($"Open-Meteo {api} API returned incomplete daily data.");

            var count = payload.Daily.Time.Length;
            if (payload.Daily.Temperature2mMax.Length != count || payload.Daily.Temperature2mMin.Length != count ||
                payload.Daily.PrecipitationSum.Length != count ||
                payload.Daily.WeatherCode is { Length: var codeCount } && codeCount != count)
                throw new WeatherProviderException($"Open-Meteo {api} API returned inconsistent daily arrays.");

            return payload.Daily.Time.Select((date, index) => new WeatherSample(
                DateOnly.Parse(date, CultureInfo.InvariantCulture),
                payload.Daily.Temperature2mMax[index], payload.Daily.Temperature2mMin[index],
                payload.Daily.PrecipitationSum[index], payload.Daily.WeatherCode?[index])).ToArray();
        }
        catch (WeatherProviderException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException exception)
        {
            throw new WeatherProviderException($"Open-Meteo {api} weather request timed out.", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or FormatException)
        {
            throw new WeatherProviderException($"Open-Meteo {api} weather data could not be retrieved.", exception);
        }
    }

    private sealed class OpenMeteoResponse { public DailyData? Daily { get; init; } }
    private sealed class DailyData
    {
        [JsonPropertyName("time")]
        public string[]? Time { get; init; }
        [JsonPropertyName("temperature_2m_max")]
        public double?[]? Temperature2mMax { get; init; }
        [JsonPropertyName("temperature_2m_min")]
        public double?[]? Temperature2mMin { get; init; }
        [JsonPropertyName("precipitation_sum")]
        public double?[]? PrecipitationSum { get; init; }
        [JsonPropertyName("weather_code")]
        public int?[]? WeatherCode { get; init; }
    }
}
