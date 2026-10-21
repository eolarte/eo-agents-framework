using ai_travel_planner.Modules.Attractions.Application;
using ai_travel_planner.Modules.Attractions.Domain;
using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.TravelPlans.Domain;
using ai_travel_planner.Modules.TravelPlans.Infrastructure;
using ai_travel_planner.Modules.Weather.Application;
using ai_travel_planner.Modules.Weather.Domain;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class TravelResearchAgentTests
{
    private static readonly Destination Destination = new("open-meteo:2988507", "Paris", "France", null,
        48.8566, 2.3522, "Europe/Paris");
    private static readonly CreateTravelPlanRequest Request = new("open-meteo:2988507",
        new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 3),
        new TravelerPreferences("relaxed", new TimeOnly(9, 0), new TimeOnly(19, 0),
            "walking", "medium", true, ["history"]));

    [Fact]
    public async Task Weather_retries_provider_wrapped_timeout_once()
    {
        var service = new FlakyWeatherService();
        var agent = new WeatherResearchAgent(service, TimeProvider.System);

        var result = await agent.AssessAsync(Destination, Request, CancellationToken.None);

        Assert.Equal(2, service.Calls);
        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public async Task Attractions_retries_provider_wrapped_timeout_once()
    {
        var provider = new FlakyAttractionsProvider();
        var agent = new AttractionsResearchAgent(provider, TimeProvider.System);

        var result = await agent.AssessAsync(Destination, Request, CancellationToken.None);

        Assert.Equal(2, provider.Calls);
        Assert.Equal("completed", result.Status);
    }

    private sealed class FlakyWeatherService : IWeatherAssessmentService
    {
        public int Calls { get; private set; }
        public Task<WeatherAssessment> GetAsync(string destinationId, WeatherLocation location,
            DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1) throw new WeatherProviderException("Provider timeout.", new OperationCanceledException());
            return Task.FromResult(new WeatherAssessment(destinationId, "Open-Meteo", "weather-1", "°C", "mm",
                [new DailyWeather(startDate, WeatherBasis.Historical, 25, 15, 0, null, 10, 10, [])], [], []));
        }
    }

    private sealed class FlakyAttractionsProvider : IAttractionsProvider
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<Attraction>> SearchAsync(AttractionsQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1) throw new AttractionsProviderException("Provider timeout.", new OperationCanceledException());
            IReadOnlyList<Attraction> attractions =
                [new Attraction("place-1", "Museum", 48.85, 2.35, ["museum"], 1,
                    new AttractionScoreFactors(1, 1, []), new AttractionSource("Google Places", "place-1"))];
            return Task.FromResult(attractions);
        }
    }
}
