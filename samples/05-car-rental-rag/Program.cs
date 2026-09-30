using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using OpenAI;
using CommunityToolkit.VectorData.InMemory;

const string CollectionName = "car-rental-policies";
const int EmbeddingDimensions = 1536;
const int SearchResultCount = 3;

var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");
}

var modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? "gpt-4o-mini";
var embeddingModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL")
    ?? "text-embedding-3-small";
var openAiClient = new OpenAIClient(apiKey);
var embeddingGenerator = openAiClient
    .GetEmbeddingClient(embeddingModelName)
    .AsIEmbeddingGenerator(EmbeddingDimensions);

VectorStore vectorStore = new InMemoryVectorStore(new()
{
    EmbeddingGenerator = embeddingGenerator
});
var policyCollection = vectorStore.GetCollection<string, RentalPolicyRecord>(CollectionName);
await policyCollection.EnsureCollectionExistsAsync();

var policyDirectory = Path.Combine(AppContext.BaseDirectory, "policies");
var policyFiles = Directory.GetFiles(policyDirectory, "*.md")
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (policyFiles.Length == 0)
{
    throw new InvalidOperationException($"No rental policy files were found in '{policyDirectory}'.");
}

foreach (var policyFile in policyFiles)
{
    var policyText = await File.ReadAllTextAsync(policyFile);
    var embedding = await embeddingGenerator.GenerateAsync(policyText);

    await policyCollection.UpsertAsync(new RentalPolicyRecord
    {
        Id = Path.GetFileNameWithoutExtension(policyFile),
        SourceName = Path.GetFileName(policyFile),
        Text = policyText,
        Embedding = embedding.Vector
    });
}

async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchPoliciesAsync(
    string query,
    CancellationToken cancellationToken)
{
    var queryEmbedding = await embeddingGenerator.GenerateAsync(
        [query],
        cancellationToken: cancellationToken);
    var results = policyCollection.SearchAsync(
        queryEmbedding[0].Vector,
        top: SearchResultCount,
        cancellationToken: cancellationToken);
    var searchResults = new List<TextSearchProvider.TextSearchResult>();

    await foreach (var result in results.WithCancellation(cancellationToken))
    {
        searchResults.Add(new TextSearchProvider.TextSearchResult
        {
            SourceName = result.Record.SourceName,
            Text = result.Record.Text,
            RawRepresentation = result
        });
    }

    return searchResults;
}

var agent = openAiClient
    .GetChatClient(modelName)
    .AsIChatClient()
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "CarRentalPolicyAgent",
        ChatOptions = new ChatOptions
        {
            Instructions =
                "You are a helpful car rental policy assistant. First establish which car type " +
                "the customer wants to rent when it is not clear from the conversation. Use the " +
                "retrieved policy context to explain the rental requirements and rules for that " +
                "car type. Cite the source policy file by name. Do not infer rules that are not " +
                "stated in the retrieved context; if the policy files do not cover a question, " +
                "say so and ask what else you can clarify. These files describe demonstration " +
                "policies, not a real rental company's terms."
        },
        AIContextProviders =
        [
            new TextSearchProvider(SearchPoliciesAsync, new TextSearchProviderOptions
            {
                SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke
            })
        ]
    });

var session = await agent.CreateSessionAsync();
Console.WriteLine("Car rental policy assistant");
Console.WriteLine("Ask about the rules and requirements for a car type.");
Console.WriteLine("Available types: SUVs, small cars, electric cars, vans, and luxury cars.");
Console.WriteLine("Type exit or press Enter to finish.");
Console.WriteLine($"Loaded {policyFiles.Length} policy files into the in-memory search index.");

var opening = await agent.RunAsync(
    "Welcome the customer and ask which type of car they would like to rent.",
    session);
Console.WriteLine($"\nRental assistant: {opening}");

while (true)
{
    Console.Write("\nCustomer: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var response = await agent.RunAsync(input.Trim(), session);
    Console.WriteLine($"\nRental assistant: {response}");
}

public sealed class RentalPolicyRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string SourceName { get; set; } = string.Empty;

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    [VectorStoreVector(1536)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
