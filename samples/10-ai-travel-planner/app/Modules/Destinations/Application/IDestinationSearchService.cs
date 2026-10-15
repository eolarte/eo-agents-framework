using ai_travel_planner.Modules.Destinations.Domain;

namespace ai_travel_planner.Modules.Destinations.Application;

public interface IDestinationSearchService
{
    Task<IReadOnlyList<Destination>> SearchAsync(string query, CancellationToken cancellationToken);
}

public sealed class DestinationProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
