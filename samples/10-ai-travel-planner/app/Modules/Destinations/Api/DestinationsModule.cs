using ai_travel_planner.Modules.Destinations.Application;
using ai_travel_planner.Modules.Destinations.Infrastructure;

namespace ai_travel_planner.Modules.Destinations.Api;

public static class DestinationsModule
{
    public static IServiceCollection AddDestinationsModule(this IServiceCollection services)
    {
        services.AddHttpClient<IDestinationSearchService, OpenMeteoGeocodingClient>(client =>
        {
            client.BaseAddress = new Uri("https://geocoding-api.open-meteo.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }

    public static IEndpointRouteBuilder MapDestinationsModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDestinationEndpoints();
        return endpoints;
    }
}
