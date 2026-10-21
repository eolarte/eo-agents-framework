using System.Globalization;
using System.Text.Json;
using ai_travel_planner.Modules.Tourism.Application;
using ai_travel_planner.Modules.Tourism.Domain;

namespace ai_travel_planner.Modules.Tourism.Infrastructure;

public sealed class EurostatTourismStatisticsProvider(HttpClient httpClient, TimeProvider clock)
    : ITourismStatisticsProvider
{
    public async Task<TourismStatistics?> GetAsync(string geographyCode, CancellationToken cancellationToken)
    {
        var earliestYear = clock.GetUtcNow().Year - 4;
        var path = "api/dissemination/statistics/1.0/data/tour_occ_nin2m" +
            $"?lang=en&freq=A&nace_r2=I551-I553&c_resid=TOTAL&unit=NR&geo={Uri.EscapeDataString(geographyCode)}" +
            $"&sinceTimePeriod={earliestYear}";
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var idsElement) ||
            !root.TryGetProperty("size", out var sizesElement) ||
            !root.TryGetProperty("dimension", out var dimensions) ||
            !root.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Object)
            return null;

        var ids = idsElement.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
        var sizes = sizesElement.EnumerateArray().Select(item => item.GetInt32()).ToArray();
        if (ids.Length != sizes.Length || sizes.Any(size => size <= 0)) return null;

        var yearAxis = Array.FindIndex(ids, id => id.Equals("time", StringComparison.OrdinalIgnoreCase));
        var monthAxis = Array.FindIndex(ids, id => id.Equals("month", StringComparison.OrdinalIgnoreCase));
        if (yearAxis < 0 || monthAxis < 0) return null;
        if (sizes.Where((_, index) => index != yearAxis && index != monthAxis).Any(size => size != 1))
            return null;

        var years = Categories(dimensions, ids[yearAxis], sizes[yearAxis]);
        var months = Categories(dimensions, ids[monthAxis], sizes[monthAxis]);
        if (years is null || months is null) return null;

        var byYear = new Dictionary<int, Dictionary<int, double>>();
        foreach (var observation in values.EnumerateObject())
        {
            if (!int.TryParse(observation.Name, out var linearIndex) ||
                observation.Value.ValueKind != JsonValueKind.Number ||
                !observation.Value.TryGetDouble(out var nights) || nights < 0)
                continue;

            var yearPosition = Coordinate(linearIndex, sizes, yearAxis);
            var monthPosition = Coordinate(linearIndex, sizes, monthAxis);
            if (!int.TryParse(years[yearPosition], out var year) ||
                !TryParseMonth(months[monthPosition], out var month)) continue;

            if (!byYear.TryGetValue(year, out var observations))
                byYear[year] = observations = [];
            observations[month] = nights;
        }

        var latest = byYear.Where(pair => pair.Key < clock.GetUtcNow().Year && pair.Value.Count == 12)
            .OrderByDescending(pair => pair.Key).FirstOrDefault();
        if (latest.Value is null) return null;

        return new TourismStatistics(geographyCode, latest.Key, latest.Value,
            new Uri(httpClient.BaseAddress!, path).ToString(), clock.GetUtcNow());
    }

    private static int Coordinate(int linearIndex, int[] sizes, int axis)
    {
        var stride = 1;
        for (var next = axis + 1; next < sizes.Length; next++) stride *= sizes[next];
        return linearIndex / stride % sizes[axis];
    }

    private static string[]? Categories(JsonElement dimensions, string id, int size)
    {
        if (!dimensions.TryGetProperty(id, out var dimension) ||
            !dimension.TryGetProperty("category", out var category) ||
            !category.TryGetProperty("index", out var index) || index.ValueKind != JsonValueKind.Object)
            return null;

        var result = new string[size];
        foreach (var item in index.EnumerateObject())
        {
            var position = item.Value.GetInt32();
            if (position < 0 || position >= size) return null;
            result[position] = item.Name;
        }
        return result.Any(string.IsNullOrEmpty) ? null : result;
    }

    private static bool TryParseMonth(string code, out int month)
    {
        month = 0;
        return code.Length == 3 && code[0] == 'M' &&
            int.TryParse(code.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out month) &&
            month is >= 1 and <= 12;
    }
}
