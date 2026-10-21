namespace ai_travel_planner.Modules.Attractions.Domain;

public sealed record Attraction(
    string Id,
    string Name,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> Categories,
    double Score,
    AttractionScoreFactors ScoreFactors,
    AttractionSource Source);

public sealed record AttractionScoreFactors(
    double? InterestMatch,
    double DistanceProximity,
    string[] EvidenceAvailable);

public sealed record AttractionSource(string Provider, string ProviderId);
