namespace ai_travel_planner.Modules.Destinations.Domain;

public sealed record Destination(
    string Id,
    string Name,
    string? Country,
    string? Region,
    double Latitude,
    double Longitude,
    string? Timezone);
