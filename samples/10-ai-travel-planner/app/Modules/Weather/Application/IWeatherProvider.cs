using ai_travel_planner.Modules.Weather.Domain;

namespace ai_travel_planner.Modules.Weather.Application;

public sealed record WeatherLocation(double Latitude, double Longitude, string Timezone);

public interface IWeatherProvider
{
    Task<IReadOnlyList<WeatherSample>> GetForecastAsync(
        WeatherLocation location, DateOnly start, DateOnly end, CancellationToken cancellationToken);

    Task<IReadOnlyList<WeatherSample>> GetArchiveAsync(
        WeatherLocation location, DateOnly start, DateOnly end, CancellationToken cancellationToken);
}

public sealed class WeatherProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
