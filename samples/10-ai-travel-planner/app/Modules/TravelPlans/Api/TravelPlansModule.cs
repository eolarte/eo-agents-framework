using ai_travel_planner.Modules.TravelPlans.Application;
using ai_travel_planner.Modules.TravelPlans.Infrastructure;

namespace ai_travel_planner.Modules.TravelPlans.Api;

public static class TravelPlansModule
{
    public static IServiceCollection AddTravelPlansModule(this IServiceCollection services)
    {
        services.AddSingleton<TravelPlanJobStore>();
        services.AddSingleton<KnowledgeCorpus>(_ => new KnowledgeCorpus());
        services.AddHostedService<KnowledgeCorpusStartupValidation>();
        services.AddScoped<TravelPlanProcessor>();
        services.AddScoped<IWeatherResearchAgent, WeatherResearchAgent>();
        services.AddScoped<IAttractionsResearchAgent, AttractionsResearchAgent>();
        services.AddScoped<ISeasonalityResearchAgent, SeasonalityResearchAgent>();
        services.AddScoped<IKnowledgeResearchAgent, KnowledgeResearchAgent>();
        services.AddScoped<IAdvisorAgent, OpenAiAdvisorAgent>();
        services.AddHostedService<TravelPlanWorker>();
        return services;
    }

    public static IEndpointRouteBuilder MapTravelPlansModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapTravelPlanEndpoints();
        return endpoints;
    }
}
