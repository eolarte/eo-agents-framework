using System.ComponentModel;
using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

var menu = new RestaurantMenu();

var menuAgent = CreateAgent(
    name: "MenuAgent",
    instructions:
        "You are a restaurant menu assistant. " +
        "Use the search_menu tool before making recommendations. " +
        "Only suggest items returned by the tool, explain why they fit the request, " +
        "and ask one concise follow-up question when needed.",
    tools: new List<AITool>
    {
        AIFunctionFactory.Create(menu.Search)
    });

var orderAgent = CreateAgent(
    name: "OrderAgent",
    instructions:
        "You are a restaurant order assistant. " +
        "Remember customer preferences stated earlier in this conversation when preparing later drafts. " +
        "Create a concise order summary from the customer request and menu suggestions. " +
        "Do not claim that an order was placed. Ask for confirmation at the end.");

var restoredState = await LoadStateAsync(menuAgent, orderAgent);
AgentSession menuSession = restoredState.MenuSession;
AgentSession orderSession = restoredState.OrderSession;

Console.WriteLine("Food ordering sample");
Console.WriteLine("Describe what you would like to eat, or press Enter to exit.");
Console.WriteLine("After a draft is created, type yes to confirm, no to cancel, status to inspect it, or reset to start over.");

OrderDraft? currentOrder = restoredState.CurrentOrder;

if (restoredState.WasRestored)
{
    Console.WriteLine("Restored the previous conversation and local order state.");
    if (currentOrder is not null)
    {
        Console.WriteLine($"Order status: {currentOrder.Status}");
    }
}

while (true)
{
    Console.Write(currentOrder?.Status == OrderStatus.AwaitingConfirmation
        ? "\nCustomer (yes/no/status/reset or new request): "
        : "\nCustomer: ");
    var customerInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(customerInput))
    {
        break;
    }

    if (customerInput.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        menuSession = await menuAgent.CreateSessionAsync();
        orderSession = await orderAgent.CreateSessionAsync();
        currentOrder = null;
        DeleteSavedState();
        Console.WriteLine("Conversation and local order state reset.");
        continue;
    }

    if (TryHandleOrderCommand(customerInput, ref currentOrder))
    {
        await SaveStateAsync(menuAgent, menuSession, orderAgent, orderSession, currentOrder);
        continue;
    }

    var customerRequest = customerInput.Trim();
    var menuResponse = await menuAgent.RunAsync(
        $"Customer request: {customerRequest}\n" +
        "Suggest relevant food or drink options, using preferences from earlier turns when relevant.",
        menuSession);

    var menuSuggestions = menuResponse.ToString();
    Console.WriteLine($"\nMenu agent: {menuSuggestions}");

    currentOrder = new OrderDraft(
        CustomerRequest: customerRequest,
        MenuSuggestions: menuSuggestions,
        Summary: string.Empty,
        Status: OrderStatus.Draft);
    await SaveStateAsync(menuAgent, menuSession, orderAgent, orderSession, currentOrder);
    Console.WriteLine($"Order status: {currentOrder.Status}");

    var orderResponse = await orderAgent.RunAsync(
        $"Customer request: {customerRequest}\n" +
        $"Menu agent suggestions: {menuSuggestions}\n" +
        "Prepare the order summary using any relevant preferences from earlier turns and request confirmation.",
        orderSession);

    currentOrder = currentOrder with
    {
        Summary = orderResponse.ToString(),
        Status = OrderStatus.AwaitingConfirmation
    };

    await SaveStateAsync(menuAgent, menuSession, orderAgent, orderSession, currentOrder);
    Console.WriteLine($"Order agent: {currentOrder.Summary}");
    Console.WriteLine($"Order status: {currentOrder.Status}");
}

