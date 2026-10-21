using System.Globalization;
using System.Text.Json;
using ai_travel_planner.Modules.TravelPlans.Application;
using ai_travel_planner.Modules.TravelPlans.Domain;
using ai_travel_planner.Modules.Destinations.Application;

namespace ai_travel_planner.Modules.TravelPlans.Api;

public static class TravelPlanEndpoints
{
    private static readonly SemaphoreSlim SseConnections = new(40, 40);
    public static IEndpointRouteBuilder MapTravelPlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/travel-plans");
        group.MapPost("", CreateAsync);
        group.MapGet("/{id}", GetAsync);
        group.MapGet("/{id}/events", EventsAsync);
        group.MapDelete("/{id}", CancelAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateTravelPlanRequest? request, TravelPlanJobStore jobs,
        IDestinationResolver destinations, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (string.IsNullOrWhiteSpace(configuration["AZURE_OPENAI_API_KEY"]) ||
            string.IsNullOrWhiteSpace(configuration["AZURE_OPENAI_DEPLOYMENT_NAME"]))
            return Results.Problem(title: "The travel advisor is not configured.",
                detail: "AZURE_OPENAI_API_KEY and AZURE_OPENAI_DEPLOYMENT_NAME are required.", statusCode: StatusCodes.Status503ServiceUnavailable);
        ai_travel_planner.Modules.Destinations.Domain.Destination? destination;
        try { destination = await destinations.ResolveAsync(request!.DestinationId.Trim(), cancellationToken); }
        catch (DestinationProviderException exception)
        { return Results.Problem(title: "Destination lookup is temporarily unavailable.", detail: exception.Message, statusCode: 502); }
        if (destination is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["destinationId"] = ["The selected destination could not be resolved."] });
        if (string.IsNullOrWhiteSpace(destination.Timezone) || !double.IsFinite(destination.Latitude) || !double.IsFinite(destination.Longitude))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["destinationId"] = ["The destination has no usable coordinates or timezone."] });
        if (!jobs.TryEnqueue(request!, destination, out var job))
            return Results.Problem(title: "Travel assessment capacity is full.", statusCode: StatusCodes.Status503ServiceUnavailable);
        var statusUrl = $"/api/travel-plans/{job!.Id}";
        return Results.Accepted(statusUrl, new TravelPlanAccepted(job.Id, job.Snapshot.Status, statusUrl, $"{statusUrl}/events"));
    }

    private static IResult GetAsync(string id, TravelPlanJobStore jobs) =>
        jobs.TryGet(id, out var job) ? Results.Ok(job.Snapshot) : Results.NotFound();

    private static IResult CancelAsync(string id, TravelPlanJobStore jobs)
    {
        if (!jobs.TryGet(id, out var job)) return Results.NotFound();
        if (!job.Cancel()) return Results.Conflict(job.Snapshot);
        return Results.Accepted($"/api/travel-plans/{id}", job.Snapshot);
    }

    private static async Task EventsAsync(HttpContext context, string id, TravelPlanJobStore jobs)
    {
        if (!jobs.TryGet(id, out var job)) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        if (!SseConnections.Wait(0))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        try { await StreamEventsAsync(context, job); }
        finally { SseConnections.Release(); }
    }

    private static async Task StreamEventsAsync(HttpContext context, TravelPlanJobStore.Job job)
    {
        var header = context.Request.Headers["Last-Event-ID"].FirstOrDefault();
        if (!long.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out var eventId)) eventId = 0;
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        var token = context.RequestAborted;
        foreach (var snapshot in job.Replay(eventId))
        {
            await WriteEventAsync(context, snapshot, token);
            eventId = snapshot.EventId;
            if (TravelPlanJobStore.Job.IsTerminalSnapshot(snapshot)) return;
        }
        if (job.IsTerminalAndCaughtUp(eventId)) return;
        while (!token.IsCancellationRequested)
        {
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var next = job.WaitForNextAsync(eventId, waitCancellation.Token).AsTask();
            var heartbeat = Task.Delay(TimeSpan.FromSeconds(15), waitCancellation.Token);
            var completed = await Task.WhenAny(next, heartbeat);
            waitCancellation.Cancel();
            if (completed == heartbeat)
            {
                try { await next; } catch (OperationCanceledException) { }
                if (token.IsCancellationRequested) break;
                await heartbeat;
                await context.Response.WriteAsync(": heartbeat\n\n", token);
                await context.Response.Body.FlushAsync(token);
                continue;
            }
            var snapshot = await next;
            try { await heartbeat; } catch (OperationCanceledException) { }
            if (snapshot is null) break;
            await WriteEventAsync(context, snapshot, token);
            eventId = snapshot.EventId;
            if (snapshot.Status is "completed" or "partial" or "failed" or "cancelled") break;
        }
    }

    private static async Task WriteEventAsync(HttpContext context, TravelPlanSnapshot snapshot, CancellationToken token)
    {
        var isAgentEvent = snapshot.Agent is not null;
        var json = isAgentEvent
            ? JsonSerializer.Serialize(new { planId = snapshot.Id, eventId = snapshot.EventId,
                agent = snapshot.Agent, status = snapshot.AgentStatus, planStatus = snapshot.Status,
                agentStatuses = snapshot.AgentStatuses, updatedAt = snapshot.UpdatedAt }, JsonSerializerOptions.Web)
            : JsonSerializer.Serialize(snapshot, JsonSerializerOptions.Web);
        var eventName = isAgentEvent ? "agent_status" :
            snapshot.Status is "completed" or "partial" or "failed" or "cancelled" ? "plan_completed" : "plan_status";
        await context.Response.WriteAsync($"id: {snapshot.EventId}\nevent: {eventName}\ndata: {json}\n\n", token);
        await context.Response.Body.FlushAsync(token);
    }

    private static Dictionary<string, string[]> Validate(CreateTravelPlanRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = [message];
        if (request is null) { Add("request", "A request body is required."); return errors; }
        if (string.IsNullOrWhiteSpace(request.DestinationId) || request.DestinationId.Trim().Length > 200)
            Add("destinationId", "destinationId is required and must be no more than 200 characters.");
        if (request.EndDate < request.StartDate) Add("endDate", "endDate must be on or after startDate.");
        else if (request.EndDate.DayNumber - request.StartDate.DayNumber + 1 > 14)
            Add("endDate", "The requested date range must be no longer than 14 inclusive days.");
        if (request.Preferences is null) Add("preferences", "preferences is required.");
        else
        {
            if (request.Preferences.Interests is null || request.Preferences.Interests.Count > 12 ||
                request.Preferences.Interests.Any(i => string.IsNullOrWhiteSpace(i) || i.Length > 40))
                Add("preferences.interests", "Interests must contain at most 12 non-empty values of no more than 40 characters each.");
            if (request.Preferences.Pace is not ("relaxed" or "moderate" or "intensive")) Add("preferences.pace", "pace must be relaxed, moderate, or intensive.");
            if (request.Preferences.TransportMode is not ("walking" or "transit" or "driving")) Add("preferences.transportMode", "transportMode must be walking, transit, or driving.");
            if (request.Preferences.BudgetLevel is not ("low" or "medium" or "high")) Add("preferences.budgetLevel", "budgetLevel must be low, medium, or high.");
            if (request.Preferences.DailyEndTime <= request.Preferences.DailyStartTime) Add("preferences.dailyHours", "dailyEndTime must be later than dailyStartTime.");
        }
        if (request.Language?.Length > 40) Add("language", "language must be no more than 40 characters.");
        return errors;
    }
}
