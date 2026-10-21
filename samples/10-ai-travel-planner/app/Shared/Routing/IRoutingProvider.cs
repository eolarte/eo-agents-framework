namespace ai_travel_planner.Shared.Routing;

public sealed record Coordinates(double Latitude, double Longitude);

public enum TransportMode { Walking, Transit, Driving }

public sealed record TravelSegment(
    Coordinates Origin,
    Coordinates Destination,
    TransportMode Mode,
    double DistanceMeters,
    double DurationMinutes,
    string Source);

public interface IRoutingProvider
{
    Task<TravelSegment?> GetRouteAsync(
        Coordinates origin, Coordinates destination, TransportMode mode,
        CancellationToken cancellationToken);
}
