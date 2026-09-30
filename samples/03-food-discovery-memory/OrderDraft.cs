sealed record OrderDraft(
    List<OrderConversationTurn> Conversation,
    OrderStatus Status);

sealed record OrderConversationTurn(string Speaker, string Message);

enum OrderStatus
{
    AwaitingConfirmation,
    Confirmed,
    Cancelled
}
