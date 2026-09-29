sealed record OrderDraft(
    string CustomerRequest,
    string AgentResponse,
    OrderStatus Status);

enum OrderStatus
{
    AwaitingConfirmation,
    Confirmed,
    Cancelled
}
