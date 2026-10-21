using ai_travel_planner.Modules.Weather.Domain;

namespace ai_travel_planner.Modules.Weather.Application;

public sealed class WeatherAssessmentService(IWeatherProvider provider, TimeProvider timeProvider) : IWeatherAssessmentService
{
    private const int MaximumTripDays = 14;
    private const int ForecastHorizonDays = 16;
    private const int HistoricalYears = 10;

    public async Task<WeatherAssessment> GetAsync(
        string destinationId, WeatherLocation location, DateOnly startDate, DateOnly endDate,
        CancellationToken cancellationToken)
    {
        Validate(destinationId, location, startDate, endDate);
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(location.Timezone));
        var today = DateOnly.FromDateTime(localNow.DateTime);
        // Open-Meteo's 16-day horizon includes today.
        var forecastEnd = today.AddDays(ForecastHorizonDays - 1);
        var forecastStart = startDate > today ? startDate : today;
        var forecastDays = startDate <= forecastEnd && endDate >= today;
        var historyDays = endDate > forecastEnd || startDate < today;

        IReadOnlyList<WeatherSample> forecast = [];
        if (forecastDays)
            forecast = await provider.GetForecastAsync(location, forecastStart, endDate < forecastEnd ? endDate : forecastEnd, cancellationToken);

        IReadOnlyList<WeatherSample> archive = [];
        var years = Enumerable.Range(today.Year - HistoricalYears, HistoricalYears).ToArray();
        if (historyDays)
        {
            var archiveStart = new DateOnly(years[0], 1, 1);
            var archiveEnd = new DateOnly(years[^1], 12, 31);
            archive = await provider.GetArchiveAsync(location, archiveStart, archiveEnd, cancellationToken);
        }

        var forecastByDate = forecast.ToDictionary(s => s.Date);
        var archiveByDate = archive.ToDictionary(s => s.Date);
        var warningSet = new HashSet<string>(StringComparer.Ordinal);
        var daily = new List<DailyWeather>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (forecastByDate.TryGetValue(date, out var sample))
            {
                daily.Add(new DailyWeather(date, WeatherBasis.Forecast, sample.TemperatureMaxC,
                    sample.TemperatureMinC, sample.PrecipitationMm, sample.WeatherCode, 1, 1, []));
                continue;
            }

            if (date >= today && date <= forecastEnd)
            {
                const string missingForecastWarning = "Forecast data was unavailable for this date; historical values were not substituted.";
                warningSet.Add(missingForecastWarning);
                daily.Add(new DailyWeather(date, WeatherBasis.Forecast, null, null, null, null, 0, 0,
                    [missingForecastWarning]));
                continue;
            }

            var historicalSamples = new List<WeatherSample>();
            var dayWarnings = new List<string>();
            foreach (var year in years)
            {
                if (date.Month == 2 && date.Day == 29 && !DateTime.IsLeapYear(year))
                {
                    var feb28 = archiveByDate.GetValueOrDefault(new DateOnly(year, 2, 28));
                    var mar1 = archiveByDate.GetValueOrDefault(new DateOnly(year, 3, 1));
                    if (feb28 is not null && mar1 is not null)
                        historicalSamples.Add(Interpolate(feb28, mar1));
                    continue;
                }

                var month = date.Month;
                var day = date.Day;
                if (month == 2 && day == 29 && !DateTime.IsLeapYear(year)) continue;
                var key = new DateOnly(year, month, day);
                if (archiveByDate.TryGetValue(key, out var yearSample)) historicalSamples.Add(yearSample);
            }

