namespace food_ordering.Domain;

public enum OrderStatus
{
    AwaitingConfirmation,
    Confirmed,
    Preparing,
    Ready,
    OutForDelivery,
    Delivered,
    Cancelled
}

public enum PaymentStatus
{
    Pending,
    SimulatedApproved,
    CashDueOnDelivery,
    Voided
}

public enum DeliveryStatus
{
    Pending,
    Preparing,
    Ready,
    OutForDelivery,
    Delivered,
    Cancelled
}

public sealed class CustomerOrder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CustomerKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.AwaitingConfirmation;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? NextTransitionAtUtc { get; set; }
    public CustomerPayment Payment { get; set; } = null!;
    public CustomerDelivery Delivery { get; set; } = null!;
    public List<OrderLifecycleEvent> Events { get; set; } = [];
}

public sealed class CustomerPayment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrderId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public CustomerOrder Order { get; set; } = null!;
}

public sealed class CustomerDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrderId { get; set; } = string.Empty;
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public CustomerOrder Order { get; set; } = null!;
}

public sealed class OrderLifecycleEvent
{
    public long Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public CustomerOrder Order { get; set; } = null!;
}
