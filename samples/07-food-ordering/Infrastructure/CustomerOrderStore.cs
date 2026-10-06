using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace food_ordering.Infrastructure;

public sealed class CustomerOrderStore(IDbContextFactory<CustomerOrderDbContext> dbContextFactory) : ICustomerOrderStore
{
    [Description("Creates a local draft after the customer selects menu items and a supported demo payment method. It does not place or charge an order.")]
    public async Task<string> CreateDraft(string customerId,
        [Description("A concise summary of the menu items the customer selected.")] string orderSummary,
        [Description("The demo payment choice: demo-card or cash.")] string paymentMethod)
    {
        var normalizedMethod = paymentMethod.Trim().ToLowerInvariant();
        var validation = CheckoutTools.ValidateDemoPaymentMethod(normalizedMethod);
        if (validation.StartsWith("Unsupported", StringComparison.Ordinal)) return validation;

        var now = DateTime.UtcNow;
        var order = new CustomerOrder
        {
            CustomerKey = CustomerIdentity.CreateStorageKey(customerId),
            Summary = orderSummary.Trim(),
            PaymentMethod = normalizedMethod,
            Status = OrderStatus.AwaitingConfirmation,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Payment = new CustomerPayment
            {
                Method = normalizedMethod,
                Status = PaymentStatus.Pending,
                UpdatedAtUtc = now
            },
            Delivery = new CustomerDelivery
            {
                Status = DeliveryStatus.Pending,
                UpdatedAtUtc = now
            }
        };
        AddEvent(order, "Order", OrderStatus.AwaitingConfirmation.ToString(), now);
        AddEvent(order, "Payment", PaymentStatus.Pending.ToString(), now);
        AddEvent(order, "Delivery", DeliveryStatus.Pending.ToString(), now);

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return $"Local demo order #{ShortId(order.Id)} saved with status Awaiting confirmation. {validation} This order will use simulated delivery after confirmation; no external order was placed.";
    }

    [Description("Reports the latest active local demo order and its order, payment, and delivery status. Provide an order ID to inspect an older order.")]
    public async Task<string> GetStatusAsync(string customerId, string? orderId = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var result = await FindOrderAsync(db, customerId, orderId);
        if (result.Error is not null) return result.Error;
        if (result.Order is null) return "There is no local demo order for this customer.";

        var order = result.Order;
        return $"Local demo order #{ShortId(order.Id)}: {Label(order.Status)}. " +
            $"Payment: {Label(order.Payment.Status)} ({order.Payment.Method}). " +
            $"Delivery: {Label(order.Delivery.Status)}. Order: {order.Summary}. " +
            "Statuses are simulated locally; no payment, restaurant order, or courier service is involved.";
    }

    [Description("Reports the current local simulated delivery status. Provide an order ID to inspect an older order.")]
    public async Task<string> GetDeliveryStatusAsync(string customerId, string? orderId = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var result = await FindOrderAsync(db, customerId, orderId);
        if (result.Error is not null) return result.Error;
        if (result.Order is null) return "There is no local demo delivery for this customer.";

        var order = result.Order;
        return order.Delivery.Status == DeliveryStatus.OutForDelivery
            ? $"Local demo order #{ShortId(order.Id)} is out for delivery. This is a simulated status, not a live courier update."
            : $"Local demo order #{ShortId(order.Id)} has delivery status {Label(order.Delivery.Status)} and order status {Label(order.Status)}. Delivery progress is simulated locally; no courier was contacted.";
    }

    public async Task<bool> IsOutForDeliveryAsync(string customerId, string? orderId = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var result = await FindOrderAsync(db, customerId, orderId);
        return result.Order?.Delivery.Status == DeliveryStatus.OutForDelivery;
    }

