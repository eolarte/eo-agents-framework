using ai_travel_planner.Modules.Attractions.Domain;
using ai_travel_planner.Modules.Attractions.Application;
using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.Tourism.Domain;
using ai_travel_planner.Modules.TravelPlans.Application;
using ai_travel_planner.Modules.TravelPlans.Domain;
using ai_travel_planner.Modules.Weather.Domain;
using System.Collections.Concurrent;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class TravelPlanProcessorTests
{
    private static readonly Destination Destination = new("open-meteo:2988507", "Paris", "France", null,
        48.8566, 2.3522, "Europe/Paris");
    private static readonly CreateTravelPlanRequest Request = new("open-meteo:2988507",
        new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 3),
        new TravelerPreferences("relaxed", new TimeOnly(9, 0), new TimeOnly(19, 0),
            "walking", "medium", true, ["history"]));
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Four_research_streams_start_before_any_completes_and_join_into_partial_result()
    {
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new ConcurrentBag<(string Agent, string Status)>();
        async Task<ResearchResult<T>> Run<T>(ResearchResult<T> result, CancellationToken token)
        {
            if (Interlocked.Increment(ref started) == 4) allStarted.TrySetResult();
            await release.Task.WaitAsync(token);
            return result;
        }
        var processor = NewProcessor(
            (d, r, ct) => Run(WeatherResult(), ct),
            (d, r, ct) => Run(AttractionsResult(), ct),
            (d, r, ct) => Run(SeasonalityResult(), ct),
            (d, r, ct) => Run(KnowledgeResult(), ct));

        var processing = processor.ProcessAsync("plan-test", Request, Destination,
            (agent, state) => progress.Add((agent, state)), CancellationToken.None);
        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(processing.IsCompleted);
        release.TrySetResult();
        var result = await processing.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("partial", result.Status);
        Assert.Equal("open-meteo:2988507", result.DestinationId);
        Assert.Equal("not_found", result.Knowledge.Status);
        Assert.NotNull(result.Advisor);
        foreach (var agent in new[] { "weather", "attractions", "seasonality", "knowledge", "advisor" })
            Assert.Contains(progress, item => item.Agent == agent && item.Status == "running");
        Assert.Contains(progress, item => item.Agent == "weather" && item.Status == "completed");
        Assert.Contains(progress, item => item.Agent == "knowledge" && item.Status == "not_found");
    }

    [Fact]
    public async Task One_research_stream_failure_produces_explicit_partial_result()
    {
        var processor = NewProcessor(
            (d, r, ct) => Task.FromResult(WeatherResult()),
            (d, r, ct) => throw new HttpRequestException("Provider failed"),
            (d, r, ct) => Task.FromResult(SeasonalityResult()),
            (d, r, ct) => Task.FromResult(KnowledgeResult()));

        var result = await processor.ProcessAsync("plan-test", Request, Destination, (_, _) => { }, CancellationToken.None);

        Assert.Equal("partial", result.Status);
        Assert.Equal("failed", result.Attractions.Status);
        Assert.Null(result.Attractions.Data);
        Assert.Contains(result.Warnings, w => w.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Provider failed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Set GOOGLE_PLACES_API_KEY to enable Google Places attractions.")]
    [InlineData("Google Places returned HTTP 403.")]
    [InlineData("Google Places request timed out.")]
    public async Task Attractions_provider_failures_preserve_safe_diagnostic(string diagnostic)
    {
        Exception exception = diagnostic.StartsWith("Set ", StringComparison.Ordinal)
            ? new AttractionsProviderConfigurationException(diagnostic)
            : new AttractionsProviderException(diagnostic);
        var processor = NewProcessor(
            (d, r, ct) => Task.FromResult(WeatherResult()),
            (d, r, ct) => Task.FromException<ResearchResult<IReadOnlyList<Attraction>>>(exception),
            (d, r, ct) => Task.FromResult(SeasonalityResult()),
            (d, r, ct) => Task.FromResult(KnowledgeResult()));

        var result = await processor.ProcessAsync("plan-test", Request, Destination, (_, _) => { }, CancellationToken.None);

        Assert.Equal("failed", result.Attractions.Status);
        Assert.Contains(diagnostic, result.Attractions.Warnings);
        Assert.DoesNotContain(result.Attractions.Warnings, warning => warning.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancellation_reaches_research_agents()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ResearchResult<WeatherAssessment>> Hold(CancellationToken token)
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return WeatherResult();
        }
        var processor = NewProcessor(
            (d, r, ct) => Hold(ct),
            (d, r, ct) => Task.FromResult(AttractionsResult()),
            (d, r, ct) => Task.FromResult(SeasonalityResult()),
            (d, r, ct) => Task.FromResult(KnowledgeResult()));
        using var cancellation = new CancellationTokenSource();
        var processing = processor.ProcessAsync("plan-test", Request, Destination, (_, _) => { }, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Unknown_shell_without_sourced_evidence_fails()
    {
        var emptyWeather = new ResearchResult<WeatherAssessment>("partial", "unknown", [], Now, [],
            new WeatherAssessment(Destination.Id, "Open-Meteo", "", "°C", "mm", [], [], []));
        var processor = NewProcessor(
            (d, r, ct) => Task.FromResult(emptyWeather),
            (d, r, ct) => throw new HttpRequestException("Provider failed"),
            (d, r, ct) => Task.FromResult(SeasonalityResult()),
            (d, r, ct) => Task.FromResult(KnowledgeResult()));

        var result = await processor.ProcessAsync("plan-test", Request, Destination, (_, _) => { }, CancellationToken.None);

        Assert.Equal("failed", result.Status);
    }

    private static TravelPlanProcessor NewProcessor(
        Func<Destination, CreateTravelPlanRequest, CancellationToken, Task<ResearchResult<WeatherAssessment>>> weather,
        Func<Destination, CreateTravelPlanRequest, CancellationToken, Task<ResearchResult<IReadOnlyList<Attraction>>>> attractions,
        Func<Destination, CreateTravelPlanRequest, CancellationToken, Task<ResearchResult<TourismAssessment>>> seasonality,
        Func<Destination, CreateTravelPlanRequest, CancellationToken, Task<ResearchResult<string>>> knowledge) =>
        new(new WeatherAgent(weather), new AttractionsAgent(attractions), new SeasonalityAgent(seasonality),
            new KnowledgeAgent(knowledge), new AdvisorAgent());

    private static ResearchResult<WeatherAssessment> WeatherResult() => new("completed", "medium", ["weather-1"], Now, [],
        new WeatherAssessment(Destination.Id, "Open-Meteo", "weather-1", "°C", "mm",
            [new DailyWeather(new DateOnly(2027, 7, 1), WeatherBasis.Historical, 25, 15, 1, null, 10, 10, [])], [], []));
    private static ResearchResult<IReadOnlyList<Attraction>> AttractionsResult() => new("completed", "medium", [], Now, [], []);
    private static ResearchResult<TourismAssessment> SeasonalityResult() => new("partial", "unknown", [], Now,
        ["No verified mapping."], new TourismAssessment("unknown", "unknown", "partial", null, null,
            null, [7], null, null, null, ["No verified mapping."]));
    private static ResearchResult<string> KnowledgeResult() => new("not_found", "unknown", [], Now,
        ["No corpus."], null);

    private sealed class WeatherAgent(Func<Destination, CreateTravelPlanRequest, CancellationToken,
        Task<ResearchResult<WeatherAssessment>>> run) : IWeatherResearchAgent
    {
        public Task<ResearchResult<WeatherAssessment>> AssessAsync(Destination d, CreateTravelPlanRequest r, CancellationToken ct) => run(d, r, ct);
    }
    private sealed class AttractionsAgent(Func<Destination, CreateTravelPlanRequest, CancellationToken,
        Task<ResearchResult<IReadOnlyList<Attraction>>>> run) : IAttractionsResearchAgent
    {
        public Task<ResearchResult<IReadOnlyList<Attraction>>> AssessAsync(Destination d, CreateTravelPlanRequest r, CancellationToken ct) => run(d, r, ct);
    }
    private sealed class SeasonalityAgent(Func<Destination, CreateTravelPlanRequest, CancellationToken,
        Task<ResearchResult<TourismAssessment>>> run) : ISeasonalityResearchAgent
    {
        public Task<ResearchResult<TourismAssessment>> AssessAsync(Destination d, CreateTravelPlanRequest r, CancellationToken ct) => run(d, r, ct);
    }
    private sealed class KnowledgeAgent(Func<Destination, CreateTravelPlanRequest, CancellationToken,
        Task<ResearchResult<string>>> run) : IKnowledgeResearchAgent
    {
        public Task<ResearchResult<string>> AssessAsync(Destination d, CreateTravelPlanRequest r, CancellationToken ct) => run(d, r, ct);
    }
    private sealed class AdvisorAgent : IAdvisorAgent
    {
        public Task<AdvisorResponse> RecommendAsync(Destination d, CreateTravelPlanRequest r,
            TravelPlanResearch research, CancellationToken ct) =>
            Task.FromResult(new AdvisorResponse("Research is incomplete.", []));
    }
}
