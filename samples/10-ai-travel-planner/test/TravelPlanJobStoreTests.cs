using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.TravelPlans.Application;
using ai_travel_planner.Modules.TravelPlans.Domain;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class TravelPlanJobStoreTests
{
    private static readonly Destination Destination = new("open-meteo:2988507", "Paris", "France", null,
        48.8566, 2.3522, "Europe/Paris");
    private static readonly CreateTravelPlanRequest Request = new("open-meteo:2988507",
        new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 3),
        new TravelerPreferences("relaxed", new TimeOnly(9, 0), new TimeOnly(19, 0),
            "walking", "medium", true, ["history"]));

    [Fact]
    public async Task Queued_event_is_replayable_and_updates_reach_multiple_subscribers()
    {
        var clock = new MutableTimeProvider();
        var store = new TravelPlanJobStore(clock);
        Assert.True(store.TryEnqueue(Request, Destination, out var job));
        Assert.NotNull(job);
        Assert.Equal("queued", Assert.Single(job.Replay(0)).Status);

        var subscriberA = job.WaitForNextAsync(1, CancellationToken.None).AsTask();
        var subscriberB = job.WaitForNextAsync(1, CancellationToken.None).AsTask();
        job.Update("running", null, null);

        Assert.Equal("running", (await subscriberA.WaitAsync(TimeSpan.FromSeconds(2)))!.Status);
        Assert.Equal("running", (await subscriberB.WaitAsync(TimeSpan.FromSeconds(2)))!.Status);
        Assert.Equal(new long[] { 1, 2 }, job.Replay(0).Select(e => e.EventId));
    }

    [Fact]
    public void Cancellation_is_idempotently_rejected_after_terminal_state()
    {
        var store = new TravelPlanJobStore(new MutableTimeProvider());
        Assert.True(store.TryEnqueue(Request, Destination, out var job));
        Assert.NotNull(job);

        Assert.True(job.Cancel());
        Assert.True(job.CancellationToken.IsCancellationRequested);
        var terminalEvent = job.Snapshot.EventId;
        job.Update("completed", null, null);
        job.UpdateAgent("advisor", "completed");
        Assert.Equal("cancelled", job.Snapshot.Status);
        Assert.Equal(terminalEvent, job.Snapshot.EventId);
        job.Update("cancelled", null, null);
        Assert.False(job.Cancel());
    }

    [Fact]
    public void Terminal_job_expires_after_thirty_minutes()
    {
        var clock = new MutableTimeProvider();
        var store = new TravelPlanJobStore(clock);
        Assert.True(store.TryEnqueue(Request, Destination, out var job));
        Assert.NotNull(job);
        job.Update("partial", null, null);

        clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));

        Assert.False(store.TryGet(job.Id, out _));
    }

    [Fact]
    public void Queue_refuses_more_than_twenty_waiting_jobs()
    {
        var store = new TravelPlanJobStore(new MutableTimeProvider());
        for (var index = 0; index < TravelPlanJobStore.MaximumWaiting; index++)
            Assert.True(store.TryEnqueue(Request, Destination, out _));

        Assert.False(store.TryEnqueue(Request, Destination, out var rejected));
        Assert.Null(rejected);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
