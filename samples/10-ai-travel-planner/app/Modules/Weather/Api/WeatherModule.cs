using ai_travel_planner.Modules.Weather.Application;
using ai_travel_planner.Modules.Weather.Infrastructure;

namespace ai_travel_planner.Modules.Weather.Api;

public static class WeatherModule
{
    public static IServiceCollection AddWeatherModule(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IWeatherAssessmentService, WeatherAssessmentService>();
        services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.open-meteo.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        return services;
    }

    public static IEndpointRouteBuilder MapWeatherModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapWeatherEndpoints();
        return endpoints;
    }
}
