using System.Collections.Concurrent;
using System.Threading.Channels;
using ai_travel_planner.Modules.TravelPlans.Domain;
using ai_travel_planner.Modules.Destinations.Domain;

namespace ai_travel_planner.Modules.TravelPlans.Application;

public sealed class TravelPlanJobStore(TimeProvider timeProvider)
{
    public const int MaximumWaiting = 20;
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly Channel<Job> _queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(MaximumWaiting)
    { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false });

    public ChannelReader<Job> Queue => _queue.Reader;
    public bool TryGet(string id, out Job job) { Prune(); return _jobs.TryGetValue(id, out job!); }

    public bool TryEnqueue(CreateTravelPlanRequest request, Destination destination, out Job? job)
    {
        Prune();
        job = new Job(Guid.NewGuid().ToString("N"), request, destination, timeProvider.GetUtcNow(), timeProvider);
        _jobs[job.Id] = job;
        if (!_queue.Writer.TryWrite(job)) { _jobs.TryRemove(job.Id, out _); job = null; return false; }
        return true;
    }

    public void Prune()
    {
        var threshold = timeProvider.GetUtcNow() - Retention;
        foreach (var (id, job) in _jobs)
            if (job.Snapshot.Status is "completed" or "partial" or "failed" or "cancelled" && job.Snapshot.UpdatedAt < threshold)
                _jobs.TryRemove(id, out _);
    }

    public sealed class Job(string id, CreateTravelPlanRequest request, Destination destination, DateTimeOffset createdAt, TimeProvider timeProvider)
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _cancellation = new();
        private static readonly string[] AgentNames = ["weather", "attractions", "seasonality", "knowledge", "advisor"];
        private readonly List<TravelPlanSnapshot> _events = [InitialSnapshot(id, createdAt)];
        private TaskCompletionSource _changed = NewSignal();
        public string Id { get; } = id;
        public CreateTravelPlanRequest Request { get; } = request;
        public Destination Destination { get; } = destination;
        public CancellationToken CancellationToken => _cancellation.Token;
        public TravelPlanSnapshot Snapshot { get; private set; } = InitialSnapshot(id, createdAt);


        public bool Cancel()
        {
            lock (_sync)
            {
                if (IsTerminal(Snapshot.Status)) return false;
                _cancellation.Cancel();
                var statuses = new Dictionary<string, string>(Snapshot.AgentStatuses, StringComparer.Ordinal);
                foreach (var name in AgentNames)
                    if (statuses[name] is "pending" or "running") statuses[name] = "cancelled";
                Snapshot = Snapshot with { Status = "cancelled", AgentStatuses = statuses,
                    Agent = null, AgentStatus = null, EventId = Snapshot.EventId + 1, UpdatedAt = timeProvider.GetUtcNow() };
                PublishSnapshot();
                return true;
            }
        }

        public void Update(string status, TravelPlanResult? result, string? error)
        {
            lock (_sync)
            {
                if (!IsLifecycleStatus(status)) throw new ArgumentOutOfRangeException(nameof(status), "Invalid travel plan lifecycle status.");
                if (IsTerminal(Snapshot.Status)) return;
                Snapshot = Snapshot with { Status = status, EventId = Snapshot.EventId + 1,
                    Agent = null, AgentStatus = null, UpdatedAt = timeProvider.GetUtcNow(), Result = result, Error = error };
                PublishSnapshot();
            }
        }

        public void UpdateAgent(string agent, string status)
        {
            if (!AgentNames.Contains(agent, StringComparer.Ordinal)) throw new ArgumentOutOfRangeException(nameof(agent));
            if (status is not ("pending" or "running" or "completed" or "partial" or "failed" or "not_found" or "cancelled"))
                throw new ArgumentOutOfRangeException(nameof(status));
            lock (_sync)
            {
                if (IsTerminal(Snapshot.Status)) return;
                if (Snapshot.AgentStatuses[agent] == status) return;
                var statuses = new Dictionary<string, string>(Snapshot.AgentStatuses, StringComparer.Ordinal) { [agent] = status };
                Snapshot = Snapshot with { AgentStatuses = statuses, Agent = agent, AgentStatus = status,
                    EventId = Snapshot.EventId + 1, UpdatedAt = timeProvider.GetUtcNow() };
                PublishSnapshot();
            }
        }

        private void PublishSnapshot()
        {
            _events.Add(Snapshot);
            var changed = _changed;
            _changed = NewSignal();
            changed.TrySetResult();
        }

        public IReadOnlyList<TravelPlanSnapshot> Replay(long afterEventId)
        { lock (_sync) return _events.Where(e => e.EventId > afterEventId).ToArray(); }

        public bool IsTerminalAndCaughtUp(long eventId)
        { lock (_sync) return IsTerminal(Snapshot.Status) && Snapshot.EventId <= eventId; }

        public async ValueTask<TravelPlanSnapshot?> WaitForNextAsync(long afterEventId, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Task changed;
                lock (_sync) { var next = _events.FirstOrDefault(e => e.EventId > afterEventId); if (next is not null) return next; changed = _changed.Task; }
                await changed.WaitAsync(cancellationToken);
            }
            return null;
        }

        private static bool IsTerminal(string status) => status is "completed" or "partial" or "failed" or "cancelled";
        private static bool IsLifecycleStatus(string status) => status is "queued" or "running" or "completed" or "partial" or "failed" or "cancelled";
        public static bool IsTerminalSnapshot(TravelPlanSnapshot snapshot) => IsTerminal(snapshot.Status);
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TravelPlanSnapshot InitialSnapshot(string id, DateTimeOffset createdAt) =>
            new(id, "queued", 1, AgentNames.ToDictionary(name => name, _ => "pending", StringComparer.Ordinal),
                null, null, createdAt, createdAt, null, null);
    }
}
