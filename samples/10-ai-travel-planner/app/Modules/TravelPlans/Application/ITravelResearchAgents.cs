using ai_travel_planner.Modules.Attractions.Domain;
using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.Tourism.Domain;
using ai_travel_planner.Modules.TravelPlans.Domain;
using ai_travel_planner.Modules.Weather.Domain;

namespace ai_travel_planner.Modules.TravelPlans.Application;

public interface IWeatherResearchAgent
{
    Task<ResearchResult<WeatherAssessment>> AssessAsync(Destination destination, CreateTravelPlanRequest request, CancellationToken cancellationToken);
}
public interface IAttractionsResearchAgent
{
    Task<ResearchResult<IReadOnlyList<Attraction>>> AssessAsync(Destination destination, CreateTravelPlanRequest request, CancellationToken cancellationToken);
}
public interface ISeasonalityResearchAgent
{
    Task<ResearchResult<TourismAssessment>> AssessAsync(Destination destination, CreateTravelPlanRequest request, CancellationToken cancellationToken);
}
public interface IKnowledgeResearchAgent
{
    Task<ResearchResult<string>> AssessAsync(Destination destination, CreateTravelPlanRequest request, CancellationToken cancellationToken);
}
public interface IAdvisorAgent
{
    Task<AdvisorResponse> RecommendAsync(Destination destination, CreateTravelPlanRequest request, TravelPlanResearch research, CancellationToken cancellationToken);
}

public sealed record TravelPlanResearch(
    ResearchResult<WeatherAssessment> Weather,
    ResearchResult<IReadOnlyList<Attraction>> Attractions,
    ResearchResult<TourismAssessment> Seasonality,
    ResearchResult<string> Knowledge,
    IReadOnlyList<string> Warnings);
