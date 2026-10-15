using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ai_travel_planner.Modules.Destinations.Application;
using ai_travel_planner.Modules.Destinations.Domain;

namespace ai_travel_planner.Modules.Destinations.Infrastructure;

public sealed class OpenMeteoGeocodingClient(HttpClient httpClient) : IDestinationSearchService
{
    private const string SearchPath = "v1/search";

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
            .Where(result => result.Id is not null && !string.IsNullOrWhiteSpace(result.Name))
            .Select(result => new Destination(
                Id: $"open-meteo:{result.Id}",
                Name: result.Name!,
                Country: result.Country,
                Region: result.Admin1,
                Latitude: result.Latitude,
                Longitude: result.Longitude,
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
        public double Latitude { get; init; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; init; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }

        [JsonPropertyName("admin1")]
        public string? Admin1 { get; init; }
    }
}
