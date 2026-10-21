namespace ai_travel_planner.Modules.Tourism.Application;

public interface INuts2RegionResolver
{
    Task<string?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
