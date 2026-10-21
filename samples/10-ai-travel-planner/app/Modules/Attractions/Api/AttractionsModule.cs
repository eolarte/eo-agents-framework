using ai_travel_planner.Modules.Attractions.Application;
using ai_travel_planner.Modules.Attractions.Infrastructure;

namespace ai_travel_planner.Modules.Attractions.Api;

public static class AttractionsModule
{
    public static IServiceCollection AddAttractionsModule(this IServiceCollection services)
    {
        services.AddHttpClient<IAttractionsProvider, GooglePlacesAttractionsProvider>(client =>
        {
            client.BaseAddress = new Uri("https://places.googleapis.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        return services;
    }

    public static IEndpointRouteBuilder MapAttractionsModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAttractionsEndpoints();
        return endpoints;
    }
}