static async Task<RestoredState> LoadStateAsync(AIAgent menuAgent, AIAgent orderAgent)
{
    var sessionFilePath = GetSessionFilePath();

    if (!File.Exists(sessionFilePath))
    {
        return new RestoredState(
            await menuAgent.CreateSessionAsync(),
            await orderAgent.CreateSessionAsync(),
            CurrentOrder: null,
            WasRestored: false);
    }

    await using var stream = File.OpenRead(sessionFilePath);
    var persistedState = await JsonSerializer.DeserializeAsync<PersistedState>(stream);

    if (persistedState is null ||
        persistedState.MenuSession.ValueKind == JsonValueKind.Undefined ||
        persistedState.OrderSession.ValueKind == JsonValueKind.Undefined)
    {
        throw new InvalidDataException(
            $"The saved food-ordering state at '{sessionFilePath}' is incomplete.");
    }

    return new RestoredState(
        await menuAgent.DeserializeSessionAsync(persistedState.MenuSession),
        await orderAgent.DeserializeSessionAsync(persistedState.OrderSession),
        persistedState.CurrentOrder,
        WasRestored: true);
}

static async Task SaveStateAsync(
    AIAgent menuAgent,
    AgentSession menuSession,
    AIAgent orderAgent,
    AgentSession orderSession,
    OrderDraft? currentOrder)
{
    var sessionFilePath = GetSessionFilePath();
    var directory = Path.GetDirectoryName(sessionFilePath);

    if (string.IsNullOrWhiteSpace(directory))
    {
        throw new InvalidOperationException(
            "Could not determine a directory for the saved food-ordering state.");
    }

    Directory.CreateDirectory(directory);

    var persistedState = new PersistedState(
        await menuAgent.SerializeSessionAsync(menuSession),
        await orderAgent.SerializeSessionAsync(orderSession),
        currentOrder);

    await File.WriteAllTextAsync(
        sessionFilePath,
        JsonSerializer.Serialize(persistedState, CreateJsonOptions()));
}

static void DeleteSavedState()
{
    var sessionFilePath = GetSessionFilePath();

    if (File.Exists(sessionFilePath))
    {
        File.Delete(sessionFilePath);
    }
}

static string GetSessionFilePath()
{
    var localApplicationData = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData);

    if (string.IsNullOrWhiteSpace(localApplicationData))
    {
        throw new InvalidOperationException(
            "Could not determine the local application-data directory for saved state.");
    }

    return Path.Combine(
        localApplicationData,
        "Microsoft",
        "AgentFramework",
        "food-ordering-state.json");
}

static JsonSerializerOptions CreateJsonOptions() => new()
{
    WriteIndented = true
};

static bool TryHandleOrderCommand(string input, ref OrderDraft? currentOrder)
{
    if (currentOrder is null)
    {
        return false;
    }

    var command = input.Trim().ToLowerInvariant();

    if (command == "status")
    {
        Console.WriteLine($"Order status: {currentOrder.Status}");
        if (!string.IsNullOrWhiteSpace(currentOrder.Summary))
        {
            Console.WriteLine($"Order summary: {currentOrder.Summary}");
        }

        return true;
    }

    if (currentOrder.Status == OrderStatus.AwaitingConfirmation &&
        command is "yes" or "y" or "confirm")
    {
        currentOrder = currentOrder with { Status = OrderStatus.Confirmed };
        Console.WriteLine("Order status: Confirmed");
        Console.WriteLine("The order is confirmed in this sample; no real order was placed.");
        Console.WriteLine("Type complete to simulate local fulfillment, or enter a new request.");
        return true;
    }

    if (currentOrder.Status == OrderStatus.AwaitingConfirmation &&
        command is "no" or "n" or "cancel")
    {
        currentOrder = currentOrder with { Status = OrderStatus.Cancelled };
        Console.WriteLine("Order status: Cancelled");
        return true;
    }

    if (currentOrder.Status == OrderStatus.Confirmed && command == "complete")
    {
        currentOrder = currentOrder with { Status = OrderStatus.Completed };
        Console.WriteLine("Order status: Completed locally");
        Console.WriteLine("No external restaurant order was placed.");
        return true;
    }

    return false;
}