    [Description("Lists this customer's ten most recent local demo orders with IDs and their lifecycle states, so an older order can be selected for tracking.")]
    public async Task<string> GetRecentOrdersAsync(string customerId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var customerKey = CustomerIdentity.CreateStorageKey(customerId);
        var orders = await db.Orders.AsNoTracking().Include(item => item.Payment).Include(item => item.Delivery)
            .Where(item => item.CustomerKey == customerKey)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(10)
            .ToListAsync();
        return orders.Count == 0
            ? "There is no local demo order history for this customer."
            : "Recent local demo orders (latest first): " + string.Join("; ", orders.Select(order =>
                $"#{ShortId(order.Id)} — {Label(order.Status)}, payment {Label(order.Payment.Status)}, delivery {Label(order.Delivery.Status)}"));
    }

    public async Task<CustomerOrderSnapshot?> GetCurrentOrderSnapshotAsync(string customerId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var customerKey = CustomerIdentity.CreateStorageKey(customerId);
        var order = await db.Orders.AsNoTracking()
            .Include(item => item.Payment)
            .Include(item => item.Delivery)
            .Where(item => item.CustomerKey == customerKey &&
                item.Status != OrderStatus.Delivered &&
                item.Status != OrderStatus.Cancelled)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync();

        return order is null ? null : CreateSnapshot(
            order);
    }

    public async Task<IReadOnlyList<CustomerOrderSnapshot>> GetOrderHistorySnapshotsAsync(string customerId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var customerKey = CustomerIdentity.CreateStorageKey(customerId);
        var orders = await db.Orders.AsNoTracking()
            .Include(item => item.Payment)
            .Include(item => item.Delivery)
            .Where(item => item.CustomerKey == customerKey &&
                (item.Status == OrderStatus.Delivered || item.Status == OrderStatus.Cancelled))
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync();

        return orders.Select(CreateSnapshot).ToList();
    }

    private static CustomerOrderSnapshot CreateSnapshot(CustomerOrder order)
    {
        return new CustomerOrderSnapshot(
            ShortId(order.Id),
            order.Summary,
            Label(order.Status),
            order.Payment.Method,
            Label(order.Payment.Status),
            Label(order.Delivery.Status),
            order.CreatedAtUtc);
    }

    public async Task<bool> HasPendingConfirmationAsync(string customerId)
    {
        var customerKey = CustomerIdentity.CreateStorageKey(customerId);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.Orders.AnyAsync(order =>
            order.CustomerKey == customerKey && order.Status == OrderStatus.AwaitingConfirmation);
    }

    public async Task<string> ConfirmAsync(string customerId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var customerKey = CustomerIdentity.CreateStorageKey(customerId);
        var order = await db.Orders.Include(item => item.Payment).Include(item => item.Delivery)
            .Where(item => item.CustomerKey == customerKey && item.Status == OrderStatus.AwaitingConfirmation)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (order is null)
        {
            await transaction.RollbackAsync();
            return await GetNoDraftConfirmationMessageAsync(customerKey);
        }

        var now = DateTime.UtcNow;
        order.Status = OrderStatus.Confirmed;
        order.UpdatedAtUtc = now;
        order.NextTransitionAtUtc = now;
        order.Payment.Status = order.Payment.Method == "demo-card"
            ? PaymentStatus.SimulatedApproved
            : PaymentStatus.CashDueOnDelivery;
        order.Payment.UpdatedAtUtc = now;
        order.Delivery.Status = DeliveryStatus.Preparing;
        order.Delivery.UpdatedAtUtc = now;
        AddEvent(order, "Order", OrderStatus.Confirmed.ToString(), now);
        AddEvent(order, "Payment", order.Payment.Status.ToString(), now);
        AddEvent(order, "Delivery", order.Delivery.Status.ToString(), now);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var paymentMessage = order.Payment.Status == PaymentStatus.SimulatedApproved
            ? "The demo-card choice was approved for this simulation only; no charge was made."
            : "Cash is recorded as due on delivery; the sample does not collect cash.";
        return $"Local demo order #{ShortId(order.Id)} confirmed. Preparation has started. {paymentMessage} Delivery progress will update automatically in the demo; no external order was placed.";
    }

