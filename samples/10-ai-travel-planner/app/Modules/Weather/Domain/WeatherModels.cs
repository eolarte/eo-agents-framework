namespace ai_travel_planner.Modules.Weather.Domain;

public enum WeatherBasis { Forecast, Historical }

public sealed record WeatherSample(
    DateOnly Date,
    double? TemperatureMaxC,
    double? TemperatureMinC,
    double? PrecipitationMm,
    int? WeatherCode);

public sealed record DailyWeather(
    DateOnly Date,
    WeatherBasis Basis,
    double? TemperatureMaxC,
    double? TemperatureMinC,
    double? PrecipitationMm,
    int? WeatherCode,
    int SampleYears,
    int CoverageSamples,
    IReadOnlyList<string> Warnings);

public sealed record PeriodWeather(
    DateOnly StartDate,
    DateOnly EndDate,
    WeatherBasis Basis,
    string ProviderEndpoint,
    double? AverageMaxTemperatureC,
    double? AverageMinTemperatureC,
    double? AverageDailyPrecipitationMm,
    double? TotalPrecipitationMm,
    int SampleYears,
    int CoverageDays,
    int TotalDays);

public sealed record WeatherAssessment(
    string DestinationId,
    string Provider,
    string SourceUrl,
    string TemperatureUnit,
    string PrecipitationUnit,
    IReadOnlyList<DailyWeather> Days,
    IReadOnlyList<PeriodWeather> Periods,
    IReadOnlyList<string> Warnings);
