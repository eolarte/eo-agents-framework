using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

var modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? "gpt-4o-mini";
var menu = new RestaurantMenu();
var profileStore = new JsonCustomerProfileStore();
var extractionClient = CreateExtractionClient(modelName);
var memory = new CustomerPreferencesMemory(
    extractionClient,
    modelName,
    profileStore);
var agent = CreateAgent(modelName, menu, memory);
var session = await agent.CreateSessionAsync();

Console.WriteLine("Food discovery with persistent customer memory");
Console.WriteLine("Preferences are saved per customer in a local JSON profile.");
Console.WriteLine("The agent will first ask for your name.");
Console.WriteLine(await agent.RunAsync(
    "Welcome the customer and ask for their name before helping them.",
    session));
Console.WriteLine(
    "Describe what you would like to eat, or type status, confirm, cancel, or reset.");

OrderDraft? currentOrder = null;

while (true)
{
    Console.Write(currentOrder?.Status == OrderStatus.AwaitingConfirmation
        ? "\nCustomer (confirm/cancel/status/reset or new request): "
        : "\nCustomer: ");
    var customerInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(customerInput))
    {
        break;
    }

    if (customerInput.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        session = await agent.CreateSessionAsync();
        currentOrder = null;
        Console.WriteLine("Conversation reset. The saved customer profile was kept.");
        Console.WriteLine(await agent.RunAsync(
            "Welcome the customer and ask for their name before helping them.",
            session));
        continue;
    }

    var orderCommand = await TryHandleOrderCommand(
        customerInput,
        session,
        memory,
        currentOrder);
    if (orderCommand.Handled)
    {
        currentOrder = orderCommand.UpdatedOrder;
        continue;
    }

    var customerRequest = customerInput.Trim();
    var response = await agent.RunAsync(
        $"Customer request: {customerRequest}\n" +
        "Use the search_menu tool before recommending food. " +
        "If the customer is ready to order, prepare a concise local order draft " +
        "and ask for confirmation. Do not claim that an external order was placed.",
        session);

    var responseText = response.ToString();
    Console.WriteLine($"\nFood discovery agent: {responseText}");

    currentOrder = new OrderDraft(
        CustomerRequest: customerRequest,
        AgentResponse: responseText,
        Status: OrderStatus.AwaitingConfirmation);
    Console.WriteLine($"Local order status: {currentOrder.Status}");
}

static IChatClient CreateExtractionClient(string modelName)
{
    var openAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

    if (!string.IsNullOrWhiteSpace(openAiApiKey))
    {
        return new OpenAIClient(openAiApiKey)
            .GetChatClient(modelName)
            .AsIChatClient();
    }

    var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

    if (string.IsNullOrWhiteSpace(endpoint))
    {
        throw new InvalidOperationException(
            "Set AZURE_OPENAI_API_KEY to use OpenAI, or set AZURE_OPENAI_ENDPOINT " +
            "to use Azure AI Foundry with Azure credentials.");
    }

#pragma warning disable OPENAI001
    var extractionClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
        .GetProjectOpenAIClient()
        .GetResponsesClient()
        .AsIChatClient(modelName);
#pragma warning restore OPENAI001

    return extractionClient;
}

static AIAgent CreateAgent(
    string modelName,
    RestaurantMenu menu,
    CustomerPreferencesMemory memory)
{
    var instructions =
        "You are a friendly food discovery and order-form assistant. " +
        "Use the search_menu tool before recommending menu items, and only recommend " +
        "items returned by that tool. On the first turn, ask the customer for their " +
        "name before making recommendations. Use remembered preferences as helpful " +
        "context, but ask a concise follow-up question when the request is ambiguous. " +
        "When the customer is ready, prepare a concise local order draft and ask for " +
        "confirmation. Never claim that an external order was placed.";
    var tools = new List<AITool>
    {
        AIFunctionFactory.Create(menu.Search)
    };

    var openAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

    if (!string.IsNullOrWhiteSpace(openAiApiKey))
    {
        return new OpenAIClient(openAiApiKey)
            .GetChatClient(modelName)
            .AsIChatClient()
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = "FoodDiscoveryAgent",
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    Tools = tools
                },
                AIContextProviders = [memory]
            });
    }

    var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

    if (string.IsNullOrWhiteSpace(endpoint))
    {
        throw new InvalidOperationException(
            "Set AZURE_OPENAI_API_KEY to use OpenAI, or set AZURE_OPENAI_ENDPOINT " +
            "to use Azure AI Foundry with Azure credentials.");
    }

    return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
        .AsAIAgent(new ChatClientAgentOptions
        {
            Name = "FoodDiscoveryAgent",
            ChatOptions = new ChatOptions
            {
                ModelId = modelName,
                Instructions = instructions,
                Tools = tools
            },
            AIContextProviders = [memory]
        });
}

static async Task<(bool Handled, OrderDraft? UpdatedOrder)> TryHandleOrderCommand(
    string input,
    AgentSession session,
    CustomerPreferencesMemory memory,
    OrderDraft? currentOrder)
{
    var command = input.Trim().ToLowerInvariant();

    if (command == "status")
    {
        if (currentOrder is null)
        {
            Console.WriteLine("There is no local order draft.");
        }
        else
        {
            Console.WriteLine($"Local order status: {currentOrder.Status}");
            Console.WriteLine($"Customer request: {currentOrder.CustomerRequest}");
            Console.WriteLine($"Agent response: {currentOrder.AgentResponse}");
        }

        return (true, currentOrder);
    }

    if (currentOrder?.Status == OrderStatus.AwaitingConfirmation &&
        command is "yes" or "y" or "confirm")
    {
        await memory.RecordConfirmedOrderAsync(session, currentOrder);
        currentOrder = currentOrder with { Status = OrderStatus.Confirmed };
        Console.WriteLine("Local order status: Confirmed");
        Console.WriteLine("The order was saved to the customer's local profile.");
        Console.WriteLine("No external restaurant order was placed.");
        return (true, currentOrder);
    }

    if (currentOrder?.Status == OrderStatus.AwaitingConfirmation &&
        command is "no" or "n" or "cancel")
    {
        currentOrder = currentOrder with { Status = OrderStatus.Cancelled };
        Console.WriteLine("Local order status: Cancelled");
        return (true, currentOrder);
    }

    return (false, currentOrder);
}
