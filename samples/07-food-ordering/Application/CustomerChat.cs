namespace food_ordering.Application;

public sealed record CustomerChatRequest(string CustomerId, string Message);
public sealed record CustomerChatResult(string Response, string OrderStatus);
public sealed record CustomerOrderSnapshot(
    string Id,
    string Summary,
    string OrderStatus,
    string PaymentMethod,
    string PaymentStatus,
    string DeliveryStatus,
    DateTime CreatedAtUtc);
public sealed record CustomerAgentSet(
    Microsoft.Agents.AI.AIAgent Coordinator,
    Microsoft.Agents.AI.AIAgent MenuAgent,
    Microsoft.Agents.AI.AIAgent PolicyAgent,
    Microsoft.Agents.AI.AIAgent CheckoutPaymentAgent,
    Microsoft.Agents.AI.AIAgent DeliveryAgent,
    Microsoft.Agents.AI.AIAgent OrderStatusAgent,
    Microsoft.Agents.AI.FileMemoryProvider MemoryProvider);

public interface ICustomerOrderStore
{
    Task<string> CreateDraft(string customerId, string orderSummary, string paymentMethod);
    Task<string> GetStatusAsync(string customerId, string? orderId = null);
    Task<string> GetDeliveryStatusAsync(string customerId, string? orderId = null);
    Task<bool> IsOutForDeliveryAsync(string customerId, string? orderId = null);
    Task<string> GetRecentOrdersAsync(string customerId);
    Task<CustomerOrderSnapshot?> GetCurrentOrderSnapshotAsync(string customerId);
    Task<bool> HasPendingConfirmationAsync(string customerId);
    Task<string> ConfirmAsync(string customerId);
    Task<string> CancelAsync(string customerId);
}

public interface ICustomerAgentFactory
{
    CustomerAgentSet Create(string customerId);
}

public sealed class CustomerChatService(ICustomerAgentFactory agentFactory, ICustomerOrderStore orders)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CustomerRuntime> runtimes = new(StringComparer.Ordinal);

    public async Task<CustomerChatResult> SendAsync(string customerId, string message, CancellationToken cancellationToken)
    {
        var customerKey = CustomerIdentity.CreateStorageKey(customerId);
        var runtime = runtimes.GetOrAdd(customerKey, _ => new CustomerRuntime(customerId, agentFactory.Create(customerId), orders));
        return await runtime.SendAsync(message, cancellationToken);
    }
}

