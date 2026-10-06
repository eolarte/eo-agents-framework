namespace food_ordering.Application;

using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

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
    Microsoft.Agents.AI.FileMemoryProvider MemoryProvider,
    CustomerActionClassifier ActionClassifier);

public sealed class CustomerActionClassifier(IChatClient chatClient, string modelName)
{
    public async Task<CustomerActionDecision> ClassifyAsync(
        string message,
        bool orderConfirmationPending,
        bool cancellationConfirmationPending,
        CancellationToken cancellationToken)
    {
        var result = await chatClient.GetResponseAsync<CustomerActionDecision>(
            [new ChatMessage(ChatRole.User,
                $"Order confirmation pending: {orderConfirmationPending}. " +
                $"Cancellation confirmation pending: {cancellationConfirmationPending}. " +
                $"Customer message: {message}")],
            new ChatOptions
            {
                ModelId = modelName,
                Instructions = "Choose the action. If cancellation confirmation is pending, classify the reply in " +
                    "that context: ConfirmCancellation only for a clear yes, RejectCancellation for a clear no, " +
                    "UnclearCancellationReply if ambiguous, Unrelated if the topic changed. ConfirmOrder only when " +
                    "an order confirmation is pending. Use RequestCancellation for a new cancellation request, " +
                    "OrderStatus or OrderHistory when asked, and General otherwise."
            },
            cancellationToken: cancellationToken);

        return result.Result;
    }
}

public sealed class CustomerActionDecision
{
    [JsonConverter(typeof(JsonStringEnumConverter<CustomerAction>))]
    public CustomerAction Action { get; set; }
}

public enum CustomerAction
{
    General,
    ConfirmOrder,
    RequestCancellation,
    ConfirmCancellation,
    RejectCancellation,
    UnclearCancellationReply,
    OrderStatus,
    OrderHistory,
    Unrelated
}

public interface ICustomerOrderStore
{
    Task<string> CreateDraft(string customerId, string orderSummary, string paymentMethod);
    Task<string> GetStatusAsync(string customerId, string? orderId = null);
    Task<string> GetDeliveryStatusAsync(string customerId, string? orderId = null);
    Task<bool> IsOutForDeliveryAsync(string customerId, string? orderId = null);
    Task<string> GetRecentOrdersAsync(string customerId);
    Task<CustomerOrderSnapshot?> GetCurrentOrderSnapshotAsync(string customerId);
    Task<IReadOnlyList<CustomerOrderSnapshot>> GetOrderHistorySnapshotsAsync(string customerId);
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
    private bool cancellationConfirmationPending;

    public async Task<CustomerChatResult> SendAsync(string input, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            session ??= await agents.Coordinator.CreateSessionAsync(cancellationToken);
            using var activity = FoodOrderingTelemetry.ActivitySource.StartActivity("food-ordering.customer-message");
            activity?.SetTag("food_ordering.customer_id", CustomerIdentity.CreateStorageKey(customerId));

            var orderConfirmationPending = await orders.HasPendingConfirmationAsync(customerId);
            var decision = await agents.ActionClassifier.ClassifyAsync(
                input,
                orderConfirmationPending,
                cancellationConfirmationPending,
                cancellationToken);

            if (cancellationConfirmationPending)
            {
                switch (decision.Action)
                {
                    case CustomerAction.ConfirmCancellation:
                        cancellationConfirmationPending = false;
                        var cancellation = await orders.CancelAsync(customerId);
                        return new CustomerChatResult(cancellation, await orders.GetStatusAsync(customerId));
                    case CustomerAction.RejectCancellation:
                        cancellationConfirmationPending = false;
                        return new CustomerChatResult("Cancellation declined. The order was left unchanged.", await orders.GetStatusAsync(customerId));
                    case CustomerAction.UnclearCancellationReply:
                        return new CustomerChatResult("Please confirm or decline the cancellation.", await orders.GetStatusAsync(customerId));
                    case CustomerAction.Unrelated:
                        cancellationConfirmationPending = false;
                        break;
                    default:
                        return new CustomerChatResult("Please confirm or decline the cancellation, or ask about something else.", await orders.GetStatusAsync(customerId));
                }
            }

            if (decision.Action == CustomerAction.RequestCancellation)
            {
                cancellationConfirmationPending = true;
                return new CustomerChatResult("Do you want me to cancel your order?", await orders.GetStatusAsync(customerId));
            }
            if (decision.Action == CustomerAction.ConfirmOrder)
            {
                var result = await orders.ConfirmAsync(customerId);
                return new CustomerChatResult(result, await orders.GetStatusAsync(customerId));
            }
            if (decision.Action == CustomerAction.OrderHistory)
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
            if (decision.Action == CustomerAction.OrderStatus)
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
