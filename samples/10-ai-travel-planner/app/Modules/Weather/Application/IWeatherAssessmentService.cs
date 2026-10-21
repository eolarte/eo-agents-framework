using ai_travel_planner.Modules.Weather.Domain;

namespace ai_travel_planner.Modules.Weather.Application;

public interface IWeatherAssessmentService
{
    Task<WeatherAssessment> GetAsync(
        string destinationId,
        WeatherLocation location,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken);
}

public sealed class WeatherRequestException(string message) : Exception(message);
