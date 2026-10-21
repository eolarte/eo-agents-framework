using ai_travel_planner.Modules.Tourism.Application;
using ai_travel_planner.Modules.Tourism.Infrastructure;
using ai_travel_planner.Modules.Destinations.Application;

namespace ai_travel_planner.Modules.Tourism.Api;

public static class TourismModule
{
    public static IServiceCollection AddTourismModule(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<ITourismStatisticsProvider, EurostatTourismStatisticsProvider>(client =>
        {
            client.BaseAddress = new Uri("https://ec.europa.eu/eurostat/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient<INuts2RegionResolver, GiscoNuts2RegionResolver>(client =>
        {
            client.BaseAddress = new Uri("https://gisco-services.ec.europa.eu/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<TourismAssessmentService>();
        return services;
    }

    public static IEndpointRouteBuilder MapTourismModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tourism", async (
            string? destinationId, DateOnly? startDate, DateOnly? endDate,
            TourismAssessmentService service, IDestinationResolver destinationResolver,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(destinationId) || destinationId.Length > 100 ||
                startDate is null || endDate is null || endDate < startDate ||
                endDate.Value.DayNumber - startDate.Value.DayNumber > 13)
                return Results.BadRequest(new { error = "Provide destinationId and a valid trip of 1 to 14 days." });

            var destination = await destinationResolver.ResolveAsync(destinationId, cancellationToken);
            if (destination is null)
                return Results.NotFound(new { error = "Destination was not found." });

            return Results.Ok(await service.AssessAsync(
                destination, startDate.Value, endDate.Value, cancellationToken));
        });
        return endpoints;
    }
}
