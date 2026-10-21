using ai_travel_planner.Shared.Routing;
using ai_travel_planner.Tests.Fixtures;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class RoutingMockTests
{
    [Fact]
    public async Task Missing_route_remains_unknown_and_configured_route_is_deterministic()
    {
        var origin = new Coordinates(48.8566, 2.3522);
        var destination = new Coordinates(48.8606, 2.3376);
        var route = new TravelSegment(origin, destination, TransportMode.Walking, 1300, 17, "test fixture");
        var provider = new MockRoutingProvider();

        Assert.Null(await provider.GetRouteAsync(origin, destination, TransportMode.Walking, CancellationToken.None));
        provider.Add(route);
        Assert.Equal(route, await provider.GetRouteAsync(origin, destination, TransportMode.Walking, CancellationToken.None));
        Assert.Null(await provider.GetRouteAsync(origin, destination, TransportMode.Driving, CancellationToken.None));
    }
}
