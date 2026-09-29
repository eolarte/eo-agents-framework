using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

var agent = CreateAgent();

Console.WriteLine(await agent.RunAsync("What is the largest city in France?"));

await foreach (var update in agent.RunStreamingAsync("Tell me a one-sentence fun fact."))
{
    Console.Write(update);
}

static AIAgent CreateAgent()
{
    var openAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
    var modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

    if (!string.IsNullOrWhiteSpace(openAiApiKey))
    {
        return CreateOpenAIAgent(openAiApiKey, modelName);
    }

    var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

    if (string.IsNullOrWhiteSpace(endpoint))
    {
        throw new InvalidOperationException(
            "Set AZURE_OPENAI_API_KEY to use OpenAI, or set AZURE_OPENAI_ENDPOINT " +
            "to use Azure AI Foundry with Azure credentials.");
    }

    var deploymentName =
        Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? modelName;

    return CreateAzureAIFoundryAgent(endpoint, deploymentName);
}

static AIAgent CreateOpenAIAgent(string apiKey, string modelName)
{
    return new OpenAIClient(apiKey)
        .GetChatClient(modelName)
        .AsIChatClient()
        .AsAIAgent(new ChatClientAgentOptions
        {
            Name = "HelloAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = "You are a friendly assistant. Keep your answers brief."
            }
        });
}

static AIAgent CreateAzureAIFoundryAgent(string endpoint, string deploymentName)
{
    return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
        .AsAIAgent(
            model: deploymentName,
            instructions: "You are a friendly assistant. Keep your answers brief.",
            name: "HelloAgent");
}