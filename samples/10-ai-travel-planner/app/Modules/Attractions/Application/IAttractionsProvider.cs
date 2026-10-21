using ai_travel_planner.Modules.Attractions.Domain;

namespace ai_travel_planner.Modules.Attractions.Application;

public sealed record AttractionsQuery(
    string DestinationId,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> Interests);

public interface IAttractionsProvider
{
    Task<IReadOnlyList<Attraction>> SearchAsync(AttractionsQuery query, CancellationToken cancellationToken);
}

public sealed class AttractionsProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class AttractionsProviderConfigurationException(string message)
    : Exception(message);
