using ai_travel_planner.Modules.Tourism.Domain;

namespace ai_travel_planner.Modules.Tourism.Application;

public interface ITourismStatisticsProvider
{
    Task<TourismStatistics?> GetAsync(string geographyCode, CancellationToken cancellationToken);
}
