using Microsoft.EntityFrameworkCore;

namespace food_ordering.Infrastructure;

public sealed class OrderLifecycleWorker(
    IDbContextFactory<CustomerOrderDbContext> dbContextFactory,
    FoodOrderingConfiguration configuration,
    ILogger<OrderLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await AdvanceNextDueOrderAsync(stoppingToken)) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to advance a local demo order lifecycle. It will be retried.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task<bool> AdvanceNextDueOrderAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var order = await db.Orders.Include(item => item.Delivery)
            .Where(item => item.NextTransitionAtUtc != null && item.NextTransitionAtUtc <= now &&
                item.Status != OrderStatus.Delivered && item.Status != OrderStatus.Cancelled)
            .OrderBy(item => item.NextTransitionAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        switch (order.Status)
        {
            case OrderStatus.Confirmed:
                order.Status = OrderStatus.Preparing;
                ScheduleNext(order, now);
                CustomerOrderStore.AddEvent(order, "Order", order.Status.ToString(), now);
                break;
            case OrderStatus.Preparing:
                order.Status = OrderStatus.Ready;
                order.Delivery.Status = DeliveryStatus.Ready;
                ScheduleNext(order, now);
                CustomerOrderStore.AddEvent(order, "Order", order.Status.ToString(), now);
                CustomerOrderStore.AddEvent(order, "Delivery", order.Delivery.Status.ToString(), now);
                break;
            case OrderStatus.Ready:
                order.Status = OrderStatus.OutForDelivery;
                order.Delivery.Status = DeliveryStatus.OutForDelivery;
                ScheduleNext(order, now);
                CustomerOrderStore.AddEvent(order, "Order", order.Status.ToString(), now);
                CustomerOrderStore.AddEvent(order, "Delivery", order.Delivery.Status.ToString(), now);
                break;
            case OrderStatus.OutForDelivery:
                order.Status = OrderStatus.Delivered;
                order.Delivery.Status = DeliveryStatus.Delivered;
                order.NextTransitionAtUtc = null;
                order.UpdatedAtUtc = now;
                order.Delivery.UpdatedAtUtc = now;
                CustomerOrderStore.AddEvent(order, "Order", order.Status.ToString(), now);
                CustomerOrderStore.AddEvent(order, "Delivery", order.Delivery.Status.ToString(), now);
                break;
            default:
                order.NextTransitionAtUtc = null;
                order.UpdatedAtUtc = now;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Advanced local demo order {OrderId} to {OrderStatus}.", order.Id, order.Status);
        return true;
    }

    private void ScheduleNext(CustomerOrder order, DateTime now)
    {
        var delaySeconds = Random.Shared.Next(
            configuration.SimulationMinDelaySeconds,
            configuration.SimulationMaxDelaySeconds + 1);
        order.NextTransitionAtUtc = now.AddSeconds(delaySeconds);
        order.UpdatedAtUtc = now;
        order.Delivery.UpdatedAtUtc = now;
    }
}
