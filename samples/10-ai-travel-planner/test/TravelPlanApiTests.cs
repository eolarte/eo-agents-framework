using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using ai_travel_planner.Modules.Destinations.Application;
using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.TravelPlans.Api;
using ai_travel_planner.Modules.TravelPlans.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class TravelPlanApiTests
{
    private const string ValidRequest = """
        {"destinationId":"open-meteo:2988507","startDate":"2027-07-01","endDate":"2027-07-03",
         "preferences":{"interests":["history"],"pace":"relaxed","dailyStartTime":"09:00:00",
         "dailyEndTime":"19:00:00","transportMode":"walking","budgetLevel":"medium","avoidCrowds":true},
         "language":"en"}
        """;

    [Fact]
    public async Task Create_rejects_unknown_id_before_enqueue()
    {
        await using var app = await StartAppAsync(null);
        using var client = CreateClient(app);

        var response = await client.PostAsJsonAsync("/api/travel-plans", System.Text.Json.JsonSerializer.Deserialize<object>(ValidRequest));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(app.Services.GetRequiredService<TravelPlanJobStore>().Queue.TryRead(out _));
    }

    [Fact]
    public async Task Create_accepts_resolved_id_and_returns_polling_location()
    {
        await using var app = await StartAppAsync(new Destination("open-meteo:2988507", "Paris", "France", null,
            48.8566, 2.3522, "Europe/Paris"));
        using var client = CreateClient(app);

        var response = await client.PostAsJsonAsync("/api/travel-plans", System.Text.Json.JsonSerializer.Deserialize<object>(ValidRequest));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.StartsWith("/api/travel-plans/", response.Headers.Location?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("statusUrl", body);
        Assert.Contains("eventsUrl", body);
    }

    [Fact]
    public async Task Create_rejects_invalid_local_hours()
    {
        await using var app = await StartAppAsync(new Destination("open-meteo:2988507", "Paris", "France", null,
            48.8566, 2.3522, "Europe/Paris"));
        using var client = CreateClient(app);
        var body = ValidRequest.Replace("19:00:00", "08:00:00", StringComparison.Ordinal);

        var response = await client.PostAsJsonAsync("/api/travel-plans", System.Text.Json.JsonSerializer.Deserialize<object>(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("daily", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sse_replays_queued_running_and_terminal_events_after_reconnect()
    {
        await using var app = await StartAppAsync(new Destination("open-meteo:2988507", "Paris", "France", null,
            48.8566, 2.3522, "Europe/Paris"));
        using var client = CreateClient(app);
        var created = await client.PostAsJsonAsync("/api/travel-plans", System.Text.Json.JsonSerializer.Deserialize<object>(ValidRequest));
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.True(app.Services.GetRequiredService<TravelPlanJobStore>().Queue.TryRead(out var job));
        job.UpdateAgent("weather", "running");
        job.UpdateAgent("weather", "completed");
        job.Update("running", null, null);
        job.Update("partial", null, null);

        var allEvents = await client.GetStringAsync($"/api/travel-plans/{job.Id}/events");
        using var reconnect = new HttpRequestMessage(HttpMethod.Get, $"/api/travel-plans/{job.Id}/events");
        reconnect.Headers.TryAddWithoutValidation("Last-Event-ID", "4");
        var replay = await client.SendAsync(reconnect);
        var replayBody = await replay.Content.ReadAsStringAsync();

        Assert.Contains("id: 1", allEvents);
        Assert.Contains("id: 2", allEvents);
        Assert.Contains("id: 3", allEvents);
        Assert.Contains("event: agent_status", allEvents);
        Assert.Contains("\"agent\":\"weather\"", allEvents);
        Assert.Contains("event: plan_completed", allEvents);
        Assert.DoesNotContain("id: 1", replayBody);
        Assert.DoesNotContain("id: 2", replayBody);
        Assert.Contains("id: 5", replayBody);
    }

    private static async Task<WebApplication> StartAppAsync(Destination? destination)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AZURE_OPENAI_API_KEY"] = "test-only-key",
            ["AZURE_OPENAI_DEPLOYMENT_NAME"] = "test-only-model"
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<TravelPlanJobStore>();
        builder.Services.AddSingleton<IDestinationResolver>(new StubResolver(destination));
        var app = builder.Build();
        app.MapTravelPlansModule();
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private sealed class StubResolver(Destination? destination) : IDestinationResolver
    {
        public Task<Destination?> ResolveAsync(string destinationId, CancellationToken cancellationToken) =>
            Task.FromResult(destination);
    }
}
