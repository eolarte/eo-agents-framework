using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ai_travel_planner.Modules.Destinations.Application;
using ai_travel_planner.Modules.Destinations.Domain;

namespace ai_travel_planner.Modules.Destinations.Infrastructure;

public sealed class OpenMeteoGeocodingClient(HttpClient httpClient) : IDestinationSearchService, IDestinationResolver
{
    private const string SearchPath = "v1/search";

    public async Task<Destination?> ResolveAsync(string destinationId, CancellationToken cancellationToken)
    {
        const string prefix = "open-meteo:";
        if (!destinationId.StartsWith(prefix, StringComparison.Ordinal) ||
            !long.TryParse(destinationId.AsSpan(prefix.Length), out var locationId) || locationId <= 0)
            return null;
        if (!string.Equals(destinationId, prefix + locationId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return null;

        OpenMeteoLocation? location;
        try
        {
            using var response = await httpClient.GetAsync($"v1/get?id={locationId}", cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                throw new DestinationProviderException(
                    $"Open-Meteo geocoding returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            location = await response.Content.ReadFromJsonAsync<OpenMeteoLocation>(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new DestinationProviderException("The Open-Meteo geocoding service timed out.", exception);
        }
        catch (DestinationProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new DestinationProviderException("Could not connect to the Open-Meteo geocoding service.", exception);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new DestinationProviderException("Open-Meteo returned an invalid geocoding response.", exception);
        }

        if (location?.Id != locationId || string.IsNullOrWhiteSpace(location.Name) ||
            location.Latitude is null || !double.IsFinite(location.Latitude.Value) || location.Latitude is < -90 or > 90 ||
            location.Longitude is null || !double.IsFinite(location.Longitude.Value) || location.Longitude is < -180 or > 180 ||
            string.IsNullOrWhiteSpace(location.Timezone))
            return null;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(location.Timezone); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }

        return new Destination(destinationId, location.Name, location.Country, location.Admin1,
            location.Latitude.Value, location.Longitude.Value, location.Timezone);
    }

    public async Task<IReadOnlyList<Destination>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var requestUri = $"{SearchPath}?name={Uri.EscapeDataString(query)}&count=10&language=en&format=json";
        OpenMeteoSearchResponse? response;

        try
        {
            using var httpResponse = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new DestinationProviderException(
                    $"Open-Meteo geocoding returned HTTP {(int)httpResponse.StatusCode} ({httpResponse.StatusCode}).");
            }

            response = await httpResponse.Content.ReadFromJsonAsync<OpenMeteoSearchResponse>(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new DestinationProviderException("The Open-Meteo geocoding service timed out.", exception);
        }
        catch (DestinationProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new DestinationProviderException("Could not connect to the Open-Meteo geocoding service.", exception);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new DestinationProviderException("Open-Meteo returned an invalid geocoding response.", exception);
        }

        if (response?.Results is null)
            return [];

        return response.Results
            .Where(result => result.Id is not null && !string.IsNullOrWhiteSpace(result.Name) &&
                result.Latitude is not null && result.Longitude is not null)
            .Select(result => new Destination(
                Id: $"open-meteo:{result.Id}",
                Name: result.Name!,
                Country: result.Country,
                Region: result.Admin1,
                Latitude: result.Latitude!.Value,
                Longitude: result.Longitude!.Value,
                Timezone: result.Timezone))
            .ToArray();
    }

    private sealed class OpenMeteoSearchResponse
    {
        [JsonPropertyName("results")]
        public List<OpenMeteoLocation>? Results { get; init; }
    }

    private sealed class OpenMeteoLocation
    {
        [JsonPropertyName("id")]
        public long? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("latitude")]
        public double? Latitude { get; init; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; init; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }

        [JsonPropertyName("admin1")]
        public string? Admin1 { get; init; }
    }
}
