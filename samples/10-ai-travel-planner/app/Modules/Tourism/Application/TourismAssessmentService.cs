using ai_travel_planner.Modules.Tourism.Domain;
using ai_travel_planner.Modules.Destinations.Domain;
using System.Text.Json;

namespace ai_travel_planner.Modules.Tourism.Application;

public sealed class TourismAssessmentService(
    ITourismStatisticsProvider provider,
    INuts2RegionResolver regionResolver)
{
    public async Task<TourismAssessment> AssessAsync(
        Destination destination, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        var months = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(offset => startDate.AddDays(offset).Month)
            .Distinct()
            .ToArray();

        string? geography;
        try
        {
            geography = await GetRegionWithTransientRetryAsync(destination, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Unknown(months, null, "GISCO NUTS 2 region lookup is temporarily unavailable.");
        }
        catch (OperationCanceledException)
        {
            return Unknown(months, null, "GISCO NUTS 2 region lookup timed out.");
        }
        catch (JsonException)
        {
            return Unknown(months, null, "GISCO NUTS 2 region response could not be interpreted.");
        }

        if (geography is null || !IsNuts2Code(geography))
            return Unknown(months, null, "GISCO did not identify a NUTS 2 region for this destination.");

        TourismStatistics? statistics;
        try
        {
            statistics = await GetWithTransientRetryAsync(geography, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Unknown(months, geography, "Eurostat tourism data is temporarily unavailable.");
        }
        catch (OperationCanceledException)
        {
            return Unknown(months, geography, "Eurostat tourism data timed out.");
        }
        catch (JsonException)
        {
            return Unknown(months, geography, "Eurostat tourism data could not be interpreted.");
        }
        catch (InvalidOperationException)
        {
            return Unknown(months, geography, "Eurostat tourism data had an unexpected structure.");
        }
        catch (FormatException)
        {
            return Unknown(months, geography, "Eurostat tourism data had invalid values.");
        }

        if (statistics is null || statistics.NightsByMonth.Count != 12 ||
            months.Any(month => !statistics.NightsByMonth.ContainsKey(month)))
            return Unknown(months, geography, "A complete comparable year of monthly tourism data is unavailable.");

        var annualMean = statistics.NightsByMonth.Values.Average();
        if (annualMean <= 0)
            return Unknown(months, geography, "Tourism observations cannot support a monthly comparison.");

        var index = months.Average(month => statistics.NightsByMonth[month]) / annualMean;
        var level = index switch
        {
            < 0.75 => "low",
            <= 1.10 => "moderate",
            <= 1.40 => "high",
            _ => "peak"
        };
        return new TourismAssessment(
            level, "low", "completed", statistics.GeographyCode, "NUTS 2 region",
            statistics.ObservationYear, months, Math.Round(index, 3), statistics.SourceUrl,
            statistics.RetrievedAt.ToString("O"),
            ["Regional accommodation nights are a proxy for visitor pressure, not a city-level crowd forecast.",
             "Classification thresholds are proof-of-concept heuristics."]);
    }

    private static TourismAssessment Unknown(int[] months, string? geography, string warning) =>
        new("unknown", "unknown", "partial", geography,
            geography is null ? null : "NUTS 2 region", null, months, null, null, null, [warning]);

    private static bool IsNuts2Code(string code) =>
        code.Length == 4 && code.All(char.IsLetterOrDigit);

    private async Task<string?> GetRegionWithTransientRetryAsync(
        Destination destination, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await regionResolver.ResolveAsync(
                    destination.Latitude, destination.Longitude, cancellationToken);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
            catch (OperationCanceledException) when (attempt == 0 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
        }
    }

    private async Task<TourismStatistics?> GetWithTransientRetryAsync(string geography, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await provider.GetAsync(geography, cancellationToken); }
            catch (HttpRequestException) when (attempt == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
            catch (OperationCanceledException) when (attempt == 0 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
        }
    }
}
