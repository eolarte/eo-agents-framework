namespace ai_travel_planner.Modules.Tourism.Domain;

public sealed record TourismAssessment(
    string Level,
    string Confidence,
    string Status,
    string? GeographyCode,
    string? GeographyLevel,
    int? ObservationYear,
    IReadOnlyList<int> RequestedMonths,
    double? MonthlyIndex,
    string? SourceUrl,
    string? RetrievedAtUtc,
    IReadOnlyList<string> Warnings);

public sealed record TourismStatistics(
    string GeographyCode,
    int ObservationYear,
    IReadOnlyDictionary<int, double> NightsByMonth,
    string SourceUrl,
    DateTimeOffset RetrievedAt);