static AIAgent CreateAgent(
    string name,
    string instructions,
    IList<AITool>? tools = null)
{
    var modelName =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
    var openAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

    if (!string.IsNullOrWhiteSpace(openAiApiKey))
    {
        return CreateOpenAIAgent(openAiApiKey, modelName, name, instructions, tools);
    }

    var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

    if (string.IsNullOrWhiteSpace(endpoint))
    {
        throw new InvalidOperationException(
            "Set AZURE_OPENAI_API_KEY to use OpenAI, or set AZURE_OPENAI_ENDPOINT " +
            "to use Azure AI Foundry with Azure credentials.");
    }

    return CreateAzureAIFoundryAgent(endpoint, modelName, name, instructions, tools);
}

static AIAgent CreateOpenAIAgent(
    string apiKey,
    string modelName,
    string name,
    string instructions,
    IList<AITool>? tools)
{
    return new OpenAIClient(apiKey)
        .GetChatClient(modelName)
        .AsIChatClient()
        .AsAIAgent(new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools
            }
        });
}

static AIAgent CreateAzureAIFoundryAgent(
    string endpoint,
    string deploymentName,
    string name,
    string instructions,
    IList<AITool>? tools)
{
    return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
        .AsAIAgent(
            model: deploymentName,
            instructions: instructions,
            name: name,
            tools: tools);
}

enum OrderStatus
{
    Draft,
    AwaitingConfirmation,
    Confirmed,
    Completed,
    Cancelled
}

sealed record OrderDraft(
    string CustomerRequest,
    string MenuSuggestions,
    string Summary,
    OrderStatus Status);

sealed record RestoredState(
    AgentSession MenuSession,
    AgentSession OrderSession,
    OrderDraft? CurrentOrder,
    bool WasRestored);

sealed record PersistedState(
    JsonElement MenuSession,
    JsonElement OrderSession,
    OrderDraft? CurrentOrder);

sealed record MenuItem(
    string Id,
    string Name,
    string Category,
    string Description,
    decimal Price,
    IReadOnlyList<string> Tags);

sealed class RestaurantMenu
{
    private static readonly IReadOnlyList<MenuItem> Items = new List<MenuItem>
    {
        new(
            "pizza-margherita",
            "Margherita Pizza",
            "Main",
            "Tomato, mozzarella, basil, and olive oil.",
            14.50m,
            new[] { "vegetarian", "pizza" }),
        new(
            "chicken-burger",
            "Spicy Chicken Burger",
            "Main",
            "Crispy chicken, lettuce, pickles, and spicy sauce.",
            13.00m,
            new[] { "spicy", "chicken", "burger" }),
        new(
            "falafel-bowl",
            "Falafel Grain Bowl",
            "Main",
            "Falafel, grains, hummus, cucumber, tomato, and tahini.",
            12.50m,
            new[] { "vegan", "vegetarian", "healthy" }),
        new(
            "salmon-bowl",
            "Grilled Salmon Bowl",
            "Main",
            "Grilled salmon, rice, greens, avocado, and lemon dressing.",
            18.00m,
            new[] { "pescatarian", "healthy", "fish" }),
        new(
            "caesar-salad",
            "Chicken Caesar Salad",
            "Main",
            "Romaine lettuce, grilled chicken, parmesan, and Caesar dressing.",
            11.50m,
            new[] { "chicken", "salad" }),
        new(
            "truffle-fries",
            "Truffle Fries",
            "Side",
            "Crispy fries with truffle oil and parmesan.",
            6.50m,
            new[] { "vegetarian", "side" }),
        new(
            "chocolate-brownie",
            "Chocolate Brownie",
            "Dessert",
            "Warm chocolate brownie with a soft center.",
            5.00m,
            new[] { "vegetarian", "dessert", "sweet" }),
        new(
            "sparkling-water",
            "Sparkling Water",
            "Drink",
            "Chilled sparkling mineral water.",
            3.00m,
            new[] { "vegan", "drink" })
    };

    [Description("Searches the restaurant menu by dish name, category, ingredient, or dietary tag.")]
    public IReadOnlyList<MenuItem> Search(
        [Description("A food preference, ingredient, category, or dietary requirement to search for.")]
        string query)
    {
        var terms = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Items
            .Where(item =>
                terms.Length == 0 ||
                terms.Any(term =>
                    item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .Take(8)
            .ToArray();
    }
}
