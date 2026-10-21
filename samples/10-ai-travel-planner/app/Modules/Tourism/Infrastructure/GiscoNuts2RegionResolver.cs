using System.Text.Json;
using ai_travel_planner.Modules.Tourism.Application;

namespace ai_travel_planner.Modules.Tourism.Infrastructure;

public sealed class GiscoNuts2RegionResolver(HttpClient httpClient) : INuts2RegionResolver
{
    public async Task<string?> ResolveAsync(
        double latitude, double longitude, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90 ||
            !double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Coordinates must be valid latitude and longitude values.");

        var path = "id/nuts/?" +
            $"x={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&y={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            "&year=2024&level=2&proj=4326&format=json&geometry=no";

        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError &&
            await IsGiscoNoCoverageAsync(response, cancellationToken))
            return null;
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("attributes", out var attributes) ||
                attributes.ValueKind != JsonValueKind.Object ||
                !attributes.TryGetProperty("stat_levl_code", out var level) ||
                !level.TryGetInt32(out var statLevel) || statLevel != 2 ||
                !attributes.TryGetProperty("nuts_id", out var id) ||
                id.ValueKind != JsonValueKind.String)
                continue;

            var code = id.GetString();
            if (code is { Length: 4 } && code.All(character =>
                    character is >= 'A' and <= 'Z' or >= '0' and <= '9'))
                return code;
        }

        return null;
    }

    private static async Task<bool> IsGiscoNoCoverageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("details", out var details) &&
                details.ValueKind == JsonValueKind.String &&
                details.GetString()?.Contains("Cannot read properties of null", StringComparison.Ordinal) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
