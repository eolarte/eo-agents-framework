using ai_travel_planner.Modules.Destinations.Domain;

namespace ai_travel_planner.Modules.Destinations.Application;

public interface IDestinationResolver
{
    Task<Destination?> ResolveAsync(string destinationId, CancellationToken cancellationToken);
}
