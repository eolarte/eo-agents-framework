using ai_travel_planner.Shared.Routing;

namespace ai_travel_planner.Tests.Fixtures;

public sealed class MockRoutingProvider : IRoutingProvider
{
    private readonly Dictionary<(Coordinates Origin, Coordinates Destination, TransportMode Mode), TravelSegment> _routes = [];

    public MockRoutingProvider Add(TravelSegment route)
    {
        _routes[(route.Origin, route.Destination, route.Mode)] = route;
        return this;
    }

    public Task<TravelSegment?> GetRouteAsync(
        Coordinates origin, Coordinates destination, TransportMode mode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _routes.TryGetValue((origin, destination, mode), out var route);
        return Task.FromResult(route);
    }
}
