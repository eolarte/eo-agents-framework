using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace food_ordering.Agents;

public sealed class FoodOrderingAgentFactory(
    IChatClient chatClient,
    RestaurantData restaurant,
    IRestaurantKnowledge knowledge,
    IReadOnlyList<AITool> mcpTools,
    ICustomerOrderStore orderStore,
    CustomerMemory customerMemory,
    FoodOrderingConfiguration configuration) : ICustomerAgentFactory
{
    public CustomerAgentSet Create(string customerId)
    {
#pragma warning disable MAAI001
        var fileStore = new FileSystemAgentFileStore(customerMemory.Root);
        var memoryProvider = new FileMemoryProvider(fileStore, _ => new FileMemoryState
        { WorkingFolder = $"customers/{CustomerIdentity.CreateStorageKey(customerId)}" });
#pragma warning restore MAAI001

        var menuAgent = CreateAgent("MenuAgent",
            "Find suitable dishes in the restaurant menu. Use search_menu and retrieved menu context. " +
            "Only recommend listed items, respect dietary needs, and avoid claiming allergy safety beyond the source information.",
            [AIFunctionFactory.Create(restaurant.SearchMenu)], [knowledge.MenuSearchProvider]);
        var policyAgent = CreateAgent("PolicyAgent",
            "Answer questions about restaurant hours, delivery zones, delivery timing, and ordering policies. " +
            "Use retrieved policy documents for policy answers and the MCP restaurant information tool for hours and delivery facts. " +
            "Say when the demonstration content does not cover an answer.", [.. mcpTools], [knowledge.PolicySearchProvider]);
        var checkoutAgent = CreateAgent("CheckoutPaymentAgent",
            "Prepare a local order draft and check a demo payment choice. Never request or accept real card numbers, " +
            "security codes, bank credentials, or other payment secrets. Only demo-card and cash are supported. " +
            "Use create_local_order_draft after confirming the customer has selected menu items and payment choice. " +
            "The customer must confirm the resulting draft; this does not charge or place an order. " +
            "Confirmed drafts start the local simulated preparation and delivery lifecycle.",
            [AIFunctionFactory.Create((string paymentMethod) => CheckoutTools.ValidateDemoPaymentMethod(paymentMethod)),
             AIFunctionFactory.Create((string orderSummary, string paymentMethod) => orderStore.CreateDraft(customerId, orderSummary, paymentMethod))]);
        var deliveryAgent = CreateAgent("DeliveryAgent",
            "Explain delivery options using the restaurant's MCP information tool. When a persisted lifecycle snapshot is supplied, use it " +
            "as the source of truth. For a customer's active or historical demo order, " +
            "use get_local_delivery_status for its simulated delivery progress. Never claim an order was dispatched to a real courier. " +
            "Delivery estimates are illustrative and no live courier service is involved.",
            [.. mcpTools, AIFunctionFactory.Create(
                (string? orderId = null) => orderStore.GetDeliveryStatusAsync(customerId, orderId),
                name: "get_local_delivery_status",
                description: "Read this customer's latest active local demo delivery status, or provide an order ID for history.")]);
        var statusAgent = CreateAgent("OrderStatusAgent",
            "Report the latest active local demo order, payment, and delivery statuses using get_local_order_status. " +
            "When a persisted lifecycle snapshot is supplied, use it as the source of truth. " +
            "Use get_local_order_history when a customer asks about past orders; they can provide an order ID to inspect one. " +
            "Clearly state when there is no order. " +
            "Do not imply the status is connected to a restaurant or courier.",
            [AIFunctionFactory.Create(
                 (string? orderId = null) => orderStore.GetStatusAsync(customerId, orderId),
                 name: "get_local_order_status",
                 description: "Read the latest active local demo order and its payment and delivery statuses, or provide an order ID for history."),
             AIFunctionFactory.Create(
                 () => orderStore.GetRecentOrdersAsync(customerId),
                 name: "get_local_order_history",
                 description: "List this customer's ten most recent local demo orders with IDs and statuses.")]);
        var coordinator = CreateAgent("FoodOrderingCoordinator",
            "You coordinate a fictional restaurant ordering experience. Delegate menu recommendations to MenuAgent, " +
            "restaurant rules and facts to PolicyAgent, delivery questions and delivery tracking to DeliveryAgent, " +
            "local order preparation and demo payment checks to CheckoutPaymentAgent, and order/payment status questions " +
            "to OrderStatusAgent. Use the appropriate status tool for status questions instead of guessing. " +
            "Use the file memory tools to remember customer preferences when useful. Never claim a draft is confirmed just " +
            "because the customer said they confirm it; only persisted local lifecycle status is authoritative. Do not create " +
            "another draft to confirm an existing draft. " +
            "Explain that every order and payment action is simulated locally.",
            [menuAgent.AsAIFunction(), policyAgent.AsAIFunction(), checkoutAgent.AsAIFunction(), deliveryAgent.AsAIFunction(), statusAgent.AsAIFunction()],
            [memoryProvider]);

        return new CustomerAgentSet(coordinator, menuAgent, policyAgent, checkoutAgent, deliveryAgent, statusAgent,
            memoryProvider, new CustomerActionClassifier(chatClient, configuration.ModelName));
    }

    private AIAgent CreateAgent(string name, string instructions, IList<AITool>? tools = null, IList<AIContextProvider>? providers = null)
        => chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = name,
            Description = instructions,
            ChatOptions = new ChatOptions { ModelId = configuration.ModelName, Instructions = instructions, Tools = tools },
            AIContextProviders = providers
        });
}
