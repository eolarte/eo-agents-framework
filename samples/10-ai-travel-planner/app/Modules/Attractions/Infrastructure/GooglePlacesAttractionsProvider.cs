using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ai_travel_planner.Modules.Attractions.Application;
using ai_travel_planner.Modules.Attractions.Domain;

namespace ai_travel_planner.Modules.Attractions.Infrastructure;

public sealed class GooglePlacesAttractionsProvider(HttpClient httpClient, IConfiguration configuration) : IAttractionsProvider
{
    private const string FieldMask = "places.id,places.displayName,places.location,places.types,places.primaryType";
    private const double SearchRadiusMeters = 5000;
    private const int MaximumResultCount = 20;
    private static readonly string[] DefaultVisitorTypes =
    [
        "tourist_attraction", "historical_landmark", "historical_place", "cultural_landmark",
        "museum", "art_gallery", "monument", "sculpture", "plaza", "park"
    ];
    private static readonly IReadOnlyDictionary<string, string[]> InterestTypes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["museum"] = ["museum", "art_museum", "history_museum"], ["museums"] = ["museum", "art_museum", "history_museum"],
        ["art"] = ["art_gallery", "art_museum", "museum", "sculpture"],
        ["history"] = ["historical_landmark", "historical_place", "history_museum", "monument"],
        ["architecture"] = ["historical_landmark", "historical_place", "cultural_landmark", "castle", "monument"],
        ["culture"] = ["museum", "art_gallery", "cultural_landmark", "performing_arts_theater", "cultural_center"],
        ["nature"] = ["park", "national_park", "botanical_garden", "garden", "wildlife_park"],
        ["outdoors"] = ["park", "national_park", "hiking_area", "beach"],
        ["food"] = ["restaurant", "cafe", "bakery", "market", "farmers_market"],
        ["shopping"] = ["shopping_mall", "market", "farmers_market", "book_store", "gift_shop"],
        ["landmarks"] = ["tourist_attraction", "historical_landmark", "historical_place", "cultural_landmark", "monument", "plaza"],
        ["religion"] = ["church", "mosque", "synagogue", "hindu_temple"],
        ["music"] = ["performing_arts_theater", "concert_hall", "opera_house", "live_music_venue"],
        ["family"] = ["zoo", "aquarium", "amusement_park", "botanical_garden", "park", "playground"]
    };

    public async Task<IReadOnlyList<Attraction>> SearchAsync(AttractionsQuery query, CancellationToken cancellationToken)
    {
        var apiKey = configuration["GOOGLE_PLACES_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AttractionsProviderConfigurationException("Set GOOGLE_PLACES_API_KEY to enable Google Places attractions.");

        var types = GetWantedTypes(query);
        var payload = new NearbySearchRequest(
            types,
            MaximumResultCount,
            new LocationRestriction(new SearchCircle(new PlaceLocation(query.Latitude, query.Longitude), SearchRadiusMeters)),
            "DISTANCE");

        using var request = new HttpRequestMessage(HttpMethod.Post, "./places:searchNearby")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Goog-Api-Key", apiKey);
        request.Headers.Add("X-Goog-FieldMask", FieldMask);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new AttractionsProviderException($"Google Places returned HTTP {(int)response.StatusCode}.");
            var result = await response.Content.ReadFromJsonAsync<NearbySearchResponse>(cancellationToken: cancellationToken)
                ?? throw new AttractionsProviderException("Google Places returned an empty response.");
            return NormalizeAndRank(result.Places ?? [], query);
        }
        catch (AttractionsProviderException) { throw; }
        catch (HttpRequestException exception)
        {
            throw new AttractionsProviderException("Google Places could not be reached.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AttractionsProviderException("Google Places request timed out.", exception);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new AttractionsProviderException("Google Places returned an invalid response.", exception);
        }
    }

    private static IReadOnlyList<Attraction> NormalizeAndRank(IEnumerable<PlaceResult> places, AttractionsQuery query)
    {
        var wantedTypes = GetWantedTypes(query).ToHashSet(StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<(PlaceResult Place, string Name, string[] Categories, double Distance)>();
        foreach (var place in places)
        {
            var id = place.Id?.Trim();
            var name = place.DisplayName?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || place.Location is null ||
                !double.IsFinite(place.Location.Latitude) || !double.IsFinite(place.Location.Longitude) ||
                place.Location.Latitude is < -90 or > 90 || place.Location.Longitude is < -180 or > 180)
                continue;

            var categories = (place.Types ?? []).Append(place.PrimaryType).Where(type => !string.IsNullOrWhiteSpace(type))
                .Select(type => type!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (!categories.Any(wantedTypes.Contains))
                continue;
            if (!seenIds.Add(id))
                continue;
            var distance = DistanceMeters(query.Latitude, query.Longitude, place.Location.Latitude, place.Location.Longitude);
            if (accepted.Any(existing => NormalizeName(existing.Name) == NormalizeName(name) &&
                DistanceMeters(existing.Place.Location!.Latitude, existing.Place.Location.Longitude, place.Location.Latitude, place.Location.Longitude) <= 35))
                continue;
            accepted.Add((place, name, categories, distance));
        }

        return accepted.Select(item =>
        {
            double? interestMatch = HasRecognizedInterest(query)
                ? item.Categories.Any(wantedTypes.Contains) ? 1d : 0d
                : null;
            var proximity = Math.Clamp(1 - item.Distance / SearchRadiusMeters, 0, 1);
            var score = interestMatch is null ? proximity : 0.7 * interestMatch.Value + 0.3 * proximity;
            return new Attraction(
                $"google-places:{item.Place.Id}", item.Name, item.Place.Location!.Latitude, item.Place.Location.Longitude,
                item.Categories, Math.Round(score, 4),
                new AttractionScoreFactors(interestMatch, Math.Round(proximity, 4), ["place types", "coordinates"]),
                new AttractionSource("Google Places API (New)", item.Place.Id!));
        }).OrderByDescending(attraction => attraction.Score).ThenBy(attraction => attraction.Name, StringComparer.Ordinal)
            .ThenBy(attraction => attraction.Id, StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeName(string name) => string.Concat(name.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static string[] GetWantedTypes(AttractionsQuery query)
    {
        var mappedTypes = query.Interests
            .Select(interest => interest.Trim())
            .Where(interest => InterestTypes.ContainsKey(interest))
            .SelectMany(interest => InterestTypes[interest])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return (mappedTypes.Length == 0 ? DefaultVisitorTypes : mappedTypes).Take(50).ToArray();
    }

    private static bool HasRecognizedInterest(AttractionsQuery query) =>
        query.Interests.Any(interest => InterestTypes.ContainsKey(interest.Trim()));

    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6_371_000;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private sealed record NearbySearchRequest(string[]? IncludedTypes, int MaxResultCount, LocationRestriction LocationRestriction, string RankPreference);
    private sealed record LocationRestriction(SearchCircle Circle);
    private sealed record SearchCircle(PlaceLocation Center, double Radius);
    private sealed record PlaceLocation(double Latitude, double Longitude);
    private sealed record NearbySearchResponse([property: JsonPropertyName("places")] List<PlaceResult>? Places);
    private sealed record PlaceResult(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("displayName")] PlaceDisplayName? DisplayName,
        [property: JsonPropertyName("location")] PlaceLocation? Location,
        [property: JsonPropertyName("types")] string[]? Types,
        [property: JsonPropertyName("primaryType")] string? PrimaryType);
    private sealed record PlaceDisplayName([property: JsonPropertyName("text")] string? Text);
}
