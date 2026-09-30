#pragma warning disable MAAI001 // AgentFileStore and its implementations are experimental.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

const string UserId = "demo-traveler";
const string AgentInstructions =
    "You are a friendly travel agency intake assistant. Your first task is to build an " +
    "ongoing traveler profile by asking one concise question at a time. Ask about preferred " +
    "destinations, travel style, approximate budget, preferred pace, lodging, and any dietary " +
    "or accessibility needs. Adapt to what the traveler has already shared, and let them skip " +
    "any question. Once you have enough information, summarize the profile and use the " +
    "file_memory_write tool to save it as traveler-profile.md with a short description. " +
    "If a profile already exists, use file_memory_read to review and update it with new " +
    "preferences. After intake, help with travel ideas using the saved profile when relevant. " +
    "Do not claim to book or purchase anything.";

var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");
}

var modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? "gpt-4o-mini";
var memoryRoot = Path.Combine(AppContext.BaseDirectory, "agent-memory");
var workingFolder = $"travelers/{UserId}";
var fileStore = new FileSystemAgentFileStore(memoryRoot);
using var fileMemoryProvider = new FileMemoryProvider(
    fileStore,
    _ => new FileMemoryState { WorkingFolder = workingFolder });

AIAgent agent = new OpenAIClient(apiKey)
    .GetChatClient(modelName)
    .AsIChatClient()
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "TravelAgencyIntakeAgent",
        ChatOptions = new ChatOptions
        {
            Instructions = AgentInstructions
        },
        AIContextProviders = [fileMemoryProvider]
    });

var session = await agent.CreateSessionAsync();
var memoryDirectory = Path.Combine(memoryRoot, workingFolder);

Console.WriteLine("Travel agency intake");
Console.WriteLine("Answer the agent's questions to create your traveler profile.");
Console.WriteLine("Type exit or press Enter to finish.");
Console.WriteLine($"Traveler memory folder: {memoryDirectory}");

var opening = await agent.RunAsync(
    "Welcome the traveler and begin the intake by asking the first relevant question. " +
    "If saved traveler preferences are available, briefly acknowledge them and ask whether " +
    "the traveler wants to update anything before continuing.",
    session);
Console.WriteLine($"\nTravel agent: {opening}");

while (true)
{
    Console.Write("\nTraveler: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var response = await agent.RunAsync(input.Trim(), session);
    Console.WriteLine($"\nTravel agent: {response}");
}
