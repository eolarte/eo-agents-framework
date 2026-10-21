using Microsoft.Agents.AI.Workflows;
using ai_travel_planner.Modules.Attractions.Application;
using ai_travel_planner.Modules.Attractions.Domain;
using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.Tourism.Domain;
using ai_travel_planner.Modules.TravelPlans.Domain;
using ai_travel_planner.Modules.Weather.Domain;

namespace ai_travel_planner.Modules.TravelPlans.Application;

public sealed class TravelPlanProcessor(
    IWeatherResearchAgent weather,
    IAttractionsResearchAgent attractions,
    ISeasonalityResearchAgent seasonality,
    IKnowledgeResearchAgent knowledge,
    IAdvisorAgent advisorAgent)
{
    private static readonly TimeSpan OverallDeadline = TimeSpan.FromSeconds(75);

    public async Task<TravelPlanResult> ProcessAsync(string id, CreateTravelPlanRequest request, Destination destination,
        Action<string, string> agentStatus, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(OverallDeadline);
        var token = deadline.Token;
        if (string.IsNullOrWhiteSpace(destination.Timezone) || !double.IsFinite(destination.Latitude) || !double.IsFinite(destination.Longitude))
            throw new InvalidOperationException("The destination has no usable coordinates or timezone.");

        var workflowInput = new ResearchInput(destination, request);
        var root = new ResearchRootExecutor();
        var weatherWorker = new ResearchBranchExecutor(this, "weather", agentStatus);
        var attractionsWorker = new ResearchBranchExecutor(this, "attractions", agentStatus);
        var tourismWorker = new ResearchBranchExecutor(this, "seasonality", agentStatus);
        var knowledgeWorker = new ResearchBranchExecutor(this, "knowledge", agentStatus);
        var join = new ResearchJoinExecutor();
        var builder = new WorkflowBuilder(root);
        builder.AddFanOutEdge(root, [weatherWorker, attractionsWorker, tourismWorker, knowledgeWorker]);
        builder.AddFanInBarrierEdge([weatherWorker, attractionsWorker, tourismWorker, knowledgeWorker], join);
        var workflow = builder.WithOutputFrom(join).Build();
        await using var run = await InProcessExecution.RunAsync(workflow, workflowInput, cancellationToken: token);
        token.ThrowIfCancellationRequested();
        var result = run.NewEvents.OfType<ExecutorCompletedEvent>()
            .Select(e => e.Data).OfType<ResearchOutput>().LastOrDefault()
            ?? throw new InvalidOperationException("The research workflow did not produce an assessment.");

        AdvisorResponse? advisor = null;
        IReadOnlyList<string> warnings = result.Warnings;
        agentStatus("advisor", "running");
        try
        {
            advisor = await advisorAgent.RecommendAsync(destination, request,
                new TravelPlanResearch(result.Weather, result.Attractions, result.Seasonality, result.Knowledge, result.Warnings), token);
            var validIds = result.AllSourceIds.ToHashSet(StringComparer.Ordinal);
            advisor = advisor with { Recommendations = advisor.Recommendations
                .Select(r => r with { SourceIds = r.SourceIds.Where(validIds.Contains).Distinct().ToArray() }).ToArray() };
            agentStatus("advisor", "completed");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { agentStatus("advisor", "cancelled"); throw; }
        catch (Exception) { agentStatus("advisor", "failed"); warnings = warnings.Append("Advisor recommendations are temporarily unavailable.").ToArray(); }

        token.ThrowIfCancellationRequested();
        var usable = result.Weather.Data?.Days.Any(day => day.CoverageSamples > 0) == true ||
            result.Attractions.Data?.Any(attraction => !string.IsNullOrWhiteSpace(attraction.Source.ProviderId)) == true ||
            result.Seasonality.Data is { Level: not "unknown", SourceUrl: not null } ||
            result.Knowledge.Status == "completed" && !string.IsNullOrWhiteSpace(result.Knowledge.Data);
        var finalStatus = usable ? (advisor is null || result.HasMissingEvidence ? "partial" : "completed") : "failed";
        if (!usable) warnings = warnings.Append("No usable destination assessment evidence was available.").ToArray();
        return new TravelPlanResult(id, finalStatus, destination.Id, destination.Name, destination.Latitude, destination.Longitude,
            destination.Timezone!, request.StartDate, request.EndDate,
            result.Weather, result.Attractions, result.Seasonality, result.Knowledge, advisor, warnings);
    }

    private async Task<ResearchItem> ResearchOneAsync(ResearchInput input, string stream, Action<string, string> agentStatus, CancellationToken token)
    {
        var d = input.Destination; var r = input.Request;
        agentStatus(stream, "running");
        try
        {
            var item = stream switch
            {
                "weather" => new ResearchItem(stream, await CatchAsync(() => weather.AssessAsync(d, r, token))),
                "attractions" => new ResearchItem(stream, await CatchAsync(() => attractions.AssessAsync(d, r, token))),
                "seasonality" => new ResearchItem(stream, await CatchAsync(() => seasonality.AssessAsync(d, r, token))),
                _ => new ResearchItem(stream, await CatchAsync(() => knowledge.AssessAsync(d, r, token)))
            };
            var resultStatus = item.Result switch
            {
                ResearchResult<WeatherAssessment> value => value.Status,
                ResearchResult<IReadOnlyList<Attraction>> value => value.Status,
                ResearchResult<TourismAssessment> value => value.Status,
                ResearchResult<string> value => value.Status,
                _ => "failed"
            };
            agentStatus(stream, resultStatus is "completed" or "partial" or "failed" or "not_found" ? resultStatus : "failed");
            return item;
        }
        catch (OperationCanceledException)
        {
            agentStatus(stream, "cancelled");
            throw;
        }
    }

    private static async Task<ResearchResult<T>> CatchAsync<T>(Func<Task<ResearchResult<T>>> operation)
    {
        try { return await operation(); }
        catch (OperationCanceledException) { throw; }
        catch (AttractionsProviderConfigurationException exception)
        {
            return FailedResult<T>(exception.Message);
        }
        catch (AttractionsProviderException exception)
        {
            return FailedResult<T>(exception.Message);
        }
        catch (Exception) { return new("failed", "unknown", [], DateTimeOffset.UtcNow,
            ["Research stream is temporarily unavailable."], default); }
    }

    private static ResearchResult<T> FailedResult<T>(string warning) =>
        new("failed", "unknown", [], DateTimeOffset.UtcNow, [warning], default);

    private sealed record ResearchInput(Destination Destination, CreateTravelPlanRequest Request);
    private sealed record ResearchItem(string Stream, object Result);
    private sealed record ResearchOutput(ResearchResult<WeatherAssessment> Weather,
        ResearchResult<IReadOnlyList<Attraction>> Attractions,
        ResearchResult<TourismAssessment> Seasonality,
        ResearchResult<string> Knowledge, IReadOnlyList<string> Warnings)
    {
        public bool HasMissingEvidence =>
            Weather.Status != "completed" || Weather.Data?.Days.Any(day => day.CoverageSamples > 0) != true ||
            Attractions.Status != "completed" || Attractions.Data is null ||
            Seasonality.Status != "completed" || Seasonality.Data?.Level == "unknown" ||
            Knowledge.Status != "completed" || Knowledge.Data is null;
        public IEnumerable<string> AllSourceIds => Weather.SourceIds.Concat(Attractions.SourceIds).Concat(Seasonality.SourceIds).Concat(Knowledge.SourceIds);
    }

    private sealed class ResearchRootExecutor : Executor<ResearchInput, ResearchInput>
    {
        public ResearchRootExecutor() : base("TravelPlanResearchStart") { }
        public override ValueTask<ResearchInput> HandleAsync(ResearchInput input, IWorkflowContext context,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(input);
    }

    private sealed class ResearchBranchExecutor(TravelPlanProcessor processor, string stream, Action<string, string> agentStatus)
        : Executor<ResearchInput, ResearchItem>($"{stream}ResearchExecutor")
    {
        public override async ValueTask<ResearchItem> HandleAsync(ResearchInput input, IWorkflowContext context,
            CancellationToken cancellationToken = default) => await processor.ResearchOneAsync(input, stream, agentStatus, cancellationToken);
    }

    private sealed class ResearchJoinExecutor : Executor<ResearchItem, ResearchOutput?>
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, object> _results = new(StringComparer.Ordinal);
        public ResearchJoinExecutor() : base("TravelResearchFanIn") { }
        public override ValueTask<ResearchOutput?> HandleAsync(ResearchItem item, IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _results[item.Stream] = item.Result;
                if (_results.Count < 4) return ValueTask.FromResult<ResearchOutput?>(null);
                var weatherResult = (ResearchResult<WeatherAssessment>)_results["weather"];
                var attractionsResult = (ResearchResult<IReadOnlyList<Attraction>>)_results["attractions"];
                var seasonalityResult = (ResearchResult<TourismAssessment>)_results["seasonality"];
                var knowledge = (ResearchResult<string>)_results["knowledge"];
                var warnings = weatherResult.Warnings.Concat(attractionsResult.Warnings).Concat(seasonalityResult.Warnings).Concat(knowledge.Warnings).ToArray();
                return ValueTask.FromResult<ResearchOutput?>(new(weatherResult, attractionsResult, seasonalityResult, knowledge, warnings));
            }
        }
    }
}
