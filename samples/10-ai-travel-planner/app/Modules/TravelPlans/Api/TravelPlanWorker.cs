using ai_travel_planner.Modules.TravelPlans.Application;

namespace ai_travel_planner.Modules.TravelPlans.Api;

public sealed class TravelPlanWorker(TravelPlanJobStore jobs, IServiceScopeFactory scopes, ILogger<TravelPlanWorker> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        Enumerable.Range(0, 2).Select(_ => RunWorkerAsync(stoppingToken)));

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in jobs.Queue.ReadAllAsync(stoppingToken))
        {
            if (job.CancellationToken.IsCancellationRequested)
            {
                if (job.Snapshot.Status != "cancelled") job.Update("cancelled", null, null);
                continue;
            }
            job.Update("running", null, null);
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.CancellationToken);
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<TravelPlanProcessor>();
                var result = await processor.ProcessAsync(job.Id, job.Request, job.Destination,
                    job.UpdateAgent, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                job.Update(result.Status, result, null);
            }
            catch (OperationCanceledException) when (job.CancellationToken.IsCancellationRequested)
            { if (job.Snapshot.Status != "cancelled") job.Update("cancelled", null, null); }
            catch (OperationCanceledException)
            { job.Update("failed", null, "The travel assessment exceeded its deadline."); }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Travel plan {TravelPlanId} failed", job.Id);
                job.Update("failed", null, "The travel assessment could not be completed.");
            }
        }
    }
}