    public async Task<string> CancelAsync(string customerId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var customerKey = CustomerIdentity.CreateStorageKey(customerId);
        var order = await db.Orders.Include(item => item.Payment).Include(item => item.Delivery)
            .Where(item => item.CustomerKey == customerKey && item.Status != OrderStatus.Delivered && item.Status != OrderStatus.Cancelled)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (order is null)
        {
            await transaction.RollbackAsync();
            var latest = await db.Orders.Where(item => item.CustomerKey == customerKey)
                .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync();
            return latest?.Status == OrderStatus.Delivered
                ? $"Local demo order #{ShortId(latest.Id)} has already been delivered and cannot be cancelled."
                : "There is no local demo order to cancel.";
        }

        var now = DateTime.UtcNow;
        order.Status = OrderStatus.Cancelled;
        order.UpdatedAtUtc = now;
        order.NextTransitionAtUtc = null;
        order.Payment.Status = PaymentStatus.Voided;
        order.Payment.UpdatedAtUtc = now;
        order.Delivery.Status = DeliveryStatus.Cancelled;
        order.Delivery.UpdatedAtUtc = now;
        AddEvent(order, "Order", OrderStatus.Cancelled.ToString(), now);
        AddEvent(order, "Payment", PaymentStatus.Voided.ToString(), now);
        AddEvent(order, "Delivery", DeliveryStatus.Cancelled.ToString(), now);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return $"Local demo order #{ShortId(order.Id)} was cancelled. Any payment status was updated locally only; no charge or refund occurred.";
    }

    private async Task<string> GetNoDraftConfirmationMessageAsync(string customerKey)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var active = await db.Orders.Where(item => item.CustomerKey == customerKey &&
                item.Status != OrderStatus.Delivered && item.Status != OrderStatus.Cancelled)
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync();
        return active is null
            ? "There is no local demo order draft to confirm."
            : $"Local demo order #{ShortId(active.Id)} is already {Label(active.Status)}.";
    }

    private static async Task<(CustomerOrder? Order, string? Error)> FindOrderAsync(
        CustomerOrderDbContext db, string customerId, string? orderId)
    {
        var customerKey = CustomerIdentity.CreateStorageKey(customerId);
        var query = db.Orders.Include(item => item.Payment).Include(item => item.Delivery)
            .Where(item => item.CustomerKey == customerKey);

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            var idPrefix = orderId.Trim().TrimStart('#').ToLowerInvariant();
            var matches = await query.Where(item => item.Id.StartsWith(idPrefix)).Take(2).ToListAsync();
            return matches.Count switch
            {
                0 => (null, $"No local demo order matched ID '{orderId}'."),
                1 => (matches[0], null),
                _ => (null, $"Order ID '{orderId}' is ambiguous. Use one of these full IDs: {string.Join(", ", matches.Select(item => item.Id))}.")
            };
        }

        var active = await query.Where(item => item.Status != OrderStatus.Delivered && item.Status != OrderStatus.Cancelled)
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync();
        var order = active ?? await query.OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync();
        return (order, null);
    }

    internal static void AddEvent(CustomerOrder order, string entity, string status, DateTime occurredAtUtc)
        => order.Events.Add(new OrderLifecycleEvent
        {
            Entity = entity,
            Status = status,
            OccurredAtUtc = occurredAtUtc
        });

    private static string ShortId(string id) => id[..8].ToUpperInvariant();

    private static string Label(OrderStatus status) => status switch
    {
        OrderStatus.AwaitingConfirmation => "awaiting confirmation",
        OrderStatus.OutForDelivery => "out for delivery",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string Label(PaymentStatus status) => status switch
    {
        PaymentStatus.SimulatedApproved => "simulated approval (not charged)",
        PaymentStatus.CashDueOnDelivery => "cash due on delivery (not collected)",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string Label(DeliveryStatus status) => status switch
    {
        DeliveryStatus.OutForDelivery => "out for delivery",
        _ => status.ToString().ToLowerInvariant()
    };
}
