using ai_travel_planner.Modules.Attractions.Domain;
using ai_travel_planner.Modules.Tourism.Domain;
using ai_travel_planner.Modules.Weather.Domain;

namespace ai_travel_planner.Modules.TravelPlans.Domain;

public sealed record CreateTravelPlanRequest(
    string DestinationId,
    DateOnly StartDate,
    DateOnly EndDate,
    TravelerPreferences? Preferences = null,
    string? Language = null);

public sealed record TravelerPreferences(string Pace, TimeOnly DailyStartTime, TimeOnly DailyEndTime,
    string TransportMode, string BudgetLevel, bool AvoidCrowds, IReadOnlyList<string> Interests);

public sealed record ResearchResult<T>(string Status, string Confidence, IReadOnlyList<string> SourceIds,
    DateTimeOffset RetrievedAt, IReadOnlyList<string> Warnings, T? Data);

public sealed record AdvisorResponse(string Summary, IReadOnlyList<TravelRecommendation> Recommendations);
public sealed record TravelRecommendation(string Title, string Description, IReadOnlyList<string> SourceIds);

public sealed record TravelPlanResult(string Id, string Status, string DestinationId, string DestinationName,
    double Latitude, double Longitude, string Timezone,
    DateOnly StartDate, DateOnly EndDate,
    ResearchResult<WeatherAssessment> Weather,
    ResearchResult<IReadOnlyList<Attraction>> Attractions,
    ResearchResult<TourismAssessment> Seasonality,
    ResearchResult<string> Knowledge,
    AdvisorResponse? Advisor,
    IReadOnlyList<string> Warnings);

public sealed record TravelPlanSnapshot(string Id, string Status, long EventId,
    IReadOnlyDictionary<string, string> AgentStatuses,
    string? Agent, string? AgentStatus,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, TravelPlanResult? Result, string? Error);

public sealed record TravelPlanAccepted(string PlanId, string Status, string StatusUrl, string EventsUrl);
