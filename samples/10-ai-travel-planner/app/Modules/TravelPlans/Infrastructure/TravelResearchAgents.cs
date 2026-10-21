using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using ai_travel_planner.Modules.Attractions.Application;
using ai_travel_planner.Modules.Attractions.Domain;
using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.Tourism.Application;
using ai_travel_planner.Modules.Tourism.Domain;
using ai_travel_planner.Modules.TravelPlans.Application;
using ai_travel_planner.Modules.TravelPlans.Domain;
using ai_travel_planner.Modules.Weather.Application;
using ai_travel_planner.Modules.Weather.Domain;

namespace ai_travel_planner.Modules.TravelPlans.Infrastructure;

public sealed class WeatherResearchAgent(IWeatherAssessmentService service, TimeProvider clock) : IWeatherResearchAgent
{
    public async Task<ResearchResult<WeatherAssessment>> AssessAsync(Destination destination, CreateTravelPlanRequest request, CancellationToken cancellationToken)
    {
        var result = await ResearchCall.RunAsync(ct => service.GetAsync(destination.Id,
            new WeatherLocation(destination.Latitude, destination.Longitude, destination.Timezone!), request.StartDate, request.EndDate, ct), cancellationToken);
        var coveredDays = result.Days.Count(day => day.CoverageSamples > 0);
        var warnings = result.Warnings;
        if (coveredDays < result.Days.Count)
            warnings = warnings.Append("Weather coverage is incomplete for the requested dates.").ToArray();
        return new(coveredDays == result.Days.Count ? "completed" : "partial",
            coveredDays == 0 ? "unknown" : "medium", [result.SourceUrl], clock.GetUtcNow(), warnings, result);
    }
}

public sealed class AttractionsResearchAgent(IAttractionsProvider provider, TimeProvider clock) : IAttractionsResearchAgent
{
    public async Task<ResearchResult<IReadOnlyList<Attraction>>> AssessAsync(Destination destination, CreateTravelPlanRequest request, CancellationToken cancellationToken)
    {
        var result = await ResearchCall.RunAsync(ct => provider.SearchAsync(new AttractionsQuery(destination.Id,
            destination.Latitude, destination.Longitude, request.Preferences?.Interests ?? []), ct), cancellationToken);
        var noResults = result.Count == 0;
        return new(noResults ? "partial" : "completed", noResults ? "unknown" : "medium",
            result.Select(a => $"places:{a.Source.ProviderId}").ToArray(), clock.GetUtcNow(),
            noResults ? ["No matching attractions were found for this destination and interest set."] : [], result);
    }
}

public sealed class SeasonalityResearchAgent(TourismAssessmentService service, TimeProvider clock) : ISeasonalityResearchAgent
{
    public async Task<ResearchResult<TourismAssessment>> AssessAsync(Destination destination, CreateTravelPlanRequest request, CancellationToken cancellationToken)
    {
        var result = await ResearchCall.RunAsync(ct => service.AssessAsync(destination, request.StartDate, request.EndDate, ct), cancellationToken);
        return new(result.Status == "partial" ? "partial" : "completed", result.Confidence,
            result.SourceUrl is null ? [] : [result.SourceUrl], clock.GetUtcNow(), result.Warnings, result);
    }
}

public sealed class KnowledgeResearchAgent(KnowledgeCorpus corpus, TimeProvider clock) : IKnowledgeResearchAgent
{
    public Task<ResearchResult<string>> AssessAsync(Destination destination, CreateTravelPlanRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(corpus.Search(destination, request, clock.GetUtcNow()));
    }
}

public sealed class OpenAiAdvisorAgent(IConfiguration configuration) : IAdvisorAgent
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(20);

    public async Task<AdvisorResponse> RecommendAsync(Destination destination, CreateTravelPlanRequest request,
        TravelPlanResearch research, CancellationToken cancellationToken)
    {
        var apiKey = configuration["AZURE_OPENAI_API_KEY"];
        var deployment = configuration["AZURE_OPENAI_DEPLOYMENT_NAME"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("AZURE_OPENAI_API_KEY and AZURE_OPENAI_DEPLOYMENT_NAME are required.");
        var client = new OpenAIClient(apiKey).GetChatClient(deployment).AsIChatClient();
        using var promptStream = typeof(OpenAiAdvisorAgent).Assembly
            .GetManifestResourceStream("ai_travel_planner.Modules.TravelPlans.AdvisorPromptV1.txt")
            ?? throw new InvalidOperationException("The Advisor v1 prompt resource is missing.");
        using var promptReader = new StreamReader(promptStream);
        var instructions = await promptReader.ReadToEndAsync(cancellationToken);
        var agent = client.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "TravelAdvisor",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions
            }
        });
        var evidence = JsonSerializer.Serialize(new
        {
            weather = new { research.Weather.Status, research.Weather.Confidence, research.Weather.SourceIds,
                warnings = research.Weather.Warnings.Take(8), periods = research.Weather.Data?.Periods.Take(14) },
            attractions = new { research.Attractions.Status, research.Attractions.Confidence, research.Attractions.SourceIds,
                warnings = research.Attractions.Warnings.Take(8), items = research.Attractions.Data?.Take(20)
                    .Select(a => new { a.Id, a.Name, a.Categories, a.Score, a.Source }) },
            seasonality = new { research.Seasonality.Status, research.Seasonality.Confidence, research.Seasonality.SourceIds,
                warnings = research.Seasonality.Warnings.Take(8), research.Seasonality.Data?.Level,
                research.Seasonality.Data?.ObservationYear, research.Seasonality.Data?.MonthlyIndex },
            knowledge = new { research.Knowledge.Status, research.Knowledge.Confidence, research.Knowledge.SourceIds,
                warnings = research.Knowledge.Warnings.Take(8), excerpt = research.Knowledge.Data is { Length: > 1200 } text ? text[..1200] : research.Knowledge.Data }
        });
        for (var attempt = 0; ; attempt++)
        {
            using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            call.CancelAfter(CallTimeout);
            try
            {
                var result = await agent.RunAsync<AdvisorResponse>(
                    $"Create recommendations for {destination.Name}, {request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}. " +
                    $"Travel preferences: {JsonSerializer.Serialize(request)}. Research JSON: {evidence}", cancellationToken: call.Token);
                return result.Result;
            }
            catch (Exception exception) when (attempt == 0 && !cancellationToken.IsCancellationRequested &&
                ResearchCall.IsTransient(exception, call.IsCancellationRequested)) { }
        }
    }
}

internal static class ResearchCall
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    public static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            call.CancelAfter(Timeout);
            try { return await operation(call.Token); }
            catch (Exception exception) when (attempt == 0 && !cancellationToken.IsCancellationRequested &&
                IsTransient(exception, call.IsCancellationRequested)) { }
        }
    }

    public static bool IsTransient(Exception exception, bool callTimedOut)
    {
        if (exception is HttpRequestException or TimeoutException || callTimedOut && exception is OperationCanceledException)
            return true;
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is HttpRequestException or TimeoutException || inner is OperationCanceledException)
                return true;
        return false;
    }
}