            if (date.Month == 2 && date.Day == 29)
                dayWarnings.Add("For non-leap years, February 29 values are the mean of February 28 and March 1.");
            if (historicalSamples.Count == 0)
                dayWarnings.Add("No matching historical daily samples were available.");
            foreach (var warning in dayWarnings) warningSet.Add(warning);
            daily.Add(new DailyWeather(date, WeatherBasis.Historical,
                Mean(historicalSamples.Select(s => s.TemperatureMaxC)),
                Mean(historicalSamples.Select(s => s.TemperatureMinC)),
                Mean(historicalSamples.Select(s => s.PrecipitationMm)),
                Mode(historicalSamples.Select(s => s.WeatherCode)), historicalSamples.Count,
                historicalSamples.Count, dayWarnings));
        }

        var periodGroups = new List<List<DailyWeather>>();
        foreach (var day in daily)
        {
            if (periodGroups.Count == 0 || periodGroups[^1][^1].Basis != day.Basis)
                periodGroups.Add([]);
            periodGroups[^1].Add(day);
        }
        var periods = periodGroups.Select(group => BuildPeriod(group)).ToArray();
        return new WeatherAssessment(destinationId, "Open-Meteo",
            "https://open-meteo.com/en/docs", "°C", "mm", daily, periods, warningSet.ToArray());
    }

    private static void Validate(string destinationId, WeatherLocation location, DateOnly start, DateOnly end)
    {
        if (string.IsNullOrWhiteSpace(destinationId) || destinationId.Length > 200)
            throw new WeatherRequestException("destinationId is required and must contain no more than 200 characters.");
        if (!double.IsFinite(location.Latitude) || location.Latitude is < -90 or > 90 ||
            !double.IsFinite(location.Longitude) || location.Longitude is < -180 or > 180)
            throw new WeatherRequestException("latitude must be between -90 and 90 and longitude between -180 and 180.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(location.Timezone); }
        catch (TimeZoneNotFoundException) { throw new WeatherRequestException("timezone must be a valid IANA time zone."); }
        catch (InvalidTimeZoneException) { throw new WeatherRequestException("timezone must be a valid IANA time zone."); }
        if (end < start) throw new WeatherRequestException("endDate must be on or after startDate.");
        if (end.DayNumber - start.DayNumber + 1 > MaximumTripDays)
            throw new WeatherRequestException($"The requested date range must be no longer than {MaximumTripDays} inclusive days.");
    }

    private static WeatherSample Interpolate(WeatherSample a, WeatherSample b) => new(a.Date,
        Average(a.TemperatureMaxC, b.TemperatureMaxC), Average(a.TemperatureMinC, b.TemperatureMinC),
        Average(a.PrecipitationMm, b.PrecipitationMm), a.WeatherCode ?? b.WeatherCode);

    private static double? Average(double? a, double? b) => a.HasValue && b.HasValue ? (a.Value + b.Value) / 2 : a ?? b;
    private static double? Mean(IEnumerable<double?> values)
    {
        var available = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return available.Length == 0 ? null : available.Average();
    }
    private static int? Mode(IEnumerable<int?> values) => values.Where(v => v.HasValue).GroupBy(v => v!.Value)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => (int?)g.Key).FirstOrDefault();

    private static PeriodWeather BuildPeriod(IReadOnlyList<DailyWeather> days)
    {
        var covered = days.Where(d => d.TemperatureMaxC.HasValue || d.TemperatureMinC.HasValue || d.PrecipitationMm.HasValue).ToArray();
        var providerEndpoint = days[0].Basis == WeatherBasis.Forecast
            ? "https://api.open-meteo.com/v1/forecast"
            : "https://archive-api.open-meteo.com/v1/archive";
        return new PeriodWeather(days[0].Date, days[^1].Date, days[0].Basis, providerEndpoint,
            Mean(days.Select(d => d.TemperatureMaxC)), Mean(days.Select(d => d.TemperatureMinC)),
            Mean(days.Select(d => d.PrecipitationMm)), days.All(d => d.PrecipitationMm.HasValue) ? days.Sum(d => d.PrecipitationMm!.Value) : null,
            days[0].Basis == WeatherBasis.Historical ? days.Min(d => d.SampleYears) : 1,
            covered.Length, days.Count);
    }
}