internal sealed class CustomerRuntime(string customerId, CustomerAgentSet agents, ICustomerOrderStore orders)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Microsoft.Agents.AI.AgentSession? session;
    private Microsoft.Agents.AI.AgentSession? orderStatusSession;
    private Microsoft.Agents.AI.AgentSession? deliverySession;

    public async Task<CustomerChatResult> SendAsync(string input, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            session ??= await agents.Coordinator.CreateSessionAsync(cancellationToken);
            using var activity = FoodOrderingTelemetry.ActivitySource.StartActivity("food-ordering.customer-message");
            activity?.SetTag("food_ordering.customer_id", CustomerIdentity.CreateStorageKey(customerId));

            if (IsSimpleConfirmation(input) || IsExplicitOrderConfirmation(input))
            {
                var hasPendingDraft = await orders.HasPendingConfirmationAsync(customerId);
                if (hasPendingDraft || IsExplicitOrderConfirmation(input) ||
                    input.Trim().Equals("confirm", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await orders.ConfirmAsync(customerId);
                    return new CustomerChatResult(result, await orders.GetStatusAsync(customerId));
                }
            }
            if (input.Equals("cancel", StringComparison.OrdinalIgnoreCase) || input.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                var result = await orders.CancelAsync(customerId);
                return new CustomerChatResult(result, await orders.GetStatusAsync(customerId));
            }
            if (IsOrderHistoryRequest(input))
            {
                var history = await orders.GetRecentOrdersAsync(customerId);
                orderStatusSession ??= await agents.OrderStatusAgent.CreateSessionAsync(cancellationToken);
                var historyResponse = await agents.OrderStatusAgent.RunAsync(
                    $"The customer asked: {input}\n\nCurrent persisted local order history: {history}\n\n" +
                    "Answer using this database history snapshot. It is the current source of truth.",
                    orderStatusSession,
                    cancellationToken: cancellationToken);
                return new CustomerChatResult(historyResponse.ToString(), await orders.GetStatusAsync(customerId));
            }
            if (IsOrderStatusRequest(input))
            {
                var orderId = ExtractOrderId(input);
                var status = await orders.GetStatusAsync(customerId, orderId);
                var isOutForDelivery = await orders.IsOutForDeliveryAsync(customerId, orderId);
                var agent = isOutForDelivery ? agents.DeliveryAgent : agents.OrderStatusAgent;
                var prompt = $"The customer asked: {input}\n\nCurrent persisted local lifecycle status: {status}\n\n" +
                    "Answer from this database snapshot. It is the current source of truth. Do not claim real payment, restaurant, or courier activity.";
                var agentSession = isOutForDelivery
                    ? deliverySession ??= await agent.CreateSessionAsync(cancellationToken)
                    : orderStatusSession ??= await agent.CreateSessionAsync(cancellationToken);
                var agentResponse = await agent.RunAsync(prompt, agentSession, cancellationToken: cancellationToken);
                return new CustomerChatResult(agentResponse.ToString(), await orders.GetStatusAsync(customerId));
            }

            var response = await agents.Coordinator.RunAsync(input, session, cancellationToken: cancellationToken);
            return new CustomerChatResult(response.ToString(), await orders.GetStatusAsync(customerId));
        }
        finally { gate.Release(); }
    }

    private static bool IsOrderStatusRequest(string input)
    {
        var message = input.Trim().ToLowerInvariant();
        return message is "status" or "track" or "track order" or "track my order" or "order status" or "delivery status" ||
            (message.Contains("status", StringComparison.Ordinal) &&
             (message.Contains("order", StringComparison.Ordinal) || message.Contains("delivery", StringComparison.Ordinal))) ||
            message.Contains("where is my order", StringComparison.Ordinal) ||
            message.Contains("where's my order", StringComparison.Ordinal) ||
            message.Contains("track my delivery", StringComparison.Ordinal) ||
            message.Contains("track order", StringComparison.Ordinal) ||
            message.Contains("check my order", StringComparison.Ordinal);
    }

    private static bool IsSimpleConfirmation(string input)
        => input.Trim().Equals("confirm", StringComparison.OrdinalIgnoreCase) ||
           input.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static bool IsExplicitOrderConfirmation(string input)
    {
        var message = input.Trim().ToLowerInvariant();
        return (message.Contains("confirm", StringComparison.Ordinal) &&
                (message.Contains("order", StringComparison.Ordinal) || message.Contains("draft", StringComparison.Ordinal))) ||
            message.Contains("proceed with the order", StringComparison.Ordinal) ||
            message.Contains("proceed with this order", StringComparison.Ordinal);
    }

    private static bool IsOrderHistoryRequest(string input)
    {
        var message = input.Trim().ToLowerInvariant();
        return message.Contains("order history", StringComparison.Ordinal) ||
            message.Contains("past order", StringComparison.Ordinal) ||
            message.Contains("previous order", StringComparison.Ordinal) ||
            message.Contains("my orders", StringComparison.Ordinal);
    }

    private static string? ExtractOrderId(string input)
    {
        var match = System.Text.RegularExpressions.Regex.Match(input, @"(?<![A-Za-z0-9])#?([a-fA-F0-9]{8,32})(?![A-Za-z0-9])");
        return match.Success ? match.Groups[1].Value : null;
    }
}

internal static class FoodOrderingTelemetry
{
    public static readonly System.Diagnostics.ActivitySource ActivitySource = new("FoodOrdering.Sample");
}
